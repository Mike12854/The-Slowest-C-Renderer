namespace API.Commands.Enums;

/// <summary>
/// Bitmaske für die Verwendungszwecke einer Textur auf der GPU (bestimmt Vulkan Usage Flags,
/// DirectX Bind Flags und interne Layout-Transitions).
/// </summary>
[Flags]
public enum ETextureFlags : byte
{
    None = 0,
    ShaderResource = 1 << 0, // Standard für Texturen im Shader
    RenderTarget = 1 << 1,   // Color Attachment (Offscreen / G-Buffer)
    DepthStencil = 1 << 2,   // Depth / Stencil Buffer
    UnorderedAccess = 1 << 3 // Compute Shader Read/Write (UAV / Storage Image)
}