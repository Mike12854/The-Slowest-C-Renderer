using API.Enums;

namespace API.Descriptors.Components;

/// <summary>
/// Konfiguriert die Initialisierung des Grafik-Devices und der zugrunde liegenden API.
/// </summary>
/// <param name="Backend">Gewünschte Grafik-API (z. B. Vulkan, OpenGL oder DirectX).</param>
/// <param name="EnableDebugLayer">
/// Aktiviert native Validation Layers / Debug-Outputs (z. B. <c>VK_LAYER_KHRONOS_validation</c>).
/// Im Release-Build aus Performancegründen auf <c>false</c> setzen.
/// </param>
/// <param name="ApplicationName">Anwendungsname für den Grafiktreiber (z. B. für GPU-Profiler wie RenderDoc/Nsight).</param>
/// <param name="RequiredInstanceExtensions">Optionale, plattformspezifische Instanz-Extensions (z. B. für das Window-Surface-Handling).</param>
/// <param name="MaxWorkerThreads">Maximale Anzahl vorbereiteter Command-Pools für paralleles Command-Recording (Standard: <c>16</c>).</param>

public readonly record struct RStDeviceDesc(
    EGraphicsBackend Backend,
    bool EnableDebugLayer,
    string ApplicationName,
    nint[]? RequiredInstanceExtensions = null,
    int MaxWorkerThreads = 16);