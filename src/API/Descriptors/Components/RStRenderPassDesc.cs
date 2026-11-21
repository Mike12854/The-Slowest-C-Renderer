using System.Numerics;
using API.Descriptors.Enums;

namespace API.Descriptors.Components;

/// <summary>
/// Beschreibt einen RenderPass. Ist <see cref="ColorAttachment"/>.Target <c>default</c> (Invalid),
/// wird automatisch in das aktuelle SwapChain-Backbuffer-Target gerendert.
/// </summary>
/// <param name="ColorAttachment">Konfiguration des primären Color-Targets (oder <c>default</c> für die SwapChain).</param>
/// <param name="DepthAttachment">Optionale Konfiguration des Depth/Stencil-Targets.</param>
/// <param name="RenderWidth">Viewport-/Render-Breite in Pixeln (<c>0</c> = übernimmt automatisch die Breite des Targets bzw. der SwapChain).</param>
/// <param name="RenderHeight">Viewport-/Render-Höhe in Pixeln (<c>0</c> = übernimmt automatisch die Höhe des Targets bzw. der SwapChain).</param>
public readonly record struct RStRenderPassDesc(
    RStColorAttachmentDesc ColorAttachment,
    RStDepthAttachmentDesc DepthAttachment = default,
    uint RenderWidth = 0,
    uint RenderHeight = 0
)
{
    public static RStRenderPassDesc SwapChainDefault(Vector4 clearColor, float clearDepth = 0.0f) =>
        new(
            new RStColorAttachmentDesc(default, ELoadOp.Clear, clearColor),
            new RStDepthAttachmentDesc(default, ELoadOp.Clear, clearDepth)
        );
}