using System;
using API.Commands.Components;
using API.Contracts;
using API.Interfaces;

namespace API.Threading;

/// <summary>
/// Verteilt vorsortierte <see cref="StRenderCommand"/>-Streams entkoppelt und lock-free auf
/// parallele Worker-Threads (via <see cref="IThreadDispatcher"/>) oder fällt bei kleinen Streams
/// automatisch auf den Single-Thread-Pfad zurück.
/// </summary>
public static class RenderDispatcher
{
    /// <summary>
    /// Zerlegt <paramref name="commands"/> an sicheren Draw-Call-Grenzen in Slices und lässt
    /// jeden aktiven Worker parallel in seinen eigenen Command-Buffer aufzeichnen.
    /// </summary>
    /// <param name="device">Das aktive Render-Device, das die Command-Slices entgegennimmt.</param>
    /// <param name="commands">Der vorsortierte Command-Stream des aktuellen Render-Passes.</param>
    /// <param name="threadDispatcher">Optionaler externer Thread-Pool (bei <c>null</c> oder &lt; 64 Commands läuft alles single-threaded).</param>
    public static void DispatchCommands(
        IRenderDevice device,
        Span<StRenderCommand> commands,
        IThreadDispatcher? threadDispatcher = null)
    {
        if (commands.IsEmpty)
            return;

        // 1. Single-Threaded-Fallback (oder zu wenige Commands, als dass sich Thread-Synchronisation lohnt)
        int workerCount = threadDispatcher?.ThreadCount ?? 1;
        if (workerCount <= 1 || commands.Length < 64)
        {
            device.ExecuteCommandSlice(0, commands);
            return;
        }

        // 2. Commands sicher an Draw-Call-Grenzen in stack-allokierte Slices aufteilen
        int maxSlices = Math.Min(workerCount, (int)device.MaxWorkerThreads);
        Span<(int Start, int Length)> slices = stackalloc (int, int)[maxSlices];
        int activeSlices = Rendering.RenderCommandSlicer.ComputeWorkerSlices(commands, maxSlices, slices);

        // 3. Da ref-structs (Span) in C# nicht direkt in Lambdas gecaptured werden dürfen,
        //    pinnen wir beide Spans für die Dauer des synchronen DispatchWorkers-Aufrufs.
        unsafe
        {
            fixed (StRenderCommand* pCommands = commands)
            fixed ((int Start, int Length)* pSlices = slices)
            {
                StRenderCommand* cmdPtr = pCommands;
                (int Start, int Length)* slicePtr = pSlices;

                threadDispatcher!.DispatchWorkers(workerIndex =>
                {
                    if (workerIndex >= activeSlices)
                        return;

                    var (start, length) = slicePtr[workerIndex];
                    var workerSpan = new ReadOnlySpan<StRenderCommand>(cmdPtr + start, length);

                    // Jeder Worker zeichnet lock-frei in seinen eigenen Command-Buffer!
                    device.ExecuteCommandSlice((uint)workerIndex, workerSpan);
                });
            }
        }
    }
}
