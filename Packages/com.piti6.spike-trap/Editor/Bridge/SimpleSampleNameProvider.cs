using UnityEditor.Profiling;

namespace SpikeTrap
{
    internal sealed class SimpleSampleNameProvider : IProfilerSampleNameProvider
    {
        string IProfilerSampleNameProvider.GetItemName(HierarchyFrameDataView frameData, int itemId)
        {
            return frameData.GetItemName(itemId);
        }

        string IProfilerSampleNameProvider.GetMarkerName(HierarchyFrameDataView frameData, int markerId)
        {
            return frameData.GetMarkerName(markerId);
        }

        string IProfilerSampleNameProvider.GetItemName(RawFrameDataView frameData, int itemId)
        {
            return frameData.GetSampleName(itemId);
        }
    }
}
