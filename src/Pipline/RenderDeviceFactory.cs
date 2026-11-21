using System;
using API;
using API.Enums;
using API.Interfaces;
using Vulkan;

namespace Pipline;

/// <summary>
/// Zentrale Factory zur Erstellung eines <see cref="FeatureRenderDevice"/> für das gewünschte
/// <see cref="EGraphicsBackend"/>.
/// Hinweis: Aktuell läuft nur <see cref="EGraphicsBackend.Vulkan"/> mit Lichtgeschwindigkeit –
/// alle anderen Backends glänzen durch kompromisslose 0 % CPU- und GPU-Last.
/// </summary>
public static class RenderDeviceFactory
{
    /// <summary>
    /// Instanziiert das gewählte Low-Level-Backend und verpackt es in ein modulares <see cref="FeatureRenderDevice"/>.
    /// </summary>
    /// <param name="backend">Die gewünschte Grafik-API.</param>
    /// <returns>Ein einsatzbereites <see cref="FeatureRenderDevice"/> (sofern du Vulkan gewählt hast).</returns>
    /// <exception cref="NotImplementedException">Wird geworfen, wenn du eines der noch ungebauten Backends auswählst oder ein Mac-Nutzer bist.</exception>
    public static FeatureRenderDevice Create(EGraphicsBackend backend)
    {
        IRenderDevice rawDevice = backend switch
        {
            EGraphicsBackend.Vulkan => new VulkanRenderDevice(),
            EGraphicsBackend.Direct3D12 =>
                // Glückwunsch, du hast die DirectX-Klasse gefunden!
                // Aktueller Status: Läuft mit exakt 0 FPS bei 0 % CPU-Last.
                throw new NotImplementedException("Hier könnte Ihre DirectX-Pipeline stehen."),
            EGraphicsBackend.OpenGL4_5 =>
                // Glückwunsch, du hast die OpenGL4_5-Klasse gefunden!
                // Aktueller Status: Läuft mit exakt 0 FPS bei 0 % CPU-Last.
                throw new NotImplementedException("Hier könnte Ihre OpenGL4_5-Pipeline stehen."),
            EGraphicsBackend.OpenGL4_6 =>
                // Glückwunsch, du hast die OpenGL4_6-Klasse gefunden!
                // Aktueller Status: Läuft mit exakt 0 FPS bei 0 % CPU-Last.
                throw new NotImplementedException("Hier könnte Ihre OpenGL4_6-Pipeline stehen."),
            EGraphicsBackend.Metal => throw new NotImplementedException(
                "Metal is not supported because nobody on our team owns a Mac. " +
                "If you really want Apple support, feel free to donate a Mac via GitHub Sponsors."
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(backend))
        };

        return new FeatureRenderDevice(rawDevice);
    }
}
