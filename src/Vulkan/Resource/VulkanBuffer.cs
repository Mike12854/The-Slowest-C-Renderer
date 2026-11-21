using System;
using System.Runtime.CompilerServices;
using API.Descriptors.Components;
using API.Enums;
using Silk.NET.Vulkan;
using Vulkan.Core;

namespace Vulkan.Resource;

/// <summary>
/// Kapselt einen nativen Vulkan-Buffer (<see cref="Silk.NET.Vulkan.Buffer"/> + <see cref="DeviceMemory"/>).
/// Verwendet für dynamische Buffer (<c>IsDynamic = true</c>) ein persistent gemapptes Per-Frame-in-Flight
/// Ring-Buffering (lock-free CPU-Writes via <see cref="Unsafe.CopyBlock"/>) und für statische Buffer
/// (<c>IsDynamic = false</c>) schnelles <c>DEVICE_LOCAL</c>-VRAM inklusive Staging-Buffer-Upload.
/// </summary>
internal sealed unsafe class VulkanBuffer : IDisposable
{
    private readonly VulkanContext _context;

    // Bei dynamischen Buffern ein eigener Buffer pro Frame-in-Flight; bei statischen nur 1 Buffer
    private Silk.NET.Vulkan.Buffer[] _handles = [];
    private DeviceMemory[] _memories = [];
    private void*[] _mappedPtrs = [];
    private int _activeFrameSlot;

    /// <summary>Aktives natives Vulkan-Buffer-Handle für den aktuellen Frame-Slot.</summary>
    public Silk.NET.Vulkan.Buffer Handle => _handles.Length > 0 ? _handles[_activeFrameSlot] : default;

    /// <summary>Allokierte Größe des Buffers in Bytes.</summary>
    public ulong SizeInBytes { get; private set; }

    /// <summary><c>true</c>, wenn der Buffer im persistent gemappten Host-Visible-RAM liegt (dynamischer Pfad).</summary>
    public bool IsHostVisible { get; private set; }

    public VulkanBuffer(VulkanContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Wechselt bei dynamischen Ring-Buffern auf den Speicher-Slot für <paramref name="frameIndex"/>,
    /// damit CPU-Updates niemals laufende GPU-Reads des vorherigen Frames überschreiben.
    /// </summary>
    public void SetFrameSlot(uint frameIndex)
    {
        if (_handles.Length > 1)
            _activeFrameSlot = (int)(frameIndex % (uint)_handles.Length);
    }

    /// <summary>
    /// Erstellt den/die GPU-Buffer anhand von <paramref name="desc"/> und lädt optional <paramref name="initialData"/> hoch.
    /// </summary>
    public void Initialize(in RStBufferDesc desc, ReadOnlySpan<byte> initialData, CommandPool transferPool, int framesInFlight = 2)
    {
        SizeInBytes = (ulong)desc.SizeInBytes;
        IsHostVisible = desc.IsDynamic;

        BufferUsageFlags usage = desc.Usage switch
        {
            EBufferUsage.Vertex     => BufferUsageFlags.VertexBufferBit,
            EBufferUsage.Index      => BufferUsageFlags.IndexBufferBit,
            EBufferUsage.Constant   => BufferUsageFlags.UniformBufferBit,
            EBufferUsage.Structured => BufferUsageFlags.StorageBufferBit,
            _                       => BufferUsageFlags.VertexBufferBit
        };

        int slotCount = IsHostVisible ? Math.Max(1, framesInFlight) : 1;
        _handles = new Silk.NET.Vulkan.Buffer[slotCount];
        _memories = new DeviceMemory[slotCount];
        _mappedPtrs = new void*[slotCount];

        if (IsHostVisible)
        {
            for (int i = 0; i < slotCount; i++)
            {
                CreateRawBuffer(SizeInBytes, usage | BufferUsageFlags.TransferSrcBit,
                    MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                    out var buf, out var mem);

                _handles[i] = buf;
                _memories[i] = mem;

                void* mapped;
                _context.Api.MapMemory(_context.Device, mem, 0, SizeInBytes, 0, &mapped);
                _mappedPtrs[i] = mapped;

                if (!initialData.IsEmpty)
                {
                    _activeFrameSlot = i;
                    Update(initialData, 0);
                }
            }
            _activeFrameSlot = 0;
        }
        else
        {
            CreateRawBuffer(SizeInBytes, usage | BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.DeviceLocalBit,
                out var buf, out var mem);

            _handles[0] = buf;
            _memories[0] = mem;

            if (!initialData.IsEmpty)
                UploadViaStaging(initialData, 0, transferPool);
        }
    }

    /// <summary>
    /// Schreibt <paramref name="data"/> an <paramref name="offsetInBytes"/> in den Buffer
    /// (direktes <c>memcpy</c> bei dynamischen Buffern, Staging-Copy bei statischen <c>DEVICE_LOCAL</c>-Buffern).
    /// </summary>
    public void Update(ReadOnlySpan<byte> data, uint offsetInBytes, CommandPool transferPool = default)
    {
        if (data.IsEmpty)
            return;

        if ((ulong)offsetInBytes + (ulong)data.Length > SizeInBytes)
            throw new ArgumentOutOfRangeException(nameof(data), "Buffer-Update überschreitet Buffer-Größe.");

        if (IsHostVisible && _mappedPtrs[_activeFrameSlot] != null)
        {
            fixed (byte* pSrc = data)
            {
                Unsafe.CopyBlock((byte*)_mappedPtrs[_activeFrameSlot] + offsetInBytes, pSrc, (uint)data.Length);
            }
        }
        else
        {
            UploadViaStaging(data, offsetInBytes, transferPool);
        }
    }

    private void UploadViaStaging(ReadOnlySpan<byte> data, uint dstOffset, CommandPool transferPool)
    {
        var vk = _context.Api;
        var device = _context.Device;
        ulong copySize = (ulong)data.Length;

        CreateRawBuffer(copySize, BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out var stagingBuf, out var stagingMem);

        try
        {
            void* mapped;
            vk.MapMemory(device, stagingMem, 0, copySize, 0, &mapped);
            fixed (byte* pSrc = data)
            {
                Unsafe.CopyBlock(mapped, pSrc, (uint)copySize);
            }
            vk.UnmapMemory(device, stagingMem);

            var allocInfo = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                Level = CommandBufferLevel.Primary,
                CommandPool = transferPool,
                CommandBufferCount = 1
            };
            vk.AllocateCommandBuffers(device, &allocInfo, out var cmd);

            var beginInfo = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit
            };
            vk.BeginCommandBuffer(cmd, &beginInfo);

            var copyRegion = new BufferCopy { SrcOffset = 0, DstOffset = dstOffset, Size = copySize };
            vk.CmdCopyBuffer(cmd, stagingBuf, Handle, 1, &copyRegion);

            vk.EndCommandBuffer(cmd);

            var submitInfo = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &cmd
            };
            vk.QueueSubmit(_context.GraphicsQueue, 1, &submitInfo, default);
            vk.QueueWaitIdle(_context.GraphicsQueue);
            vk.FreeCommandBuffers(device, transferPool, 1, &cmd);
        }
        finally
        {
            vk.DestroyBuffer(device, stagingBuf, null);
            vk.FreeMemory(device, stagingMem, null);
        }
    }

    private void CreateRawBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags props,
        out Silk.NET.Vulkan.Buffer buffer, out DeviceMemory memory)
    {
        var vk = _context.Api;
        var device = _context.Device;

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive
        };

        if (vk.CreateBuffer(device, &bufferInfo, null, out buffer) != Result.Success)
            throw new InvalidOperationException("Vulkan Buffer konnte nicht erstellt werden.");

        vk.GetBufferMemoryRequirements(device, buffer, out var memReq);

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReq.Size,
            MemoryTypeIndex = _context.FindMemoryType(memReq.MemoryTypeBits, props)
        };

        if (vk.AllocateMemory(device, &allocInfo, null, out memory) != Result.Success)
            throw new InvalidOperationException("Vulkan Buffer-Memory konnte nicht allokiert werden.");

        vk.BindBufferMemory(device, buffer, memory, 0);
    }

    /// <summary>Hebt alle Speicher-Mappings auf und gibt sämtliche Vulkan-Buffer sowie deren <see cref="DeviceMemory"/> frei.</summary>
    public void Dispose()
    {
        if (_context.Device.Handle == 0)
            return;

        for (int i = 0; i < _handles.Length; i++)
        {
            if (i < _mappedPtrs.Length && _mappedPtrs[i] != null && i < _memories.Length && _memories[i].Handle != 0)
            {
                _context.Api.UnmapMemory(_context.Device, _memories[i]);
                _mappedPtrs[i] = null;
            }

            if (_handles[i].Handle != 0)
            {
                _context.Api.DestroyBuffer(_context.Device, _handles[i], null);
                _handles[i] = default;
            }

            if (i < _memories.Length && _memories[i].Handle != 0)
            {
                _context.Api.FreeMemory(_context.Device, _memories[i], null);
                _memories[i] = default;
            }
        }

        _handles = [];
        _memories = [];
        _mappedPtrs = [];
    }
}
