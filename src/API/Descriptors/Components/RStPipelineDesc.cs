using System;
using API.Commands.Enums;
using API.Descriptors.Enums;
using API.Handles.Components;
using ECullMode = API.Enums.ECullMode;
using EPrimitiveTopology = API.Enums.EPrimitiveTopology;

namespace API.Descriptors.Components;

/// <summary>
/// Beschreibt den vollständigen Grafik-Pipeline-State (PSO) inklusive Shader, Vertex-Layout,
/// Rasterizer-, Depth/Stencil-, Blend-States sowie Resource-Binding-Slots.
/// </summary>
/// <param name="Shader">Handle des kompilierten Shader-Programms (Vertex + Fragment/Pixel).</param>
/// <param name="VertexLayout">Beschreibung der einzelnen Vertex-Attribute (Position, Normal, UV etc.).</param>
/// <param name="VertexStride">Größe eines einzelnen Vertex in Bytes (muss zum gebundenen Vertex-Buffer passen).</param>
/// <param name="RenderTargetFormat">Erwartetes Pixelformat des Ziel-Color-Attachments (Standard: <see cref="ETextureFormat.BGRA8_UNorm_SRgb"/>).</param>
/// <param name="DepthFormat">Erwartetes Format des Depth-Buffers (<see cref="ETextureFormat.None"/>, falls kein Depth-Attachment genutzt wird).</param>
/// <param name="Topology">Geometrie-Topologie der Eingabedaten (Standard: <see cref="EPrimitiveTopology.TriangleList"/>).</param>
/// <param name="CullMode">Face-Culling-Modus (Standard: <see cref="ECullMode.Back"/>).</param>
/// <param name="DepthTestEnable">Aktiviert den Tiefenvergleich gegen den Depth-Buffer.</param>
/// <param name="DepthWriteEnable">Erlaubt das Schreiben neuer Tiefenwerte in den Depth-Buffer (für transparente Objekte deaktivieren).</param>
/// <param name="BlendMode">Color-Blending-Modus für das Output-Attachment (Standard: <see cref="EBlendMode.Opaque"/>).</param>
/// <param name="PushConstantBytes">Größe der Inline-Push-Constants in Bytes (max. <see cref="StRenderCommand.MaxInlinePushBytes"/> = 48 Bytes).</param>
/// <param name="ConstantBufferSlotCount">Anzahl der erwarteten Constant-Buffer-Bindings (UBO / CBV).</param>
/// <param name="StructuredBufferSlotCount">Anzahl der erwarteten Structured-Buffer-Bindings (SSBO / SRV).</param>
/// <param name="TextureSlotCount">Anzahl der erwarteten Textur-/Sampler-Bindings.</param>
/// <param name="DebugName">Optionaler Bezeichner für GPU-Profiler und Validation Layers (z. B. RenderDoc / Nsight).</param>
public readonly record struct RStPipelineDesc(
    RStShaderHandle Shader,
    ReadOnlyMemory<RStVertexElement> VertexLayout,
    uint VertexStride,
    ETextureFormat RenderTargetFormat = ETextureFormat.BGRA8_UNorm_SRgb,
    ETextureFormat DepthFormat = ETextureFormat.D32_Float,
    EPrimitiveTopology Topology = EPrimitiveTopology.TriangleList,
    ECullMode CullMode = ECullMode.Back,
    bool DepthTestEnable = true,
    bool DepthWriteEnable = true,
    EBlendMode BlendMode = EBlendMode.Opaque,
    uint PushConstantBytes = 0,
    uint ConstantBufferSlotCount = 1,
    uint StructuredBufferSlotCount = 1,
    uint TextureSlotCount = 1,
    string? DebugName = null
);