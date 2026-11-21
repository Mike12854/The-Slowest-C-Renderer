using System;
using System.Collections.Generic;
using Silk.NET.Vulkan;

namespace Vulkan.Core;

/// <summary>
/// Verwaltet lock-free getrennte Vulkan-<see cref="CommandPool"/>- und <see cref="CommandBuffer"/>-Instanzen
/// für den Primary-Thread (Barriers, <c>CmdBeginRendering</c>/<c>CmdEndRendering</c>) sowie für bis zu
/// <see cref="MaxWorkers"/> parallele Worker-Threads über alle <see cref="FramesInFlight"/> hinweg.
/// Unterstützt beliebig viele Render-Passes pro Frame via Dynamic-Rendering-Inheritance.
/// </summary>
public sealed unsafe class WorkerCommandPool : IDisposable
{
    private readonly VulkanContext _context;

    // Primary CommandBuffers (1 pro Frame-In-Flight für Barriers & Begin/EndRendering)
    private CommandPool[] _primaryPools = null!;
    private CommandBuffer[] _primaryBuffers = null!;

    // Secondary CommandBuffers: Pro [Frame, Worker] eine wachsende Liste für beliebig viele Passes pro Frame
    private CommandPool[,] _workerPools = null!;
    private List<CommandBuffer>[,] _workerPassBuffers = null!;
    private int[] _currentPassIndex = null!;

    /// <summary>Maximale Anzahl vorbereiteter Worker-Command-Pools pro Frame.</summary>
    public int MaxWorkers { get; private set; }

    /// <summary>Anzahl der gleichzeitig in Bearbeitung befindlichen Frames (Ring-Slot-Anzahl).</summary>
    public int FramesInFlight { get; private set; }

    /// <summary>Anzahl der aktuell für das Recording freigeschalteten Worker-Threads.</summary>
    public int ActiveWorkerCount { get; private set; }

    public WorkerCommandPool(VulkanContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Erstellt alle Primary- und Secondary-<see cref="CommandPool"/>-Instanzen (mit <see cref="CommandPoolCreateFlags.TransientBit"/>)
    /// für <paramref name="maxWorkers"/> Threads und <paramref name="framesInFlight"/> Frame-Slots.
    /// </summary>
    public void Initialize(int maxWorkers, int framesInFlight)
    {
        MaxWorkers = Math.Max(1, maxWorkers);
        FramesInFlight = Math.Max(1, framesInFlight);
        ActiveWorkerCount = 1;

        _primaryPools = new CommandPool[FramesInFlight];
        _primaryBuffers = new CommandBuffer[FramesInFlight];
        _workerPools = new CommandPool[FramesInFlight, MaxWorkers];
        _workerPassBuffers = new List<CommandBuffer>[FramesInFlight, MaxWorkers];
        _currentPassIndex = new int[FramesInFlight];

        var vk = _context.Api;
        var device = _context.Device;

        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = _context.GraphicsQueueFamilyIndex,
            Flags = CommandPoolCreateFlags.TransientBit
        };

        for (int frame = 0; frame < FramesInFlight; frame++)
        {
            // 1. Primary Pool & Buffer für den Frame-Rahmen
            vk.CreateCommandPool(device, &poolInfo, null, out _primaryPools[frame]);
            var primaryAlloc = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = _primaryPools[frame],
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1
            };
            vk.AllocateCommandBuffers(device, &primaryAlloc, out _primaryBuffers[frame]);

            // 2. Secondary Pools & Initial-Buffer für Pass 0 anlegen
            for (int worker = 0; worker < MaxWorkers; worker++)
            {
                vk.CreateCommandPool(device, &poolInfo, null, out _workerPools[frame, worker]);
                _workerPassBuffers[frame, worker] = new List<CommandBuffer>(4);
                AllocateSecondaryBufferForWorker(frame, worker);
            }
        }
    }

    private CommandBuffer AllocateSecondaryBufferForWorker(int frame, int worker)
    {
        var secondaryAlloc = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _workerPools[frame, worker],
            Level = CommandBufferLevel.Secondary,
            CommandBufferCount = 1
        };
        _context.Api.AllocateCommandBuffers(_context.Device, &secondaryAlloc, out var cmd);
        _workerPassBuffers[frame, worker].Add(cmd);
        return cmd;
    }

    /// <summary>Setzt die Anzahl der aktiven Worker-Threads (geclampt auf <c>1..MaxWorkers</c>).</summary>
    public void SetActiveWorkers(int count)
    {
        ActiveWorkerCount = Math.Clamp(count, 1, MaxWorkers);
    }

    /// <summary>
    /// Bereitet alle aktiven Secondary CommandBuffers mit Vulkan 1.3 <see cref="CommandBufferInheritanceRenderingInfo"/>
    /// für den nächsten RenderPass vor (kann beliebig oft pro Frame für Offscreen- und SwapChain-Passes aufgerufen werden).
    /// </summary>
    public void PrepareSecondaryBuffersForPass(int frameIndex, Format colorFormat, Format depthFormat)
    {
        var vk = _context.Api;
        bool hasColor = colorFormat != Format.Undefined;
        int passIdx = _currentPassIndex[frameIndex];

        var renderingInheritance = new CommandBufferInheritanceRenderingInfo
        {
            SType = StructureType.CommandBufferInheritanceRenderingInfo,
            ColorAttachmentCount = hasColor ? 1u : 0u,
            PColorAttachmentFormats = hasColor ? &colorFormat : null,
            DepthAttachmentFormat = depthFormat,
            RasterizationSamples = SampleCountFlags.Count1Bit
        };

        var inheritanceInfo = new CommandBufferInheritanceInfo
        {
            SType = StructureType.CommandBufferInheritanceInfo,
            PNext = &renderingInheritance
        };

        var secBeginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit | CommandBufferUsageFlags.RenderPassContinueBit,
            PInheritanceInfo = &inheritanceInfo
        };

        for (int w = 0; w < ActiveWorkerCount; w++)
        {
            var list = _workerPassBuffers[frameIndex, w];
            while (list.Count <= passIdx)
            {
                AllocateSecondaryBufferForWorker(frameIndex, w);
            }

            var secCmd = list[passIdx];
            vk.BeginCommandBuffer(secCmd, &secBeginInfo);
        }
    }

    /// <summary>
    /// Schließt alle aktiven Secondary CommandBuffers des aktuellen RenderPasses, schreibt sie in <paramref name="outBuffers"/>
    /// und erhöht den internen Pass-Index für den nächsten Pass desselben Frames.
    /// </summary>
    public void EndSecondaryBuffersForPass(int frameIndex, Span<CommandBuffer> outBuffers)
    {
        var vk = _context.Api;
        int passIdx = _currentPassIndex[frameIndex];

        for (int w = 0; w < ActiveWorkerCount; w++)
        {
            var secCmd = _workerPassBuffers[frameIndex, w][passIdx];
            vk.EndCommandBuffer(secCmd);
            outBuffers[w] = secCmd;
        }

        // Nächster RenderPass im selben Frame bekommt einen unbenutzten Satz Secondary CommandBuffers!
        _currentPassIndex[frameIndex]++;
    }

    /// <summary>
    /// Setzt den Primary-Pool sowie sämtliche Worker-Pools des <paramref name="frameIndex"/>-Slots in einem Rutsch zurück
    /// (<c>vkResetCommandPool</c> – deutlich schneller als Einzel-Resets) und startet den Primary CommandBuffer.
    /// </summary>
    public CommandBuffer ResetAndBeginFrame(int frameIndex, Format colorAttachmentFormat)
    {
        var vk = _context.Api;
        var device = _context.Device;

        _currentPassIndex[frameIndex] = 0;

        // 1. Primary CommandBuffer zurücksetzen und starten
        vk.ResetCommandPool(device, _primaryPools[frameIndex], CommandPoolResetFlags.None);
        var primaryBegin = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        vk.BeginCommandBuffer(_primaryBuffers[frameIndex], &primaryBegin);

        // 2. Gesamte Worker-Pools für diesen Frame auf einen Schlag zurücksetzen (schneller als Einzel-Resets!)
        for (int worker = 0; worker < MaxWorkers; worker++)
        {
            vk.ResetCommandPool(device, _workerPools[frameIndex, worker], CommandPoolResetFlags.None);
        }
        return _primaryBuffers[frameIndex];
    }

    /// <summary>
    /// Gibt den aktuellen Secondary CommandBuffer für <paramref name="workerIndex"/> im laufenden RenderPass zurück.
    /// Kann von jedem Worker-Thread völlig lock-free aufgerufen werden.
    /// </summary>
    public CommandBuffer GetWorkerSecondaryBuffer(int frameIndex, int workerIndex)
    {
        if ((uint)workerIndex >= (uint)ActiveWorkerCount)
            throw new ArgumentOutOfRangeException(nameof(workerIndex), $"Worker {workerIndex} ist nicht aktiv (Aktiv: {ActiveWorkerCount}).");

        int passIdx = _currentPassIndex[frameIndex];
        return _workerPassBuffers[frameIndex, workerIndex][passIdx];
    }

    /// <summary>Zerstört alle Primary- und Worker-<see cref="CommandPool"/>-Instanzen (gibt alle zugehörigen Buffer automatisch mit frei).</summary>
    public void Dispose()
    {
        if (_primaryPools == null || _context.Device.Handle == 0)
            return;

        var vk = _context.Api;
        var device = _context.Device;

        for (int frame = 0; frame < FramesInFlight; frame++)
        {
            if (_primaryPools[frame].Handle != 0)
                vk.DestroyCommandPool(device, _primaryPools[frame], null);

            for (int worker = 0; worker < MaxWorkers; worker++)
            {
                if (_workerPools[frame, worker].Handle != 0)
                    vk.DestroyCommandPool(device, _workerPools[frame, worker], null);
            }
        }
    }
}
