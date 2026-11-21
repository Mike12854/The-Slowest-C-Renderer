using System;
using System.Collections.Generic;
using API.Commands.Components;
using API.Components;
using API.Descriptors.Components;
using API.Enums;
using API.Handles.Components;
using API.Interfaces;

namespace Pipline;

/// <summary>
/// Decorator um ein beliebiges <see cref="IRenderDevice"/> (Vulkan, OpenGL, DirectX), der modulare
/// <see cref="IRenderFeature"/>-Pässe (z. B. Shadows, FXAA) verwaltet und ausführt,
/// ohne dass der Core der Grafik-Backends angepasst werden muss.
/// </summary>
/// <param name="inner">Das zugrunde liegende Low-Level-RenderDevice.</param>
public sealed class FeatureRenderDevice(IRenderDevice inner) : IRenderDevice
{
    private readonly List<IRenderFeature> _features = new();

    /// <summary>
    /// Initialisiert das <paramref name="feature"/> einmalig auf dem inneren Device und hängt es
    /// ans Ende der Ausführungs-Pipeline an.
    /// </summary>
    public void AddFeature(IRenderFeature feature)
    {
        feature.Initialize(inner);
        _features.Add(feature);
    }

    /// <summary>
    /// Führt alle registrierten Features in Registrierungsreihenfolge allokationsfrei (<c>for</c>-Loop)
    /// für den aktuellen Frame aus.
    /// </summary>
    public void ExecuteFeatures(in RStFrameContext context)
    {
        for (int i = 0; i < _features.Count; i++)
            _features[i].Execute(inner, in context);
    }

    /// <inheritdoc />
    public string BackendName => inner.BackendName;

    /// <inheritdoc />
    public uint MaxWorkerThreads => inner.MaxWorkerThreads;

    /// <inheritdoc />
    public uint FramesInFlight => inner.FramesInFlight;

    /// <inheritdoc />
    public uint CurrentFrameIndex => inner.CurrentFrameIndex;

    /// <inheritdoc />
    public RStBufferHandle CreateBuffer(in RStBufferDesc desc, ReadOnlySpan<byte> initialData = default) => inner.CreateBuffer(in desc, initialData);

    /// <inheritdoc />
    public void UpdateBuffer(RStBufferHandle handle, ReadOnlySpan<byte> data, uint offsetInBytes = 0) => inner.UpdateBuffer(handle, data, offsetInBytes);

    /// <inheritdoc />
    public void DestroyBuffer(RStBufferHandle handle) => inner.DestroyBuffer(handle);

    /// <inheritdoc />
    public RStMeshHandle CreateMesh(ReadOnlySpan<byte> vertexData, uint vertexStride, ReadOnlySpan<byte> indexData, EIndexFormat indexFormat, string? debugName = null) => inner.CreateMesh(vertexData, vertexStride, indexData, indexFormat, debugName);

    /// <inheritdoc />
    public void DestroyMesh(RStMeshHandle handle) => inner.DestroyMesh(handle);

    /// <inheritdoc />
    public RStTextureHandle CreateTexture(in RStTextureDesc desc, ReadOnlySpan<byte> initialData = default) => inner.CreateTexture(in desc, initialData);

    /// <inheritdoc />
    public void DestroyTexture(RStTextureHandle handle) => inner.DestroyTexture(handle);

    /// <inheritdoc />
    public RStShaderHandle CreateShader(in RStShaderDesc desc) => inner.CreateShader(in desc);

    /// <inheritdoc />
    public RStShaderHandle CreateShaderFromBytecode(ReadOnlySpan<byte> vertexBytecode, ReadOnlySpan<byte> fragmentBytecode, string vertexEntryPoint = "VSMain", string fragmentEntryPoint = "PSMain", string? debugName = null) => inner.CreateShaderFromBytecode(vertexBytecode, fragmentBytecode, vertexEntryPoint, fragmentEntryPoint, debugName);

    /// <inheritdoc />
    public void DestroyShader(RStShaderHandle handle) => inner.DestroyShader(handle);

    /// <inheritdoc />
    public RStPipelineHandle CreatePipeline(in RStPipelineDesc desc) => inner.CreatePipeline(in desc);

    /// <inheritdoc />
    public void DestroyPipeline(RStPipelineHandle handle) => inner.DestroyPipeline(handle);

    /// <inheritdoc />
    public void Resize(uint width, uint height, bool vsync) => inner.Resize(width, height, vsync);

    /// <inheritdoc />
    public bool BeginFrame() => inner.BeginFrame();

    /// <inheritdoc />
    public void BeginRenderPass(in RStRenderPassDesc passDesc) => inner.BeginRenderPass(in passDesc);

    /// <inheritdoc />
    public void ExecuteCommandSlice(uint workerIndex, ReadOnlySpan<StRenderCommand> commands) => inner.ExecuteCommandSlice(workerIndex, commands);

    /// <inheritdoc />
    public void EndRenderPass() => inner.EndRenderPass();

    /// <inheritdoc />
    public void EndFrame() => inner.EndFrame();

    /// <inheritdoc />
    public void WaitIdle() => inner.WaitIdle();

    /// <summary>
    /// Wartet auf das Ende aller laufenden GPU-Befehle, gibt alle Features in umgekehrter Reihenfolge
    /// frei und zerstört anschließend das zugrunde liegende <see cref="IRenderDevice"/>.
    /// </summary>
    public void Dispose()
    {
        inner.WaitIdle();
        for (int i = _features.Count - 1; i >= 0; i--)
            _features[i].Dispose();
        _features.Clear();
        inner.Dispose();
    }
}
