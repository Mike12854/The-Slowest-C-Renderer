namespace API.Commands.Enums;

/// <summary>
/// Bestimmt die Ausführungsreihenfolge innerhalb desselben SortKey-Buckets (teuer -> billig -> Draw).
/// </summary>
public enum ECommandSortOrder : byte
{
    SetPipeline       = 0,
    SetConstantBuffer = 1,
    SetTexture        = 2,
    SetPushConstants  = 3,
    Draw              = 4
}