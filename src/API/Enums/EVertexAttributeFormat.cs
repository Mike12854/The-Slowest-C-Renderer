namespace API.Enums;

/// <summary>
/// Definiert den Datentyp und die Komponentenanzahl eines einzelnen Vertex-Attributs
/// (wird von <see cref="RStVertexAttributeDesc"/> verwendet).
/// </summary>
public enum EVertexAttributeFormat : byte
{
    Float2, // 2 × 32-Bit Float (8 Bytes  | z. B. UV-Koordinaten)
    Float3, // 3 × 32-Bit Float (12 Bytes | z. B. Positionen, Normalen, Tangenten)
    Float4, // 4 × 32-Bit Float (16 Bytes | z. B. RGBA-Vertex-Farben, Tangenten mit Handedness)
    UInt    // 1 × 32-Bit Unsigned Int (4 Bytes | z. B. Bone-Indices, Material-IDs, gepackte Farben)
}