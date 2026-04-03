using Unity.Profiling.Editor;

namespace LightningProfiler
{
    /// <summary>
    /// Base class for profiler modules with chart filter threshold support.
    /// The threshold is used by the view controller to draw highlight/greyout strips.
    /// </summary>
    public abstract class FilterableProfilerModule : ProfilerModule
    {
        float m_ChartFilterThresholdMs;
        bool m_ChartFilterEnabled;

        protected FilterableProfilerModule(
            ProfilerCounterDescriptor[] chartCounters,
            ProfilerModuleChartType chartType)
            : base(chartCounters, chartType)
        {
        }

        public void SetChartFilterThreshold(float thresholdMs)
        {
            m_ChartFilterThresholdMs = thresholdMs;
            m_ChartFilterEnabled = thresholdMs > 0f;
        }

        public float GetChartFilterThreshold() => m_ChartFilterThresholdMs;
        public bool IsChartFilterEnabled() => m_ChartFilterEnabled;
    }
}
