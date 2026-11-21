using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using API.Commands.Components;
using API.Commands.Enums;
using API.Components;
using API.Descriptors.Components;
using API.Enums;
using API.Handles.Components;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Vulkan;
using API.Descriptors.Enums;
using API.Interfaces;
using Pipline;
using Pipline.Features;
using Pipline.Optimizer;
using Silk.NET.Input;

namespace Sandbox;

[StructLayout(LayoutKind.Sequential)]
public struct SandboxVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector3 Color;
    public Vector2 TexCoord;

    public SandboxVertex(Vector3 pos, Vector3 normal, Vector3 color, Vector2 uv)
    {
        Position = pos;
        Normal = normal;
        Color = color;
        TexCoord = uv;
    }
}

[StructLayout(LayoutKind.Sequential)]
struct FrameCameraUBO
{
    public Matrix4x4 ViewProj;
    public Vector4 RotAndOffset;
    public Matrix4x4 LightViewProj;
    public Vector4 LightDirectionAndIntensity; // xyz = Richtung (zur Sonne!), w = Intensity
    public Vector4 LightColorAndAmbient;       // rgb = Farbe, a = AmbientStrength
}

// Passt exakt in die 48 Bytes Inline-PushConstants!
[StructLayout(LayoutKind.Sequential)]
public struct DrawPushData
{
    public Vector4 TintColor;   // 16 Bytes
    public Vector4 WorldOffsetAndRotZ;    // 16 Bytes (Offset 16: xyz = Offset, w = RotationY)
}

[StructLayout(LayoutKind.Sequential)]
public struct CubeInstanceData
{
    public Vector4 WorldOffsetAndRotZ; // xyz = Position, w = Rotation Z
    public Vector4 TintColor;          // rgb = Farbe, a = Rotationsgeschwindigkeit
}

class Program
{
    // 1. Vertex-Struct definieren
    
    // 2. Plattformübergreifende GLSL-Shader (funktionieren identisch auf Linux & Windows!)
    const string VertexShaderHlsl = """
                                    [[vk::binding(0, 0)]]
                                    cbuffer FrameCameraUBO : register(b0)
                                    {
                                        float4x4 ViewProj;
                                        float4   TimeAndParams;
                                        float4x4 LightViewProj;
                                        float4   LightDirectionAndIntensity;
                                        float4   LightColorAndAmbient;
                                    };

                                    struct InstanceData
                                    {
                                        float4 WorldOffsetAndRotZ;
                                        float4 TintColor;
                                    };

                                    // Hinweis: Prüfe kurz, auf welchem Vulkan-Binding dein StructuredBuffer Slot 0 liegt!
                                    [[vk::binding(1, 0)]]
                                    StructuredBuffer<InstanceData> Instances : register(t0);

                                    struct VSInput
                                    {
                                        float3 Position   : POSITION;
                                        float3 Normal     : NORMAL;
                                        float3 Color      : COLOR0;
                                        float2 TexCoord   : TEXCOORD0;
                                        uint   InstanceId : SV_InstanceID;
                                    };

                                    struct VSOutput
                                    {
                                        float4 Position    : SV_POSITION;
                                        float3 WorldNormal : NORMAL;
                                        float3 Color       : COLOR0;
                                        float2 TexCoord    : TEXCOORD0;
                                        float4 LightSpacePos : TEXCOORD1;
                                    };

                                    VSOutput VSMain(VSInput input)
                                    {
                                        InstanceData inst = Instances[input.InstanceId];

                                        // Rotation individuell pro Instanz berechnen (spart CPU-Updates für statische Positionen!)
                                        float rotZ = inst.WorldOffsetAndRotZ.w + TimeAndParams.x * inst.TintColor.a;
                                        float cz = cos(rotZ);
                                        float sz = sin(rotZ);

                                        float3 p = float3(
                                            input.Position.x * cz - input.Position.y * sz,
                                            input.Position.x * sz + input.Position.y * cz,
                                            input.Position.z
                                        );

                                        float3 n = float3(
                                            input.Normal.x * cz - input.Normal.y * sz,
                                            input.Normal.x * sz + input.Normal.y * cz,
                                            input.Normal.z
                                        );

                                        float4 worldPos = float4(p + inst.WorldOffsetAndRotZ.xyz, 1.0f);

                                        VSOutput output;
                                        output.Position = mul(ViewProj, worldPos);
                                        output.WorldNormal = normalize(n);
                                        output.Color = input.Color * inst.TintColor.rgb;
                                        output.TexCoord = input.TexCoord;
                                        output.LightSpacePos = mul(LightViewProj, worldPos);
                                        return output;
                                    }
                                    """;
    

    const string FragmentShaderHlsl = """
                                  [[vk::binding(0, 0)]]
                                  cbuffer FrameCameraUBO : register(b0)
                                  {
                                      float4x4 ViewProj;
                                      float4   TimeAndParams;
                                      float4x4 LightViewProj;
                                      float4   LightDirectionAndIntensity;
                                      float4   LightColorAndAmbient;
                                  };
                                  
                                  [[vk::binding(2, 0)]]
                                  Texture2D AlbedoTex : register(t1);

                                  [[vk::binding(3, 0)]]
                                  SamplerState AlbedoSampler : register(s0);
                                  
                                  [[vk::binding(4, 0)]]
                                  Texture2D ShadowMap : register(t2);

                                  [[vk::binding(5, 0)]]
                                  SamplerState ShadowSampler : register(s1);
                                  
                                  struct PSInput
                                  {
                                      float4 Position    : SV_POSITION;
                                      float3 WorldNormal : NORMAL;
                                      float3 Color       : COLOR0;
                                      float2 TexCoord    : TEXCOORD0;
                                      float4 LightSpacePos : TEXCOORD1;
                                  };
                                  
                                  float CalculateShadow(float4 lightSpacePos, float3 normal, float3 sunDir)
                                  {
                                      float3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
                                      float2 shadowUV = projCoords.xy * 0.5f + 0.5f;
                                  
                                      if (shadowUV.x < 0.0f || shadowUV.x > 1.0f || shadowUV.y < 0.0f || shadowUV.y > 1.0f)
                                          return 1.0f;
                                  
                                      float currentDepth = projCoords.z;
                                      float bias = max(0.0025f * (1.0f - dot(normal, sunDir)), 0.0005f);
                                  
                                      float shadow = 0.0f;
                                      float2 texelSize = 1.0f / 2048.0f;
                                      for (int x = -1; x <= 1; ++x)
                                      {
                                          for (int y = -1; y <= 1; ++y)
                                          {
                                              float closestDepth = ShadowMap.Sample(ShadowSampler, shadowUV + float2(x, y) * texelSize).r;
                                              shadow += (currentDepth + bias >= closestDepth) ? 1.0f : 0.15f;
                                          }
                                      }
                                      return shadow / 9.0f;
                                  }

                                  float4 PSMain(PSInput input) : SV_TARGET
                                  {
                                      float4 texColor = AlbedoTex.Sample(AlbedoSampler, input.TexCoord);
                                  
                                      float3 sunDir = normalize(-LightDirectionAndIntensity.xyz);
                                      float3 normal = normalize(input.WorldNormal);
                                  
                                      float ndotl = max(dot(normal, sunDir), 0.0f);
                                      float3 ambient = LightColorAndAmbient.rgb * LightColorAndAmbient.a;
                                      float3 diffuse = LightColorAndAmbient.rgb * LightDirectionAndIntensity.w * ndotl;
                                  
                                  
                                      float shadowFactor = CalculateShadow(input.LightSpacePos, normal, sunDir);
                                      float3 litColor = texColor.rgb * input.Color * (ambient + diffuse * shadowFactor);
                                      
                                      return float4(litColor, texColor.a);
                                  }
                                  
                                  """;
    

    static unsafe void Main(string[] args)
    {
        var camera = new Camera
        {
            Position = new Vector3(-160.0f, 0.0f, 45.0f), // 4 Meter zurück auf -X, 1.5 Meter hoch auf +Z
            YawDegrees = 0f,                           // Schaut direkt entlang +X zum Ursprung (0,0,0)
            PitchDegrees = -18.5f                      // Blickt leicht nach unten auf den Würfel
        };
        
        var options = WindowOptions.DefaultVulkan with
        {
            Size = new Vector2D<int>(720, 480),
            Title = "Slowest C# Renderer in the World - Sandbox (Silk.NET Vulkan 1.3)",
            ShouldSwapAutomatically = false,
            VSync = false,
            FramesPerSecond = 0,
            UpdatesPerSecond = 0,
            IsVisible = true
        };
        
        using var window = Window.Create(options);
        using var optimizer = new FrameOptimizer();

        VulkanRenderDevice? vkDevice = null;
        IRenderDevice? device = null;

        RStPipelineHandle pipeline = default;
        RStMeshHandle cubeMesh = default;
        RStTextureHandle myTextureHandle = default;
        RStBufferHandle frameUbo = default;
        
        RStBufferHandle instanceBuffer = default;
        const int gridDim = 67;
        int instanceCount = gridDim * gridDim * gridDim; // 1.000 Würfel
        int idx = 0;

        int workerCount = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
        
        using var threadDispatcher = new SimpleThreadDispatcher(workerCount);
        using var renderDispatcher = new MultiThreadedRenderDispatcher();
        
        FeatureRenderDevice? featureDevice = null;
        FxaaFeature? fxaaFeature = null;
        
        IInputContext? input = null;
        IKeyboard? primaryKeyboard = null;
        IMouse? primaryMouse = null;
        DirectionalShadowFeature? shadowFeature = null;
        
        window.Load += () =>
        {
            input = window.CreateInput();
            if (input.Keyboards.Count > 0)
                primaryKeyboard = input.Keyboards[0];

            if (input.Mice.Count > 0)
            {
                primaryMouse = input.Mice[0];
                primaryMouse.Scroll += (_, wheel) => camera.OnMouseScroll(wheel.Y * 5.0f);
            }
            
            var extPtrs = window.VkSurface!.GetRequiredExtensions(out var extCount);
            var extensions = new nint[extCount];
            for (int i = 0; i < extCount; i++)
                extensions[i] = (nint)extPtrs[i];

            RStNativeWindow nativeWindow;
            if (window.Native!.Win32.HasValue)
            {
                nativeWindow = new RStNativeWindow(
                    ENativeWindowKind.Win32,
                    window.Native.Win32.Value.Hwnd,
                    window.Native.Win32.Value.HInstance);
            }
            else if (window.Native.X11.HasValue)
            {
                nativeWindow = new RStNativeWindow(
                    ENativeWindowKind.X11,
                    (nint)window.Native.X11.Value.Window,
                    window.Native.X11.Value.Display);
            }
            else if (window.Native.Wayland.HasValue)
            {
                nativeWindow = new RStNativeWindow(
                    ENativeWindowKind.Wayland,
                    window.Native.Wayland.Value.Surface,
                    window.Native.Wayland.Value.Display);
            }
            else
            {
                throw new PlatformNotSupportedException("Fenstersystem wird nicht unterstützt.");
            }

            // 1. Device & SwapChain initialisieren
            vkDevice = new VulkanRenderDevice();
            vkDevice.Initialize(
                new RStDeviceDesc(
                    EGraphicsBackend.Vulkan,
                    ApplicationName: "FreshRender Sandbox",
                    MaxWorkerThreads: 8,
                    EnableDebugLayer: true,
                    RequiredInstanceExtensions: extensions),
                new RStSwapChainDesc(
                    Window: nativeWindow,
                    Width: window.Size.X,
                    Height: window.Size.Y,
                    ColorFormat: ETextureFormat.BGRA8_UNorm_SRgb,
                    VSync: false));
            vkDevice.SetActiveWorkerThreads(workerCount);

            featureDevice = new FeatureRenderDevice(vkDevice);
            shadowFeature = new DirectionalShadowFeature(2048);
            fxaaFeature = new FxaaFeature();
            featureDevice.AddFeature(shadowFeature); // Läuft als Erstes (schreibt ShadowMap)
            featureDevice.AddFeature(fxaaFeature);
            device = featureDevice;
            
            // 2. Prozedurale 64x64 Schachbrett-Textur direkt ins VRAM laden
            const int texSize = 64;
            byte[] pixels = new byte[texSize * texSize * 4];
            for (int y = 0; y < texSize; y++)
            {
                for (int x = 0; x < texSize; x++)
                {
                    bool isWhite = ((x / 8) + (y / 8)) % 2 == 0;
                    byte color = isWhite ? (byte)255 : (byte)50;
                    int idx = (y * texSize + x) * 4;
                    pixels[idx + 0] = color;
                    pixels[idx + 1] = color;
                    pixels[idx + 2] = color;
                    pixels[idx + 3] = 255;
                }
            }

            myTextureHandle = device.CreateTexture(
                new RStTextureDesc(texSize, 
                    texSize, 
                    ETextureFormat.RGBA8_UNorm_SRgb, 
                    Usage: ETextureUsage.Sampled),
                pixels);

            // 3. 24-Vertex-Würfel (saubere UVs pro Seite)
            SandboxVertex[] cubeVertices =
            [
                // Oben (+Z in UE5)
                new(new(-0.5f, -0.5f, 0.5f), new(0, 0, 1), new(1, 0, 0), new(0, 1)),
                new(new(0.5f, -0.5f, 0.5f), new(0, 0, 1), new(0, 1, 0), new(1, 1)),
                new(new(0.5f, 0.5f, 0.5f), new(0, 0, 1), new(0, 0, 1), new(1, 0)),
                new(new(-0.5f, 0.5f, 0.5f), new(0, 0, 1), new(1, 1, 0), new(0, 0)),
                // Unten (-Z in UE5)
                new(new(0.5f, -0.5f, -0.5f), new(0, 0, -1), new(1, 0, 1), new(0, 1)),
                new(new(-0.5f, -0.5f, -0.5f), new(0, 0, -1), new(0, 1, 1), new(1, 1)),
                new(new(-0.5f, 0.5f, -0.5f), new(0, 0, -1), new(1, 1, 1), new(1, 0)),
                new(new(0.5f, 0.5f, -0.5f), new(0, 0, -1), new(0.2f, 0.5f, 1), new(0, 0)),
                // Hinten (-X in UE5)
                new(new(-0.5f, -0.5f, -0.5f), new(-1, 0, 0), new(0, 1, 1), new(0, 1)),
                new(new(-0.5f, -0.5f, 0.5f), new(-1, 0, 0), new(1, 0, 0), new(1, 1)),
                new(new(-0.5f, 0.5f, 0.5f), new(-1, 0, 0), new(1, 1, 0), new(1, 0)),
                new(new(-0.5f, 0.5f, -0.5f), new(-1, 0, 0), new(1, 1, 1), new(0, 0)),
                // Vorne (+X in UE5)
                new(new(0.5f, -0.5f, 0.5f), new(1, 0, 0), new(0, 1, 0), new(0, 1)),
                new(new(0.5f, -0.5f, -0.5f), new(1, 0, 0), new(1, 0, 1), new(1, 1)),
                new(new(0.5f, 0.5f, -0.5f), new(1, 0, 0), new(0.2f, 0.5f, 1), new(1, 0)),
                new(new(0.5f, 0.5f, 0.5f), new(1, 0, 0), new(0, 0, 1), new(0, 0)),
                // Rechts (+Y in UE5)
                new(new(-0.5f, 0.5f, 0.5f), new(0, 1, 0), new(1, 1, 0), new(0, 1)),
                new(new(0.5f, 0.5f, 0.5f), new(0, 1, 0), new(0, 0, 1), new(1, 1)),
                new(new(0.5f, 0.5f, -0.5f), new(0, 1, 0), new(0.2f, 0.5f, 1), new(1, 0)),
                new(new(-0.5f, 0.5f, -0.5f), new(0, 1, 0), new(1, 1, 1), new(0, 0)),
                // Links (-Y in UE5)
                new(new(-0.5f, -0.5f, -0.5f), new(0, -1, 0), new(0, 1, 1), new(0, 1)),
                new(new(0.5f, -0.5f, -0.5f), new(0, -1, 0), new(1, 0, 1), new(1, 1)),
                new(new(0.5f, -0.5f, 0.5f), new(0, -1, 0), new(0, 1, 0), new(1, 0)),
                new(new(-0.5f, -0.5f, 0.5f), new(0, -1, 0), new(1, 0, 0), new(0, 0)),
            ];

            uint[] cubeIndices =
            [
                 0,  1,  2,  2,  3,  0, // Front
                 4,  5,  6,  6,  7,  4, // Back
                 8,  9, 10, 10, 11,  8, // Left
                12, 13, 14, 14, 15, 12, // Right
                16, 17, 18, 18, 19, 16, // Top
                20, 21, 22, 22, 23, 20  // Bottom
            ];

            cubeMesh = device.CreateMesh(
                MemoryMarshal.AsBytes(cubeVertices.AsSpan()),
                (uint)Marshal.SizeOf<SandboxVertex>(),
                MemoryMarshal.AsBytes(cubeIndices.AsSpan()),
                EIndexFormat.UInt32);

            // 4. Shader & Pipeline
            var shader = device.CreateShader(new RStShaderDesc(VertexShaderHlsl, FragmentShaderHlsl));

            RStVertexElement[] vertexLayout =
            [
                new(0, EVertexFormat.Float3, 0),  // Position
                new(1, EVertexFormat.Float3, 12), // Normal
                new(2, EVertexFormat.Float3, 24), // Color
                new(3, EVertexFormat.Float2, 36)  // TexCoord
            ];

            pipeline = device.CreatePipeline(new RStPipelineDesc(
                Shader: shader,
                VertexLayout: vertexLayout,
                VertexStride: (uint)Marshal.SizeOf<SandboxVertex>(),
                RenderTargetFormat: ETextureFormat.BGRA8_UNorm_SRgb,
                DepthFormat: ETextureFormat.D32_Float,
                CullMode: ECullMode.Back,
                DepthTestEnable: true,
                DepthWriteEnable: true,
                PushConstantBytes: 0,
                ConstantBufferSlotCount: 1,
                StructuredBufferSlotCount: 1,
                TextureSlotCount: 2));

            // 5. Dynamischer Frame-UBO
            frameUbo = device.CreateBuffer(new RStBufferDesc(
                SizeInBytes: Marshal.SizeOf<FrameCameraUBO>(),
                StrideInBytes: 0,
                Usage: EBufferUsage.Constant,
                IsDynamic: true));
            
            instanceCount = gridDim * gridDim * gridDim; // 1.000 Würfel!
            var instances = new CubeInstanceData[instanceCount];

            idx = 0;
            for (int x = 0; x < gridDim; x++)
            for (int y = 0; y < gridDim; y++)
            for (int z = 0; z < gridDim; z++)
            {
                instances[idx++] = new CubeInstanceData
                {
                    WorldOffsetAndRotZ = new Vector4(
                        x * 2.2f,                  // Tiefe (+X)
                        (y - gridDim * 0.5f) * 2.2f, // Links/Rechts (Y)
                        (z - gridDim * 0.5f) * 2.2f, // Höhe (Z)
                        (x + y + z) * 0.5f),         // Start-Winkel
                    TintColor = new Vector4(
                        0.4f + 0.6f * (x / (float)gridDim),
                        0.4f + 0.6f * (y / (float)gridDim),
                        0.4f + 0.6f * (z / (float)gridDim),
                        ((idx % 2 == 0) ? 1.0f : -1.0f) * 1.2f) // Drehgeschwindigkeit im Alpha-Kanal
                };
            }

            instanceBuffer = device.CreateBuffer(new RStBufferDesc(
                SizeInBytes: instanceCount * Marshal.SizeOf<CubeInstanceData>(),
                StrideInBytes: Marshal.SizeOf<CubeInstanceData>(),
                Usage: EBufferUsage.Structured,
                IsDynamic: false)); // Statischer VRAM-Upload, weil der Shader die Rotation über TimeAndParams berechnet!

            device.UpdateBuffer(instanceBuffer, MemoryMarshal.AsBytes(instances.AsSpan()));

        };

        float totalTime = 0f;

        const int drawCallCount = 200; // 200 Draw-Calls * 5 Commands = 1.000 Commands -> triggert Multi-Threading!
        uint instancesPerDraw = (uint)(instanceCount / drawCallCount);
        
        var fpsStopwatch = Stopwatch.StartNew();
        int frameCounter = 0;
        
        window.Render += delta =>
        {
            if (primaryKeyboard != null && primaryMouse != null)
            {
                camera.Update((float)delta, primaryKeyboard, primaryMouse);
            }
            
            if (device == null || !cubeMesh.IsValid) return;
            
            // 1. FPS & Framezeit über Stopwatch in der Konsole ausgeben (alle 1.0 Sekunden)
            frameCounter++;
            double elapsedSeconds = fpsStopwatch.Elapsed.TotalSeconds;
            if (elapsedSeconds >= 1.0)
            {
                double fps = frameCounter / elapsedSeconds;
                double msPerFrame = (elapsedSeconds / frameCounter) * 1000.0;
                Console.WriteLine($"[Vulkan MT | {workerCount} Threads | {drawCallCount} Draws | {instanceCount:N0} Cubes] FPS: {fps:F1} ({msPerFrame:F2} ms)");
                frameCounter = 0;
                fpsStopwatch.Restart();
            }
            
            totalTime += (float)delta * 1.5f; // ~85 Grad pro Sekunde
            
            if (!device.BeginFrame())
                return;
            
            if (featureDevice == null || fxaaFeature == null || shadowFeature == null)
                return;
            
            fxaaFeature.EnsureTargets(device, (uint)window.Size.X, (uint)window.Size.Y);
            
            float aspect = (float)window.Size.X / Math.Max(1, window.Size.Y);
            var viewProj = camera.GetViewProjectionMatrix(aspect);
            
            var mainLight = new RStDirectionalLight
            {
                // Schräges Sonnenlicht von links-oben, damit man die Schlagschatten von der Kamera aus sieht!
                Direction = Vector3.Normalize(new Vector3(0.25f, 0.85f, -0.46f)),
                Intensity = 1.4f,
                Color = new Vector3(1.00f, 0.96f, 0.90f),
                AmbientStrength = 0.15f
            };
            
            var frameContext = new RStFrameContext
            {
                FrameIndex = device.CurrentFrameIndex,
                TotalTime = totalTime,
                DeltaTime = (float)delta,
                ViewportWidth = (uint)window.Size.X,
                ViewportHeight = (uint)window.Size.Y,
                ViewProj = viewProj,
                CameraPosition = camera.Position,
                MainLight = mainLight,
                ShadowCasterMesh = cubeMesh,
                ShadowInstanceBuffer = instanceBuffer,
                ShadowInstanceCount = (uint)instanceCount,
                SceneColorTexture = fxaaFeature.SceneColorTarget
            };
            
            // 1. Shadow-Pass ZUERST ausführen, damit ShadowMap und LightViewProj für diesen Frame bereitstehen!
            shadowFeature.Execute(device, in frameContext);

            // 2. Haupt-UBO inklusive der aktuellen LightViewProj-Matrix hochladen
            var ubo = new FrameCameraUBO
            {
                ViewProj = viewProj,
                RotAndOffset = new Vector4(totalTime, 0f, 0f, 0f),
                LightViewProj = shadowFeature.LightViewProj,
                LightDirectionAndIntensity = new Vector4(mainLight.Direction, mainLight.Intensity),
                LightColorAndAmbient = new Vector4(mainLight.Color, mainLight.AmbientStrength)
            };
            device.UpdateBuffer(frameUbo, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref ubo, 1)));
            
            // 3. 6 Commands pro Draw-Call (inkl. ShadowMap auf Texture Slot 1)
            int totalCommands = drawCallCount * 6;
            var commandArray = ArrayPool<StRenderCommand>.Shared.Rent(totalCommands);
            try
            {
                var frameCommands = commandArray.AsSpan(0, totalCommands);
                int cmdIdx = 0;

                for (int i = 0; i < drawCallCount; i++)
                {
                    ulong baseKey = (ulong)i << 8;

                    frameCommands[cmdIdx++] = new StRenderCommand { SortKey = baseKey | 0, Type = ERenderCommandType.SetPipeline, Pipeline = pipeline };
                    frameCommands[cmdIdx++] = new StRenderCommand { SortKey = baseKey | 1, Type = ERenderCommandType.SetConstantBuffer, Slot = 0, Buffer = frameUbo };
                    frameCommands[cmdIdx++] = new StRenderCommand { SortKey = baseKey | 2, Type = ERenderCommandType.SetStructuredBuffer, Slot = 0, Buffer = instanceBuffer };
                    frameCommands[cmdIdx++] = new StRenderCommand { SortKey = baseKey | 3, Type = ERenderCommandType.SetTexture, Slot = 0, Texture = myTextureHandle };
                    frameCommands[cmdIdx++] = new StRenderCommand { SortKey = baseKey | 4, Type = ERenderCommandType.SetTexture, Slot = 1, Texture = shadowFeature.ShadowMap };
                    frameCommands[cmdIdx++] = new StRenderCommand
                    {
                        SortKey = baseKey | 5,
                        Type = ERenderCommandType.DrawMesh,
                        Mesh = cubeMesh,
                        IndexCount = 36,
                        InstanceCount = instancesPerDraw,
                        StartInstance = (uint)i * instancesPerDraw
                    };
                }

                // 4. Hauptszene mit Schatten in das Offscreen-Target rendern
                var scenePass = new RStRenderPassDesc(
                    new RStColorAttachmentDesc(fxaaFeature.SceneColorTarget, ELoadOp.Clear, new Vector4(0.08f, 0.10f, 0.14f, 1.0f)),
                    new RStDepthAttachmentDesc(fxaaFeature.SceneDepthTarget, ELoadOp.Clear, 0.0f));

                device.BeginRenderPass(scenePass);
                renderDispatcher.Dispatch(device, frameCommands, threadDispatcher);
                device.EndRenderPass();

                // 5. FXAA-Pass glättet das fertige Bild und schreibt es in die Swapchain
                fxaaFeature.Execute(device, in frameContext);
                device.EndFrame();
            }
            finally
            {
                ArrayPool<StRenderCommand>.Shared.Return(commandArray);
            }
        };
        window.Resize += newSize =>
        {
            device?.Resize((uint)newSize.X, (uint)newSize.Y, false);
        };
        window.Closing += () =>
        {
            input?.Dispose();
            device?.Dispose();
        };
        window.Run();
    }
}
