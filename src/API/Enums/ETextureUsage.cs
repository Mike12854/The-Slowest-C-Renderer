using System;

namespace API.Enums;

/// <summary>
/// Bitmaske für den primären Speicher- und Zugriffsmodus einer Textur auf der GPU.
/// </summary>
[Flags]
public enum ETextureUsage : byte
{
    Sampled      = 1 << 0, // Textur kann im Shader über einen Sampler ausgelesen werden (SRV / Sampled Image)
    RenderTarget = 1 << 1, // Textur dient als Color-Output-Attachment (RTV / Color Attachment)
    DepthStencil = 1 << 2, // Textur dient als Tiefen-/Stencil-Puffer (DSV / Depth-Stencil Attachment)
    Storage      = 1 << 3  // Textur erlaubt unstrukturierten Lese-/Schreibzugriff (UAV / Compute Storage Image)
}