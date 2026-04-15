using UnityEditor.Profiling;

namespace SpikeTrap
{
    internal interface IProfilerSampleNameProvider
    {
        string GetItemName(HierarchyFrameDataView frameData, int itemId);
        string GetMarkerName(HierarchyFrameDataView frameData, int markerId);
        string GetItemName(RawFrameDataView frameData, int itemId);
    }
}
