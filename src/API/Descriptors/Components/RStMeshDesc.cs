namespace API.Descriptors.Components;

/// <summary>
/// Beschreibt das Speicher-Layout eines Meshs (kombiniert Vertex- und optionalen Index-Buffer)
/// (<c>readonly record struct</c>, 0 Byte GC-Overhead).
/// </summary>
/// <param name="VertexStrideInBytes">Größe eines einzelnen Vertex in Bytes (z. B. <c>sizeof(MyVertex)</c>).</param>
/// <param name="VertexCount">Gesamtanzahl der Vertices im Mesh.</param>
/// <param name="IndexCount">Anzahl der Indices (<c>0</c> für nicht-indizierte Meshes).</param>
/// <param name="Use32BitIndices"><c>true</c> für 32-Bit-Indices (<c>uint</c>, > 65.535 Vertices); <c>false</c> für 16-Bit-Indices (<c>ushort</c>, halbiert Speicherbandbreite).</param>
public readonly record struct RStMeshDesc(
    int VertexStrideInBytes,
    int VertexCount,
    int IndexCount,
    bool Use32BitIndices = true);