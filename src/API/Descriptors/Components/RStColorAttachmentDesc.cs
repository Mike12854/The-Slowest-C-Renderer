using System.Numerics;
using API.Descriptors.Enums;
using API.Handles.Components;

namespace API.Descriptors.Components;

/// <summary>
/// Beschreibt ein Color-Attachment (RenderTarget) für den Start eines Render-Passes
/// (<c>readonly record struct</c>, 0 Byte GC-Overhead).
/// </summary>
/// <param name="Target">Handle der Ziel-Textur (muss mit <see cref="ETextureFlags.RenderTarget"/> erstellt worden sein).</param>
/// <param name="LoadOp">Aktion zu Beginn des Render-Passes (z. B. <see cref="ELoadOp.Clear"/> oder <see cref="ELoadOp.Load"/> für Overlays).</param>
/// <param name="ClearColor">RGBA-Löschfarbe, falls <paramref name="LoadOp"/> auf <see cref="ELoadOp.Clear"/> steht (Standard: Schwarz/Transparent).</param>
public readonly record struct RStColorAttachmentDesc(
    RStTextureHandle Target,
    ELoadOp LoadOp = ELoadOp.Clear,
    Vector4 ClearColor = default
);