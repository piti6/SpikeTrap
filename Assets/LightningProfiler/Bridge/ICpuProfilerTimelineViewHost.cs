using UnityEditorInternal;

namespace LightningProfiler
{
    internal interface ICpuProfilerTimelineViewHost
    {
        void DrawTimelineToolbar(ProfilerFrameDataIterator iter, ref bool updateViewLive);
    }
}
