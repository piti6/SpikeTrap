# Lightning Profiler

An enhanced CPU profiler module for Unity Editor with pluggable frame filters, visual highlight strips, and marked-frame collection.

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

Displays per-frame screenshots captured by the `screenshot2profiler` runtime package. Screenshots persist while scrubbing through frames without screenshot data, and clear on new recording or file load.

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
    string DisplayName { get; }
    Color StripColor { get; }
    string StripLabel { get; }
    bool IsActive { get; }
    bool DrawToolbarControls();
    bool Matches(in CachedFrameData frameData);
    void OnMarkerDiscovered(int markerId, string markerName);
    void InvalidateCache();
}
```

**`FrameFilterBase`** provides defaults for optional members. Custom filter authors implement only 4 members:

```csharp
public class MyFilter : FrameFilterBase
{
    public override string DisplayName => "MyFilter";
    public override Color StripColor => Color.cyan;
    public override bool IsActive => /* your condition */;
    public override bool Matches(in CachedFrameData frameData)
    {
        // Pure managed matching logic — no Unity API calls needed
    }
}
```

### Registering Custom Filters

```csharp
[InitializeOnLoad]
static class MyFilterRegistration
{
    static MyFilterRegistration()
    {
        CpuUsageBridgeDetailsViewController.RegisterCustomFilterFactory(() => new MyFilter());
    }
}
```

### Performance

- **One native call per frame per session**: `GetRawFrameDataView` is called once per frame. All filter data (CPU time, GC bytes, marker IDs) is extracted in a single sample iteration loop.
- **Cached frame data**: Extracted data is stored in a `ConcurrentDictionary`. Threshold/search changes re-evaluate cached managed data without native API calls.
- **Parallel matching**: Full rescans above 500 frames use `Parallel.For`. `OnMarkerDiscovered` and `Matches` are thread-safe.
- **Marker ID caching**: Search filter resolves marker names once per unique ID. Subsequent checks are integer comparisons via `ConcurrentDictionary`.
- **Amortized extraction**: Loading large `.data` files extracts 50 frames per editor frame to avoid blocking.
- **Session-aware caching**: Caches invalidate on file load, clear, or new recording.

### Thread Safety

| Component | Thread-safe | Mechanism |
|---|---|---|
| `SearchFrameFilter.Matches` | Yes | Single volatile `SearchState` reference, local capture |
| `SearchFrameFilter.OnMarkerDiscovered` | Yes | `ConcurrentDictionary.TryAdd`, local state capture |
| `SpikeFrameFilter.Matches` | Yes | Reads only immutable `CachedFrameData` fields |
| `GcFrameFilter.Matches` | Yes | Reads only immutable `CachedFrameData` fields |
| `m_FrameDataCache` | Yes | `ConcurrentDictionary` |
| `m_MarkerNames` | Yes | `ConcurrentDictionary` |
| `CollectMatchingFrames` | Yes | `Parallel.For` with `ConcurrentBag` result collection |
| Native API extraction | Main thread only | `GetRawFrameDataView`, `ProfilerFrameDataIterator` |

## Tests

36 EditMode tests covering:
- Spike/GC/Search filter matching logic and edge cases
- Thread safety: concurrent `OnMarkerDiscovered`, concurrent `Matches`, `SetSearchString` race
- Multi-filter OR composition semantics
- Custom filter API usability
- `CachedFrameData` struct correctness

Run via Unity Test Runner (EditMode, assembly `LightningProfiler.Tests`).

## License

MIT
