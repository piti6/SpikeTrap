using System;
using System.Collections.Generic;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace LightningProfiler
{
    /// <summary>
    /// Base class for frame filters with built-in incremental caching.
    /// <para>
    /// <b>Custom filter authors</b> — implement only these 4 members:
    /// <list type="bullet">
    ///   <item><see cref="DisplayName"/> — filter name</item>
    ///   <item><see cref="StripColor"/> — highlight strip color</item>
    ///   <item><see cref="IsActive"/> — whether the filter is configured</item>
    ///   <item><see cref="IsMatch"/> — frame matching logic using pre-fetched data</item>
    /// </list>
    /// Call <see cref="SetDirty"/> when your filter parameter changes.
    /// Optionally override <see cref="StripLabel"/> and <see cref="DrawToolbarControls"/>.
    /// </para>
    /// </summary>
    public abstract class FrameFilterBase : IFrameFilter
    {
        readonly HashSet<int> m_MatchedFrames = new HashSet<int>();
        int m_CachedLastFrame = -1;
        bool m_Dirty = true;

        /// <summary>Direct access to the matched frames set for subclass optimization.</summary>
        protected HashSet<int> MatchedFramesSet => m_MatchedFrames;
        /// <summary>Last frame index processed by UpdateMatches.</summary>
        protected int CachedLastFrame { get => m_CachedLastFrame; set => m_CachedLastFrame = value; }
        /// <summary>Whether a full rescan is pending.</summary>
        protected bool IsDirty => m_Dirty;
        /// <summary>Clear the dirty flag without rescanning.</summary>
        protected void ClearDirty() => m_Dirty = false;

        // ─── Required (implement these) ─────────────────────────────────────

        /// <summary>Display name for the filter.</summary>
        public abstract string DisplayName { get; }

        /// <summary>Highlight strip color.</summary>
        public abstract Color StripColor { get; }

        /// <summary>Whether the filter is currently active (parameter is set).</summary>
        public abstract bool IsActive { get; }

        // ─── Optional (override if needed) ──────────────────────────────────

        /// <summary>Label on the strip. Defaults to <see cref="DisplayName"/>.</summary>
        public virtual string StripLabel => DisplayName;

        /// <summary>Draw IMGUI controls. Return true if the parameter changed.</summary>
        public virtual bool DrawToolbarControls() => false;

        // ─── Call this when your parameter changes ──────────────────────────

        /// <summary>
        /// Mark the cache as dirty so matched frames are rescanned.
        /// Call this whenever your filter parameter changes.
        /// </summary>
        protected void SetDirty() { m_Dirty = true; }

        // ─── Provided by base (no need to touch) ───────────────────────────

        public IReadOnlyCollection<int> MatchedFrames => m_MatchedFrames;

        /// <summary>
        /// Test a single frame by index. Opens a RawFrameDataView, builds a
        /// <see cref="FrameDataContext"/>, and calls <see cref="IsMatch"/>.
        /// Override for more efficient per-frame checks.
        /// </summary>
        public abstract bool FrameMatches(int frameIndex);
        
        public virtual void UpdateMatches()
        {
            if (!IsActive) return;

            int lastFrame = ProfilerDriver.lastFrameIndex;

            if (!m_Dirty && m_CachedLastFrame == lastFrame) return;

            int firstFrame = ProfilerDriver.firstFrameIndex;
            if (firstFrame < 0 || lastFrame < 0) return;

            if (m_Dirty)
            {
                m_MatchedFrames.Clear();
                m_Dirty = false;

                int visibleFirst = Mathf.Max(firstFrame,
                    lastFrame + 1 - ProfilerUserSettings.frameCount);
                for (int frame = visibleFirst; frame <= lastFrame; frame++)
                {
                    if (FrameMatches(frame))
                        m_MatchedFrames.Add(frame);
                }
            }
            else
            {
                int scanFrom = Mathf.Max(firstFrame, m_CachedLastFrame + 1);
                for (int frame = scanFrom; frame <= lastFrame; frame++)
                {
                    if (FrameMatches(frame))
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
            m_Dirty = true;
            m_MatchedFrames.Clear();
        }

        public virtual void Dispose() { }
    }
}
