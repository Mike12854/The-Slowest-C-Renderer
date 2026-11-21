using System;
using API.Commands.Components;
using API.Commands.Enums;

namespace API.Rendering;

/// <summary>
/// Zerlegt einen vorsortierten Command-Stream allokationsfrei (<c>Span</c>-basiert) in
/// unabhängige Worker-Slices für paralleles Command-Buffer-Recording.
/// </summary>
public static class RenderCommandSlicer
{
    /// <summary>
    /// Berechnet sichere Start- und End-Indizes für <paramref name="requestedWorkers"/> Threads,
    /// sodass niemals zwischen State-Setups (<c>SetPipeline</c>, <c>SetPushConstants</c> etc.)
    /// und dem dazugehörigen Draw-Call geschnitten wird.
    /// </summary>
    /// <param name="sortedCommands">Der nach <see cref="StRenderCommand.SortKey"/> sortierte Command-Stream.</param>
    /// <param name="requestedWorkers">Gewünschte Anzahl paralleler Worker-Threads.</param>
    /// <param name="outSlices">Stack-allokierter Ziel-Span für die berechneten <c>(Start, Length)</c>-Bereiche.</param>
    /// <returns>Die tatsächlich erzeugte Anzahl an Worker-Slices (kann bei kleinen Streams kleiner als <paramref name="requestedWorkers"/> sein).</returns>
    public static int ComputeWorkerSlices(
        ReadOnlySpan<StRenderCommand> sortedCommands,
        int requestedWorkers,
        Span<(int Start, int Length)> outSlices)
    {
        if (sortedCommands.IsEmpty || requestedWorkers <= 0)
            return 0;

        int actualWorkers = Math.Min(requestedWorkers, outSlices.Length);
        int approxChunkSize = Math.Max(1, sortedCommands.Length / actualWorkers);

        int currentStart = 0;
        int sliceCount = 0;

        for (int w = 0; w < actualWorkers && currentStart < sortedCommands.Length; w++)
        {
            int end = (w == actualWorkers - 1)
                ? sortedCommands.Length
                : Math.Min(sortedCommands.Length, currentStart + approxChunkSize);

            // Bis zum nächsten Draw-Befehl vorrücken, damit kein Draw-Call von seinen State-Setups getrennt wird
            while (end < sortedCommands.Length && !IsDrawCommand(sortedCommands[end - 1].Type))
            {
                end++;
            }

            outSlices[sliceCount++] = (currentStart, end - currentStart);
            currentStart = end;
        }

        return sliceCount;
    }

    private static bool IsDrawCommand(ERenderCommandType type) =>
        type is ERenderCommandType.Draw
            or ERenderCommandType.DrawIndexed
            or ERenderCommandType.DrawMesh;
}
