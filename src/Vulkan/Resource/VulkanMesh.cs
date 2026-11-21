using API.Descriptors.Components;
using API.Enums;
using Silk.NET.Vulkan;
using Vulkan.Core;

namespace Vulkan.Resource;

/// <summary>
/// Bündelt einen statischen Vertex- und optionalen Index-<see cref="VulkanBuffer"/> im schnellen
/// <c>DEVICE_LOCAL</c>-VRAM (<c>IsDynamic = false</c>) für das Rendering über <c>ERenderCommandType.DrawMesh</c>.
/// </summary>
internal sealed class VulkanMesh : IDisposable
{
    /// <summary>GPU-Vertex-Buffer des Meshs (<c>DEVICE_LOCAL</c>).</summary>
    public VulkanBuffer VertexBuffer { get; }

    /// <summary>Optionaler GPU-Index-Buffer des Meshs (<c>DEVICE_LOCAL</c>, leer bei nicht-indizierten Meshes).</summary>
    public VulkanBuffer IndexBuffer { get; }

    /// <summary>Gesamtanzahl der Indices (berechnet aus <c>indexData.Length</c> und <see cref="GetIndexFormat"/>).</summary>
    public uint IndexCount { get; private set; }

    /// <summary>Natives Vulkan-<see cref="Silk.NET.Vulkan.IndexType"/> des Index-Buffers.</summary>
    public IndexType IndexType { get; private set; }

    private uint VertexStride;
    private EIndexFormat _indexFormat;

    /// <summary>Gibt das API-übergreifende Index-Format (<see cref="EIndexFormat.UInt16"/> oder <see cref="EIndexFormat.UInt32"/>) zurück.</summary>
    public EIndexFormat GetIndexFormat() => _indexFormat;

    public VulkanMesh(VulkanContext context)
    {
        VertexBuffer = new VulkanBuffer(context);
        IndexBuffer = new VulkanBuffer(context);
    }

    /// <summary>
    /// Lädt Vertex- und optionale Index-Daten per Staging-Transfer in den <c>DEVICE_LOCAL</c>-VRAM der GPU.
    /// </summary>
    public void Initialize(
        ReadOnlySpan<byte> vertexData, uint vertexStride,
        ReadOnlySpan<byte> indexData, EIndexFormat indexFormat,
        CommandPool transferPool)
    {
        VertexStride = vertexStride;
        _indexFormat = indexFormat;
        IndexCount = indexFormat == EIndexFormat.UInt16
            ? (uint)(indexData.Length / sizeof(ushort))
            : (uint)(indexData.Length / sizeof(uint));

        VertexBuffer.Initialize(
            new RStBufferDesc(vertexData.Length, (int)vertexStride, EBufferUsage.Vertex, IsDynamic: false),
            vertexData,
            transferPool);

        if (!indexData.IsEmpty)
        {
            int indexStride = indexFormat == EIndexFormat.UInt16 ? sizeof(ushort) : sizeof(uint);
            IndexBuffer.Initialize(
                new RStBufferDesc(indexData.Length, indexStride, EBufferUsage.Index, IsDynamic: false),
                indexData,
                transferPool);
        }
    }

    /// <summary>Gibt Index- und Vertex-Buffer deterministisch frei.</summary>
    public void Dispose()
    {
        IndexBuffer.Dispose();
        VertexBuffer.Dispose();
    }
}
