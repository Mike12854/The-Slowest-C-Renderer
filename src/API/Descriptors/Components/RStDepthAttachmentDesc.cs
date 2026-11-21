using API.Descriptors.Enums;
using API.Handles.Components;

namespace API.Descriptors.Components;

/// <summary>
/// Beschreibt ein Depth/Stencil-Attachment für den Start eines Render-Passes
/// (<c>readonly record struct</c>, 0 Byte GC-Overhead).
/// </summary>
/// <param name="Target">Handle der Tiefentextur (muss mit <see cref="ETextureFlags.DepthStencil"/> erstellt worden sein).</param>
/// <param name="LoadOp">Aktion zu Beginn des Render-Passes (z. B. <see cref="ELoadOp.Clear"/> oder <see cref="ELoadOp.Load"/>).</param>
/// <param name="ClearDepth">Initialer Tiefenwert beim Löschen (Standard: <c>1.0f</c> für Standard-Depth; bei Reversed-Z <c>0.0f</c> übergeben).</param>
/// <param name="ClearStencil">Initialer 8-Bit-Stencil-Wert beim Löschen (Standard: <c>0</c>).</param>

public readonly record struct RStDepthAttachmentDesc(
    RStTextureHandle Target,
    ELoadOp LoadOp = ELoadOp.Clear,
    float ClearDepth = 1.0f,
    byte ClearStencil = 0
);