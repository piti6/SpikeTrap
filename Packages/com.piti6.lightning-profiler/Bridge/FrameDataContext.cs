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

        /// <summary>Total frame time in milliseconds (from ProfilerFrameDataIterator).</summary>
        public readonly float FrameTimeMs;

        /// <summary>Whether the current profiling session is an editor session.</summary>
        public readonly bool IsEditorSession;

        /// <summary>Pre-opened raw frame data view for thread 0. May be null or invalid.</summary>
        public readonly RawFrameDataView RawData;

        /// <summary>EditorLoop time in ms (pre-computed). Only meaningful when <see cref="IsEditorSession"/> is true.</summary>
        public readonly float EditorLoopTimeMs;

        public FrameDataContext(int frameIndex, float frameTimeMs, bool isEditorSession,
            RawFrameDataView rawData, float editorLoopTimeMs)
        {
            FrameIndex = frameIndex;
            FrameTimeMs = frameTimeMs;
            IsEditorSession = isEditorSession;
            RawData = rawData;
            EditorLoopTimeMs = editorLoopTimeMs;
        }
    }
}
