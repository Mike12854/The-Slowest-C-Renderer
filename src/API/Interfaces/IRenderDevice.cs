using System;
using API.Commands.Components;
using API.Descriptors.Components;
using API.Enums;
using API.Handles.Components;

namespace API.Interfaces;

/// <summary>
/// Basis-Interface für alle Grafik-Backends (Vulkan, OpenGL, DirectX usw.).
/// Kapselt Ressourcen-Lebenszyklen, Shader-/Pipeline-Erstellung sowie die parallele
/// Aufzeichnung und Ausführung von Render-Passes ohne GC-Allokationen im Hotpath.
/// </summary>
public interface IRenderDevice : IDisposable
{
    /// <summary>Anzeigename des aktiven Grafik-Backends inkl. GPU-Bezeichnung (z. B. <c>"Vulkan 1.3 (NVIDIA GeForce RTX 3090)"</c>).</summary>
    string BackendName { get; }

    /// <summary>Maximale Anzahl unterstützter Worker-Threads für paralleles Command-Recording.</summary>
    uint MaxWorkerThreads { get; }

    /// <summary>Anzahl der gleichzeitig in Bearbeitung befindlichen Frames (Double-/Triple-Buffering).</summary>
    uint FramesInFlight { get; }

    /// <summary>Aktueller Frame-Slot-Index (<c>0</c> bis <c>FramesInFlight - 1</c>) für Ring-Buffer- und Sync-Verwaltung.</summary>
    uint CurrentFrameIndex { get; }

    // --- Resource Creation (Direkter VRAM-Upload aus ReadOnlySpan<byte>) ---

    /// <summary>Erstellt einen GPU-Buffer (Vertex, Index, Constant oder Structured) und lädt optional initiale Daten hoch.</summary>
    RStBufferHandle CreateBuffer(in RStBufferDesc desc, ReadOnlySpan<byte> initialData = default);

    /// <summary>Aktualisiert einen Teilbereich eines bestehenden GPU-Buffers per Zero-Copy-Span.</summary>
    void UpdateBuffer(RStBufferHandle handle, ReadOnlySpan<byte> data, uint offsetInBytes = 0);

    /// <summary>Gibt den GPU-Buffer frei und erhöht den Generationszähler des Slots.</summary>
    void DestroyBuffer(RStBufferHandle handle);

    /// <summary>Erstellt ein kombiniertes GPU-Mesh aus Vertex- und optionalen Index-Daten im schnellen VRAM.</summary>
    RStMeshHandle CreateMesh(
        ReadOnlySpan<byte> vertexData, uint vertexStride,
        ReadOnlySpan<byte> indexData, EIndexFormat indexFormat,
        string? debugName = null);

    /// <summary>Gibt das GPU-Mesh frei und erhöht den Generationszähler des Slots.</summary>
    void DestroyMesh(RStMeshHandle handle);

    /// <summary>Erstellt eine GPU-Textur (2D, 3D, Cube, RenderTarget oder DepthBuffer) inkl. Sampler und optionalen Pixeldaten.</summary>
    RStTextureHandle CreateTexture(in RStTextureDesc desc, ReadOnlySpan<byte> initialData = default);

    /// <summary>Gibt die GPU-Textur frei und erhöht den Generationszähler des Slots.</summary>
    void DestroyTexture(RStTextureHandle handle);

    // --- Shader & Pipelines ---

    /// <summary>Kompiliert ein Shader-Programm zur Laufzeit aus Quellcode (z. B. HLSL -> SPIR-V).</summary>
    RStShaderHandle CreateShader(in RStShaderDesc desc);

    /// <summary>Erstellt ein Shader-Programm direkt aus vorkompiliertem Bytecode (z. B. SPIR-V oder DXIL) ohne Runtime-Compiler-Overhead.</summary>
    RStShaderHandle CreateShaderFromBytecode(
        ReadOnlySpan<byte> vertexBytecode,
        ReadOnlySpan<byte> fragmentBytecode,
        string vertexEntryPoint = "VSMain",
        string fragmentEntryPoint = "PSMain",
        string? debugName = null);

    /// <summary>Gibt das Shader-Programm frei und erhöht den Generationszähler des Slots.</summary>
    void DestroyShader(RStShaderHandle handle);

    /// <summary>Erstellt ein unveränderliches Pipeline-State-Object (PSO) für den angegebenen Shader und Render-State.</summary>
    RStPipelineHandle CreatePipeline(in RStPipelineDesc desc);

    /// <summary>Gibt den Pipeline-State frei und erhöht den Generationszähler des Slots.</summary>
    void DestroyPipeline(RStPipelineHandle handle);

    // --- Frame & RenderPass Execution ---

    /// <summary>Passt die SwapChain-Auflösung und den VSync-Modus nach einem Fenster-Resize an.</summary>
    void Resize(uint width, uint height, bool vsync);

    /// <summary>Startet einen neuen Frame, wartet auf den zugehörigen Frame-Fence und holt das nächste SwapChain-Image. Gibt <c>false</c> zurück, wenn der Frame übersprungen werden muss (z. B. minimiertes Fenster).</summary>
    bool BeginFrame();

    /// <summary>Startet einen Render-Pass (SwapChain-Backbuffer oder Offscreen-RenderTarget).</summary>
    void BeginRenderPass(in RStRenderPassDesc passDesc);

    /// <summary>Zeichnet einen vorsortierten <see cref="StRenderCommand"/>-Slice lock-free auf Worker <paramref name="workerIndex"/> auf.</summary>
    void ExecuteCommandSlice(uint workerIndex, ReadOnlySpan<StRenderCommand> commands);

    /// <summary>Schließt den aktuellen Render-Pass und führt alle aufgezeichneten Secondary CommandBuffers aus.</summary>
    void EndRenderPass();

    /// <summary>Beendet den Frame, schickt die Command-Queues ab und präsentiert das fertige Bild auf dem Bildschirm.</summary>
    void EndFrame();

    /// <summary>Blockiert die CPU, bis alle laufenden GPU-Operationen vollständig abgeschlossen sind (z. B. vor dem Shutdown oder Cleanup).</summary>
    void WaitIdle();
}
