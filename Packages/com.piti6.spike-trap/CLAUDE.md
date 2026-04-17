# SpikeTrap - AI Automation Guide

### Set traps, catch bad frames.

## SpikeTrap API

Static API for programmatic profiling. Call from `execute-dynamic-code` (uLoop MCP) or editor scripts. Main-thread only.

### Quick Reference

```csharp
using SpikeTrap.Editor;

// Status
SpikeTrapApi.IsViewActive      // Is the profiler module open?
SpikeTrapApi.IsCollecting       // Is Collect mode active?
SpikeTrapApi.CachedFrameCount   // Total cached frames across all sessions

// Start collecting with spike threshold 33ms (1 frame @ 30fps)
SpikeTrapApi.StartCollecting(spikeThresholdMs: 33f);

// Start collecting with both spike + GC threshold
SpikeTrapApi.StartCollecting(spikeThresholdMs: 16.6f, gcThresholdKB: 100f);

// Update thresholds while collecting (-1 = don't change, 0 = disable)
SpikeTrapApi.SetFilterThresholds(spikeThresholdMs: 16f);

// Stop and save collected frames — await completion (file on disk when Task resolves)
bool saved = await SpikeTrapApi.StopCollectingAndSaveAsync("/path/to/spikes.data");

// Fire-and-forget variant — returns immediately, save completes later
SpikeTrapApi.StopCollectingAndSave("/path/to/spikes.data");

// Stop collecting without saving (discard captured frames)
SpikeTrapApi.StopCollecting();

// Analyze: get all frames exceeding threshold, sorted worst-first
FrameSummary[] spikes = SpikeTrapApi.GetSpikeFrames(33f);

// Analyze: get all cached frames for current session
FrameSummary[] all = SpikeTrapApi.GetCachedFrameSummaries();

// Resolve marker name
string name = SpikeTrapApi.GetMarkerName(markerId);

// Custom filters
SpikeTrapApi.RegisterCustomFilterFactory(() => new MyFilter());

// Runtime screenshot capture (using SpikeTrap.Runtime;)
SpikeTrap.Runtime.SpikeTrapApi.InitializeScreenshotCapture();                       // default quarter resolution
SpikeTrap.Runtime.SpikeTrapApi.InitializeScreenshotCapture(0.5f);                   // half resolution
SpikeTrap.Runtime.SpikeTrapApi.InitializeScreenshotCapture(0.25f,
    TextureCompress.JPG_BufferRGB565);                                               // custom compression
SpikeTrap.Runtime.SpikeTrapApi.InitializeScreenshotCapture(captureBehaviour: rt =>
    { Camera.main.targetTexture = rt; Camera.main.Render(); Camera.main.targetTexture = null; });
SpikeTrap.Runtime.SpikeTrapApi.DestroyScreenshotCapture();                           // stop and release
```

### FrameSummary

Each `FrameSummary` contains:
- `FrameIndex` (int) - profiler frame index
- `EffectiveTimeMs` (float) - CPU time minus EditorLoop overhead
- `GcAllocBytes` (long) - total GC allocations
- `TopMarkerNames` (string[]) - top 10 costliest markers, sorted by time descending
- `TopMarkerTimesMs` (float[]) - corresponding inclusive times
- `ToString()` - human-readable one-liner

### Automated Profiling Skill

This package ships a Claude Code skill at `.claude/skills/spike-trap/SKILL.md`.

If the `/spike-trap` slash command is available, use it directly:

```
/spike-trap profile 33ms 10 seconds
/spike-trap analyze 16ms
```

If the slash command is not available, read the SKILL.md file for the full step-by-step workflow instructions including play mode control, collection, saving, and analysis.

### QA Test Skill

Comprehensive QA test suite at `.claude/skills/qa-test/SKILL.md`.

```
/qa-test full          — all 10 suites (52 tests), requires play mode + Profiler window
/qa-test api-only      — API contract + analysis tests only, no play mode
/qa-test suite 3       — run only a specific suite
```

Covers: API contracts, collect lifecycle, filter accuracy, threshold updates, discard flow, domain reload survival, marker resolution, UI verification, and data completeness. Uses ProfilerStressTest scene for reproducible spike/GC/marker generation.

### AI Profiling Workflow (Manual)

1. Open Unity Profiler window (required for the module view to be active)
2. Enter Play mode
3. Call `SpikeTrapApi.StartCollecting(spikeThresholdMs)` to begin
4. Let the game run - matched frames are captured automatically
5. Call `SpikeTrapApi.StopCollectingAndSave(path)` to save
6. Call `SpikeTrapApi.GetSpikeFrames(threshold)` or load the saved file and read cache
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

// Register (e.g., from an editor script or [InitializeOnLoad] constructor)
SpikeTrapApi.RegisterCustomFilterFactory(() => new MyFilter());
```
