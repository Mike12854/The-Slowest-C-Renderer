namespace API.Commands.Enums;

/// <summary>
/// API-übergreifende Pixelformate für Texturen, RenderTargets und Depth-Stencil-Buffer.
/// Hinweis: <c>_SRgb</c>-Varianten führen beim Sampling automatisch eine Gamma-to-Linear-Konvertierung
/// durch und beim Schreiben in ein RenderTarget eine Linear-to-SRGB-Konvertierung.
/// </summary>
public enum ETextureFormat : ushort
{
    None = 0,

    // Standard Color (8 Bit pro Kanal | Linear vs. SRGB)
    R8_UNorm,              // 1 Byte/Pixel  (z. B. Masken, Font-Atlanten)
    RG8_UNorm,             // 2 Bytes/Pixel (z. B. 2-Kanal-Daten)
    RGBA8_UNorm,           // 4 Bytes/Pixel (Linear Color / Normal Maps)
    RGBA8_UNorm_SRgb,      // 4 Bytes/Pixel (Albedo / Diffuse Texturen mit Gamma-Korrektur)
    BGRA8_UNorm,           // 4 Bytes/Pixel (Typisches Windows/DXGI Swapchain-Format)
    BGRA8_UNorm_SRgb,      // 4 Bytes/Pixel (Swapchain-Format mit Hardware-SRGB)

    // HDR / Floating Point (für Lighting, Bloom, G-Buffer)
    R16_Float,             // 2 Bytes/Pixel (Half-Float Single Channel)
    RG16_Float,            // 4 Bytes/Pixel (z. B. Velocity / Motion Vectors)
    RGBA16_Float,          // 8 Bytes/Pixel (Standard HDR-Color-Buffer)
    R32_Float,             // 4 Bytes/Pixel (Full-Float Single Channel)
    RG32_Float,            // 8 Bytes/Pixel
    RGBA32_Float,          // 16 Bytes/Pixel (Präzise Berechnungen / World-Position-Buffer)

    // Depth / Stencil
    D16_UNorm,             // 2 Bytes/Pixel (Kompakte ShadowMaps)
    D24_UNorm_S8_UInt,     // 4 Bytes/Pixel (Standard Depth + 8-Bit Stencil)
    D32_Float,             // 4 Bytes/Pixel (Hohe Tiefenpräzision, ideal für Reversed-Z)
    D32_Float_S8X24_UInt   // 8 Bytes/Pixel (32-Bit Depth + 8-Bit Stencil + 24-Bit Padding)
}