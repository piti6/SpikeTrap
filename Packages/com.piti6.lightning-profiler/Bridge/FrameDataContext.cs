using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEditorInternal.Profiling;

namespace LightningProfiler
{
    /// <summary>
    /// Pre-fetched frame data passed to <see cref="IFrameFilter.IsMatch"/> so each filter
    /// does not independently call <c>ProfilerDriver.GetRawFrameDataView</c>.
    /// The <see cref="RawData"/> view is owned by the caller and must not be stored beyond the call.
    /// </summary>
    public readonly struct FrameDataContext
    {
        /// <summary>Index of the frame being evaluated.</summary>
        public readonly int FrameIndex;

        /// <summary>Pre-opened raw frame data view for thread 0. May be null or invalid.</summary>
        public readonly RawFrameDataView RawData;

        public FrameDataContext(int frameIndex, RawFrameDataView rawData)
        {
            FrameIndex = frameIndex;
            RawData = rawData;
        }
    }
}
