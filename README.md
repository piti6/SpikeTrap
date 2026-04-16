# SpikeTrap

### Set traps, catch bad frames.

**[日本語版はこちら](README_JP.md)**

An enhanced CPU profiler module for Unity Editor with pluggable frame filters, visual highlight strips, AI-driven profiling automation, and marked-frame collection.

![SpikeTrap — spike detection with filter strips and hierarchy view](Documentation~/spike-detection-overview.png)

## Why SpikeTrap?

Unity's built-in CPU module is a **rolling buffer** — it keeps the last 300-2000 frames and silently discards everything older. At 60fps that's 5-33 seconds of history. SpikeTrap replaces this with **selective capture**: only frames that match your filters are kept, so you can profile for minutes or hours without losing data.

### SpikeTrap vs. Native CPU Module

| | Native CPU Module | SpikeTrap |
|---|---|---|
| **Frame retention** | Rolling buffer (300-2000 frames) — old frames overwritten | Selective: only matched frames kept, no limit on session length |
| **10 min @ 60fps** (36,000 frames) | Keeps last 300-2000, loses 95%+ | Keeps only the ~20 spikes that matter |
| **Rare spike (1 in 10,000)** | Almost certainly overwritten before you see it | Captured automatically by filter |
| **Filter types** | None built-in | Spike (CPU time), GC (alloc size), Search (marker name), custom |
| **Visual indicators** | None | Color-coded highlight strips per filter with prev/next navigation |
| **Filter composition** | N/A | Match Any (OR) / Match All (AND) with result strip |
| **Automation API** | Low-level `ProfilerDriver` + `HierarchyFrameDataView` — frame-by-frame traversal, manual marker ID resolution | `SpikeTrap.Editor.SpikeTrapApi` static class — fire-and-forget collection, pre-sorted results, marker names resolved |
| **AI code-first profiling** | Must poll constantly before buffer overwrites data | `SpikeTrapApi.StartCollecting()` → wait → `SpikeTrapApi.StopCollectingAndSave()` → `SpikeTrapApi.GetSpikeFrames()` |
| **Output format** | Standard `.data` (all frames, rolling) | Standard `.data` (matched frames only, mergeable) |
| **Hierarchy view** | Built-in | Inherited — same hierarchy, same detail columns |

### How Selective Capture Works

The native profiler records every frame into a fixed-size ring buffer. When the buffer fills, the oldest frame is gone forever.

SpikeTrap hooks into the same native profiler data stream but evaluates each frame against your active filters in real-time. Only frames that match (spike threshold exceeded, GC allocation too high, specific marker found) are saved to temporary `.data` files. At the end of a session, matched frames are merged into a single file.

This means a 100-minute profiling session that produces 360,000 frames might save only 50 spike frames — each one with full call-stack detail, ready for analysis.

## Features

### Frame Filters

Three built-in filters, each with a visual highlight strip and prev/next navigation buttons:

| Filter | What it detects | Unit selector | Strip color |
|---|---|---|---|
| **Spike** | Frames exceeding a CPU time threshold | ms / s | Green |
| **GC** | Frames exceeding a GC allocation threshold | KB / MB | Red |
| **Search** | Frames containing a named profiler sample (case-insensitive) | — | Orange |

![Filter strips with live spike and GC detection](Documentation~/filter-strips-live.png)

### Match Any / Match All

Control how filters combine with a dropdown in the toolbar:

- **Match any** (OR) — a frame matches if any active filter matches. Default behavior.
- **Match all** (AND) — a frame matches only if all active filters match. A **Result** strip appears showing the intersection with its own prev/next navigation.

Inactive filters (threshold = 0 or empty search) are skipped — they don't affect the result.

### Pause & Log on Match

- **Pause on match** — automatically pauses play mode when filters match a frame
- **Log on match** — dumps the full sample call-stack hierarchy to the console for matched frames

### Collect Marked Frames

Record only frames that match active filters into a `.data` file:

1. Set up filters (spike threshold, GC threshold, search term)
2. Press **Collect** — filter controls stay visible so you can adjust thresholds while collecting
3. Matched frames are saved to temp files as they occur
4. Press **Save (N)** to merge collected frames into a single `.data` file, or **Stop** to exit without saving

![Collect mode overlay](Documentation~/collect-mode.png)

### Screenshot Preview

Displays per-frame screenshots captured by the [ScreenshotToUnityProfiler](https://github.com/wotakuro/ScreenshotToUnityProfiler) runtime package inline in the profiler details view. Screenshots persist while scrubbing through frames without screenshot data, and clear on new recording or file load.

Enable screenshot capture at runtime:

```csharp
using SpikeTrap.Runtime;

// Start capturing (default: quarter resolution)
SpikeTrapApi.InitializeScreenshotCapture();

// Or specify scale (0.5 = half resolution)
SpikeTrapApi.InitializeScreenshotCapture(0.5f);

// Custom compression
SpikeTrapApi.InitializeScreenshotCapture(0.25f, TextureCompress.JPG_BufferRGB565);

// Custom capture routine (e.g., capture a specific camera instead of screen)
SpikeTrapApi.InitializeScreenshotCapture(captureBehaviour: target =>
{
    Camera.main.targetTexture = target;
    Camera.main.Render();
    Camera.main.targetTexture = null;
});

// Stop capturing and release resources
SpikeTrapApi.DestroyScreenshotCapture();
```

### Custom Filters

Create your own filters by extending `FrameFilterBase` and registering via `SpikeTrapApi`:

```csharp
using SpikeTrap.Editor;
using UnityEngine;

public class MyFilter : FrameFilterBase
{
    public override Color HighlightColor => Color.cyan;
    public override bool IsActive => true;

    public override bool Matches(in CachedFrameData frameData)
    {
        // Must be thread-safe — called from Parallel.For
        return frameData.EffectiveTimeMs > 16f && frameData.GcAllocBytes > 1024;
    }
}

// Register (e.g., from an editor script or [InitializeOnLoad] constructor)
SpikeTrapApi.RegisterCustomFilterFactory(() => new MyFilter());
```

### Scripting API

`SpikeTrap.Editor.SpikeTrapApi` and `SpikeTrap.Runtime.SpikeTrapApi` provide the API for profiling automation, custom filters, and screenshot capture:

```csharp
using SpikeTrap.Editor;

SpikeTrapApi.StartCollecting(spikeThresholdMs: 33f);
// ... game runs, spikes are captured ...
SpikeTrapApi.StopCollectingAndSave("/path/to/spikes.data");

FrameSummary[] spikes = SpikeTrapApi.GetSpikeFrames(33f);
foreach (var s in spikes)
    Debug.Log(s); // "Frame 296: 2038.40ms, GC 5.7KB | NavMeshManager=1953.89ms, ..."
```

## Architecture

### Pluggable Filter System

Filters implement `IFrameFilter` (or extend `FrameFilterBase`). The controller handles all native API access, caching, and matched-frame tracking.

```
Native API (main thread)          Managed cache              Filters (thread-safe)
ProfilerDriver.GetRawFrameDataView  -->  CachedFrameData  -->  filter.Matches()
  one call per frame per session         { EffectiveTimeMs,     pure managed,
  extracts all data in one pass            GcAllocBytes,        parallelized for
                                           UniqueMarkerIds }    large frame ranges
```

**`CachedFrameData`** is a readonly struct containing all filter-relevant data extracted from a single frame. Filters receive this pre-extracted data and never touch native APIs.

**`IFrameFilter`** interface:

```csharp
public interface IFrameFilter : IDisposable
{
    Color HighlightColor { get; }
    bool IsActive { get; }
    bool DrawToolbarControls();
    bool Matches(in CachedFrameData frameData);
    void InvalidateCache();
}
```

### Performance

- **One native call per frame per session**: `GetRawFrameDataView` is called once per frame. All filter data (CPU time, GC bytes, marker IDs) is extracted in a single sample iteration loop.
- **Cached frame data**: Extracted data is stored in a `ConcurrentDictionary`. Threshold/search changes re-evaluate cached managed data without native API calls.
- **Parallel matching**: Full rescans above 500 frames use `Parallel.For`. `OnMarkerDiscovered` and `Matches` are thread-safe.
- **Marker ID caching**: Search filter resolves marker names once per unique ID. Subsequent checks are integer comparisons via `ConcurrentDictionary`.
- **Amortized extraction**: Loading large `.data` files extracts 50 frames per editor frame to avoid blocking.
- **Session-aware caching**: Caches use `frameStartTimeNs` as session fingerprint. Same `.data` file loaded multiple times shares the same cache (A→B→A reuses A's cached data).

### Thread Safety

| Component | Thread-safe | Mechanism |
|---|---|---|
| `SearchFrameFilter.Matches` | Yes | Single volatile `SearchState` reference, local capture |
| `SearchFrameFilter.OnMarkerDiscovered` | Yes | `ConcurrentDictionary.TryAdd`, local state capture (internal to search filter) |
| `SpikeFrameFilter.Matches` | Yes | Reads only immutable `CachedFrameData` fields |
| `GcFrameFilter.Matches` | Yes | Reads only immutable `CachedFrameData` fields |
| `s_FrameDataCache` | Yes | `ConcurrentDictionary` |
| `s_MarkerNames` | Yes | `ConcurrentDictionary` |
| `CollectMatchingFrames` | Yes | `Parallel.For` with `ConcurrentBag` result collection |
| Native API extraction | Main thread only | `GetRawFrameDataView`, `ProfilerFrameDataIterator` |

## Requirements

- Unity 2022.3+

## Installation

Add via Unity Package Manager using the git URL:

```
https://github.com/piti6/SpikeTrap.git?path=Packages/com.piti6.spike-trap
```

Or clone the repository and copy `Packages/com.piti6.spike-trap` into your project's `Packages/` directory.

## Quick Start

1. Open the Profiler window (**Window > Analysis > Profiler**)
2. Select the **SpikeTrap CPU Usage** module from the module dropdown
3. Set filter thresholds in each strip row (Spike ms, GC KB, search term)
4. Choose **Match any** or **Match all** from the dropdown
5. Toggle **Pause on match** / **Log on match** as needed
6. Use the **Collect** button to record only matched frames

## Development

This repository is a Unity project. `Assets/ProfilerStressTest.cs` generates random CPU spikes, GC pressure, and named profiler samples (`HeavyComputation`, `GarbageBlast`, `NetworkSync`, `SaveCheckpoint`) for testing.

### Tests

34 EditMode tests (assembly `SpikeTrap.Tests`) covering:
- Spike/GC/Search filter matching logic and edge cases
- Thread safety: concurrent `OnMarkerDiscovered`, concurrent `Matches`, `SetSearchString` race
- Multi-filter OR composition semantics
- Custom filter API usability
- `CachedFrameData` struct correctness

Run via Unity Test Runner (EditMode).

## License

MIT
