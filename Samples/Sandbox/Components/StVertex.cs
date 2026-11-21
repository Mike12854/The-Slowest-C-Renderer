using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Sandbox.Components;

[StructLayout(LayoutKind.Sequential)]
public struct StVertex
{
    public Vector3 Position;
    public Vector3 Color;
    public Vector2 TexCoord;
    
    
    
    public static VertexInputBindingDescription GetBindingDescription() => new()
    {
        Binding = 0,
        Stride = (uint)Marshal.SizeOf<StVertex>(),
        InputRate = VertexInputRate.Vertex
    };

    public static VertexInputAttributeDescription[] GetAttributeDescriptions() =>
    [
        new() { Binding = 0, Location = 0, Format = Format.R32G32B32Sfloat, Offset = (uint)Marshal.OffsetOf<StVertex>(nameof(Position)) },
        new() { Binding = 0, Location = 1, Format = Format.R32G32B32Sfloat, Offset = (uint)Marshal.OffsetOf<StVertex>(nameof(Color)) },
        new() { Binding = 0, Location = 2, Format = Format.R32G32Sfloat, Offset = (uint)Marshal.OffsetOf<StVertex>(nameof(TexCoord)) }
    ];
}