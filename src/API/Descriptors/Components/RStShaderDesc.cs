using API.Enums;

namespace API.Descriptors.Components;

/// <summary>
/// Beschreibt Shader-Quellcode (z. B. HLSL/GLSL als String), der vom Backend kompiliert wird.
/// Für vorkompiliertes SPIR-V / DXIL bietet <see cref="IRenderDevice"/> <c>CreateShaderFromBytecode</c>.
/// </summary>
/// <param name="VertexSource">Quellcode des Vertex-Shaders.</param>
/// <param name="FragmentSource">Quellcode des Fragment-/Pixel-Shaders.</param>
/// <param name="VertexEntryPoint">Einstiegsfunktion im Vertex-Shader (Standard: <c>"VSMain"</c>; bei GLSL in der Regel <c>"main"</c>).</param>
/// <param name="FragmentEntryPoint">Einstiegsfunktion im Fragment-/Pixel-Shader (Standard: <c>"PSMain"</c>; bei GLSL in der Regel <c>"main"</c>).</param>
/// <param name="DebugName">Optionaler Bezeichner für Shader-Fehlermeldungen und GPU-Profiler (z. B. RenderDoc / Nsight).</param>
public readonly record struct RStShaderDesc(
    string VertexSource,
    string FragmentSource,
    string VertexEntryPoint = "VSMain",
    string FragmentEntryPoint = "PSMain",
    string? DebugName = null
    );