# Lightning Profiler

**[日本語版はこちら](README_JP.md)**

An enhanced CPU profiler module for Unity Editor with pluggable frame filters, visual highlight strips, and marked-frame collection.

## Features

### Frame Filters

Three built-in filters, each with a visual highlight strip and prev/next navigation buttons:

| Filter | What it detects | Strip color |
|---|---|---|
| **Spike** | Frames exceeding a CPU time threshold (ms/s) | Green |
| **GC** | Frames exceeding a GC allocation threshold (KB/MB) | Red |
| **Search** | Frames containing a named profiler sample (case-insensitive) | Orange |

### Pause & Log on Match

- **Pause** — automatically pauses play mode when any active filter matches a frame
- **Log** — dumps the full sample call-stack hierarchy to the console for matched frames

### Collect Marked Frames

Record only frames that match active filters into a `.data` file. During collection, the profiler shows a "Collecting..." overlay. Save the collected frames as a merged profile when done.

### Screenshot Preview

Displays per-frame screenshots captured by the `screenshot2profiler` runtime package inline in the profiler details view.

### Custom Filters

Create your own filters by implementing `IFrameFilter` or extending `FrameFilterBase`:

```csharp
public class MyFilter : FrameFilterBase
{
    public override string DisplayName => "MyFilter";
    public override Color StripColor => Color.cyan;
    public override bool IsActive => true;

    public override bool Matches(in CachedFrameData frameData)
    {
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

## Requirements

- Unity 2022.3+

## Installation

Add via Unity Package Manager using the git URL:

```
https://github.com/piti6/LightningProfiler.git?path=Packages/com.piti6.lightning-profiler
```

Or clone the repository and copy `Packages/com.piti6.lightning-profiler` into your project's `Packages/` directory.

## Quick Start

1. Open the Profiler window (**Window > Analysis > Profiler**)
2. Select the **LightningProfiler CPU Usage** module from the module dropdown
3. Set filter thresholds in each strip row (Spike ms, GC KB, search term)
4. Toggle **Pause** / **Log** as needed
5. Use the **Collect** button to record only matched frames

## Documentation

See the [package README](Packages/com.piti6.lightning-profiler/README.md) for detailed architecture, performance characteristics, thread safety model, and test coverage.

## Development

This repository is a Unity project. `Assets/NewBehaviourScript.cs` generates random CPU spikes, GC pressure, and named profiler samples (`HeavyComputation`, `GarbageBlast`, `NetworkSync`, `SaveCheckpoint`) for testing.

Run tests via Unity Test Runner (EditMode, assembly `LightningProfiler.Tests`).

## License

MIT
