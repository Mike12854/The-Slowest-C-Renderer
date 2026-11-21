using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Vulkan.Core;

/// <summary>
/// Verwaltet den grundlegenden Vulkan-1.3-Kontext: <see cref="Instance"/>, optionalen Validation-Debug-Messenger,
/// GPU-Auswahl (<see cref="PhysicalDevice"/>, bevorzugt dedizierte GPUs), <see cref="Device"/>
/// inkl. <c>DynamicRendering</c> &amp; <c>Synchronization2</c> sowie die <see cref="GraphicsQueue"/>.
/// </summary>
public sealed unsafe class VulkanContext : IDisposable
{
    /// <summary>Globale Silk.NET Vulkan-API-Instanz.</summary>
    public Vk Api { get; private set; } = null!;

    /// <summary>Native Vulkan-Instanz.</summary>
    public Instance Instance { get; private set; }

    /// <summary>Ausgewählte physische Grafikkarte (bevorzugt <see cref="PhysicalDeviceType.DiscreteGpu"/>).</summary>
    public PhysicalDevice PhysicalDevice { get; private set; }

    /// <summary>Logisches Vulkan-1.3-Device mit aktivem <c>DynamicRendering</c>, <c>Synchronization2</c> und <c>SamplerAnisotropy</c>.</summary>
    public Device Device { get; private set; }

    /// <summary>Primäre Graphics-Queue für Command-Buffer-Submissions und SwapChain-Present.</summary>
    public Queue GraphicsQueue { get; private set; }

    /// <summary>Index der ausgewählten Graphics-Queue-Family.</summary>
    public uint GraphicsQueueFamilyIndex { get; private set; }

    /// <summary>Geladene <c>VK_KHR_surface</c>-Extension für plattformübergreifende Fenster-Surfaces.</summary>
    public KhrSurface? KhrSurface { get; private set; }

    /// <summary>Geladene <c>VK_KHR_swapchain</c>-Extension für Backbuffer-Präsentation.</summary>
    public KhrSwapchain? KhrSwapchain { get; private set; }

    private ExtDebugUtils? _debugUtils;
    private DebugUtilsMessengerEXT _debugMessenger;

    /// <summary>
    /// Initialisiert Vulkan 1.3, erstellt Instanz und Validation-Layer, wählt die beste verfügbare GPU
    /// und richtet das logische Device inklusive Graphics-Queue ein.
    /// </summary>
    public void Initialize(string appName, bool enableValidation, ReadOnlySpan<nint> requiredInstanceExtensions)
    {
        Api = Vk.GetApi();

        CreateInstance(appName, enableValidation, requiredInstanceExtensions);
        SetupDebugMessenger(enableValidation);
        SelectPhysicalDevice();
        CreateLogicalDevice();

        var extensionList = new List<nint>();
        if (!requiredInstanceExtensions.IsEmpty)
        {
            foreach (var ext in requiredInstanceExtensions)
                extensionList.Add(ext);
        }
        else
        {
            // Automatische Plattform-Erkennung für Surface-Extensions
            extensionList.Add((nint)SilkMarshal.StringToPtr(KhrSurface.ExtensionName));
            if (OperatingSystem.IsWindows())
                extensionList.Add((nint)SilkMarshal.StringToPtr("VK_KHR_win32_surface"));
            else if (OperatingSystem.IsLinux())
            {
                extensionList.Add((nint)SilkMarshal.StringToPtr("VK_KHR_xlib_surface"));
                extensionList.Add((nint)SilkMarshal.StringToPtr("VK_KHR_wayland_surface"));
            }
        }
    }

    /// <summary>
    /// Sucht einen passenden GPU-Speichertyp-Index anhand der vom Treiber geforderten <paramref name="typeFilter"/>-Bitmaske
    /// und den gewünschten <paramref name="properties"/> (z. B. <c>DeviceLocalBit</c> oder <c>HostVisibleBit</c>).
    /// </summary>
    public uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        Api.GetPhysicalDeviceMemoryProperties(PhysicalDevice, out var memProperties);

        for (uint i = 0; i < memProperties.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1u << (int)i)) != 0 &&
                (memProperties.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
            {
                return i;
            }
        }

        throw new InvalidOperationException("Kein passender Vulkan-Speichertyp gefunden.");
    }

    private void CreateInstance(string appName, bool enableValidation, ReadOnlySpan<nint> requiredInstanceExtensions)
    {
        var appNamePtr = SilkMarshal.StringToPtr(appName);
        var engineNamePtr = SilkMarshal.StringToPtr("SlowestRenderer");

        var appInfo = new ApplicationInfo
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = (byte*)appNamePtr,
            ApplicationVersion = new Version32(1, 0, 0),
            PEngineName = (byte*)engineNamePtr,
            EngineVersion = new Version32(1, 0, 0),
            ApiVersion = Vk.Version13
        };

        // Extensions sammeln (Fenster-Extensions + optional DebugUtils)
        var extensions = new List<nint>(requiredInstanceExtensions.Length + 2);
        foreach (var ext in requiredInstanceExtensions)
            extensions.Add(ext);

        nint debugExtPtr = 0;
        if (enableValidation)
        {
            debugExtPtr = SilkMarshal.StringToPtr(ExtDebugUtils.ExtensionName);
            extensions.Add(debugExtPtr);
        }

        // Validation Layer vorbereiten
        var layers = new List<nint>(1);
        if (enableValidation)
        {
            layers.Add(SilkMarshal.StringToPtr("VK_LAYER_KHRONOS_validation"));
        }

        fixed (nint* ppExtensions = CollectionsMarshal.AsSpan(extensions))
        fixed (nint* ppLayers = CollectionsMarshal.AsSpan(layers))
        {
            var createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo,
                EnabledExtensionCount = (uint)extensions.Count,
                PpEnabledExtensionNames = (byte**)ppExtensions,
                EnabledLayerCount = (uint)layers.Count,
                PpEnabledLayerNames = layers.Count > 0 ? (byte**)ppLayers : null
            };

            var result = Api.CreateInstance(&createInfo, null, out var instance);
            if (result != Result.Success)
                throw new InvalidOperationException($"VkCreateInstance fehlgeschlagen: {result}");

            Instance = instance;
        }

        foreach (var layerPtr in layers)
            SilkMarshal.Free(layerPtr);

        // KHR_surface Extension laden
        if (Api.TryGetInstanceExtension(Instance, out KhrSurface khrSurface))
            KhrSurface = khrSurface;

        SilkMarshal.Free(appNamePtr);
        SilkMarshal.Free(engineNamePtr);
        if (debugExtPtr != 0) SilkMarshal.Free(debugExtPtr);
    }

    private void SetupDebugMessenger(bool enableValidation)
    {
        if (!enableValidation || !Api.TryGetInstanceExtension(Instance, out ExtDebugUtils debugUtils))
            return;

        _debugUtils = debugUtils;

        var createInfo = new DebugUtilsMessengerCreateInfoEXT
        {
            SType = StructureType.DebugUtilsMessengerCreateInfoExt,
            MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.WarningBitExt |
                              DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt,
            MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
                          DebugUtilsMessageTypeFlagsEXT.ValidationBitExt |
                          DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt,
            PfnUserCallback = (PfnDebugUtilsMessengerCallbackEXT)DebugCallback
        };

        _debugUtils.CreateDebugUtilsMessenger(Instance, &createInfo, null, out _debugMessenger);
    }

    private static uint DebugCallback(
        DebugUtilsMessageSeverityFlagsEXT severity,
        DebugUtilsMessageTypeFlagsEXT types,
        DebugUtilsMessengerCallbackDataEXT* pCallbackData,
        void* pUserData)
    {
        var message = Marshal.PtrToStringAnsi((nint)pCallbackData->PMessage);
        Console.WriteLine($"[Vulkan {severity}] {message}");
        return Vk.False;
    }

    private void SelectPhysicalDevice()
    {
        var devices = Api.GetPhysicalDevices(Instance);
        if (devices.Count == 0)
            throw new InvalidOperationException("Keine Vulkan-fähige GPU gefunden.");

        PhysicalDevice bestDevice = default;
        uint bestQueueFamily = uint.MaxValue;

        foreach (var device in devices)
        {
            Api.GetPhysicalDeviceProperties(device, out var props);

            uint queueFamilyCount = 0;
            Api.GetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, null);
            var queueFamilies = new QueueFamilyProperties[queueFamilyCount];
            fixed (QueueFamilyProperties* pFamilies = queueFamilies)
                Api.GetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, pFamilies);

            for (uint i = 0; i < queueFamilyCount; i++)
            {
                if ((queueFamilies[i].QueueFlags & QueueFlags.GraphicsBit) != 0)
                {
                    bestDevice = device;
                    bestQueueFamily = i;
                    break;
                }
            }

            // Dedizierte GPU (NVIDIA/AMD/Intel Arc) bevorzugen
            if (bestDevice.Handle != 0 && props.DeviceType == PhysicalDeviceType.DiscreteGpu)
                break;
        }

        if (bestDevice.Handle == 0)
            throw new InvalidOperationException("Keine GPU mit Graphics-Queue gefunden.");

        PhysicalDevice = bestDevice;
        GraphicsQueueFamilyIndex = bestQueueFamily;
    }

    private void CreateLogicalDevice()
    {
        float queuePriority = 1.0f;
        var queueCreateInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = GraphicsQueueFamilyIndex,
            QueueCount = 1,
            PQueuePriorities = &queuePriority
        };

        // Vulkan 1.3 Features (beinhaltet DynamicRendering & Synchronization2)
        var features13 = new PhysicalDeviceVulkan13Features
        {
            SType = StructureType.PhysicalDeviceVulkan13Features,
            DynamicRendering = true,
            Synchronization2 = true
        };
        var swapchainExtPtr = SilkMarshal.StringToPtr(KhrSwapchain.ExtensionName);
        var deviceFeatures = new PhysicalDeviceFeatures
        {
            SamplerAnisotropy = true
        };
        var deviceCreateInfo = new DeviceCreateInfo
        {
            SType = StructureType.DeviceCreateInfo,
            PNext = &features13,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueCreateInfo,
            PEnabledFeatures = &deviceFeatures,
            EnabledExtensionCount = 1,
            PpEnabledExtensionNames = (byte**)&swapchainExtPtr
        };
        var result = Api.CreateDevice(PhysicalDevice, &deviceCreateInfo, null, out var device);
        SilkMarshal.Free(swapchainExtPtr);
        if (result != Result.Success)
            throw new InvalidOperationException($"VkCreateDevice fehlgeschlagen: {result}");
        Device = device;
        Api.GetDeviceQueue(Device, GraphicsQueueFamilyIndex, 0, out var queue);
        GraphicsQueue = queue;
        if (Api.TryGetDeviceExtension(Instance, Device, out KhrSwapchain khrSwapchain))
            KhrSwapchain = khrSwapchain;
    }
    /// 
    /// Wartet auf den Abschluss aller GPU-Operationen und zerstört Device, Debug-Messenger sowie Instanz in sauberer Reihenfolge.
    /// 
    public void Dispose()
    {
        if (Device.Handle != 0)
        {
            Api.DeviceWaitIdle(Device);
            Api.DestroyDevice(Device, null);
            Device = default;
        }
        if (_debugUtils != null && _debugMessenger.Handle != 0)
        {
            _debugUtils.DestroyDebugUtilsMessenger(Instance, _debugMessenger, null);
        }
        if (Instance.Handle != 0)
        {
            Api.DestroyInstance(Instance, null);
            Instance = default;
        }
        Api.Dispose();
    }
}