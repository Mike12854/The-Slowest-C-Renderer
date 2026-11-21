using API.Commands.Enums;

namespace API.Descriptors.Components;

/// <summary>
/// Beschreibt einen Texture-Sampler (Filterung, UV-Wrapping, Anisotropie und Mipmap-Bias)
/// (<c>readonly record struct</c>, 0 Byte GC-Overhead).
/// </summary>
/// <param name="Filter">Sampling-Filtermodus für Minification, Magnification und Mipmapping (Standard: <see cref="ETextureFilter.Anisotropic"/>).</param>
/// <param name="AddressU">Adressierungsmodus für die horizontale U-Koordinate.</param>
/// <param name="AddressV">Adressierungsmodus für die vertikale V-Koordinate.</param>
/// <param name="AddressW">Adressierungsmodus für die W-Koordinate (relevant bei <see cref="ETextureDimension.Texture3D"/>).</param>
/// <param name="MaxAnisotropy">Maximale anisotrope Filterstufe (typisch 1 bis 16; wird nur bei <see cref="ETextureFilter.Anisotropic"/> ausgewertet).</param>
/// <param name="MipLodBias">Verschiebung des berechneten Mipmap-Levels (negative Werte = schärfer/mehr Aliasing, positive = unschärfer).</param>
public readonly  record struct RStSamplerDesc(
    ETextureFilter Filter = ETextureFilter.Anisotropic,
    ETextureAddressMode AddressU = ETextureAddressMode.Wrap,
    ETextureAddressMode AddressV = ETextureAddressMode.Wrap,
    ETextureAddressMode AddressW = ETextureAddressMode.Wrap,
    int MaxAnisotropy = 16,
    float MipLodBias = 0.0f
)
{
    public static RStSamplerDesc DefaultLinear => new(ETextureFilter.Linear);
    public static RStSamplerDesc DefaultPoint => new(ETextureFilter.Point);
    public static RStSamplerDesc DefaultAnisotropic => new(ETextureFilter.Anisotropic);
}