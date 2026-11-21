namespace API.Enums;

/// <summary>
/// Definiert die Bitbreite der Indices in einem Index-Buffer.
/// </summary>
public enum EIndexFormat : byte
{
    UInt16 = 0, // 16-Bit Indices (max. 65.535 Vertices pro Mesh; halbiert Speicherbedarf und Bandbreite)
    UInt32 = 1  // 32-Bit Indices (bis zu 4,29 Mrd. Vertices pro Mesh; Standard für große Geometrien)
}