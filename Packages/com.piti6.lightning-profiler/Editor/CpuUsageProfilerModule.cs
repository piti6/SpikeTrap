using System;
using Unity.Profiling;
using Unity.Profiling.Editor;

namespace LightningProfiler
{
    [Serializable]
    [ProfilerModuleMetadata("LightningProfiler CPU Usage", IconPath = "Profiler.CPU")]
    public sealed class CpuUsageProfilerModule : ProfilerModule
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

        public CpuUsageProfilerModule()
            : base(ChartCounters, ProfilerModuleChartType.StackedTimeArea)
        {
        }

        public override ProfilerModuleViewController CreateDetailsViewController()
        {
            return CpuUsageBridgeDetailsViewController.CreateDetailsViewController(ProfilerWindow);
        }
    }
}
