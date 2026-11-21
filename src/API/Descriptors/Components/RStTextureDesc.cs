using System;
using API.Commands.Enums;
using API.Enums;
using ETextureFormat = API.Commands.Enums.ETextureFormat;

namespace API.Descriptors.Components;

/// <summary>
/// Beschreibt die Spezifikation einer GPU-Textur (Dimensionen, Format, Verwendungszweck, Mipmaps, MSAA und Sampler)
/// (<c>readonly record struct</c>, 0 Byte GC-Overhead).
/// </summary>
/// <param name="Width">Breite der Textur in Pixeln (Mip-Level 0).</param>
/// <param name="Height">Höhe der Textur in Pixeln (Mip-Level 0).</param>
/// <param name="Format">Pixelformat der Textur (Color, HDR oder Depth/Stencil).</param>
/// <param name="Flags">Bitmaske der GPU-Verwendungszwecke (z. B. <see cref="ETextureFlags.ShaderResource"/>, <see cref="ETextureFlags.RenderTarget"/>).</param>
/// <param name="Dimension">Dimensionalität der Textur (Standard: <see cref="ETextureDimension.Texture2D"/>).</param>
/// <param name="Depth">Tiefe in Pixeln (nur relevant für <see cref="ETextureDimension.Texture3D"/>, sonst <c>1</c>).</param>
/// <param name="ArraySize">Anzahl der Array-Slices (bei <see cref="ETextureDimension.TextureCube"/> ein Vielfaches von <c>6</c>).</param>
/// <param name="MipLevels">Anzahl der Mipmap-Stufen (<c>1</c> = keine Mipmaps; siehe <see cref="CalculateMipLevels"/> für volle Chain).</param>
/// <param name="SampleCount">MSAA-Sample-Anzahl (<c>1</c> = kein MSAA; <c>2</c>, <c>4</c> oder <c>8</c> für Multisampling).</param>
/// <param name="Sampler">Zugehörige Sampler-Konfiguration für das Auslesen im Shader.</param>
/// <param name="Usage">Primärer Speicher-/Zugriffsmodus der Textur.</param>
public readonly record struct RStTextureDesc(int Width,
    int Height,
    ETextureFormat Format,
    ETextureFlags Flags = ETextureFlags.ShaderResource,
    ETextureDimension Dimension = ETextureDimension.Texture2D,
    int Depth = 1,
    int ArraySize = 1,
    int MipLevels = 1,
    int SampleCount = 1,
    RStSamplerDesc Sampler = default,
    ETextureUsage Usage = ETextureUsage.Sampled
)
{
    /// <summary>Erstellt eine Standard-2D-Textur (Standard-Sampler: <see cref="RStSamplerDesc.DefaultAnisotropic"/>).</summary>
    public static RStTextureDesc New2D(
        int width,
        int height,
        ETextureFormat format,
        ETextureFlags flags = ETextureFlags.ShaderResource,
        int mipLevels = 1,
        RStSamplerDesc? sampler = null) =>
        new(width, height, format, flags, ETextureDimension.Texture2D, 1, 1, mipLevels, 1, sampler ?? RStSamplerDesc.DefaultAnisotropic);

    /// <summary>Erstellt ein Offscreen-RenderTarget (Standard: <see cref="ETextureFormat.RGBA16_Float"/> für HDR, optional direkt im Shader samplebar).</summary>
    public static RStTextureDesc NewRenderTarget(
        int width,
        int height,
        ETextureFormat format = ETextureFormat.RGBA16_Float,
        bool sampled = true) =>
        new(width, height, format,
            sampled ? ETextureFlags.RenderTarget | ETextureFlags.ShaderResource : ETextureFlags.RenderTarget,
            Sampler: RStSamplerDesc.DefaultLinear);

    /// <summary>Erstellt einen Depth-Buffer (bei <paramref name="sampled"/> = <c>true</c> ideal für ShadowMaps mit Point-Sampling).</summary>
    public static RStTextureDesc NewDepthBuffer(
        int width,
        int height,
        ETextureFormat format = ETextureFormat.D32_Float,
        bool sampled = false) =>
        new(width, height, format,
            sampled ? ETextureFlags.DepthStencil | ETextureFlags.ShaderResource : ETextureFlags.DepthStencil,
            Sampler: RStSamplerDesc.DefaultPoint);

    /// <summary>Berechnet die maximale Mip-Chain-Anzahl für <paramref name="width"/> × <paramref name="height"/>.</summary>
    public static int CalculateMipLevels(int width, int height)
        => (int)Math.Floor(Math.Log2(Math.Max(width, height))) + 1;
}