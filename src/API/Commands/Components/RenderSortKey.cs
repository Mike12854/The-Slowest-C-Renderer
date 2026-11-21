using API.Commands.Enums;

namespace API.Commands.Components;

/// <summary>
/// Hilft der Render-Pipeline beim Packen des 64-Bit SortKeys.
/// Layout: [8 Bit Layer][16 Bit Pipeline][16 Bit Material/Texture][24 Bit Depth]
/// </summary>
public static class RenderSortKey
{
    public static ulong Pack(
        byte layer, 
        ushort pipelineIndex, 
        ushort textureIndex, 
        uint depth24Bit,
        ECommandSortOrder order = ECommandSortOrder.Draw)
    {
        // Die untersten 3 Bits der Tiefe zugunsten der deterministischen Befehlsreihenfolge nutzen
        uint depth21Bit = (depth24Bit >> 3) & 0x001F_FFFFu;
        
        return ((ulong)layer << 56)
               | ((ulong)pipelineIndex << 40)
               | ((ulong)textureIndex << 24)
               | ((ulong)depth21Bit << 3)
               | ((ulong)order & 0x7u);
    }
}