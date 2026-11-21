using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using API.Commands.Enums;
using API.Handles.Components;

namespace API.Commands.Components;

[StructLayout(LayoutKind.Explicit, Size = 64)]
public unsafe struct StRenderCommand
{
    public const int MaxInlinePushBytes = 48;
    
    [FieldOffset(0)]  public ulong SortKey;
    [FieldOffset(8)]  public ERenderCommandType Type;
    [FieldOffset(10)] public byte PushDataSize;
    [FieldOffset(11)] public byte PushDataOffset;
    
    // Resource Handles (Index + Generation = 8 Bytes, Offset 12..19)
    [FieldOffset(12)] public RStPipelineHandle Pipeline;
    [FieldOffset(12)] public RStBufferHandle Buffer;
    [FieldOffset(12)] public RStMeshHandle Mesh;
    [FieldOffset(12)] public RStTextureHandle Texture;
    
    [FieldOffset(16)] public fixed byte PushData[MaxInlinePushBytes]; // Offset 16..63!
    
    // Draw- & Binding-Parameter (Offset 20..39)
    [FieldOffset(20)] public uint VertexCount;
    [FieldOffset(20)] public uint IndexCount;
    [FieldOffset(24)] public uint StartIndex;
    [FieldOffset(28)] public int BaseVertex;
    [FieldOffset(32)] public uint InstanceCount;
    [FieldOffset(36)] public uint Slot;
    [FieldOffset(40)] public uint StartInstance;
    
    
    /// <summary>
    /// Kopiert beliebige unmanaged Push-Daten (bis 48 Bytes) direkt inline in das Command.
    /// </summary>
    public static unsafe StRenderCommand CreatePushConstants<T>(in T data, byte offsetInBytes = 0) where T : unmanaged
    {
        int size = sizeof(T);
        if (size > MaxInlinePushBytes)
            throw new ArgumentOutOfRangeException(nameof(data), $"Inline PushConstants sind auf {MaxInlinePushBytes} Bytes begrenzt.");

        var cmd = new StRenderCommand
        {
            Type = ERenderCommandType.SetPushConstants,
            PushDataSize = (byte)size,
            PushDataOffset = offsetInBytes
        };

        fixed (T* pSrc = &data)
        {
            Unsafe.CopyBlock(cmd.PushData, pSrc, (uint)size);
        }
        return cmd;
    }
}