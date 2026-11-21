namespace API.Commands.Enums;

/// <summary>
/// Definiert die Operation eines 64-Byte <see cref="StRenderCommand"/> und bestimmt,
/// welcher Bereich der Union (Handle, PushData oder Draw-Parameter) ausgewertet wird.
/// </summary>
public enum ERenderCommandType : byte
{
    NoOp = 0, //// Macht nichts (aber das immerhin mit 3.000 FPS)
    SetPipeline = 1,
    SetVertexBuffer = 2,
    SetIndexBuffer = 3,
    SetConstantBuffer = 4,
    SetStructuredBuffer = 5,
    SetTexture = 6,
    SetPushConstants = 7,
    Draw = 8,
    DrawIndexed = 9,
    DrawMesh = 10
}