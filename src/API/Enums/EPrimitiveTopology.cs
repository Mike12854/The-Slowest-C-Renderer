namespace API.Enums;

/// <summary>
/// Legt fest, wie die GPU aufeinanderfolgende Vertices/Indices zu geometrischen Primitiven zusammensetzt.
/// </summary>
public enum EPrimitiveTopology : byte
{
    TriangleList = 0, // Je 3 Vertices/Indices bilden ein unabhängiges Dreieck (Standard für 3D-Meshes)
    LineList     = 1, // Je 2 Vertices/Indices bilden eine unabhängige Linie (ideal für Debug-Wireframes, Gizmos, Raycasts)
    PointList    = 2  // Jeder Vertex wird als einzelner Punkt gerendert (z. B. für Partikel oder Punktwolken)
}