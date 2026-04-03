# Lightning Profiler

**[日本語版はこちら](README_JP.md)**

An enhanced CPU profiler module for the Unity Editor that replaces the default CPU Usage module with spike-focused tooling.

## Features

### Spike Detection
- **Pause on spike** — automatically pauses play mode when a frame exceeds your threshold
- **Log on spike** — dumps the full sample call-stack hierarchy to the console on spike frames, works in both editor and play mode

### Frame Filtering
- **Chart filter threshold** — set a millisecond threshold to hide frames below it from the chart
- **Threshold highlight strip** — visual bar below the chart showing which frames exceed the threshold at a glance

### Search Matching
- **Pause on match** — pauses play mode when a frame contains a profiler sample matching your search term
- **Search highlight strip** — visual bar showing which frames contain the search match

### Views
- **Timeline view** — thread-based timeline visualization with color-coded samples
- **Hierarchy view** — merged sample hierarchy with sorting and thread selection

## Requirements

- Unity 2022.3 or later

## Installation

Add via Unity Package Manager using the git URL:
```
https://github.com/piti6/LightningProfiler.git?path=Assets/LightningProfiler
```

Or clone the repository and copy the `Assets/LightningProfiler` folder into your project's `Packages/` directory.

## Usage

1. Open the Profiler window (**Window > Analysis > Profiler**)
2. Select the **LightningProfiler CPU Usage** module from the module dropdown
3. Set a **Chart Filter** threshold (ms) to enable spike detection buttons
4. Toggle **Pause on spike** / **Log on spike** as needed

## License

MIT