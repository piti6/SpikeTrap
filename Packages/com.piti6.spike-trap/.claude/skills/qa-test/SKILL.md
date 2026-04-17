---
name: qa-test
description: "Run comprehensive QA tests for SpikeTrap. Covers all API methods, collect mode lifecycle, filter accuracy, domain reload survival, edge cases, and UI verification. Use when testing after code changes or before release."
---

# SpikeTrap QA Test Suite

Comprehensive, reproducible QA test covering all SpikeTrap specifications. $ARGUMENTS

All C# code runs via `mcp__uLoopMCP__execute-dynamic-code`. Report each test as PASS/FAIL with details.

## Test Environment

- **Stress test scene**: ProfilerStressTest.cs generates known markers at these rates:
  - `HeavyComputation` — 5% chance per frame, ~500K sqrt iterations (~15-50ms spike)
  - `GarbageBlast` — 8% chance per frame, 64KB allocation
  - `NetworkSync` — 10% chance per frame, ~50K sin iterations
  - `SaveCheckpoint` — 6% chance per frame, 32KB write
- **Target frame rate**: 30fps (set by stress test)
- **Standard test thresholds**: spike=5ms, GC=32KB, duration=5s

## Modes

Choose based on user request:

- **full** (default): Run all test suites including play mode tests. Requires Profiler window open with SpikeTrap module.
- **api-only**: Run only API contract and analysis tests on cached/loaded data. No play mode needed.
- **suite N**: Run only suite N (e.g., "suite 3" runs only Collect Lifecycle).

If user says "test" or "QA" without specifying, run **full**.

## Execution Rules

1. Run each test in order — some suites depend on prior suite state.
2. Report results as they complete, do NOT batch.
3. On FAIL, log details but continue to next test (do not abort suite).
4. At the end, print a summary table: suite name, pass count, fail count.
5. If a test needs play mode and game is not running, enter play mode first.
6. Always clean up: stop collecting, stop play mode when done.

---

## Suite 1: Prerequisites

### T1.1 — Compile Check

Use `mcp__uLoopMCP__compile` to verify 0 errors.

**PASS**: 0 compile errors. **FAIL**: any error.

### T1.2 — View Active (auto-setup if needed)

```csharp
using SpikeTrap.Editor;
return $"IsViewActive={SpikeTrapApi.IsViewActive}";
```

If `IsViewActive=True`, **PASS**.

If `IsViewActive=False`, auto-open the Profiler window and select the SpikeTrap module, then re-check:

```csharp
using SpikeTrap.Editor;
bool opened = SpikeTrapApi.EnsureProfilerWindowOpen();
return $"Opened={opened}, IsViewActive={SpikeTrapApi.IsViewActive}";
```

**PASS**: `IsViewActive=True` after setup. **FAIL**: still False after setup — abort all tests (likely running in `-batchmode -nographics` without a display).

### T1.3 — Clean State

```csharp
using SpikeTrap.Editor;
return $"IsCollecting={SpikeTrapApi.IsCollecting}";
```

**PASS**: `IsCollecting=False`. If True, run StopCollecting first to reset, then re-check.

---

## Suite 2: API Contract (No Play Mode)

These tests verify API returns correct types and handles edge cases without requiring gameplay.

### T2.1 — GetSpikeFrames Returns Array

```csharp
using SpikeTrap.Editor;
var frames = SpikeTrapApi.GetSpikeFrames(33f);
return $"Type={frames?.GetType().Name ?? "null"}, Length={frames?.Length ?? -1}";
```

**PASS**: Type=FrameSummary[], Length >= 0. **FAIL**: null or exception.

### T2.2 — GetCachedFrameSummaries Returns Array

```csharp
using SpikeTrap.Editor;
var frames = SpikeTrapApi.GetCachedFrameSummaries();
return $"Type={frames?.GetType().Name ?? "null"}, Length={frames?.Length ?? -1}";
```

**PASS**: Type=FrameSummary[], Length >= 0. **FAIL**: null or exception.

### T2.3 — CachedFrameCount Non-Negative

```csharp
using SpikeTrap.Editor;
return $"CachedFrameCount={SpikeTrapApi.CachedFrameCount}";
```

**PASS**: >= 0. **FAIL**: negative or exception.

### T2.4 — StopCollecting When Not Collecting

```csharp
using SpikeTrap.Editor;
bool result = SpikeTrapApi.StopCollecting();
return $"StopCollecting={result}, IsCollecting={SpikeTrapApi.IsCollecting}";
```

**PASS**: Returns false (nothing to stop), IsCollecting=false. **FAIL**: exception or IsCollecting=true.

### T2.5 — StopCollectingAndSave When Not Collecting

```csharp
using SpikeTrap.Editor;
bool result = SpikeTrapApi.StopCollectingAndSave("/tmp/should-not-exist.data");
return $"StopCollectingAndSave={result}";
```

**PASS**: Returns false. **FAIL**: exception or returns true.

### T2.6 — SetFilterThresholds When Not Collecting

```csharp
using SpikeTrap.Editor;
bool result = SpikeTrapApi.SetFilterThresholds(spikeThresholdMs: 10f);
return $"SetFilterThresholds={result}, IsCollecting={SpikeTrapApi.IsCollecting}";
```

**PASS**: Returns true (thresholds stored for next collection, per API contract "update while collecting or any time"), IsCollecting still false. **FAIL**: exception or IsCollecting becomes true.

---

## Suite 3: Collect Mode Lifecycle

Full start -> collect -> save -> verify cycle. **Requires play mode.**

### T3.1 — Enter Play Mode

Use `mcp__uLoopMCP__control-play-mode` with Action=Play.
Wait 2 seconds for scene to initialize (stress test spawns cubes, sets target framerate).

### T3.2 — Enable Profiler and Start Collecting

```csharp
using UnityEditorInternal;
using SpikeTrap.Editor;
ProfilerDriver.enabled = true;
bool result = SpikeTrapApi.StartCollecting(spikeThresholdMs: 5f, gcThresholdKB: 0f);
return $"StartCollecting={result}";
```

**PASS**: Returns true. **FAIL**: false or exception.

### T3.3 — Verify Collecting State (after 1 second delay)

Wait 1 second, then:

```csharp
using SpikeTrap.Editor;
return $"IsCollecting={SpikeTrapApi.IsCollecting}";
```

**PASS**: IsCollecting=True. **FAIL**: False (delayed entry may not have completed — wait 2 more seconds and retry once).

### T3.4 — Double Start Is No-Op

```csharp
using SpikeTrap.Editor;
bool result = SpikeTrapApi.StartCollecting(spikeThresholdMs: 5f);
return $"DoubleStart={result}, IsCollecting={SpikeTrapApi.IsCollecting}";
```

**PASS**: Returns true (API returns true when collect mode is/stays active; internal StartCollectingInternal early-returns if already collecting, so no side effects). IsCollecting still true. **FAIL**: exception or IsCollecting becomes false.

### T3.5 — Wait for Collection (5 seconds)

Use ScheduleWakeup with 5 second delay. Tell user "Collecting spike frames for 5 seconds..."

### T3.6 — Verify Frames Captured

```csharp
using SpikeTrap.Editor;
return $"IsCollecting={SpikeTrapApi.IsCollecting}, CachedFrameCount={SpikeTrapApi.CachedFrameCount}";
```

**PASS**: IsCollecting=True, CachedFrameCount > 0. **FAIL**: not collecting or zero frames.

### T3.7 — Check Temp Files Exist

```csharp
using SpikeTrap.Editor;
string tempDir = System.IO.Path.Combine(UnityEngine.Application.temporaryCachePath, "MarkedFrames");
int count = System.IO.Directory.Exists(tempDir) ? System.IO.Directory.GetFiles(tempDir, "marked_*.data").Length : 0;
return $"TempDir={tempDir}, TempFileCount={count}";
```

**PASS**: TempFileCount > 0 (frames were captured to disk). **FAIL**: 0 temp files.

### T3.8 — Stop and Save

```csharp
using SpikeTrap.Editor;
string path = System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "qa-test-capture.data");
bool result = SpikeTrapApi.StopCollectingAndSave(path);
return $"StopCollectingAndSave={result}, Path={path}";
```

**PASS**: Returns true. **FAIL**: false or exception.

Wait 3 seconds for the deferred save to complete (clear buffer → 2-frame delay → load/merge/save).

### T3.9 — Verify Collecting Stopped

```csharp
using SpikeTrap.Editor;
return $"IsCollecting={SpikeTrapApi.IsCollecting}";
```

**PASS**: IsCollecting=False. **FAIL**: still true.

### T3.10 — Verify Saved File

```csharp
string path = System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "qa-test-capture.data");
bool exists = System.IO.File.Exists(path);
long size = exists ? new System.IO.FileInfo(path).Length : 0;
return $"Exists={exists}, SizeBytes={size}";
```

**PASS**: Exists=True, SizeBytes > 0. **FAIL**: file missing or empty.

### T3.11 — Verify Temp Files Cleaned Up

```csharp
string tempDir = System.IO.Path.Combine(UnityEngine.Application.temporaryCachePath, "MarkedFrames");
int count = System.IO.Directory.Exists(tempDir) ? System.IO.Directory.GetFiles(tempDir, "marked_*.data").Length : 0;
return $"TempFileCount={count}";
```

**PASS**: TempFileCount=0 (cleaned up after save). **FAIL**: leftover temp files.

### T3.12 — Stop Play Mode

Use `mcp__uLoopMCP__control-play-mode` with Action=Stop. Wait 3 seconds for domain reload.

---

## Suite 4: Analysis Accuracy

Tests run on data from Suite 3. No play mode needed.

### T4.1 — GetSpikeFrames Returns Matches

```csharp
using SpikeTrap.Editor;
var spikes = SpikeTrapApi.GetSpikeFrames(5f);
return $"Count={spikes.Length}";
```

**PASS**: Count > 0 (stress test with 5% spike chance over 5s should produce spikes). **FAIL**: 0.

### T4.2 — All Frames Meet Threshold

```csharp
using SpikeTrap.Editor;
var spikes = SpikeTrapApi.GetSpikeFrames(5f);
int violations = 0;
float minTime = float.MaxValue;
foreach (var s in spikes)
{
    if (s.EffectiveTimeMs < 5f) violations++;
    if (s.EffectiveTimeMs < minTime) minTime = s.EffectiveTimeMs;
}
return $"Count={spikes.Length}, Violations={violations}, MinTimeMs={minTime:F2}";
```

**PASS**: Violations=0 (every frame >= 5ms). **FAIL**: any violation.

### T4.3 — Frames Sorted Descending

```csharp
using SpikeTrap.Editor;
var spikes = SpikeTrapApi.GetSpikeFrames(5f);
bool sorted = true;
for (int i = 1; i < spikes.Length; i++)
{
    if (spikes[i].EffectiveTimeMs > spikes[i-1].EffectiveTimeMs)
    {
        sorted = false;
        break;
    }
}
return $"Count={spikes.Length}, Sorted={sorted}";
```

**PASS**: Sorted=True. **FAIL**: not sorted by EffectiveTimeMs descending.

### T4.4 — Impossible Threshold Returns Empty

```csharp
using SpikeTrap.Editor;
var spikes = SpikeTrapApi.GetSpikeFrames(999999f);
return $"Count={spikes.Length}";
```

**PASS**: Count=0. **FAIL**: any matches at 999999ms.

### T4.5 — Lower Threshold Returns More Frames

```csharp
using SpikeTrap.Editor;
int countLow = SpikeTrapApi.GetSpikeFrames(0.001f).Length;
int countHigh = SpikeTrapApi.GetSpikeFrames(5f).Length;
return $"CountAt0.001ms={countLow}, CountAt5ms={countHigh}, LowGteHigh={countLow >= countHigh}";
```

**PASS**: countLow >= countHigh (lower threshold captures superset). **FAIL**: reversed.

### T4.6 — FrameSummary Fields Valid

```csharp
using SpikeTrap.Editor;
var spikes = SpikeTrapApi.GetSpikeFrames(5f);
if (spikes.Length == 0) return "SKIP: no spikes";
var s = spikes[0];
bool namesValid = s.TopMarkerNames != null && s.TopMarkerNames.Length > 0;
bool timesValid = s.TopMarkerTimesMs != null && s.TopMarkerTimesMs.Length == s.TopMarkerNames.Length;
bool gcValid = s.GcAllocBytes >= 0;
bool toStringValid = !string.IsNullOrEmpty(s.ToString());
return $"FrameIndex={s.FrameIndex}, TimeMs={s.EffectiveTimeMs:F2}, GC={s.GcAllocBytes}, " +
       $"NamesValid={namesValid}, NamesLen={s.TopMarkerNames?.Length ?? 0}, " +
       $"TimesParallel={timesValid}, GcNonNeg={gcValid}, ToStringOk={toStringValid}";
```

**PASS**: All valid flags true, TopMarkerNames.Length == TopMarkerTimesMs.Length. **FAIL**: any false.

### T4.7 — Known Markers Present

```csharp
using SpikeTrap.Editor;
var all = SpikeTrapApi.GetCachedFrameSummaries();
var found = new System.Collections.Generic.HashSet<string>();
string[] known = {"HeavyComputation", "GarbageBlast", "NetworkSync", "SaveCheckpoint"};
foreach (var f in all)
{
    if (f.TopMarkerNames == null) continue;
    foreach (var name in f.TopMarkerNames)
        foreach (var k in known)
            if (name != null && name.Contains(k)) found.Add(k);
}
return $"TotalFrames={all.Length}, FoundMarkers=[{string.Join(", ", found)}], Count={found.Count}/4";
```

**PASS**: At least 2 of 4 known markers found (randomness may exclude some in short runs). **FAIL**: 0 markers found.

### T4.8 — GC Allocation Detection

```csharp
using SpikeTrap.Editor;
var all = SpikeTrapApi.GetCachedFrameSummaries();
int gcFrames = 0;
long maxGc = 0;
foreach (var f in all)
{
    if (f.GcAllocBytes > 1024) gcFrames++;
    if (f.GcAllocBytes > maxGc) maxGc = f.GcAllocBytes;
}
return $"TotalFrames={all.Length}, FramesWithGC={gcFrames}, MaxGcBytes={maxGc}";
```

**PASS**: At least 1 frame with GcAllocBytes > 1024 (stress test allocates 64KB at 8% rate). **FAIL**: zero GC frames.

---

## Suite 5: SetFilterThresholds

**Requires play mode.** Tests threshold adjustment while collecting.

### T5.1 — Enter Play Mode and Start Collecting at 33ms

```csharp
using UnityEditorInternal;
using SpikeTrap.Editor;
ProfilerDriver.enabled = true;
SpikeTrapApi.StartCollecting(spikeThresholdMs: 33f, gcThresholdKB: 0f);
return "Started at 33ms threshold";
```

Wait 3 seconds for initial collection.

### T5.2 — SetFilterThresholds While Collecting

```csharp
using SpikeTrap.Editor;
bool result = SpikeTrapApi.SetFilterThresholds(spikeThresholdMs: 5f);
return $"SetFilterThresholds={result}, IsCollecting={SpikeTrapApi.IsCollecting}";
```

**PASS**: Returns true, still collecting. **FAIL**: false or stopped.

### T5.3 — SetFilterThresholds Keep-Current Semantics

```csharp
using SpikeTrap.Editor;
bool result = SpikeTrapApi.SetFilterThresholds(spikeThresholdMs: -1f, gcThresholdKB: 64f);
return $"SetFilterThresholds={result}";
```

**PASS**: Returns true (-1 means keep current spike threshold, set GC to 64KB). **FAIL**: false.

### T5.4 — SetFilterThresholds Disable Semantics

```csharp
using SpikeTrap.Editor;
bool result = SpikeTrapApi.SetFilterThresholds(spikeThresholdMs: 0f);
return $"SetFilterThresholds={result}";
```

**PASS**: Returns true (0 disables spike filter). **FAIL**: false.

### T5.5 — Cleanup

```csharp
using SpikeTrap.Editor;
SpikeTrapApi.StopCollecting();
return $"IsCollecting={SpikeTrapApi.IsCollecting}";
```

Stop play mode. Wait 3 seconds.

---

## Suite 6: Stop Without Save (Discard)

**Requires play mode.**

### T6.1 — Start Collecting

Enter play mode, then:

```csharp
using UnityEditorInternal;
using SpikeTrap.Editor;
ProfilerDriver.enabled = true;
SpikeTrapApi.StartCollecting(spikeThresholdMs: 5f);
return "Started";
```

Wait 3 seconds.

### T6.2 — Verify Temp Files Created

```csharp
string tempDir = System.IO.Path.Combine(UnityEngine.Application.temporaryCachePath, "MarkedFrames");
int count = System.IO.Directory.Exists(tempDir) ? System.IO.Directory.GetFiles(tempDir, "marked_*.data").Length : 0;
return $"TempFileCount={count}";
```

**PASS**: > 0. **FAIL**: 0.

### T6.3 — StopCollecting (Discard)

```csharp
using SpikeTrap.Editor;
bool result = SpikeTrapApi.StopCollecting();
return $"StopCollecting={result}, IsCollecting={SpikeTrapApi.IsCollecting}";
```

**PASS**: result=true, IsCollecting=false. **FAIL**: otherwise.

### T6.4 — Verify Temp Files Cleaned Up

```csharp
string tempDir = System.IO.Path.Combine(UnityEngine.Application.temporaryCachePath, "MarkedFrames");
int count = System.IO.Directory.Exists(tempDir) ? System.IO.Directory.GetFiles(tempDir, "marked_*.data").Length : 0;
return $"TempFileCount={count}";
```

**PASS**: 0 (discarded). **FAIL**: leftover files.

### T6.5 — Cleanup

Stop play mode. Wait 3 seconds.

---

## Suite 7: Domain Reload Survival

Tests that collect mode persists across play mode transitions (domain reload).

### T7.1 — Start Collecting in Play Mode

Enter play mode. Wait 2 seconds.

```csharp
using UnityEditorInternal;
using SpikeTrap.Editor;
ProfilerDriver.enabled = true;
SpikeTrapApi.StartCollecting(spikeThresholdMs: 5f);
return "Started";
```

Wait 3 seconds to confirm collecting.

```csharp
using SpikeTrap.Editor;
return $"IsCollecting={SpikeTrapApi.IsCollecting}";
```

**PASS**: True.

### T7.2 — Exit Play Mode (Domain Reload)

Use `mcp__uLoopMCP__control-play-mode` with Action=Stop.
Wait 5 seconds for full domain reload.

### T7.3 — Verify Collect Mode Survived

```csharp
using SpikeTrap.Editor;
return $"IsCollecting={SpikeTrapApi.IsCollecting}";
```

**PASS**: IsCollecting=True (EditorPrefs persistence survived domain reload). **FAIL**: False.

### T7.4 — Re-Enter Play Mode

Use `mcp__uLoopMCP__control-play-mode` with Action=Play. Wait 3 seconds.

### T7.5 — Verify Still Collecting After Re-Enter

```csharp
using SpikeTrap.Editor;
return $"IsCollecting={SpikeTrapApi.IsCollecting}";
```

**PASS**: True. **FAIL**: False.

### T7.6 — Cleanup

```csharp
using SpikeTrap.Editor;
SpikeTrapApi.StopCollecting();
return $"IsCollecting={SpikeTrapApi.IsCollecting}";
```

Stop play mode. Wait 3 seconds.

---

## Suite 8: Marker Name Resolution

### T8.1 — GetMarkerName for Known IDs

```csharp
using SpikeTrap.Editor;
var all = SpikeTrapApi.GetCachedFrameSummaries();
if (all.Length == 0) return "SKIP: no cached frames";
var sb = new System.Text.StringBuilder();
var seen = new System.Collections.Generic.HashSet<string>();
int resolved = 0, total = 0;
foreach (var f in all)
{
    if (f.TopMarkerNames == null) continue;
    foreach (var name in f.TopMarkerNames)
    {
        total++;
        if (!string.IsNullOrEmpty(name)) { resolved++; seen.Add(name); }
    }
}
sb.AppendLine($"TotalMarkerSlots={total}, Resolved={resolved}, Unique={seen.Count}");
foreach (var n in seen) sb.AppendLine($"  - {n}");
return sb.ToString();
```

**PASS**: Resolved > 0, no empty/null marker names. **FAIL**: all null.

---

## Suite 9: UI Verification (Screenshot-Based)

These tests use `mcp__uLoopMCP__screenshot` to visually verify UI state. Report observations — exact pixel matching is not required, but describe what you see.

### T9.1 — Profiler Window with SpikeTrap Module

Take screenshot via `mcp__uLoopMCP__screenshot`.
Verify: Profiler window is visible, SpikeTrap CPU module is selected, filter strip area is visible.

### T9.2 — Toolbar Controls Visible

Enter play mode. Set spike threshold to 5ms.

```csharp
using UnityEditorInternal;
using SpikeTrap.Editor;
ProfilerDriver.enabled = true;
SpikeTrapApi.StartCollecting(spikeThresholdMs: 5f);
return "Collecting started for UI check";
```

Wait 2 seconds. Take screenshot.
Verify: "Collecting..." overlay is visible on chart area, Save/Stop buttons visible in toolbar.

### T9.3 — Highlight Strips During Recording

Stop collecting and stop play mode. Load the saved qa-test-capture.data file if available.

Take screenshot.
Verify: Filter highlight strips are visible below the chart, showing color-coded match indicators.

### T9.4 — Cleanup

```csharp
using SpikeTrap.Editor;
if (SpikeTrapApi.IsCollecting) SpikeTrapApi.StopCollecting();
return "Cleaned up";
```

---

## Suite 10: GetCachedFrameSummaries Completeness

### T10.1 — All vs Spike Subset Relationship

```csharp
using SpikeTrap.Editor;
var all = SpikeTrapApi.GetCachedFrameSummaries();
var spikes = SpikeTrapApi.GetSpikeFrames(5f);
bool subset = true;
var allIndices = new System.Collections.Generic.HashSet<int>();
foreach (var f in all) allIndices.Add(f.FrameIndex);
foreach (var s in spikes)
    if (!allIndices.Contains(s.FrameIndex)) { subset = false; break; }
return $"AllFrames={all.Length}, Spikes={spikes.Length}, SpikesSubsetOfAll={subset}";
```

**PASS**: Every spike frame index exists in all frames (spikes is a subset). **FAIL**: orphan spike frame.

### T10.2 — FrameSummary SessionId Consistency

```csharp
using SpikeTrap.Editor;
var all = SpikeTrapApi.GetCachedFrameSummaries();
if (all.Length == 0) return "SKIP: no frames";
var sessions = new System.Collections.Generic.HashSet<int>();
foreach (var f in all) sessions.Add(f.SessionId);
return $"TotalFrames={all.Length}, UniqueSessions={sessions.Count}, SessionIds=[{string.Join(",", sessions)}]";
```

**PASS**: At least 1 session. Note: multiple sessions are valid if .data files were loaded.

---

## Summary Report

After all suites complete, print a summary table:

```
| Suite | Name                     | Pass | Fail | Skip |
|-------|--------------------------|------|------|------|
| 1     | Prerequisites            | x/3  | x/3  | 0    |
| 2     | API Contract             | x/6  | x/6  | 0    |
| 3     | Collect Lifecycle        | x/12 | x/12 | 0    |
| 4     | Analysis Accuracy        | x/8  | x/8  | 0    |
| 5     | SetFilterThresholds      | x/5  | x/5  | 0    |
| 6     | Stop Without Save        | x/5  | x/5  | 0    |
| 7     | Domain Reload Survival   | x/6  | x/6  | 0    |
| 8     | Marker Resolution        | x/1  | x/1  | 0    |
| 9     | UI Verification          | x/4  | x/4  | 0    |
| 10    | Summaries Completeness   | x/2  | x/2  | 0    |
|-------|--------------------------|------|------|------|
| TOTAL |                          | x/52 | x/52 | 0    |
```

If any FAILs: list them with suite.test ID, expected vs actual, and suggested investigation steps.

## Important Notes

- Suite 3 saves to `qa-test-capture.data` in project root — Suites 4, 8, 10 depend on this data.
- Suites 5-7 each need their own play mode cycle — enter/exit per suite.
- Wait times are minimums — if a check fails, retry once after an additional 2-second wait before marking FAIL.
- The stress test is random — spike counts will vary between runs. Assertions use "at least N" thresholds, not exact counts.
- Domain reload (Suite 7) takes 3-5 seconds. Be patient.
- If `mcp__uLoopMCP__execute-dynamic-code` blocks System.IO calls, mark file-check tests as SKIP (not FAIL).
