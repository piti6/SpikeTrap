---
name: spike-trap
description: "Run SpikeTrap CPU profiling workflow. Use to: (1) Profile the game and capture spike frames, (2) Analyze existing cached profiler data for bottlenecks, (3) Save matched frames to .data files. Requires Unity Profiler window open with SpikeTrap module selected."
---

# SpikeTrap Profiling Skill

Run an automated CPU profiling session using SpikeTrap. $ARGUMENTS

## Modes

Determine which mode to use based on the user's request:

- **profile** (default): Enter play mode, collect spike frames, stop, analyze, and report.
- **analyze**: Skip profiling — analyze already-cached data or a loaded .data file.

If the user provides a spike threshold (e.g. "33ms", "16.6ms"), use it. Default: 33ms.
If the user provides a GC threshold (e.g. "100KB"), use it. Default: 0 (disabled).
If the user provides a duration (e.g. "10 seconds"), wait that long before stopping. Default: 10 seconds.

## Profile Workflow

### Step 1: Check prerequisites

```csharp
using SpikeTrap.Editor;
return $"ViewActive={SpikeTrapApi.IsViewActive}, Collecting={SpikeTrapApi.IsCollecting}, Cached={SpikeTrapApi.CachedFrameCount}";
```

If `IsViewActive` is false, tell the user to open the Profiler window and select the SpikeTrap CPU module, then stop.

### Step 2: Enter play mode

Use `mcp__uLoopMCP__control-play-mode` with Action=Play. Wait for Unity to be ready.

### Step 3: Enable profiler and start collecting

```csharp
using UnityEditorInternal;
using SpikeTrap.Editor;
ProfilerDriver.enabled = true;
SpikeTrapApi.StartCollecting(spikeThresholdMs: <THRESHOLD>f, gcThresholdKB: <GC_THRESHOLD>f);
return $"Collecting: spike>{<THRESHOLD>}ms, gc>{<GC_THRESHOLD>}KB";
```

### Step 4: Wait for the requested duration

Tell the user profiling is running. Use ScheduleWakeup or repeated checks to wait.

### Step 5: Stop and save

```csharp
using SpikeTrap.Editor;
string path = System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "spike-trap-capture.data");
bool saved = await SpikeTrapApi.StopCollectingAndSaveAsync(path);
return $"Saved={saved}, Path={path}";
```

Then stop play mode with `mcp__uLoopMCP__control-play-mode` Action=Stop.

### Step 6: Analyze (same as Analyze Workflow below)

## Analyze Workflow

### Step 1: Get spike frames

```csharp
using SpikeTrap.Editor;
var spikes = SpikeTrapApi.GetSpikeFrames(<THRESHOLD>f);
if (spikes == null || spikes.Length == 0) return "No spike frames found.";
var sb = new System.Text.StringBuilder();
sb.AppendLine($"Found {spikes.Length} spike frames (>{<THRESHOLD>}ms):");
for (int i = 0; i < System.Math.Min(20, spikes.Length); i++)
    sb.AppendLine(spikes[i].ToString());
return sb.ToString();
```

### Step 2: Summarize findings

Report to the user:
1. **Total spike count** and the threshold used
2. **Worst frames** — list the top 5 by CPU time
3. **Common bottlenecks** — identify marker names that appear repeatedly in TopMarkerNames across spike frames (e.g. if "NavMeshManager" appears in 80% of spikes, flag it)
4. **GC pressure** — if any spikes have significant GcAllocBytes, call them out
5. **Actionable suggestions** — based on the marker names, suggest what to investigate (e.g. "NavMeshManager dominates — check NavMesh carving settings or disable runtime carving")

## API Reference

All calls go through `mcp__uLoopMCP__execute-dynamic-code`. Always include `using SpikeTrap.Editor;` for profiling APIs.

| Method | Description |
|--------|-------------|
| `SpikeTrapApi.IsViewActive` | Is the profiler module open? |
| `SpikeTrapApi.IsCollecting` | Is Collect mode active? |
| `SpikeTrapApi.CachedFrameCount` | Total cached frames |
| `SpikeTrapApi.StartCollecting(spikeThresholdMs, gcThresholdKB)` | Begin collecting matched frames |
| `SpikeTrapApi.StopCollecting()` | Stop collecting, discard captured frames |
| `SpikeTrapApi.StopCollectingAndSaveAsync(path)` | Stop and save; `await` for completion (file on disk when Task resolves) |
| `SpikeTrapApi.StopCollectingAndSave(path)` | Fire-and-forget variant; returns immediately |
| `SpikeTrapApi.SetFilterThresholds(spikeThresholdMs, gcThresholdKB)` | Update thresholds (-1=keep, 0=disable) |
| `SpikeTrapApi.GetSpikeFrames(thresholdMs)` | Get all frames above threshold, sorted worst-first |
| `SpikeTrapApi.GetCachedFrameSummaries()` | Get all cached frames for current session |
| `SpikeTrapApi.GetMarkerName(markerId)` | Resolve marker ID to name |

### FrameSummary fields

- `FrameIndex` (int) — profiler frame index
- `EffectiveTimeMs` (float) — CPU time minus EditorLoop overhead
- `GcAllocBytes` (long) — total GC allocations in bytes
- `TopMarkerNames` (string[]) — top 10 costliest markers by inclusive time
- `TopMarkerTimesMs` (float[]) — corresponding inclusive times in ms
- `ToString()` — human-readable one-liner

## Important Notes

- The Profiler window must be open with SpikeTrap CPU module selected for StartCollecting/StopCollectingAndSaveAsync
- GetSpikeFrames and GetCachedFrameSummaries work without an active view (reads static cache)
- TopMarkerTimesMs are inclusive (include children) — may double-count nested markers
- After stopping play mode, wait for domain reload before analyzing
