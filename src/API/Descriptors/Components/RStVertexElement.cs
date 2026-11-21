using API.Enums;

namespace API.Descriptors.Components;

/// <summary>
/// Beschreibt ein einzelnes Attribut im Vertex-Buffer (z. B. Position, Normal, UV)
/// inklusive Ziel-Binding-Slot für interleaved oder separate Vertex-Streams.
/// </summary>
/// <param name="Location">Shader-Input-Location (entspricht <c>layout(location = X)</c> in GLSL/SPIR-V bzw. dem Semantic-Slot in HLSL).</param>
/// <param name="Format">Datentyp und Komponentenanzahl des Attributs (z. B. <c>Float3</c> für Position/Normal, <c>Float2</c> für UVs).</param>
/// <param name="OffsetInBytes">Byte-Offset des Attributs ab Beginn des Vertex-Elements im jeweiligen Buffer-Stream.</param>
/// <param name="Binding">Vertex-Buffer-Binding-Slot (Standard: <c>0</c>; höhere Slots erlauben separate Streams oder Per-Instance-Attribute).</param>
public readonly record struct RStVertexElement(
    uint Location,
    EVertexFormat Format,
    uint OffsetInBytes,
    uint Binding = 0
);