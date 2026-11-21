namespace API.Enums;

/// <summary>
/// Legt fest, welche Dreiecksseiten beim Rasterizing anhand ihrer Winding-Order verworfen werden.
/// </summary>
public enum ECullMode : byte
{
    None  = 0, // Kein Culling (beide Seiten werden gerendert, z. B. für Blätter, Zäune oder PostFX-Quads)
    Back  = 1, // Standard: Rückseiten werden verworfen (spart ~50 % Fragment-Shader-Last bei geschlossenen Meshes)
    Front = 2  // Vorderseiten werden verworfen (z. B. für Skybox-Innenseiten oder Shadow-Bias-Tricks)
}