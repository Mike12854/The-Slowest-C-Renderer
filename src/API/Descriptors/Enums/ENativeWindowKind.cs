namespace API.Descriptors.Enums;

public enum ENativeWindowKind : byte
{
    Win32,      // Handle = HWND, Display = HINSTANCE (optional)
    X11,        // Handle = Window (XID), Display = Display*
    Wayland,    // Handle = wl_surface*, Display = wl_display*
    Cocoa,      // Handle = NSView* / CAMetalLayer*
    Android     // Handle = ANativeWindow*
}