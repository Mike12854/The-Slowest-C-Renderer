using System.Numerics;
using System.Runtime.InteropServices;
using API.Commands.Components;
using API.Commands.Enums;
using API.Components;
using API.Descriptors.Components;
using API.Descriptors.Enums;
using API.Enums;
using API.Handles.Components;
using API.Interfaces;

namespace Pipline.Features;

/// <summary>
/// Rendert eine orthografische Reversed-Z Directional-Shadow-Map (<see cref="ETextureFormat.D32_Float"/>)
/// für instanzierte Geometrie und stellt <see cref="ShadowMap"/> sowie <see cref="LightViewProj"/>
/// für den Haupt-Render-Pass bereit.
/// </summary>
public class DirectionalShadowFeature : IRenderFeature
{
    private IRenderDevice? _device;
    private RStShaderHandle _shadowShader;
    private RStPipelineHandle _shadowPipeline;
    private RStBufferHandle _shadowUbo;
    private RStTextureHandle _shadowMap;

    private readonly uint _resolution;

    /// <summary>Normalisierte Strahlrichtung des gerichteten Lichts (wird im <c>Execute</c> aus <see cref="RStFrameContext.MainLight"/> übernommen).</summary>
    public Vector3 LightDirection { get; set; } = Vector3.Normalize(new Vector3(0.55f, 0.40f, -0.73f));

    /// <summary>Weltkoordinaten-Zentrum, auf das die orthografische Schatten-Kamera ausgerichtet wird.</summary>
    public Vector3 ShadowCenter { get; set; } = new(70f, 0f, 0f);

    /// <summary>Halbe Kantenlänge (Radius) des orthografischen Schatten-Frustums in Welteinheiten.</summary>
    public float ShadowOrthoRadius { get; set; } = 120.0f;

    /// <summary>Handle der erzeugten <see cref="ETextureFormat.D32_Float"/>-Shadow-Map für das Sampling im Haupt-Shader.</summary>
    public RStTextureHandle ShadowMap => _shadowMap;

    /// <summary>Kombinierte View-Projection-Matrix der Lichtquelle (Reversed-Z) für den aktuellen Frame.</summary>
    public Matrix4x4 LightViewProj { get; private set; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShadowPassUBO
    {
        public Matrix4x4 LightViewProj;
        public Vector4 TimeAndParams; // x = TotalTime
    }

    private const string ShadowVertexShaderHlsl = """
                                                  [[vk::binding(0, 0)]]
                                                  cbuffer ShadowUBO : register(b0)
                                                  {
                                                      float4x4 LightViewProj;
                                                      float4 TimeAndParams;
                                                  };

                                                  struct InstanceData
                                                  {
                                                      float4 WorldOffsetAndRotZ;
                                                      float4 TintColor;
                                                  };

                                                  [[vk::binding(1, 0)]]
                                                  StructuredBuffer<InstanceData> Instances : register(t0);

                                                  struct VSInput
                                                  {
                                                      float3 Position   : POSITION;
                                                      uint   InstanceId : SV_InstanceID;
                                                  };

                                                  float4 VSMain(VSInput input) : SV_POSITION
                                                  {
                                                      InstanceData inst = Instances[input.InstanceId];

                                                      float rotZ = inst.WorldOffsetAndRotZ.w + TimeAndParams.x * inst.TintColor.a;
                                                      float cz = cos(rotZ);
                                                      float sz = sin(rotZ);

                                                      float3 p = float3(
                                                          input.Position.x * cz - input.Position.y * sz,
                                                          input.Position.x * sz + input.Position.y * cz,
                                                          input.Position.z
                                                      );

                                                      float4 worldPos = float4(p + inst.WorldOffsetAndRotZ.xyz, 1.0f);
                                                      return mul(LightViewProj, worldPos);
                                                  }
                                                  """;

    private const string ShadowFragmentShaderHlsl = """
                                                    void PSMain()
                                                    {
                                                        // Depth-Only Pass: GPU schreibt Tiefe automatisch in D32_Float
                                                    }
                                                    """;

    /// <summary>
    /// Erstellt ein neues Directional-Shadow-Feature mit quadratischer Shadow-Map-Auflösung (Standard: 2048 × 2048).
    /// </summary>
    public DirectionalShadowFeature(uint resolution = 2048)
    {
        _resolution = resolution;
    }

    /// <inheritdoc />
    public void Initialize(IRenderDevice device)
    {
        _device = device;

        _shadowMap = device.CreateTexture(
            RStTextureDesc.NewDepthBuffer((int)_resolution, (int)_resolution, ETextureFormat.D32_Float, sampled: true)
                with { Usage = ETextureUsage.DepthStencil | ETextureUsage.Sampled });

        _shadowShader = device.CreateShader(new RStShaderDesc(
            VertexSource: ShadowVertexShaderHlsl,
            FragmentSource: ShadowFragmentShaderHlsl,
            DebugName: "DirectionalShadow_Shader"));

        // VertexLayout passend zu SandboxVertex (44 Bytes Stride, hier wird nur Position at Offset 0 benötigt)
        RStVertexElement[] layout =
        [
            new(0, EVertexFormat.Float3, 0)
        ];

        _shadowPipeline = device.CreatePipeline(new RStPipelineDesc(
            Shader: _shadowShader,
            VertexLayout: layout,
            VertexStride: 44,
            RenderTargetFormat: ETextureFormat.None, // Depth-Only!
            DepthFormat: ETextureFormat.D32_Float,
            CullMode: ECullMode.Back, // Tipp: ECullMode.Front verhindert Shadow-Acne bei geschlossenen Meshes
            DepthTestEnable: true,
            DepthWriteEnable: true,
            ConstantBufferSlotCount: 1,
            StructuredBufferSlotCount: 1,
            TextureSlotCount: 0,
            DebugName: "DirectionalShadow_Pipeline"));

        _shadowUbo = device.CreateBuffer(new RStBufferDesc(
            SizeInBytes: Marshal.SizeOf<ShadowPassUBO>(),
            StrideInBytes: Marshal.SizeOf<ShadowPassUBO>(),
            Usage: EBufferUsage.Constant,
            IsDynamic: true));
    }

    /// <inheritdoc />
    public void Execute(IRenderDevice device, in RStFrameContext context)
    {
        if (context.MainLight.Direction.LengthSquared() > 0.0001f)
            LightDirection = Vector3.Normalize(context.MainLight.Direction);

        if (!context.ShadowCasterMesh.IsValid || !context.ShadowInstanceBuffer.IsValid || context.ShadowInstanceCount == 0)
            return;

        LightViewProj = ComputeLightViewProj();

        var ubo = new ShadowPassUBO
        {
            LightViewProj = LightViewProj,
            TimeAndParams = new Vector4(context.TotalTime, 0f, 0f, 0f)
        };
        device.UpdateBuffer(_shadowUbo, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref ubo, 1)));

        Span<StRenderCommand> cmds = stackalloc StRenderCommand[4];
        cmds[0] = new StRenderCommand { SortKey = 0, Type = ERenderCommandType.SetPipeline, Pipeline = _shadowPipeline };
        cmds[1] = new StRenderCommand { SortKey = 1, Type = ERenderCommandType.SetConstantBuffer, Slot = 0, Buffer = _shadowUbo };
        cmds[2] = new StRenderCommand { SortKey = 2, Type = ERenderCommandType.SetStructuredBuffer, Slot = 0, Buffer = context.ShadowInstanceBuffer };
        cmds[3] = new StRenderCommand
        {
            SortKey = 3,
            Type = ERenderCommandType.DrawMesh,
            Mesh = context.ShadowCasterMesh,
            InstanceCount = context.ShadowInstanceCount,
            IndexCount = 36,
        };

        // Reversed-Z Depth-Only Pass (ClearDepth = 0.0f)
        var shadowPass = new RStRenderPassDesc(
            ColorAttachment: default,
            DepthAttachment: new RStDepthAttachmentDesc(_shadowMap, ELoadOp.Clear, 0.0f),
            RenderWidth: _resolution,
            RenderHeight: _resolution);

        device.BeginRenderPass(shadowPass);
        device.ExecuteCommandSlice(0, cmds);
        device.EndRenderPass();
    }

    private Matrix4x4 ComputeLightViewProj()
    {
        Vector3 dir = Vector3.Normalize(LightDirection);
        Vector3 lightPos = ShadowCenter - dir * (ShadowOrthoRadius * 1.5f);

        Vector3 upRef = MathF.Abs(dir.Z) > 0.99f ? Vector3.UnitX : Vector3.UnitZ;
        Vector3 r = Vector3.Normalize(Vector3.Cross(upRef, dir));
        Vector3 u = Vector3.Cross(dir, r);

        var view = new Matrix4x4(
            r.X, -u.X, dir.X, 0.0f,
            r.Y, -u.Y, dir.Y, 0.0f,
            r.Z, -u.Z, dir.Z, 0.0f,
            -Vector3.Dot(r, lightPos),
            Vector3.Dot(u, lightPos),
            -Vector3.Dot(dir, lightPos),
            1.0f
        );

        // Orthografische Reversed-Z Projektion (Near -> 1.0, Far -> 0.0)
        float size = ShadowOrthoRadius;
        float near = 0.1f;
        float far = ShadowOrthoRadius * 3.5f;
        float invRange = 1.0f / (near - far);

        var proj = new Matrix4x4(
            1.0f / size, 0.0f,        0.0f,            0.0f,
            0.0f,        -1.0f / size, 0.0f,            0.0f,
            0.0f,        0.0f,        invRange,        0.0f,
            0.0f,        0.0f,       -far * invRange,  1.0f
        );
        return view * proj;
    }
    /// 
    public void Dispose()
    {
        if (_device == null) return;
        if (_shadowMap.IsValid) _device.DestroyTexture(_shadowMap);
        if (_shadowPipeline.IsValid) _device.DestroyPipeline(_shadowPipeline);
        if (_shadowShader.IsValid) _device.DestroyShader(_shadowShader);
        if (_shadowUbo.IsValid) _device.DestroyBuffer(_shadowUbo);
    }
}