# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
