using System;
using System.Numerics;
using API.Commands.Components;
using API.Handles.Components;

namespace API.Components;

/// <summary>
/// Stack-allokierter Frame-Kontext (<c>ref struct</c>, 0 Byte GC-Overhead), der alle relevanten
/// Kamera-, Licht-, RenderTarget- und Command-Daten eines Frames durch die Feature-Pipeline
/// (z. B. Shadow-Pass -> Main-Pass -> FXAA) reicht.
/// </summary>
public ref struct RStFrameContext
{
    // Frame- & Viewport-Basisdaten
    public uint FrameIndex;
    public float TotalTime;
    public float DeltaTime;
    public uint ViewportWidth;
    public uint ViewportHeight;
    
    // Kamera & Beleuchtung
    public Matrix4x4 ViewProj;
    public Vector3 CameraPosition;
    public RStDirectionalLight MainLight;

    // Input für den Shadow-Pass (damit das Feature weiß, welche Geometrie Schatten wirft)
    public RStMeshHandle ShadowCasterMesh;
    public RStBufferHandle ShadowInstanceBuffer;
    public uint ShadowInstanceCount;
    
    // Output vom Shadow-Pass -> Input für den Haupt-Shader
    public Matrix4x4 LightViewProj;
    public RStTextureHandle ShadowMapTexture;
    
    // Output vom Haupt-Pass -> Input für PostFX (FXAA)
    public RStTextureHandle SceneColorTexture;
    
    // Zero-Copy-View auf die aktuellen Scene-Draw-Commands (z. B. für Shadow-Map-Re-Rendering)
    public ReadOnlySpan<StRenderCommand> SceneDrawCommands;
    
    
}