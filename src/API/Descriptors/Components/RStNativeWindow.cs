using API.Descriptors.Enums;

namespace API.Descriptors.Components;

/// <summary>
/// Kapselt die nativen Betriebssystem-Handles eines Fensters zur Erstellung der Swapchain-Surface
/// (<c>readonly record struct</c>, komplett entkoppelt von spezifischen Windowing-Libraries).
/// </summary>
/// <param name="Kind">Plattform-/Fenstertyp (z. B. <see cref="ENativeWindowKind.Win32"/>, <see cref="ENativeWindowKind.X11"/> oder <see cref="ENativeWindowKind.Wayland"/>).</param>
/// <param name="WindowHandle">Primärer Fenster-Handle (<c>HWND</c> unter Windows, <c>Window</c> unter X11 oder <c>wl_surface*</c> unter Wayland).</param>
/// <param name="DisplayHandle">Optionaler Display-/Instanz-Handle (<c>HINSTANCE</c> unter Windows, <c>Display*</c> unter X11 oder <c>wl_display*</c> unter Wayland).</param>

public readonly record struct RStNativeWindow(
    ENativeWindowKind Kind,
    nint WindowHandle,
    nint DisplayHandle = 0
);