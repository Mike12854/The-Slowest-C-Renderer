namespace API.Enums;

/// <summary>
/// Auswahl des zugrunde liegenden Low-Level-Grafik-Backends.
/// </summary>
public enum EGraphicsBackend : byte
{
    Vulkan,     // Vulkan 1.3 Only (Dynamic Rendering / Synchronization2) – keine Rückwärtskompatibilität für Museums-Hardware!
    Direct3D12, // Geplant für Windows-Systeme => steht noch aus (wirft aktuell hochperformant NotImplementedException).
    OpenGL4_5,  // Nutzt Direct State Access (DSA) und unterstützt GPUs ab 2014 => steht noch aus.
    OpenGL4_6,  // Unterstützung für SPIR-V und geringeren CPU-Overhead (ab 2017) => steht noch aus.
    Metal       // Apple-spezifische API. Kann von uns nicht getestet werden, weil wir kein Apple-System haben (und lieber Gold kaufen)!
}