namespace API.Enums;

/// <summary>
/// Definiert den primären Verwendungszweck eines GPU-Buffers (bestimmt Vulkan Usage Flags,
/// DirectX Bind Flags und OpenGL Buffer Targets).
/// </summary>
public enum EBufferUsage : byte
{
    Vertex,     // Vertex-Daten (Positionen, Normalen, UVs, Per-Instance-Daten)
    Index,      // 16-Bit oder 32-Bit Dreiecks-Indices
    Constant,   // Uniform- / Constant-Buffer (UBO / CBV) für schnelle Parameter pro Frame/Draw
    Structured  // Strukturierte Daten-Arrays / Instanz-Buffer (SSBO / SRV)
}