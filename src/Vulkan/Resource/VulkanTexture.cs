using System;
using API.Commands.Enums;
using API.Descriptors.Components;
using API.Enums;
using Silk.NET.Vulkan;
using Vulkan.Core;

namespace Vulkan.Resource;

/// <summary>
/// Kapselt eine native Vulkan-2D-Textur (<see cref="Silk.NET.Vulkan.Image"/>, <see cref="DeviceMemory"/>,
/// <see cref="ImageView"/> und <see cref="Silk.NET.Vulkan.Sampler"/>) inklusive automatischem
/// <see cref="CurrentLayout"/>-Tracking für Vulkan 1.3 <c>Synchronization2</c>-Barriers
/// sowie Staging-Buffer-Upload für initiale Pixeldaten.
/// </summary>
internal sealed unsafe class VulkanTexture : IDisposable
{
    private readonly VulkanContext _context;

    /// <summary>Natives Vulkan-<see cref="Silk.NET.Vulkan.Image"/>-Handle (<c>DEVICE_LOCAL</c>).</summary>
    public Image Image { get; private set; }

    /// <summary>Zugehöriger VRAM-Speicherblock der Textur.</summary>
    public DeviceMemory Memory { get; private set; }

    /// <summary>Natives <see cref="ImageView"/> für Shader-Sampling oder RenderPass-Attachments.</summary>
    public ImageView View { get; private set; }

    /// <summary>Zugehöriger Vulkan-<see cref="Silk.NET.Vulkan.Sampler"/>.</summary>
    public Sampler Sampler { get; private set; }

    /// <summary>Natives Vulkan-<see cref="Format"/> der Textur.</summary>
    public Format VkFormat { get; private set; }

    /// <summary>Auflösung der Textur in Pixeln.</summary>
    public Extent2D Extent { get; private set; }

    /// <summary>Aktuell getracktes <see cref="ImageLayout"/> (verhindert redundante Layout-Barriers).</summary>
    public ImageLayout CurrentLayout { get; private set; } = ImageLayout.Undefined;

    /// <summary><c>true</c>, wenn die Textur ein Tiefen-/Stencil-Format besitzt.</summary>
    public bool IsDepth { get; private set; }

    /// <summary><c>true</c>, wenn die Textur im Shader gesampelt werden darf (<see cref="ETextureUsage.Sampled"/>).</summary>
    public bool CanSample { get; private set; }

    public VulkanTexture(VulkanContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Erstellt Image, VRAM-Allokation, ImageView sowie Sampler anhand von <paramref name="desc"/>
    /// und lädt optional <paramref name="initialData"/> per Staging-Buffer in das <c>DEVICE_LOCAL</c>-Image hoch.
    /// </summary>
    public void Initialize(in RStTextureDesc desc, ReadOnlySpan<byte> initialData, CommandPool transferPool)
    {
        var vk = _context.Api;
        var device = _context.Device;

        Extent = new Extent2D((uint)desc.Width, (uint)desc.Height);
        IsDepth = desc.Format is ETextureFormat.D32_Float or ETextureFormat.D24_UNorm_S8_UInt;

        VkFormat = desc.Format switch
        {
            ETextureFormat.RGBA8_UNorm      => Format.R8G8B8A8Unorm,
            ETextureFormat.RGBA8_UNorm_SRgb => Format.R8G8B8A8Srgb,
            ETextureFormat.BGRA8_UNorm      => Format.B8G8R8A8Unorm,
            ETextureFormat.BGRA8_UNorm_SRgb => Format.B8G8R8A8Srgb,
            ETextureFormat.RGBA16_Float     => Format.R16G16B16A16Sfloat,
            ETextureFormat.D32_Float        => Format.D32Sfloat,
            ETextureFormat.D24_UNorm_S8_UInt => Format.D24UnormS8Uint,
            _                               => Format.R8G8B8A8Srgb
        };

        CanSample = desc.Usage.HasFlag(ETextureUsage.Sampled);

        ImageUsageFlags usage = ImageUsageFlags.TransferDstBit;
        if (CanSample) usage |= ImageUsageFlags.SampledBit;
        if (desc.Usage.HasFlag(ETextureUsage.RenderTarget)) usage |= ImageUsageFlags.ColorAttachmentBit;
        if (desc.Usage.HasFlag(ETextureUsage.DepthStencil)) usage |= ImageUsageFlags.DepthStencilAttachmentBit;
        if (desc.Usage.HasFlag(ETextureUsage.Storage)) usage |= ImageUsageFlags.StorageBit;

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Extent = new Extent3D(Extent.Width, Extent.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Format = VkFormat,
            Tiling = ImageTiling.Optimal,
            InitialLayout = ImageLayout.Undefined,
            Usage = usage,
            Samples = SampleCountFlags.Count1Bit,
            SharingMode = SharingMode.Exclusive
        };

        if (vk.CreateImage(device, &imageInfo, null, out var image) != Result.Success)
            throw new InvalidOperationException("Vulkan Image konnte nicht erstellt werden.");
        Image = image;

        vk.GetImageMemoryRequirements(device, Image, out var memReq);
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReq.Size,
            MemoryTypeIndex = _context.FindMemoryType(memReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        vk.AllocateMemory(device, &allocInfo, null, out var memory);
        Memory = memory;
        vk.BindImageMemory(device, Image, Memory, 0);

        // ImageView
        ImageAspectFlags aspect = IsDepth ? ImageAspectFlags.DepthBit : ImageAspectFlags.ColorBit;
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = Image,
            ViewType = ImageViewType.Type2D,
            Format = VkFormat,
            SubresourceRange = new ImageSubresourceRange(aspect, 0, 1, 0, 1)
        };
        vk.CreateImageView(device, &viewInfo, null, out var view);
        View = view;

        // Sampler
        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Linear,
            AddressModeU = SamplerAddressMode.Repeat,
            AddressModeV = SamplerAddressMode.Repeat,
            AddressModeW = SamplerAddressMode.Repeat,
            MaxAnisotropy = 1.0f,
            MaxLod = 1.0f
        };
        vk.CreateSampler(device, &samplerInfo, null, out var sampler);
        Sampler = sampler;

        // Initial-Daten hochladen, falls vorhanden
        if (!initialData.IsEmpty)
            UploadPixels(initialData, transferPool);
    }

    /// <summary>
    /// Überführt die Textur per Vulkan 1.3 <c>CmdPipelineBarrier2</c> in <paramref name="newLayout"/>,
    /// sofern sie sich nicht bereits in diesem Layout befindet, und aktualisiert <see cref="CurrentLayout"/>.
    /// </summary>
    public void TransitionLayout(CommandBuffer cmd, ImageLayout newLayout)
    {
        if (CurrentLayout == newLayout)
            return;

        ImageAspectFlags aspect = VkFormat switch
        {
            Format.D24UnormS8Uint or Format.D32SfloatS8Uint => ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit,
            Format.D32Sfloat or Format.D16Unorm => ImageAspectFlags.DepthBit,
            _ => ImageAspectFlags.ColorBit
        };

        var barrier = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = PipelineStageFlags2.AllCommandsBit,
            SrcAccessMask = AccessFlags2.MemoryWriteBit,
            DstStageMask = PipelineStageFlags2.AllCommandsBit,
            DstAccessMask = AccessFlags2.MemoryReadBit | AccessFlags2.MemoryWriteBit,
            OldLayout = CurrentLayout,
            NewLayout = newLayout,
            Image = Image,
            SubresourceRange = new ImageSubresourceRange(aspect, 0, 1, 0, 1)
        };

        var depInfo = new DependencyInfo
        {
            SType = StructureType.DependencyInfo,
            ImageMemoryBarrierCount = 1,
            PImageMemoryBarriers = &barrier
        };

        _context.Api.CmdPipelineBarrier2(cmd, &depInfo);
        CurrentLayout = newLayout;
    }

    private void UploadPixels(ReadOnlySpan<byte> data, CommandPool transferPool)
    {
        var vk = _context.Api;
        var device = _context.Device;
        ulong imageSize = (ulong)data.Length;

        var stagingDesc = new RStBufferDesc((int)imageSize, 0, EBufferUsage.Constant, IsDynamic: true);
        using var staging = new VulkanBuffer(_context);
        staging.Initialize(stagingDesc, data, transferPool);

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

        TransitionLayout(cmd, ImageLayout.TransferDstOptimal);

        var region = new BufferImageCopy
        {
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageExtent = new Extent3D(Extent.Width, Extent.Height, 1)
        };
        vk.CmdCopyBufferToImage(cmd, staging.Handle, Image, ImageLayout.TransferDstOptimal, 1, &region);

        TransitionLayout(cmd, ImageLayout.ShaderReadOnlyOptimal);

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

    /// <summary>Gibt Sampler, ImageView, Image und VRAM-Speicher deterministisch frei.</summary>
    public void Dispose()
    {
        if (_context.Device.Handle == 0)
            return;

        var vk = _context.Api;
        if (Sampler.Handle != 0) vk.DestroySampler(_context.Device, Sampler, null);
        if (View.Handle != 0) vk.DestroyImageView(_context.Device, View, null);
        if (Image.Handle != 0) vk.DestroyImage(_context.Device, Image, null);
        if (Memory.Handle != 0) vk.FreeMemory(_context.Device, Memory, null);
    }
}