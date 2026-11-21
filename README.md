# SlowestRenderer 🐢

Ein extrem schlanker, datenorientierter Low-Level-Renderer in modernem C# – inspiriert von der Philosophie hinter **BepuPhysics**.

Kein aufgeblähter Scene-Graph. Kein integrierter Asset-Loader. Kein GC-Müll im Render-Loop. Du lieferst rohe Daten und Shader über eine direkte API, **SlowestRenderer** malt Bildchen.

---

## Warum „SlowestRenderer“?

Weil Understatement alles ist. Unter der Haube läuft der Hotpath komplett ohne Heap-Allokationen (`Span<T>`, `Unsafe`, Structs), ohne LINQ und ohne Babysitting. Machst du einen Fehler, schmiert die Anwendung sofort und ehrlich ab.

### Aktuelle Benchmarks (RTX 3090 | Vulkan Multithreaded | 8 Threads)
* **27.000 Würfel** (30 × 30 × 30 Instanzen): **~3.000 FPS** (0,33 ms)
* **300.763 Würfel** (67 × 67 × 67 Instanzen, 3,6 Mio. Dreiecke): **~184 FPS** (5,44 ms – Hardware-Rasterizer-Limit der GPU)
* **CPU-Last:** **2–4 %** pro Kern

---

## Features & Architektur

* **Multi-Backend-Abstraktion:**
  * ✅ **Vulkan** (Multithreaded Command-Buffer-Recording)
  * 🚧 **OpenGL 4.5 / 4.6** (Direct State Access) (Wirft aktuell hochperformant eine `NotImplementedException` bei 0 % CPU-Last)
  * 🚧 **DirectX** (Wirft aktuell hochperformant eine `NotImplementedException` bei 0 % CPU-Last)
* **Modulares Feature-System:** Rendering-Features wie **Shadows** und **FXAA** lassen sich sauber einklinken, ohne den Core umbauen zu müssen.
* **Zero-Asset-Philosophie:** Du entscheidest, wie du deine Meshes, Texturen oder Welt-Daten lädst. Der Renderer nimmt nur Buffer, Handles und Shader entgegen.
* **Plattformen:**
  * ✅ Windows (`Win32`)
  * ✅ Linux (`X11` & `Wayland`)
  * ❌ macOS (Kein Budget für angebissenes Obst – PRs für `VK_EXT_metal_surface` sind aber willkommen)

---

## Minimal-Beispiel (Konzept)

```csharp
Siehe SampleProjekt. 
```

---

## Easter Eggs & Unrat 🗑️

Wer tief genug im Repository gräbt, findet unbenutzte Shader-Dateien aus den ersten Entwicklungstagen sowie liebevoll platzierte Entwickler-Kommentare. Wer zuerst alle findet, darf sich offiziell „Low-Level-Archäologe“ nennen.

---

## Status

Gebaut in **5 Tagen** als kompromissloses Fundament für eigene Engines, Tools und Grafik-Experimente.
