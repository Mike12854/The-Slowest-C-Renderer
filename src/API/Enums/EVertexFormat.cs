namespace API.Enums;

/// <summary>
/// Definiert den Datentyp, die Komponentenanzahl und die Byte-Größe eines Vertex-Attributs
/// (wird von <see cref="RStVertexElement"/> im Pipeline-Layout verwendet).
/// </summary>
public enum EVertexFormat : byte
{
    Float1    = 0, //  4 Bytes | 1 × 32-Bit Float (z. B. Skalare, Gewichte, Höhenwerte)
    Float2    = 1, //  8 Bytes | 2 × 32-Bit Float (z. B. UV-Koordinaten)
    Float3    = 2, // 12 Bytes | 3 × 32-Bit Float (z. B. Positionen, Normalen)
    Float4    = 3, // 16 Bytes | 4 × 32-Bit Float (z. B. Tangenten mit Handedness, RGBA-Farben)
    UInt1     = 4, //  4 Bytes | 1 × 32-Bit Unsigned Int (z. B. Material- oder Instanz-ID)
    UInt2     = 5, //  8 Bytes | 2 × 32-Bit Unsigned Int
    UInt4     = 6, // 16 Bytes | 4 × 32-Bit Unsigned Int (z. B. Bone-Indices beim Skinning)
    Byte4Norm = 7  //  4 Bytes | 4 × 8-Bit Unsigned Byte, von der GPU auf [0.0, 1.0] normalisiert (ideal für komprimierte RGBA-Farben)
}
