using System.Numerics;
using System.Runtime.InteropServices;
using API.Commands.Components;
using API.Commands.Enums;
using API.Components;
using API.Descriptors.Components;
using API.Enums;
using API.Handles.Components;
using API.Interfaces;

namespace Pipline.Features;

/// <summary>
/// Post-Processing-Feature für Fast Approximate Anti-Aliasing (FXAA 3.11-Stil).
/// Verwaltet die Offscreen-RenderTargets (<see cref="SceneColorTarget"/> &amp; <see cref="SceneDepthTarget"/>),
/// in die die Hauptszene zeichnet, und glättet Kanten anschließend per vertexlosem
/// Fullscreen-Triangle (<c>SV_VertexID</c>, 3 Vertices) direkt in den SwapChain-Backbuffer.
/// </summary>
public class FxaaFeature : IRenderFeature
{
    private RStShaderHandle _fxaaShader;
    private RStPipelineHandle _fxaaPipeline;
    private RStBufferHandle _paramsUbo;
    private RStTextureHandle _sceneColorTarget;
    private RStTextureHandle _sceneDepthTarget;

    private uint _currentWidth;
    private uint _currentHeight;

    /// <summary>Offscreen-Color-Target (<see cref="ETextureFormat.BGRA8_UNorm_SRgb"/>), in das der Haupt-Render-Pass zeichnen muss.</summary>
    public RStTextureHandle SceneColorTarget => _sceneColorTarget;

    /// <summary>Zugehöriger Offscreen-Depth-Buffer (<see cref="ETextureFormat.D32_Float"/>) für den Haupt-Render-Pass.</summary>
    public RStTextureHandle SceneDepthTarget => _sceneDepthTarget;

    private IRenderDevice? _renderDevice;

    [StructLayout(LayoutKind.Sequential)]
    private struct FxaaParamsUBO
    {
        public Vector4 InverseScreenSize; // x = 1/width, y = 1/height, z = qualitySubpix, w = edgeThreshold
    }

    private const string FxaaVertexShaderHlsl = """
                                                struct VSOutput
                                                {
                                                    float4 Position : SV_POSITION;
                                                    float2 TexCoord : TEXCOORD0;
                                                };

                                                // Fullscreen-Triangle ohne Vertex-Buffer (3 Vertices decken den gesamten Screen ab)
                                                VSOutput VSMain(uint vertexId : SV_VertexID)
                                                {
                                                    VSOutput output;
                                                    output.TexCoord = float2((vertexId << 1) & 2, vertexId & 2);
                                                    output.Position = float4(output.TexCoord * 2.0f - 1.0f, 0.5f, 1.0f);
                                                    return output;
                                                }
                                                """;

    private const string FxaaFragmentShaderHlsl = """
                                                  [[vk::binding(0, 0)]]
                                                  cbuffer FxaaParams : register(b0)
                                                  {
                                                      float4 InverseScreenSize; // xy = 1.0 / resolution
                                                  };

                                                  [[vk::binding(2, 0)]]
                                                  SamplerState LinearSampler : register(s2);

                                                  [[vk::binding(1, 0)]]
                                                  Texture2D SceneTex : register(t1);

                                                  struct PSInput
                                                  {
                                                      float4 Position : SV_POSITION;
                                                      float2 TexCoord : TEXCOORD0;
                                                  };

                                                  float Luma(float3 color)
                                                  {
                                                      return dot(color, float3(0.299f, 0.587f, 0.114f));
                                                  }

                                                  float4 PSMain(PSInput input) : SV_TARGET
                                                  {
                                                      float2 uv = input.TexCoord;
                                                      float2 texel = InverseScreenSize.xy;

                                                      float3 rgbM  = SceneTex.Sample(LinearSampler, uv).rgb;
                                                      float3 rgbNW = SceneTex.Sample(LinearSampler, uv + float2(-1.0f, -1.0f) * texel).rgb;
                                                      float3 rgbNE = SceneTex.Sample(LinearSampler, uv + float2( 1.0f, -1.0f) * texel).rgb;
                                                      float3 rgbSW = SceneTex.Sample(LinearSampler, uv + float2(-1.0f,  1.0f) * texel).rgb;
                                                      float3 rgbSE = SceneTex.Sample(LinearSampler, uv + float2( 1.0f,  1.0f) * texel).rgb;

                                                      float lumaM  = Luma(rgbM);
                                                      float lumaNW = Luma(rgbNW);
                                                      float lumaNE = Luma(rgbNE);
                                                      float lumaSW = Luma(rgbSW);
                                                      float lumaSE = Luma(rgbSE);

                                                      float lumaMin = min(lumaM, min(min(lumaNW, lumaNE), min(lumaSW, lumaSE)));
                                                      float lumaMax = max(lumaM, max(max(lumaNW, lumaNE), max(lumaSW, lumaSE)));
                                                      float lumaRange = lumaMax - lumaMin;

                                                      // Kein Kontrast-Unterschied -> Pixel unverändert lassen (erhält volle Schärfe!)
                                                      if (lumaRange < max(0.0312f, lumaMax * 0.125f))
                                                          return float4(rgbM, 1.0f);

                                                      // Kantenrichtung berechnen
                                                      float2 dir;
                                                      dir.x = -((lumaNW + lumaNE) - (lumaSW + lumaSE));
                                                      dir.y =  ((lumaNW + lumaSW) - (lumaNE + lumaSE));

                                                      float dirReduce = max((lumaNW + lumaNE + lumaSW + lumaSE) * (0.25f * 0.125f), 1.0f / 128.0f);
                                                      float rcpDirMin = 1.0f / (min(abs(dir.x), abs(dir.y)) + dirReduce);
                                                      dir = clamp(dir * rcpDirMin, float2(-8.0f, -8.0f), float2(8.0f, 8.0f)) * texel;

                                                      float3 rgbA = 0.5f * (
                                                          SceneTex.Sample(LinearSampler, uv + dir * (1.0f / 3.0f - 0.5f)).rgb +
                                                          SceneTex.Sample(LinearSampler, uv + dir * (2.0f / 3.0f - 0.5f)).rgb);

                                                      float3 rgbB = rgbA * 0.5f + 0.25f * (
                                                          SceneTex.Sample(LinearSampler, uv + dir * -0.5f).rgb +
                                                          SceneTex.Sample(LinearSampler, uv + dir *  0.5f).rgb);

                                                      float lumaB = Luma(rgbB);
                                                      float3 finalRgb = (lumaB < lumaMin || lumaB > lumaMax) ? rgbA : rgbB;

                                                      return float4(finalRgb, 1.0f);
                                                  }
                                                  """;

    /// <inheritdoc />
    public void Initialize(IRenderDevice device)
    {
        _renderDevice = device;

        _fxaaShader = device.CreateShader(new RStShaderDesc(
            VertexSource: FxaaVertexShaderHlsl,
            FragmentSource: FxaaFragmentShaderHlsl,
            DebugName: "FXAA_Shader"));

        _fxaaPipeline = device.CreatePipeline(new RStPipelineDesc(
            Shader: _fxaaShader,
            VertexLayout: ReadOnlyMemory<RStVertexElement>.Empty,
            VertexStride: 0,
            RenderTargetFormat: ETextureFormat.BGRA8_UNorm_SRgb,
            DepthFormat: ETextureFormat.D32_Float,
            CullMode: ECullMode.None,
            DepthTestEnable: false,
            DepthWriteEnable: false,
            ConstantBufferSlotCount: 1,
            StructuredBufferSlotCount: 0,
            TextureSlotCount: 1,
            DebugName: "FXAA_Pipeline"));

        _paramsUbo = device.CreateBuffer(new RStBufferDesc(
            SizeInBytes: Marshal.SizeOf<FxaaParamsUBO>(),
            StrideInBytes: Marshal.SizeOf<FxaaParamsUBO>(),
            Usage: EBufferUsage.Constant,
            IsDynamic: true));
    }

    /// <summary>
    /// Stellt sicher, dass die Offscreen-Targets exakt der aktuellen Viewport-Größe (<paramref name="width"/> × <paramref name="height"/>) entsprechen.
    /// Bei Größenänderung werden alte Texturen sicher über die Deferred-Destruction-Queue freigegeben.
    /// </summary>
    public void EnsureTargets(IRenderDevice device, uint width, uint height)
    {
        width = Math.Max(1u, width);
        height = Math.Max(1u, height);

        if (_sceneColorTarget.IsValid && _currentWidth == width && _currentHeight == height)
            return;

        // Nutzt die frame-sichere Deferred-Destruction-Queue des RenderDevices
        if (_sceneColorTarget.IsValid) device.DestroyTexture(_sceneColorTarget);
        if (_sceneDepthTarget.IsValid) device.DestroyTexture(_sceneDepthTarget);
        _currentWidth = width;
        _currentHeight = height;
// Hauptszene rendert in BGRA8_UNorm_SRgb (passend zur Haupt-Pipeline!)
        _sceneColorTarget = device.CreateTexture(
            RStTextureDesc.NewRenderTarget((int)width, (int)height, ETextureFormat.BGRA8_UNorm_SRgb, sampled: true)
                with
                {
                    Usage = ETextureUsage.RenderTarget | ETextureUsage.Sampled
                });
        _sceneDepthTarget = device.CreateTexture(
            RStTextureDesc.NewDepthBuffer((int)width, (int)height, ETextureFormat.D32_Float, sampled: false)
                with
                {
                    Usage = ETextureUsage.DepthStencil
                });
    }

    /// 
    public void Execute(IRenderDevice device, in RStFrameContext context)
    {
        if (!_sceneColorTarget.IsValid)
            return;
        var ubo = new FxaaParamsUBO
        {
            InverseScreenSize = new Vector4(1.0f / _currentWidth, 1.0f / _currentHeight, 0.75f, 0.166f)
        };
        device.UpdateBuffer(_paramsUbo, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref ubo, 1)));
        Span<StRenderCommand> cmds = stackalloc StRenderCommand[4];
        cmds[0] = new StRenderCommand { SortKey = 0, Type = ERenderCommandType.SetPipeline, Pipeline = _fxaaPipeline };
        cmds[1] = new StRenderCommand
            { SortKey = 1, Type = ERenderCommandType.SetConstantBuffer, Slot = 0, Buffer = _paramsUbo };
        cmds[2] = new StRenderCommand
            { SortKey = 2, Type = ERenderCommandType.SetTexture, Slot = 0, Texture = _sceneColorTarget };
        cmds[3] = new StRenderCommand
            { SortKey = 3, Type = ERenderCommandType.Draw, VertexCount = 3, InstanceCount = 1 };
// Finaler Pass direkt in die Swapchain!
        device.BeginRenderPass(RStRenderPassDesc.SwapChainDefault(new Vector4(0f, 0f, 0f, 1f)));
        device.ExecuteCommandSlice(0, cmds);
        device.EndRenderPass();
    }

    /// 
    public void Dispose()
    {
// Hinweis: Falls Dispose vom FeatureRenderDevice aufgerufen wird, hat das Device bereits WaitIdle() ausgeführt
        if (_renderDevice == null) return;
        if (_sceneColorTarget.IsValid) _renderDevice.DestroyTexture(_sceneColorTarget);
        if (_sceneDepthTarget.IsValid) _renderDevice.DestroyTexture(_sceneDepthTarget);
        if (_fxaaPipeline.IsValid) _renderDevice.DestroyPipeline(_fxaaPipeline);
        if (_fxaaShader.IsValid) _renderDevice.DestroyShader(_fxaaShader);
        if (_paramsUbo.IsValid) _renderDevice.DestroyBuffer(_paramsUbo);
    }
}