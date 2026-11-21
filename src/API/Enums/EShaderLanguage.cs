namespace API.Enums;

/// <summary>
/// Gibt die Quellsprache eines Shaders für die Runtime-Kompilierung (z. B. via Shaderc / DXC) an.
/// </summary>
public enum EShaderLanguage : byte
{
    GLSL, // OpenGL Shading Language (liegt hier aktuell noch als Dekoration rum – wird intern noch nicht kompiliert)
    HLSL  // High-Level Shading Language (aktueller Standard-Pfad für Shader-Quellcode -> SPIR-V / DXIL)
}