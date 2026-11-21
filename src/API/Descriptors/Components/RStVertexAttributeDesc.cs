using API.Enums;

namespace API.Descriptors.Components;

/// <summary>
/// Beschreibt ein einzelnes Vertex-Attribut innerhalb eines Vertex-Layouts
/// (<c>readonly record struct</c>, 0 Byte GC-Overhead).
/// </summary>
/// <param name="Location">Shader-Input-Location (entspricht <c>layout(location = X)</c> in GLSL/SPIR-V bzw. dem Semantic-Slot in HLSL).</param>
/// <param name="OffsetInBytes">Byte-Offset des Attributs vom Beginn eines Vertex-Structs (z. B. via <see cref="System.Runtime.InteropServices.Marshal.OffsetOf{T}"/>).</param>
/// <param name="Format">Datentyp und Komponentenanzahl des Attributs (z. B. <c>Float3</c> für Position/Normal, <c>Float2</c> für UVs).</param>
public readonly record struct RStVertexAttributeDesc(
    uint Location,
    uint OffsetInBytes,
    EVertexAttributeFormat Format);