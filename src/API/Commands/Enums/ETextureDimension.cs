namespace API.Commands.Enums;

/// <summary>
/// Definiert die Dimensionalität und den Typ einer GPU-Textur-Ressource.
/// </summary>
public enum ETextureDimension : byte
{
    Texture1D, // 1D-Lookup-Tabellen / Gradienten
    Texture2D, // Standard-2D-Texturen, RenderTargets & ShadowMaps
    Texture3D, // Volumetrische Texturen (z. B. Noise, Voxel, LUTs)
    TextureCube // 6-seitige Cubemaps (Skybox, Environment Maps, Point-Light Shadows)
}