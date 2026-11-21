using System;
using API.Descriptors.Components;
using API.Descriptors.Enums;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Vulkan.Core;

/// <summary>
/// Verwaltet die Vulkan-SwapChain inklusive nativer Fenster-Surface (Win32, X11, Wayland),
/// Backbuffer-ImageViews, automatischem <c>D32_Sfloat</c>-Depth-Buffer, Vulkan 1.3 <c>Synchronization2</c>-Barriers
/// sowie Frame-in-Flight-Synchronisation (Fences &amp; Semaphoren).
/// </summary>
internal sealed unsafe class VulkanSwapChain : IDisposable
{
    private readonly VulkanContext _context;

    /// <summary>Native Fenster-Surface (<c>VkSurfaceKHR</c>).</summary>
    public SurfaceKHR Surface { get; private set; }

    /// <summary>Aktives SwapChain-Handle (<c>VkSwapchainKHR</c>).</summary>
    public SwapchainKHR SwapChain { get; private set; }

    /// <summary>Pixelformat der SwapChain-Backbuffer (aktuell <see cref="Format.B8G8R8A8Srgb"/>).</summary>
    public Format Format { get; private set; }

    /// <summary>Aktuelle Auflösung der SwapChain in Pixeln.</summary>
    public Extent2D Extent { get; private set; }

    /// <summary>Array aller von der SwapChain bereitgestellten Backbuffer-Images.</summary>
    public Image[] Images { get; private set; } = Array.Empty<Image>();

    /// <summary>Zugehörige ImageViews für alle Backbuffer-Images.</summary>
    public ImageView[] ImageViews { get; private set; } = Array.Empty<ImageView>();

    private Silk.NET.Vulkan.Semaphore[] _imageAvailableSemaphores = Array.Empty<Silk.NET.Vulkan.Semaphore>();
    private Silk.NET.Vulkan.Semaphore[] _renderFinishedSemaphores = Array.Empty<Silk.NET.Vulkan.Semaphore>();
    private Fence[] _inFlightFences = Array.Empty<Fence>();

    private int _framesInFlight;

    /// <summary>Aktueller Frame-in-Flight-Index (<c>0</c> bis <c>_framesInFlight - 1</c>).</summary>
    public int CurrentFrameIndex { get; private set; }

    /// <summary>Index des aktuell von <c>vkAcquireNextImageKHR</c> zugewiesenen SwapChain-Images.</summary>
    public uint CurrentImageIndex { get; private set; }

    /// <summary>Standard-Depth-Buffer-Image der SwapChain.</summary>
    public Image DepthImage { get; private set; }

    /// <summary>Zugehöriger <c>DEVICE_LOCAL</c>-VRAM-Speicher für den SwapChain-Depth-Buffer.</summary>
    public DeviceMemory DepthImageMemory { get; private set; }

    /// <summary>ImageView für den SwapChain-Depth-Buffer.</summary>
    public ImageView DepthImageView { get; private set; }

    /// <summary>Pixelformat des SwapChain-Depth-Buffers (<see cref="Format.D32Sfloat"/>).</summary>
    public Format DepthFormat => Format.D32Sfloat;

    public VulkanSwapChain(VulkanContext context)
    {
        _context = context;
    }

    private SurfaceKHR CreateNativeSurface(RStNativeWindow window)
    {
        var instance = _context.Instance;
        var vk = _context.Api;

        switch (window.Kind)
        {
            case ENativeWindowKind.Win32:
            {
                if (!vk.TryGetInstanceExtension(instance, out KhrWin32Surface win32Ext))
                    throw new PlatformNotSupportedException("VK_KHR_win32_surface Extension nicht aktiv.");

                var createInfo = new Win32SurfaceCreateInfoKHR
                {
                    SType = StructureType.Win32SurfaceCreateInfoKhr,
                    Hwnd = window.WindowHandle,
                    Hinstance = window.DisplayHandle != 0
                        ? window.DisplayHandle
                        : System.Diagnostics.Process.GetCurrentProcess().Handle
                };

                if (win32Ext.CreateWin32Surface(instance, &createInfo, null, out var surface) != Result.Success)
                    throw new InvalidOperationException("Win32 VkSurfaceKHR konnte nicht erstellt werden.");
                return surface;
            }

            case ENativeWindowKind.X11:
            {
                if (!vk.TryGetInstanceExtension(instance, out KhrXlibSurface xlibExt))
                    throw new PlatformNotSupportedException("VK_KHR_xlib_surface Extension nicht aktiv.");

                var createInfo = new XlibSurfaceCreateInfoKHR
                {
                    SType = StructureType.XlibSurfaceCreateInfoKhr,
                    Dpy = (nint*)window.DisplayHandle,
                    Window = (nint)window.WindowHandle
                };

                if (xlibExt.CreateXlibSurface(instance, &createInfo, null, out var surface) != Result.Success)
                    throw new InvalidOperationException("X11 VkSurfaceKHR konnte nicht erstellt werden.");
                return surface;
            }

            case ENativeWindowKind.Wayland:
            {
                if (!vk.TryGetInstanceExtension(instance, out KhrWaylandSurface waylandExt))
                    throw new PlatformNotSupportedException("VK_KHR_wayland_surface Extension nicht aktiv.");

                var createInfo = new WaylandSurfaceCreateInfoKHR
                {
                    SType = StructureType.WaylandSurfaceCreateInfoKhr,
                    Display = (nint*)window.DisplayHandle,
                    Surface = (nint*)window.WindowHandle
                };

                if (waylandExt.CreateWaylandSurface(instance, &createInfo, null, out var surface) != Result.Success)
                    throw new InvalidOperationException("Wayland VkSurfaceKHR konnte nicht erstellt werden.");
                return surface;
            }

            default:
                throw new NotSupportedException($"Plattform {window.Kind} wird aktuell nicht unterstützt.");
        }
    }

    private void CreateDepthResources()
    {
        var vk = _context.Api;
        var device = _context.Device;

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Extent = new Extent3D(Extent.Width, Extent.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Format = DepthFormat,
            Tiling = ImageTiling.Optimal,
            InitialLayout = ImageLayout.Undefined,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit,
            Samples = SampleCountFlags.Count1Bit,
            SharingMode = SharingMode.Exclusive
        };

        vk.CreateImage(device, &imageInfo, null, out var depthImage);
        DepthImage = depthImage;

        vk.GetImageMemoryRequirements(device, DepthImage, out var memReq);
        uint memType =
            VulkanMemoryHelper.FindMemoryType(_context, memReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit);

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReq.Size,
            MemoryTypeIndex = memType
        };

        vk.AllocateMemory(device, &allocInfo, null, out var depthMem);
        DepthImageMemory = depthMem;
        vk.BindImageMemory(device, DepthImage, DepthImageMemory, 0);

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = DepthImage,
            ViewType = ImageViewType.Type2D,
            Format = DepthFormat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, 1)
        };

        vk.CreateImageView(device, &viewInfo, null, out var depthView);
        DepthImageView = depthView;
    }

    /// <summary>
    /// Erstellt die native Fenster-Surface, die SwapChain inkl. Backbuffer-Views und Depth-Buffer
    /// sowie alle benötigten Synchronisationsobjekte für <paramref name="framesInFlight"/> parallele Frames.
    /// </summary>
    public void Initialize(in RStSwapChainDesc desc, int framesInFlight)
    {
        _framesInFlight = framesInFlight;
        Surface = CreateNativeSurface(desc.Window);

        CreateSwapChainAndViews(desc.Width, desc.Height, desc.VSync);
        CreateSyncObjects();
    }

    private void CreateSwapChainAndViews(int width, int height, bool vsync)
    {
        var khrSurface = _context.KhrSurface ?? throw new InvalidOperationException("KHR_surface nicht geladen.");
        var khrSwapchain = _context.KhrSwapchain ?? throw new InvalidOperationException("KHR_swapchain nicht geladen.");

        khrSurface.GetPhysicalDeviceSurfaceCapabilities(_context.PhysicalDevice, Surface, out var capabilities);

        Format = Format.B8G8R8A8Srgb;
        Extent = new Extent2D(
            Math.Clamp((uint)width, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width),
            Math.Clamp((uint)height, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height));

        uint imageCount = capabilities.MinImageCount + 1;
        if (capabilities.MaxImageCount > 0 && imageCount > capabilities.MaxImageCount)
            imageCount = capabilities.MaxImageCount;

        var createInfo = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = Surface,
            MinImageCount = imageCount,
            ImageFormat = Format,
            ImageColorSpace = ColorSpaceKHR.SpaceSrgbNonlinearKhr,
            ImageExtent = Extent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferDstBit,
            ImageSharingMode = SharingMode.Exclusive,
            PreTransform = capabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = vsync ? PresentModeKHR.FifoKhr : PresentModeKHR.ImmediateKhr,
            Clipped = true
        };

        if (khrSwapchain.CreateSwapchain(_context.Device, &createInfo, null, out var swapchain) != Result.Success)
            throw new InvalidOperationException("SwapChain konnte nicht erstellt werden.");

        SwapChain = swapchain;

        // SwapChain Images abrufen
        uint actualCount = 0;
        khrSwapchain.GetSwapchainImages(_context.Device, SwapChain, &actualCount, null);
        Images = new Image[actualCount];
        fixed (Image* pImages = Images)
            khrSwapchain.GetSwapchainImages(_context.Device, SwapChain, &actualCount, pImages);
// ImageViews erstellen
        ImageViews = new ImageView[actualCount];
        for (int i = 0; i < actualCount; i++)
        {
            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = Images[i],
                ViewType = ImageViewType.Type2D,
                Format = Format,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
            };
            if (_context.Api.CreateImageView(_context.Device, &viewInfo, null, out var view) != Result.Success)
                throw new InvalidOperationException($"ImageView {i} konnte nicht erstellt werden.");
            ImageViews[i] = view;
        }

        CreateDepthResources();
    }

    private void CreateSyncObjects()
    {
        var vk = _context.Api;
        var device = _context.Device;
        _imageAvailableSemaphores = new Silk.NET.Vulkan.Semaphore[_framesInFlight];
        _renderFinishedSemaphores = new Silk.NET.Vulkan.Semaphore[Images.Length];
        _inFlightFences = new Fence[_framesInFlight];
        var semInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        var fenceInfo = new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit
        };
// 1. Objekte pro Frame-in-Flight erstellen
        for (int i = 0; i < _framesInFlight; i++)
        {
            vk.CreateSemaphore(device, &semInfo, null, out _imageAvailableSemaphores[i]);
            vk.CreateFence(device, &fenceInfo, null, out _inFlightFences[i]);
        }

// 2. RenderFinished-Semaphoren pro SwapChain-Image erstellen (verhindert Semaphore-Reuse-Konflikte)
        for (int i = 0; i < Images.Length; i++)
        {
            vk.CreateSemaphore(device, &semInfo, null, out _renderFinishedSemaphores[i]);
        }
    }

    /// 
    /// Wartet auf den Fence des aktuellen Frame-Slots und fordert das nächste freie Backbuffer-Image von der SwapChain an.
    /// 
    public void BeginFrame()
    {
        var vk = _context.Api;
        var device = _context.Device;
        var fence = _inFlightFences[CurrentFrameIndex];
// 1. Auf GPU warten, bis dieser Frame-In-Flight-Slot wieder frei ist
        vk.WaitForFences(device, 1, &fence, true, ulong.MaxValue);
        vk.ResetFences(device, 1, &fence);
// 2. Nächstes Backbuffer-Image von der SwapChain holen
        uint imageIndex = 0;
        _context.KhrSwapchain!.AcquireNextImage(
            device,
            SwapChain,
            ulong.MaxValue,
            _imageAvailableSemaphores[CurrentFrameIndex],
            default,
            &imageIndex);
        CurrentImageIndex = imageIndex;
    }

    /// 
    /// Überführt das aktuelle Backbuffer-Image via Vulkan 1.3 PipelineBarrier2 in das Layout .
    /// 
    public void TransitionBackbufferToRender(CommandBuffer cmd)
    {
        var barrier = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = PipelineStageFlags2.TopOfPipeBit,
            SrcAccessMask = AccessFlags2.None,
            DstStageMask = PipelineStageFlags2.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlags2.ColorAttachmentWriteBit,
            OldLayout = ImageLayout.Undefined,
            NewLayout = ImageLayout.ColorAttachmentOptimal,
            Image = Images[CurrentImageIndex],
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        var depInfo = new DependencyInfo
        {
            SType = StructureType.DependencyInfo,
            ImageMemoryBarrierCount = 1,
            PImageMemoryBarriers = &barrier
        };
        _context.Api.CmdPipelineBarrier2(cmd, &depInfo);
    }

    /// 
    /// Überführt das aktuelle Backbuffer-Image nach dem Rendering in das Präsentations-Layout .
    /// 
    public void TransitionBackbufferToPresent(CommandBuffer cmd)
    {
        var barrier = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = PipelineStageFlags2.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlags2.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlags2.None,
            DstAccessMask = AccessFlags2.None,
            OldLayout = ImageLayout.ColorAttachmentOptimal,
            NewLayout = ImageLayout.PresentSrcKhr,
            Image = Images[CurrentImageIndex],
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        var depInfo = new DependencyInfo
        {
            SType = StructureType.DependencyInfo,
            ImageMemoryBarrierCount = 1,
            PImageMemoryBarriers = &barrier
        };
        _context.Api.CmdPipelineBarrier2(cmd, &depInfo);
    }

    /// 
    /// Überführt den SwapChain-Depth-Buffer in das Layout .
    /// 
    public void TransitionDepthToAttachment(CommandBuffer cmd)
    {
        if (DepthImage.Handle == 0)
            return;
        var barrier = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = PipelineStageFlags2.TopOfPipeBit,
            SrcAccessMask = AccessFlags2.None,
            DstStageMask = PipelineStageFlags2.EarlyFragmentTestsBit,
            DstAccessMask = AccessFlags2.DepthStencilAttachmentReadBit | AccessFlags2.DepthStencilAttachmentWriteBit,
            OldLayout = ImageLayout.Undefined,
            NewLayout = ImageLayout.DepthStencilAttachmentOptimal,
            Image = DepthImage,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, 1)
        };
        var depInfo = new DependencyInfo
        {
            SType = StructureType.DependencyInfo,
            ImageMemoryBarrierCount = 1,
            PImageMemoryBarriers = &barrier
        };
        _context.Api.CmdPipelineBarrier2(cmd, &depInfo);
    }

    /// 
    /// Schickt alle aufgezeichneten CommandBuffer an die ,
    /// präsentiert das fertige Bild auf dem Bildschirm und schaltet  weiter.
    /// 
    public void SubmitAndPresent(ReadOnlySpan<CommandBuffer> activeWorkerBuffers)
    {
        var vk = _context.Api;
        var waitSem = _imageAvailableSemaphores[CurrentFrameIndex];
        var signalSem = _renderFinishedSemaphores[CurrentImageIndex];
        var waitStage = PipelineStageFlags.ColorAttachmentOutputBit;
        fixed (CommandBuffer* pBuffers = activeWorkerBuffers)
        {
            var submitInfo = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                WaitSemaphoreCount = 1,
                PWaitSemaphores = &waitSem,
                PWaitDstStageMask = &waitStage,
                CommandBufferCount = (uint)activeWorkerBuffers.Length,
                PCommandBuffers = pBuffers,
                SignalSemaphoreCount = 1,
                PSignalSemaphores = &signalSem
            };
            vk.QueueSubmit(_context.GraphicsQueue, 1, &submitInfo, _inFlightFences[CurrentFrameIndex]);
        }

        var swapchain = SwapChain;
        uint imageIndex = CurrentImageIndex;
        var presentInfo = new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &signalSem,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &imageIndex
        };
        _context.KhrSwapchain!.QueuePresent(_context.GraphicsQueue, &presentInfo);
        CurrentFrameIndex = (CurrentFrameIndex + 1) % _framesInFlight;
    }

    /// 
    /// Baut SwapChain, ImageViews und Depth-Buffer nach einem Fenster-Resize mit neuer Auflösung und VSync-Einstellung neu auf.
    /// 
    public void Resize(int width, int height, bool vsync)
    {
        if (width <= 0 || height <= 0)
            return;
        _context.Api.DeviceWaitIdle(_context.Device);
        DestroySwapChainResources();
        CreateSwapChainAndViews(width, height, vsync);
    }

    private void DestroySwapChainResources()
    {
        var vk = _context.Api;
        foreach (var view in ImageViews)
        {
            if (view.Handle != 0) vk.DestroyImageView(_context.Device, view, null);
        }

        ImageViews = Array.Empty<ImageView>();
        if (SwapChain.Handle != 0)
        {
            _context.KhrSwapchain!.DestroySwapchain(_context.Device, SwapChain, null);
            SwapChain = default;
        }

        vk.DestroyImageView(_context.Device, DepthImageView, null);
        vk.DestroyImage(_context.Device, DepthImage, null);
        vk.FreeMemory(_context.Device, DepthImageMemory, null);
    }

    /// 
    /// Gibt alle SwapChain-, Depth-, Synchronisations- und Surface-Ressourcen deterministisch frei.
    /// 
    public void Dispose()
    {
        if (_context.Device.Handle == 0)
            return;
        _context.Api.DeviceWaitIdle(_context.Device);
        DestroySwapChainResources();
        var vk = _context.Api;
        for (int i = 0; i < _imageAvailableSemaphores.Length; i++)
        {
            if (_imageAvailableSemaphores[i].Handle != 0)
                vk.DestroySemaphore(_context.Device, _imageAvailableSemaphores[i], null);
        }

        for (int i = 0; i < _renderFinishedSemaphores.Length; i++)
        {
            if (_renderFinishedSemaphores[i].Handle != 0)
                vk.DestroySemaphore(_context.Device, _renderFinishedSemaphores[i], null);
        }

        for (int i = 0; i < _inFlightFences.Length; i++)
        {
            if (_inFlightFences[i].Handle != 0)
                vk.DestroyFence(_context.Device, _inFlightFences[i], null);
        }

        if (Surface.Handle != 0)
        {
            _context.KhrSurface!.DestroySurface(_context.Instance, Surface, null);
            Surface = default;
        }
    }
}