using System;
using System.Collections.Generic;
using API.Commands.Components;
using API.Descriptors.Components;
using API.Descriptors.Enums;
using API.Enums;
using API.Handles.Components;
using API.Interfaces;
using Silk.NET.Vulkan;
using Vulkan.Core;
using Vulkan.Execution;
using Vulkan.Resource;
using Vulkan.Shaders;

namespace Vulkan;

/// <summary>
/// Haupt-Implementierung von <see cref="IRenderDevice"/> auf Basis von Vulkan 1.3
/// (<c>DynamicRendering</c>, <c>Synchronization2</c>, HLSL->SPIR-V via <c>libshaderc</c>).
/// Unterstützt lock-freies Multithreaded-Command-Recording über <see cref="WorkerCommandPool"/>,
/// generationenbasierte Resource-Pools sowie frame-sichere Deferred-Destruction-Queues.
/// </summary>
public sealed unsafe class VulkanRenderDevice : IRenderDevice
{
    private const uint MaxFramesInFlight = 2;

    private readonly VulkanContext _context = new();
    private readonly VulkanCommandExecutor _executor = new();
    private readonly CrossPlatformShaderCompiler _shaderCompiler = new();

    private readonly VulkanResourcePool<VulkanBuffer> _buffers = new();
    private readonly VulkanResourcePool<VulkanShaderModule> _shaders = new();
    private readonly VulkanResourcePool<VulkanPipeline> _pipelines = new();
    private readonly VulkanResourcePool<VulkanMesh> _meshes = new();
    private readonly VulkanResourcePool<VulkanTexture> _textures = new();

    private VulkanSwapChain? _swapChain;
    private WorkerCommandPool? _workerPool;
    private CommandPool _transferPool;

    private CommandBuffer _activePrimaryCmd;
    private Extent2D _activePassExtent;
    private Format _activeColorFormat;
    private Format _activeDepthFormat;
    private VulkanTexture? _activeOffscreenColor;
    private VulkanTexture? _activeOffscreenDepth;
    private bool _isSwapChainPass;

    /// <inheritdoc />
    public string BackendName => "Vulkan 1.3";

    /// <inheritdoc />
    public uint MaxWorkerThreads { get; private set; }

    /// <inheritdoc />
    public uint FramesInFlight => MaxFramesInFlight;

    /// <inheritdoc />
    public uint CurrentFrameIndex => (uint)(_swapChain?.CurrentFrameIndex ?? 0);

    // Pro Frame-in-Flight eine Deletion-Queue, damit Ressourcen erst nach Ablauf ihres Frame-Fences zerstört werden
    private List<Action>[] _deletionQueues = [];

    /// <summary>
    /// Initialisiert den Vulkan-1.3-Kontext, die Worker-Command-Pools, den Transfer-Pool,
    /// die Deferred-Destruction-Queues sowie die Fenster-SwapChain.
    /// </summary>
    public void Initialize(in RStDeviceDesc deviceDesc, in RStSwapChainDesc swapChainDesc)
    {
        MaxWorkerThreads = (uint)Math.Max(1, deviceDesc.MaxWorkerThreads);

        _context.Initialize(
            deviceDesc.ApplicationName,
            deviceDesc.EnableDebugLayer,
            deviceDesc.RequiredInstanceExtensions ?? ReadOnlySpan<nint>.Empty);

        _workerPool = new WorkerCommandPool(_context);
        _workerPool.Initialize((int)MaxWorkerThreads, (int)MaxFramesInFlight);

        _deletionQueues = new List<Action>[FramesInFlight];
        for (int i = 0; i < FramesInFlight; i++)
            _deletionQueues[i] = new List<Action>();

        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = _context.GraphicsQueueFamilyIndex,
            Flags = CommandPoolCreateFlags.TransientBit
        };

        if (_context.Api.CreateCommandPool(_context.Device, &poolInfo, null, out _transferPool) != Result.Success)
            throw new InvalidOperationException("Transfer-CommandPool konnte nicht erstellt werden.");

        _swapChain = new VulkanSwapChain(_context);
        _swapChain.Initialize(swapChainDesc, (int)MaxFramesInFlight);
    }

    /// <summary>Legt die Anzahl der aktiven Worker-Threads für das parallele Command-Recording fest.</summary>
    public void SetActiveWorkerThreads(int workerCount) => _workerPool?.SetActiveWorkers(workerCount);

    // --- Ressourcen (Direkter VRAM-Upload) ---

    /// <inheritdoc />
    public RStBufferHandle CreateBuffer(in RStBufferDesc desc, ReadOnlySpan<byte> initialData = default)
    {
        var buffer = new VulkanBuffer(_context);
        buffer.Initialize(desc, initialData, _transferPool, (int)MaxFramesInFlight);
        var (index, gen) = _buffers.Add(buffer);
        return new RStBufferHandle(index, gen);
    }

    /// <inheritdoc />
    public void UpdateBuffer(RStBufferHandle handle, ReadOnlySpan<byte> data, uint offsetInBytes = 0)
    {
        var buffer = _buffers.Get(handle.Index, handle.Generation);
        buffer.SetFrameSlot(CurrentFrameIndex);
        buffer.Update(data, offsetInBytes, _transferPool);
    }

    /// <inheritdoc />
    public void DestroyBuffer(RStBufferHandle handle)
    {
        var buffer = _buffers.Detach(handle.Index, (uint)handle.Generation);
        if (buffer != null)
        {
            _deletionQueues[CurrentFrameIndex].Add(() => buffer.Dispose());
        }
    }

    /// <inheritdoc />
    public RStMeshHandle CreateMesh(
        ReadOnlySpan<byte> vertexData, uint vertexStride,
        ReadOnlySpan<byte> indexData, EIndexFormat indexFormat,
        string? debugName = null)
    {
        var mesh = new VulkanMesh(_context);
        mesh.Initialize(vertexData, vertexStride, indexData, indexFormat, _transferPool);
        var (index, gen) = _meshes.Add(mesh);
        return new RStMeshHandle(index, gen);
    }

    /// <inheritdoc />
    public void DestroyMesh(RStMeshHandle handle)
    {
        var mesh = _meshes.Detach(handle.Index, (uint)handle.Generation);
        if (mesh != null)
        {
            _deletionQueues[CurrentFrameIndex].Add(() => mesh.Dispose());
        }
    }

    /// <inheritdoc />
    public RStTextureHandle CreateTexture(in RStTextureDesc desc, ReadOnlySpan<byte> initialData = default)
    {
        var texture = new VulkanTexture(_context);
        texture.Initialize(desc, initialData, _transferPool);
        var (index, gen) = _textures.Add(texture);
        return new RStTextureHandle(index, gen);
    }

    /// <inheritdoc />
    public void DestroyTexture(RStTextureHandle handle)
    {
        var texture = _textures.Detach(handle.Index, (uint)handle.Generation);
        if (texture != null)
        {
            _deletionQueues[CurrentFrameIndex].Add(() => texture.Dispose());
        }
    }

    // --- Shader & Pipelines ---

    /// <inheritdoc />
    public RStShaderHandle CreateShader(in RStShaderDesc desc)
    {
        byte[] vertSpirv = _shaderCompiler.CompileStage(
            desc.VertexSource, Silk.NET.Shaderc.ShaderKind.VertexShader, desc.VertexEntryPoint, EShaderLanguage.HLSL,
            "vertex");
        byte[] fragSpirv = _shaderCompiler.CompileStage(
            desc.FragmentSource, Silk.NET.Shaderc.ShaderKind.FragmentShader, desc.FragmentEntryPoint,
            EShaderLanguage.HLSL, "fragment");

        return CreateShaderFromBytecode(vertSpirv, fragSpirv, desc.VertexEntryPoint, desc.FragmentEntryPoint,
            desc.DebugName);
    }

    /// <inheritdoc />
    public RStShaderHandle CreateShaderFromBytecode(
        ReadOnlySpan<byte> vertexBytecode,
        ReadOnlySpan<byte> fragmentBytecode,
        string vertexEntryPoint = "VSMain",
        string fragmentEntryPoint = "PSMain",
        string? debugName = null)
    {
        var module = new VulkanShaderModule(_context);
        module.Initialize(vertexBytecode.ToArray(), fragmentBytecode.ToArray(), vertexEntryPoint, fragmentEntryPoint);
        var (index, gen) = _shaders.Add(module);
        return new RStShaderHandle(index, gen);
    }

    /// <inheritdoc />
    public void DestroyShader(RStShaderHandle handle)
    {
        var shader = _shaders.Detach(handle.Index, (uint)handle.Generation);
        if (shader != null)
        {
            _deletionQueues[CurrentFrameIndex].Add(() => shader.Dispose());
        }
    }

    /// <inheritdoc />
    public RStPipelineHandle CreatePipeline(in RStPipelineDesc desc)
    {
        var shader = _shaders.Get(desc.Shader.Index, desc.Shader.Generation);
        var pipeline = new VulkanPipeline(_context);
        pipeline.Initialize(desc, shader);
        var (index, gen) = _pipelines.Add(pipeline);
        return new RStPipelineHandle(index, gen);
    }

    /// <inheritdoc />
    public void DestroyPipeline(RStPipelineHandle handle)
    {
        var pipeline = _pipelines.Detach(handle.Index, (uint)handle.Generation);
        if (pipeline != null)
        {
            _deletionQueues[CurrentFrameIndex].Add(() => pipeline.Dispose());
        }
    }

    // --- Frame & RenderPass Execution ---

    /// <inheritdoc />
    public void Resize(uint width, uint height, bool vsync)
    {
        if (width == 0 || height == 0 || _swapChain == null) return;
        _swapChain.Resize((int)width, (int)height, vsync: vsync);
    }

    /// <inheritdoc />
    public bool BeginFrame()
    {
        if (_swapChain == null || _workerPool == null)
            return false;

        // Wartet intern auf den In-Flight-Fence für diesen Frame-Slot
        _swapChain.BeginFrame();

        // Jetzt ist garantiert, dass die GPU diesen Frame-Slot fertig abgearbeitet hat:
        // verzögerte Ressourcen-Freigaben sicher abarbeiten
        var queue = _deletionQueues[CurrentFrameIndex];
        for (int i = 0; i < queue.Count; i++)
        {
            queue[i].Invoke();
        }

        queue.Clear();

        _activePrimaryCmd = _workerPool.ResetAndBeginFrame(_swapChain.CurrentFrameIndex, _swapChain.Format);
        return true;
    }

    /// <inheritdoc />
    public void BeginRenderPass(in RStRenderPassDesc passDesc)
    {
        if (_swapChain == null || _workerPool == null)
            return;

        bool hasColorTarget = passDesc.ColorAttachment.Target.IsValid;
        bool hasDepthTarget = passDesc.DepthAttachment.Target.IsValid;
        _isSwapChainPass = !hasColorTarget && !hasDepthTarget;
        ImageView colorView = default;
        ImageView depthView = default;
        bool hasColor = false;
        bool hasDepth = false;
        if (_isSwapChainPass)
        {
            _activeOffscreenColor = null;
            _activeOffscreenDepth = null;
            _activePassExtent = _swapChain.Extent;
            _activeColorFormat = _swapChain.Format;
            _activeDepthFormat = Format.D32Sfloat;
            _swapChain.TransitionBackbufferToRender(_activePrimaryCmd);
            _swapChain.TransitionDepthToAttachment(_activePrimaryCmd);
            colorView = _swapChain.ImageViews[_swapChain.CurrentImageIndex];
            depthView = _swapChain.DepthImageView;
            hasColor = true;
            hasDepth = true;
        }
        else
        {
            if (hasColorTarget)
            {
                _activeOffscreenColor = _textures.Get(passDesc.ColorAttachment.Target.Index,
                    passDesc.ColorAttachment.Target.Generation);
                _activeOffscreenColor.TransitionLayout(_activePrimaryCmd, ImageLayout.ColorAttachmentOptimal);
                _activeColorFormat = _activeOffscreenColor.VkFormat;
                colorView = _activeOffscreenColor.View;
                hasColor = true;
            }
            else
            {
                _activeOffscreenColor = null;
                _activeColorFormat = Format.Undefined;
            }

            if (hasDepthTarget)
            {
                _activeOffscreenDepth = _textures.Get(passDesc.DepthAttachment.Target.Index,
                    passDesc.DepthAttachment.Target.Generation);
                _activeOffscreenDepth.TransitionLayout(_activePrimaryCmd, ImageLayout.DepthStencilAttachmentOptimal);
                _activeDepthFormat = _activeOffscreenDepth.VkFormat;
                depthView = _activeOffscreenDepth.View;
                hasDepth = true;
            }
            else
            {
                _activeOffscreenDepth = null;
                _activeDepthFormat = Format.Undefined;
            }

            _activePassExtent = passDesc.RenderWidth > 0 && passDesc.RenderHeight > 0
                ? new Extent2D(passDesc.RenderWidth, passDesc.RenderHeight)
                : (_activeOffscreenColor?.Extent ?? _activeOffscreenDepth!.Extent);
        }

// Secondary CommandBuffers aller aktiven Worker für diesen Pass vorbereiten
        _workerPool.PrepareSecondaryBuffersForPass(
            _swapChain.CurrentFrameIndex,
            _activeColorFormat,
            _activeDepthFormat);
        var c = passDesc.ColorAttachment.ClearColor;
        var colorAttachment = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = colorView,
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = passDesc.ColorAttachment.LoadOp switch
            {
                ELoadOp.Load => AttachmentLoadOp.Load,
                ELoadOp.DontCare => AttachmentLoadOp.DontCare,
                _ => AttachmentLoadOp.Clear
            },
            StoreOp = AttachmentStoreOp.Store,
            ClearValue = new ClearValue { Color = new ClearColorValue(c.X, c.Y, c.Z, c.W) }
        };
        var depthAttachment = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = depthView,
            ImageLayout = ImageLayout.DepthStencilAttachmentOptimal,
            LoadOp = passDesc.DepthAttachment.LoadOp switch
            {
                ELoadOp.Load => AttachmentLoadOp.Load,
                ELoadOp.DontCare => AttachmentLoadOp.DontCare,
                _ => AttachmentLoadOp.Clear
            },
            StoreOp = hasDepth && !_isSwapChainPass ? AttachmentStoreOp.Store : AttachmentStoreOp.DontCare,
            ClearValue = new ClearValue
            {
                DepthStencil = new ClearDepthStencilValue(passDesc.DepthAttachment.ClearDepth,
                    passDesc.DepthAttachment.ClearStencil)
            }
        };
        var renderingInfo = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            Flags = RenderingFlags.ContentsSecondaryCommandBuffersBit,
            RenderArea = new Rect2D(new Offset2D(0, 0), _activePassExtent),
            LayerCount = 1,
            ColorAttachmentCount = hasColor ? 1u : 0u,
            PColorAttachments = hasColor ? &colorAttachment : null,
            PDepthAttachment = hasDepth ? &depthAttachment : null
        };
        _context.Api.CmdBeginRendering(_activePrimaryCmd, &renderingInfo);
    }

    /// 
    public void ExecuteCommandSlice(uint workerIndex, ReadOnlySpan<StRenderCommand> commands)
    {
        if (_swapChain == null || _workerPool == null)
            return;
        var secCmd = _workerPool.GetWorkerSecondaryBuffer(_swapChain.CurrentFrameIndex, (int)workerIndex);
        _executor.RecordSlice(_context, secCmd, _activePassExtent, _pipelines, _buffers, commands, _meshes, _textures);
    }

    /// 
    public void EndRenderPass()
    {
        if (_swapChain == null || _workerPool == null)
            return;
        int frameIndex = _swapChain.CurrentFrameIndex;
        int activeCount = _workerPool.ActiveWorkerCount;
        Span<CommandBuffer> secondaryBuffers = stackalloc CommandBuffer[activeCount];
        _workerPool.EndSecondaryBuffersForPass(frameIndex, secondaryBuffers);
        fixed (CommandBuffer* pSecondary = secondaryBuffers)
        {
            _context.Api.CmdExecuteCommands(_activePrimaryCmd, (uint)activeCount, pSecondary);
        }

        _context.Api.CmdEndRendering(_activePrimaryCmd);
// Offscreen-Targets nur dann direkt wieder in ShaderReadOnlyOptimal überführen,
// wenn sie auch tatsächlich mit SampledBit erstellt wurden
        if (!_isSwapChainPass)
        {
            if (_activeOffscreenColor is { CanSample: true })
                _activeOffscreenColor.TransitionLayout(_activePrimaryCmd, ImageLayout.ShaderReadOnlyOptimal);
            if (_activeOffscreenDepth is { CanSample: true })
                _activeOffscreenDepth.TransitionLayout(_activePrimaryCmd, ImageLayout.ShaderReadOnlyOptimal);
        }
    }

    /// 
    public void EndFrame()
    {
        if (_swapChain == null)
            return;
        _swapChain.TransitionBackbufferToPresent(_activePrimaryCmd);
        _context.Api.EndCommandBuffer(_activePrimaryCmd);
        Span<CommandBuffer> submitBuffer = stackalloc CommandBuffer[1] { _activePrimaryCmd };
        _swapChain.SubmitAndPresent(submitBuffer);
    }

    /// 
    public void WaitIdle()
    {
        if (_context.Device.Handle != 0)
            _context.Api.DeviceWaitIdle(_context.Device);
    }

    /// 
    /// Wartet auf den Abschluss aller GPU-Befehle, leert alle Deletion-Queues und gibt
    /// SwapChain, Command-Pools, Resource-Pools, Shader-Compiler sowie den Vulkan-Kontext frei.
    /// 
    public void Dispose()
    {
        WaitIdle();
        _swapChain?.Dispose();
        _workerPool?.Dispose();
        _buffers.DisposeAll();
        _pipelines.DisposeAll();
        _shaders.DisposeAll();
        _shaderCompiler.Dispose();
        _meshes.DisposeAll();
        _textures.DisposeAll();
        if (_transferPool.Handle != 0 && _context.Device.Handle != 0)
        {
            _context.Api.DestroyCommandPool(_context.Device, _transferPool, null);
            _transferPool = default;
        }

        foreach (var queue in _deletionQueues)
        {
            foreach (var action in queue)
                action();
            queue.Clear();
        }

        _context.Dispose();
    }
}