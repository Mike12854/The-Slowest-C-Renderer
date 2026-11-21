using API.Commands.Enums;
using API.Enums;

namespace API.Descriptors.Components;

/// <summary>
/// Callback-Delegate, mit dem die Anwendung die plattformspezifische <c>VkSurfaceKHR</c>
/// (Win32, X11, Wayland – oder für Spendable auch Metal) selbst erzeugt,
/// ohne dass die Core-API eine Abhängigkeit auf ein Windowing-Framework benötigt.
/// </summary>
/// <param name="vkInstance">Nativer <c>VkInstance</c>-Handle.</param>
/// <param name="allocator">Optionaler <c>VkAllocationCallbacks*</c>-Zeiger (meist <c>0</c>).</param>
/// <returns>Der erzeugte native <c>VkSurfaceKHR</c>-Handle als <see cref="ulong"/>.</returns>
public unsafe delegate ulong CreateVulkanSurfaceCallback(nint vkInstance, nint allocator);

/// <summary>
/// Beschreibt die Konfiguration einer SwapChain (Backbuffer-Kette und Präsentationsmodus).
/// </summary>
/// <param name="Window">Plattformspezifische Fenster-Handles für die Surface-Erstellung.</param>
/// <param name="Width">Initiale Breite der SwapChain in Pixeln.</param>
/// <param name="Height">Initiale Höhe der SwapChain in Pixeln.</param>
/// <param name="ColorFormat">Pixelformat der Backbuffer (Standard: <see cref="ETextureFormat.BGRA8_UNorm_SRgb"/>).</param>
/// <param name="DepthFormat">Pixelformat des automatischen SwapChain-Depth-Buffers (<see cref="ETextureFormat.None"/>, falls nicht benötigt).</param>
/// <param name="VSync"><c>true</c> begrenzt die Ausgabe auf die Monitor-Refreshrate (<c>FIFO</c>); <c>false</c> für ungebremste FPS (<c>Immediate</c>/<c>Mailbox</c>).</param>
/// <param name="BufferCount">Anzahl der Backbuffer-Images (z. B. <c>2</c> für Double-Buffering, <c>3</c> für Triple-Buffering / Frames-in-Flight).</param>
public record struct RStSwapChainDesc(
    RStNativeWindow Window,
    int Width,
    int Height,
    ETextureFormat ColorFormat = ETextureFormat.BGRA8_UNorm_SRgb,
    ETextureFormat DepthFormat = ETextureFormat.D32_Float,
    bool VSync = true,
    int BufferCount = 2
);