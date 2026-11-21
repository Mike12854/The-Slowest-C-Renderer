using System;
using API.Components;

namespace API.Interfaces;

/// <summary>
/// Erweiterungspunkt für optionale Render-Pässe (Schatten, PostFX, oder ein Raytracing-FPS-Vernichter).
/// Wer hier Fehler einbaut, fliegt ohne Auffangnetz direkt aus der Kurve.
/// </summary>
public interface IRenderFeature : IDisposable
{
    /// <summary>
    /// Einmalige Einrichtung im Coldpath (erstellt benötigte RenderTargets, Shader, Pipelines und Buffer auf dem <paramref name="device"/>).
    /// </summary>
    void Initialize(IRenderDevice device);

    /// <summary>
    /// Führt den Render-Pass pro Frame im Hotpath aus (liest Inputs aus <paramref name="context"/> wie z. B. <c>SceneColorTexture</c> und zeichnet das Ergebnis).
    /// </summary>
    void Execute(IRenderDevice device, in RStFrameContext context);
}