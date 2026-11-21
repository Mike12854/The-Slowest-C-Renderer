using System;

namespace API.Contracts;

/// <summary>
/// Abstraktion für parallele Worker-Ausführung (inspiriert von BepuPhysics).
/// Ermöglicht es der Anwendung, einen eigenen Thread-Pool (z. B. für multithreaded
/// Command-Buffer-Recording) bereitzustellen, ohne im Hotpath <see cref="System.Threading.Tasks.Task"/>-Objekte zu allokieren.
/// </summary>
public interface IThreadDispatcher
{
    /// <summary>
    /// Anzahl der verfügbaren Worker-Threads (bestimmt u. a. die Anzahl der Command-Pools/Buckets).
    /// </summary>
    int ThreadCount { get; }
    
    /// <summary>
    /// Führt <paramref name="workerBody"/> parallel auf allen Worker-Threads aus und blockiert,
    /// bis alle Worker (Index <c>0</c> bis <c>ThreadCount - 1</c>) abgeschlossen sind.
    /// Hinweis: Das Delegate sollte einmalig gecacht werden, um GC-Allokationen im Render-Loop zu vermeiden.
    /// </summary>
    void DispatchWorkers(Action<int> workerBody);
}