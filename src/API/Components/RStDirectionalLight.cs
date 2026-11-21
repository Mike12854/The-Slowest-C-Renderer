using System.Numerics;
using System.Runtime.InteropServices;

namespace API.Components;

/// <summary>
/// GPU-kompatibles Constant-Buffer-Struct für ein gerichtetes Licht (z. B. Sonne).
/// Gesamtgröße: 32 Bytes (2 × 16-Byte <c>std140</c>/<c>cbuffer</c>-Blöcke ohne Padding).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RStDirectionalLight
{
    public Vector3 Direction;     // Offset  0..11 | Strahlrichtung (normalisiert, z. B. von oben-links nach unten-rechts)
    public float Intensity;       // Offset 12..15 | Lichtstärke (z. B. 1.5f)
    public Vector3 Color;         // Offset 16..27 | Lichtfarbe (z. B. warmes Sonnenweiß: 1.0f, 0.96f, 0.90f)
    public float AmbientStrength; // Offset 28..31 | Umgebungslicht im Schatten (z. B. 0.15f)
}