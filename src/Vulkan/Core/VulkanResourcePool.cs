using System;
using System.Collections.Generic;

namespace Vulkan.Core;

/// <summary>
/// Generischer, generationenbasierter Resource-Pool für GPU-Objekte (Buffer, Texturen, Shader, Pipelines, Meshes).
/// Verwendet eine Free-List für O(1)-Slot-Wiederverwendung und erhöht bei jeder Freigabe den Generationszähler,
/// um Use-After-Free-Zugriffe zuverlässig zu erkennen.
/// Index <c>0</c> bleibt permanent reserviert, damit <c>default(Handle)</c> immer ungültig ist.
/// </summary>
internal sealed class VulkanResourcePool<T> where T : class, IDisposable
{
    private readonly List<T?> _items = new() { null }; // Index 0 bleibt immer Invalid
    private readonly List<int> _generations = new() { 0 };
    private readonly Stack<int> _freeIndices = new();

    /// <summary>
    /// Fügt eine neue Ressource in den Pool ein (bevorzugt aus der Free-List) und gibt das zugehörige <c>(Index, Generation)</c>-Paar zurück.
    /// </summary>
    public (int Index, int Generation) Add(T resource)
    {
        if (_freeIndices.TryPop(out int index))
        {
            _items[index] = resource;
            return (index, _generations[index]);
        }

        int newIndex = _items.Count;
        _items.Add(resource);
        _generations.Add(1);
        return (newIndex, 1);
    }

    /// <summary>
    /// Löst die Ressource aus dem Pool, macht den Handle durch Erhöhen der Generation sofort ungültig
    /// und gibt den Slot frei – ohne <see cref="IDisposable.Dispose"/> sofort aufzurufen
    /// (ideal für frame-sichere Deferred-Destruction-Queues).
    /// </summary>
    public T? Detach(int index, uint generation)
    {
        if (index <= 0 || generation == 0)
            return null;

        var item = _items[index];
        _items[index] = null;
        _generations[index]++; // Handle sofort ungültig machen!
        _freeIndices.Push(index);
        return item;
    }

    /// <summary>
    /// Löst ein <c>(Index, Generation)</c>-Handle in O(1) auf und wirft bei veraltetem oder ungültigem Handle eine Exception.
    /// </summary>
    public T Get(int index, int generation)
    {
        if ((uint)index >= (uint)_items.Count || _generations[index] != generation || _items[index] is not { } item)
            throw new ArgumentException($"Ungültiges oder bereits zerstörtes Handle (Index: {index}, Gen: {generation}).");

        return item;
    }

    /// <summary>
    /// Zerstört die Ressource sofort, erhöht den Generationszähler und gibt den Slot für neue Allokationen frei.
    /// </summary>
    public void Remove(int index, int generation)
    {
        var item = Get(index, generation);
        item.Dispose();

        _items[index] = null;
        _generations[index]++;
        _freeIndices.Push(index);
    }

    /// <summary>
    /// Gibt alle noch im Pool verbliebenen GPU-Ressourcen beim Shutdown deterministisch frei.
    /// </summary>
    public void DisposeAll()
    {
        for (int i = 1; i < _items.Count; i++)
        {
            _items[i]?.Dispose();
            _items[i] = null;
        }
        _freeIndices.Clear();
    }
}
