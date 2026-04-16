# SpikeTrap - AI Automation Guide

> Set traps, catch bad frames.

## SpikeTrapAPI

Static API for programmatic profiling. Call from `execute-dynamic-code` (uLoop MCP) or editor scripts. Main-thread only.

### Quick Reference

```csharp
using SpikeTrap;

// Status
SpikeTrapAPI.IsViewActive      // Is the profiler module open?
SpikeTrapAPI.IsCollecting       // Is Collect mode active?
SpikeTrapAPI.CachedFrameCount   // Total cached frames across all sessions

// Start collecting with spike threshold 33ms (1 frame @ 30fps)
SpikeTrapAPI.StartCollecting(spikeThresholdMs: 33f);

// Start collecting with both spike + GC threshold
SpikeTrapAPI.StartCollecting(spikeThresholdMs: 16.6f, gcThresholdKB: 100f);

// Update thresholds while collecting (-1 = don't change, 0 = disable)
SpikeTrapAPI.SetFilterThresholds(spikeThresholdMs: 16f);

// Stop and save collected frames
SpikeTrapAPI.StopCollectingAndSave("/path/to/spikes.data");

// Stop collecting without saving (discard captured frames)
SpikeTrapAPI.StopCollecting();

// Analyze: get all frames exceeding threshold, sorted worst-first
FrameSummary[] spikes = SpikeTrapAPI.GetSpikeFrames(33f);

// Analyze: get all cached frames for current session
FrameSummary[] all = SpikeTrapAPI.GetCachedFrameSummaries();

// Resolve marker name
string name = SpikeTrapAPI.GetMarkerName(markerId);
```

### FrameSummary

Each `FrameSummary` contains:
- `FrameIndex` (int) - profiler frame index
- `EffectiveTimeMs` (float) - CPU time minus EditorLoop overhead
- `GcAllocBytes` (long) - total GC allocations
- `TopMarkerNames` (string[]) - top 10 costliest markers, sorted by time descending
- `TopMarkerTimesMs` (float[]) - corresponding inclusive times
- `ToString()` - human-readable one-liner

### AI Profiling Workflow

1. Open Unity Profiler window (required for the module view to be active)
2. Enter Play mode
3. Call `StartCollecting(spikeThresholdMs)` to begin
4. Let the game run - matched frames are captured automatically
5. Call `StopCollectingAndSave(path)` to save
6. Call `GetSpikeFrames(threshold)` or load the saved file and read cache
7. Analyze `FrameSummary.TopMarkerNames` to identify bottlenecks

### Important Notes

- The Profiler window must be open with the SpikeTrap CPU module selected for `StartCollecting`/`StopCollectingAndSave` to work
- `GetSpikeFrames` and `GetCachedFrameSummaries` work without an active view (reads static cache)
- Static cache survives across file loads: loading A, then B, then A again reuses A's cached data
- `TopMarkerTimesMs` are inclusive times (include children) - useful for identifying hot paths but may double-count nested markers
- Filter thresholds: spike = CPU time in ms, GC = allocation size in KB

### Session-Aware Caching

The cache uses `frameStartTimeNs` from the first frame as a session fingerprint. Same `.data` file loaded multiple times shares the same cache. Different recordings have different fingerprints.

### Custom Filters

Register custom filters for specialized detection:

```csharp
[InitializeOnLoad]
static class MyFilterSetup
{
    static MyFilterSetup()
    {
        CpuUsageBridgeDetailsViewController.RegisterCustomFilterFactory(() => new MyFilter());
    }
}

public class MyFilter : FrameFilterBase
{
    public override Color HighlightColor => Color.cyan;
    public override bool IsActive => true;
    public override bool Matches(in CachedFrameData frameData)
    {
        // Pure managed, thread-safe - called from Parallel.For
        return frameData.EffectiveTimeMs > 16.6f && frameData.GcAllocBytes > 1024;
    }
}
```
