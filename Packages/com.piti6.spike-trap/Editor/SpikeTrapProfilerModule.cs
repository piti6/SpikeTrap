using System;
using Unity.Profiling;
using Unity.Profiling.Editor;

namespace SpikeTrap.Editor
{
    [Serializable]
    [ProfilerModuleMetadata("SpikeTrap", IconPath = "Profiler.CPU")]
    public sealed class SpikeTrapProfilerModule : ProfilerModule
    {
        private static readonly ProfilerCounterDescriptor[] ChartCounters =
        {
            new("Rendering", ProfilerCategory.Scripts),
            new("Scripts", ProfilerCategory.Scripts),
            new("Physics", ProfilerCategory.Scripts),
            new("Animation", ProfilerCategory.Scripts),
            new("GarbageCollector", ProfilerCategory.Scripts),
            new("VSync", ProfilerCategory.Scripts),
            new("Global Illumination", ProfilerCategory.Scripts),
            new("UI", ProfilerCategory.Scripts),
            new("Others", ProfilerCategory.Scripts),
        };

        public SpikeTrapProfilerModule()
            : base(ChartCounters, ProfilerModuleChartType.StackedTimeArea)
        {
        }

        public override ProfilerModuleViewController CreateDetailsViewController()
        {
            return SpikeTrapViewController.CreateDetailsViewController(ProfilerWindow);
        }
    }
}
