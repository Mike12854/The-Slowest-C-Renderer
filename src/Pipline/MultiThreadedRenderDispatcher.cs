using System;
using System.Threading;
using API.Commands.Components;
using API.Contracts;
using API.Interfaces;
using API.Rendering;
using Pipline.Optimizer;

namespace Pipline;

/// <summary>
/// Koordiniert die globale Sortierung (<see cref="FrameOptimizer.SortByState"/>) sowie die
/// parallele, lock-free Slice-Optimierung und Command-Buffer-Aufzeichnung über mehrere Worker-Threads.
/// Jeder Worker nutzt dabei eine eigene <see cref="ThreadLocal{T}"/>-Instanz von <see cref="FrameOptimizer"/>
/// ohne Speicher- oder Thread-Konflikte.
/// </summary>
public sealed class MultiThreadedRenderDispatcher : IDisposable
{
    private readonly ThreadLocal<FrameOptimizer> _threadOptimizers = new(() => new FrameOptimizer(), trackAllValues: true);

    /// <summary>
    /// Sortiert <paramref name="commands"/> global nach <see cref="StRenderCommand.SortKey"/>, teilt den Stream
    /// an sicheren Draw-Call-Grenzen in Worker-Slices auf und lässt jeden Worker seinen eigenen Slice
    /// unabhängig deduplizieren und aufzeichnen.
    /// </summary>
    /// <param name="device">Das aktive Render-Device.</param>
    /// <param name="commands">Der rohe, unsortierte Command-Stream des aktuellen Render-Passes.</param>
    /// <param name="threadDispatcher">Optionaler externer Worker-Pool (bei <c>null</c> oder &lt; 64 Commands greift der Single-Thread-Fastpath).</param>
    public unsafe void Dispatch(
        IRenderDevice device,
        Span<StRenderCommand> commands,
        IThreadDispatcher? threadDispatcher = null)
    {
        if (commands.IsEmpty)
            return;

        int requestedWorkers = threadDispatcher?.ThreadCount ?? 1;
        int maxWorkers = Math.Min(requestedWorkers, (int)device.MaxWorkerThreads);

        // Single-Threaded-Fastpath für kleine Szenen (vermeidet Thread-Sync-Overhead)
        if (maxWorkers <= 1 || commands.Length < 64)
        {
            int count = _threadOptimizers.Value!.Optimize(commands);
            device.ExecuteCommandSlice(0, commands.Slice(0, count));
            return;
        }

        // 1. Zuerst global sortieren (nur Radix-Sort, noch OHNE State-Deduplizierung,
        //    da jeder Worker-CommandBuffer mit einem eigenen, leeren GPU-State startet!)
        _threadOptimizers.Value!.SortByState(commands);

        // 2. Sicher an Draw-Call-Grenzen in stack-allokierte Slices aufteilen
        Span<(int Start, int Length)> slices = stackalloc (int, int)[maxWorkers];
        int activeSlices = RenderCommandSlicer.ComputeWorkerSlices(commands, maxWorkers, slices);

        // 3. Zero-Allocation Parallel-Recording auf alle Worker-Threads verteilen
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
                var workerSpan = new Span<StRenderCommand>(cmdPtr + start, length);

                // Jeder Thread dedupliziert NUR seinen eigenen Slice (behält seinen initialen Pipeline/Texture-State!)
                int optimizedLength = _threadOptimizers.Value!.Optimize(workerSpan);
                device.ExecuteCommandSlice((uint)workerIndex, workerSpan.Slice(0, optimizedLength));
            });
        }
    }

    /// <summary>Gibt alle <see cref="ThreadLocal{T}"/>-Instanzen inkl. ihrer unmanaged Scratch-Buffer frei.</summary>
    public void Dispose()
    {
        foreach (var opt in _threadOptimizers.Values)
            opt.Dispose();
        _threadOptimizers.Dispose();
    }
}
