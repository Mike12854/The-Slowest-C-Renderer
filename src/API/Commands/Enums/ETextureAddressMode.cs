namespace API.Commands.Enums;

/// <summary>
/// Legt fest, wie UV-Koordinaten außerhalb des Bereichs [0.0, 1.0] behandelt werden.
/// Verwendet DirectX-Bezeichnungen; die Kommentare zeigen die Vulkan/OpenGL-Äquivalente.
/// </summary>
public enum ETextureAddressMode : byte
{
    Wrap,        // Vulkan: Repeat          | OpenGL: GL_REPEAT
    Mirror,      // Vulkan: MirroredRepeat  | OpenGL: GL_MIRRORED_REPEAT
    Clamp,       // Vulkan: ClampToEdge     | OpenGL: GL_CLAMP_TO_EDGE
    Border       // Vulkan: ClampToBorder   | OpenGL: GL_CLAMP_TO_BORDER
}