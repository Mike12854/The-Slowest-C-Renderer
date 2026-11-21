namespace API.Handles.Components;

/// <summary>
/// Typsicherer 8-Byte-Handle für ein GPU-Mesh (<c>Index</c> + <c>Generation</c>, 0 Byte GC-Overhead).
/// Schützt durch den Generationszähler zuverlässig vor Use-After-Free-Zugriffen auf bereits freigegebene Slots.
/// </summary>
/// <param name="Index">1-basierter Slot-Index im internen Resource-Pool (<c>0</c> = ungültig).</param>
/// <param name="Generation">Generationszähler des Slots zur Erkennung veralteter Handles.</param>
public readonly record struct RStMeshHandle(int Index, int Generation)
{
    /// <summary>Gibt <c>true</c> zurück, wenn der Handle auf einen gültigen Pool-Slot verweist (<c>Index &gt; 0</c>).</summary>
    public bool IsValid => Index > 0;

    /// <summary>Repräsentiert einen ungültigen/leeren Mesh-Handle (<c>default</c>).</summary>
    public static readonly RStMeshHandle Invalid = default;
}

