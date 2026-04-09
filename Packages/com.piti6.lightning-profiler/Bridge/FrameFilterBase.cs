using System.Collections.Generic;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace LightningProfiler
{
    /// <summary>
    /// Abstract base class providing incremental caching for frame filters.
    /// Subclasses implement <see cref="HasParameterChanged"/>, <see cref="SnapshotParameter"/>,
    /// <see cref="TestFrame"/>, and <see cref="IsMatch"/>.
    /// </summary>
    public abstract class FrameFilterBase : IFrameFilter
    {
        protected readonly HashSet<int> m_MatchedFrames = new HashSet<int>();
        protected int m_CachedLastFrame = -1;

        public abstract string DisplayName { get; }
        public abstract Color StripColor { get; }
        public abstract string StripLabel { get; }
        public abstract bool IsActive { get; }
        public IReadOnlyCollection<int> MatchedFrames => m_MatchedFrames;

        public abstract bool DrawToolbarControls();
        public abstract bool IsMatch(in FrameDataContext context);

        /// <summary>Returns true when the filter parameter has changed since the last full scan.</summary>
        protected abstract bool HasParameterChanged();

        /// <summary>Records the current parameter value as cached after a full rescan.</summary>
        protected abstract void SnapshotParameter();

        /// <summary>Test a single frame index. Called during UpdateMatches for incremental/full scan.</summary>
        protected abstract bool TestFrame(int frameIndex);

        public virtual void UpdateMatches()
        {
            if (!IsActive) return;

            int lastFrame = ProfilerDriver.lastFrameIndex;
            bool paramChanged = HasParameterChanged();

            if (!paramChanged && m_CachedLastFrame == lastFrame) return;

            int firstFrame = ProfilerDriver.firstFrameIndex;
            if (firstFrame < 0 || lastFrame < 0) return;

            if (paramChanged)
            {
                m_MatchedFrames.Clear();
                SnapshotParameter();

                int visibleFirst = Mathf.Max(firstFrame,
                    lastFrame + 1 - ProfilerUserSettings.frameCount);
                for (int frame = visibleFirst; frame <= lastFrame; frame++)
                {
                    if (TestFrame(frame))
                        m_MatchedFrames.Add(frame);
                }
            }
            else
            {
                int scanFrom = Mathf.Max(firstFrame, m_CachedLastFrame + 1);
                for (int frame = scanFrom; frame <= lastFrame; frame++)
                {
                    if (TestFrame(frame))
                        m_MatchedFrames.Add(frame);
                }

                if (m_MatchedFrames.Count > 0)
                    m_MatchedFrames.RemoveWhere(f => f < firstFrame);
            }

            m_CachedLastFrame = lastFrame;
        }

        public virtual void InvalidateCache()
        {
            m_CachedLastFrame = -1;
            m_MatchedFrames.Clear();
        }

        public virtual void Dispose() { }
    }
}
