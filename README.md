# SpikeTrap

> Set traps, catch bad frames.

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
| **Automation API** | Low-level `ProfilerDriver` + `HierarchyFrameDataView` — frame-by-frame traversal, manual marker ID resolution | `SpikeTrapAPI` — fire-and-forget collection, pre-sorted results, marker names resolved |
| **AI code-first profiling** | Must poll constantly before buffer overwrites data | `StartCollecting()` → wait → `StopCollectingAndSave()` → `GetSpikeFrames()` |
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
