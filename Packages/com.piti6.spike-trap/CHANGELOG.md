# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.2] - 2026-04-19

### Changed
- Search has been split into two independent fields (#9). The native hierarchy search bar narrows the visible rows of the selected frame (matches Unity's built-in CPU Usage module). A separate filter-toolbar search narrows which frames qualify as matches for the highlight strip and Collect capture. Typing in one field no longer silently changes two different mental models.
- Merging large Collect captures now shows a progress bar (gated at 10+ temp files so short merges don't flicker) (#6).
- Timeline screenshot loading switched from `HierarchyFrameDataView` to `RawFrameDataView` — removes the hierarchy tree-build cost per frame selection, which was a multi-second hit under Deep Profile (#7).

### Added
- Guidance banner + analysis short-circuit when the Profiler is targeting the Editor buffer with Deep Profile enabled (#7). This combination made each repaint hang the Editor for multiple seconds; the module now renders a help banner pointing users to Play mode, disabling Deep Profile, or the built-in CPU Usage module, and skips the per-frame extraction / overlay paths.

### Fixed
- Search filter matched zero frames on cached data. `UniqueMarkerIds` was populated lazily only when the search filter was already active at extraction time, so any frame cached before typing stayed permanently unmatchable — the highlight strip and Collect match set silently stayed empty. Always populated now (#9).
- Clicking the X in the hierarchy search bar left stale filtered rows on screen — the non-empty→empty transition was missed by the tree-view rebuild cache. `SearchChanged` now reloads and `BuildRows` picks up the clear via a dedicated signal (#10).
- `ExtractFrameData` called `raw.GetSampleName(i)` for every sample because `ConcurrentDictionary.TryAdd` eagerly evaluates its value argument (#7). Guarded with `ContainsKey` so the call only fires for newly-seen markers.
- Editor frames inflating self-time in Deep Profile traces — `[IgnoredByDeepProfiler]` applied to `SpikeTrapViewController` and `UnityProfilerWindowControllerAdapter` (#7). Partial mitigation: the attribute is per-method so callees are still instrumented.

## [0.1.1] - 2026-04-18

### Added
- Screenshot preview in the Timeline details view (previously only shown in Hierarchy).

### Fixed
- Duplicate Timeline toolbar strip on empty frame state — native `ProfilerTimelineGUI` no longer renders its own toolbar + "No frame data" label underneath ours.
- `NullReferenceException` from `ProfilerTimelineGUI.HandleNativeProfilerTimelineInput` when clicking a sample in the Timeline view (subscribe a no-op handler to the unconditionally invoked `selectionChanged` event).
- Empty-state label in Timeline view now shows the live-view-disabled guidance when recording overhead has suspended fetching, matching the Hierarchy view's behaviour.
- `Releasing render texture that is set to be RenderTexture.active!` warning from the runtime screenshot capture — active RT is now saved/restored around `ReadPixels` and the default capture path, and cleared on dispose.

### Changed
- Timeline and Hierarchy modes now share the same layout order (screenshot preview → toolbar → content) so the view-type dropdown sits at a consistent vertical position.

### CI
- Cancel in-progress workflow runs when a new commit lands on the same ref.
- Skip the Tests workflow for docs-only changes (`*.md`, `Documentation~/`, `LICENSE`, `.gitignore`).

## [0.1.0] - 2026-04-16

### Added
- Spike frame filter with configurable CPU time threshold (ms/s)
- GC allocation frame filter with configurable threshold (KB/MB)
- Search frame filter for named profiler samples (case-insensitive)
- Visual highlight strips with per-filter colors and prev/next navigation
- Match Any (OR) / Match All (AND) filter composition modes
- Collect mode: record only filter-matched frames into `.data` files
- Pause on match and Log on match options
- Per-frame screenshot preview (via ScreenshotToUnityProfiler runtime capture)
- Scripting API (`SpikeTrap.Editor.SpikeTrapApi`) for profiling automation
- Runtime API (`SpikeTrap.Runtime.SpikeTrapApi`) for screenshot capture control
- Custom filter support via `IFrameFilter` / `FrameFilterBase`
- Session-aware frame data caching with `ConcurrentDictionary`
- Parallel filter matching for large frame ranges
- Claude Code skill for AI-driven profiling automation
