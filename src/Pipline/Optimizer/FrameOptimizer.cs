using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using API.Commands.Components;
using API.Commands.Enums;
using API.Handles.Components;

namespace Pipline.Optimizer;

/// <summary>
/// Sortiert und komprimiert <see cref="StRenderCommand"/>-Streams in O(n) ohne jegliche GC-Allokationen:
/// 1. Stabiler 8-Pass LSD Radix Sort über den 64-Bit <see cref="StRenderCommand.SortKey"/> (via unmanaged Scratch-Buffer).
/// 2. In-Place Redundant-State-Elimination (entfernt doppelte Pipeline-, Buffer- und Textur-Bindings).
/// </summary>
public sealed unsafe class FrameOptimizer : IDisposable
{
    private StRenderCommand* _scratchBuffer;
    private int _scratchCapacity;

    /// <summary>
    /// Sortiert <paramref name="commands"/> nach State und entfernt anschließend alle redundanten
    /// State-Changes in-place.
    /// </summary>
    /// <param name="commands">Der rohe Command-Stream des aktuellen Render-Passes.</param>
    /// <returns>Die neue, komprimierte Anzahl gültiger Commands im vorderen Teil von <paramref name="commands"/>.</returns>
    public int Optimize(Span<StRenderCommand> commands)
    {
        SortByState(commands);

        const int MaxTrackedSlots = 8;
        RStPipelineHandle activePipeline = default;

        // Stack-Allokation (0 Bytes Heap-Overhead) für das Tracking der aktuell gebundenen Slots
        Span<RStBufferHandle> activeCBuffers = stackalloc RStBufferHandle[MaxTrackedSlots];
        Span<RStBufferHandle> activeSBuffers = stackalloc RStBufferHandle[MaxTrackedSlots];
        Span<RStTextureHandle> activeTextures = stackalloc RStTextureHandle[MaxTrackedSlots];

        int writeIdx = 0;

        for (int readIdx = 0; readIdx < commands.Length; readIdx++)
        {
            ref readonly var cmd = ref commands[readIdx];

            switch (cmd.Type)
            {
                case ERenderCommandType.SetPipeline:
                    if (cmd.Pipeline == activePipeline)
                        continue;
                    activePipeline = cmd.Pipeline;
                    activeCBuffers.Clear();
                    activeSBuffers.Clear();
                    activeTextures.Clear();
                    break;

                case ERenderCommandType.SetConstantBuffer:
                    if (cmd.Slot < MaxTrackedSlots)
                    {
                        if (cmd.Buffer == activeCBuffers[(int)cmd.Slot])
                            continue;
                        activeCBuffers[(int)cmd.Slot] = cmd.Buffer;
                    }
                    break;

                case ERenderCommandType.SetStructuredBuffer:
                    if (cmd.Slot < MaxTrackedSlots)
                    {
                        if (cmd.Buffer == activeSBuffers[(int)cmd.Slot])
                            continue;
                        activeSBuffers[(int)cmd.Slot] = cmd.Buffer;
                    }
                    break;

                case ERenderCommandType.SetTexture:
                    if (cmd.Slot < MaxTrackedSlots)
                    {
                        if (cmd.Texture == activeTextures[(int)cmd.Slot])
                            continue;
                        activeTextures[(int)cmd.Slot] = cmd.Texture;
                    }
                    break;
            }

            commands[writeIdx++] = cmd;
        }

        return writeIdx; // Reduziert z. B. 5 identische Draw-Blöcke (25 Commands) auf 13 Commands
    }

    /// <summary>
    /// Sortiert Commands anhand ihres 64-Bit <see cref="StRenderCommand.SortKey"/> in O(n)
    /// mittels eines stabilen 8-Pass LSD Radix Sorts (0 Byte GC-Allokationen im Hot-Path).
    /// </summary>
    public void SortByState(Span<StRenderCommand> commands)
    {
        int count = commands.Length;
        if (count <= 1)
            return;

        EnsureScratchCapacity(count);

        int* counts = stackalloc int[256];
        int* offsets = stackalloc int[256];

        fixed (StRenderCommand* pCommands = commands)
        {
            StRenderCommand* src = pCommands;
            StRenderCommand* dst = _scratchBuffer;

            for (int pass = 0; pass < 8; pass++)
            {
                int shift = pass * 8;
                Unsafe.InitBlock(counts, 0, 256 * sizeof(int));

                // 1. Histogramm zählen
                for (int i = 0; i < count; i++)
                {
                    byte bucket = (byte)((src[i].SortKey >> shift) & 0xFF);
                    counts[bucket]++;
                }

                // 2. Prefix-Summe berechnen
                int total = 0;
                for (int b = 0; b < 256; b++)
                {
                    offsets[b] = total;
                    total += counts[b];
                }

                // 3. Stabil umsortieren
                for (int i = 0; i < count; i++)
                {
                    byte bucket = (byte)((src[i].SortKey >> shift) & 0xFF);
                    dst[offsets[bucket]++] = src[i];
                }

                // Puffer tauschen
                StRenderCommand* tmp = src;
                src = dst;
                dst = tmp;
            }
            // Nach 8 (geraden) Durchläufen liegt das Ergebnis automatisch wieder direkt in pCommands!
        }
    }

    private void EnsureScratchCapacity(int requiredCount)
    {
        if (_scratchCapacity >= requiredCount)
            return;

        if (_scratchBuffer != null)
            NativeMemory.Free(_scratchBuffer);

        int newCapacity = Math.Max(1024, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)requiredCount));
        _scratchBuffer = (StRenderCommand*)NativeMemory.Alloc((nuint)newCapacity, (nuint)sizeof(StRenderCommand));
        _scratchCapacity = newCapacity;
    }

    /// <summary>Gibt den unmanaged Scratch-Speicher deterministisch frei.</summary>
    public void Dispose()
    {
        if (_scratchBuffer != null)
        {
            NativeMemory.Free(_scratchBuffer);
            _scratchBuffer = null;
            _scratchCapacity = 0;
        }
    }
}
