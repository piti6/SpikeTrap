# SpikeTrap

### Set traps, catch bad frames.

An enhanced CPU profiler module for Unity Editor with pluggable frame filters, visual highlight strips, AI-driven profiling automation, and marked-frame collection.

## Requirements

- Unity 2022.3+

## Features

### Built-in Frame Filters

| Filter | What it detects | Strip color |
|---|---|---|
| **Spike** | Frames exceeding a CPU time threshold (ms/s) | Green |
| **GC** | Frames exceeding a GC allocation threshold (KB/MB) | Red |
| **Search** | Frames containing a named profiler sample (case-insensitive substring) | Orange |

Each filter strip includes prev/next navigation buttons to jump between matched frames.

### Collect Marked Frames

Record only frames that match active filters into `.data` files. During collection, the profiler chart and details area show a "Collecting..." overlay. Save the collected frames as a merged profile when done.

### Pause & Log on Match

- **Pause**: Automatically pause play mode when any active filter matches a frame.
- **Log**: Log full call-stack details for matched frames to the console.

### Screenshot Preview

Displays per-frame screenshots captured by the [ScreenshotToUnityProfiler](https://github.com/wotakuro/ScreenshotToUnityProfiler) runtime package. Screenshots persist while scrubbing through frames without screenshot data, and clear on new recording or file load.

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

**`FrameFilterBase`** provides defaults for optional members. Custom filter authors implement only 3 members:

```csharp
public class MyFilter : FrameFilterBase
{
    public override Color HighlightColor => Color.cyan;
    public override bool IsActive => /* your condition */;
    public override bool Matches(in CachedFrameData frameData)
    {
        // Pure managed matching logic — no Unity API calls needed
    }
}
```

### Registering Custom Filters

```csharp
using SpikeTrap.Editor;

[InitializeOnLoad]
static class MyFilterRegistration
{
    static MyFilterRegistration()
    {
        SpikeTrapApi.RegisterCustomFilterFactory(() => new MyFilter());
    }
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

## Scripting API

`SpikeTrap.Editor.SpikeTrapApi` and `SpikeTrap.Runtime.SpikeTrapApi` static classes provide the API for profiling, custom filters, and screenshot capture:

```csharp
using SpikeTrap.Editor;

// Start collecting spike frames (>33ms)
SpikeTrapApi.StartCollecting(spikeThresholdMs: 33f);

// Stop and save matched frames
SpikeTrapApi.StopCollectingAndSave("/path/to/spikes.data");

// Or stop without saving (discard captured frames)
SpikeTrapApi.StopCollecting();

// Analyze cached data (no active view needed)
FrameSummary[] spikes = SpikeTrapApi.GetSpikeFrames(33f);
foreach (var s in spikes)
    Debug.Log(s); // "Frame 296: 2038.40ms, GC 5.7KB | NavMeshManager=1953.89ms, ..."
```

See `CLAUDE.md` for full AI automation guide.

## License

MIT
