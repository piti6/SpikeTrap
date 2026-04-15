# SpikeTrap

> Set traps, catch bad frames.

**[日本語版はこちら](README_JP.md)**

An enhanced CPU profiler module for Unity Editor with pluggable frame filters, visual highlight strips, AI-driven profiling automation, and marked-frame collection.

## Features

### Frame Filters

Three built-in filters, each with a visual highlight strip and prev/next navigation buttons:

| Filter | What it detects | Unit selector | Strip color |
|---|---|---|---|
| **Spike** | Frames exceeding a CPU time threshold | ms / s | Green |
| **GC** | Frames exceeding a GC allocation threshold | KB / MB | Red |
| **Search** | Frames containing a named profiler sample (case-insensitive) | — | Orange |

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

### Screenshot Preview

Displays per-frame screenshots captured by the `screenshot2profiler` runtime package inline in the profiler details view. Screenshots persist while scrubbing through frames without screenshot data, and clear on new recording or file load.

### Custom Filters

Create your own filters by extending `FrameFilterBase`:

```csharp
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
```

Register via `[InitializeOnLoad]`:

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

### Scripting API

`SpikeTrapAPI` provides static methods for AI-driven profiling automation:

```csharp
using SpikeTrap;

SpikeTrapAPI.StartCollecting(spikeThresholdMs: 33f);
// ... game runs, spikes are captured ...
SpikeTrapAPI.StopCollectingAndSave("/path/to/spikes.data");

FrameSummary[] spikes = SpikeTrapAPI.GetSpikeFrames(33f);
foreach (var s in spikes)
    Debug.Log(s); // "Frame 296: 2038.40ms, GC 5.7KB | NavMeshManager=1953.89ms, ..."
```

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

## Documentation

See the [package README](Packages/com.piti6.spike-trap/README.md) for detailed architecture, performance characteristics, thread safety model, and test coverage.

## Development

This repository is a Unity project. `Assets/ProfilerStressTest.cs` generates random CPU spikes, GC pressure, and named profiler samples (`HeavyComputation`, `GarbageBlast`, `NetworkSync`, `SaveCheckpoint`) for testing.

Run tests via Unity Test Runner (EditMode, assembly `SpikeTrap.Tests`).

## License

MIT
