namespace API.Commands.Enums;

/// <summary>
/// Definiert den Sampling-Filtermodus beim Auslesen von Texturen auf der GPU.
/// </summary>
public enum ETextureFilter : byte
{
    Point,       // Nearest (Pixel-Art / G-Buffer Lookups)
    Linear,      // Bilinear / Trilinear
    Anisotropic  // Hochwertig für schräge Winkel (Böden, Wände)
}