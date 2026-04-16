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

## Custom Filters

Create your own filters by extending `FrameFilterBase` and registering via `SpikeTrapApi`:

```csharp
using SpikeTrap.Editor;
using UnityEngine;

public class MyFilter : FrameFilterBase
{
    public override Color HighlightColor => Color.cyan;
    public override bool IsActive => /* your condition */;
    public override bool Matches(in CachedFrameData frameData)
    {
        // Pure managed matching logic — no Unity API calls needed
        // Must be thread-safe — called from Parallel.For
    }
}

// Register (e.g., from an editor script or [InitializeOnLoad] constructor)
SpikeTrapApi.RegisterCustomFilterFactory(() => new MyFilter());
```

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
