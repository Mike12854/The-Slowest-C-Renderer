using API.Enums;

namespace API.Descriptors.Components;

/// <summary>
/// Beschreibt die Spezifikation eines neu anzulegenden GPU-Buffers (<c>readonly record struct</c>, 0 Byte GC-Overhead).
/// </summary>
/// <param name="SizeInBytes">Gesamtgröße des Buffers in Bytes.</param>
/// <param name="StrideInBytes">Größe eines einzelnen Elements in Bytes (z. B. Vertex- oder Instanz-Stride; <c>0</c> für unstrukturierte/Constant-Buffer).</param>
/// <param name="Usage">Verwendungszweck auf der GPU (z. B. Vertex, Index, Constant, Structured).</param>
/// <param name="IsDynamic">
/// <c>true</c> für häufig per CPU aktualisierte Buffer (Host-Visible / Ring-Buffer-Pfad);
/// <c>false</c> für statische Daten im schnellen <c>DEVICE_LOCAL</c>-VRAM.
/// </param>
public readonly record struct RStBufferDesc(
    int SizeInBytes,
    int StrideInBytes,
    EBufferUsage Usage,
    bool IsDynamic);
