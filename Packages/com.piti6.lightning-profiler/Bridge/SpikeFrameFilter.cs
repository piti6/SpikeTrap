using System;
using LightningProfiler.Runtime;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace LightningProfiler
{
    /// <summary>
    /// Filters frames whose CPU time (minus EditorLoop) exceeds a millisecond threshold.
    /// </summary>
    public sealed class SpikeFrameFilter : FrameFilterBase
    {
        const string k_EditorPrefsKey = "LightningProfiler.ChartFilterThresholdMs";
        const string k_UnitKey = "LightningProfiler.SpikeUnit";

        bool m_IsEditorSession = true;
        int m_SessionCheckedFrame = -1;

        float m_ThresholdMs;
        float m_PrevThresholdMs;

        readonly Action<float> m_SyncModule;

        enum TimeUnit
        {
            ms,
            s
        }

        static readonly string[] k_TimeUnitLabels = { "ms", "s" };
        TimeUnit m_Unit;

        public SpikeFrameFilter(Action<float> syncModule)
        {
            m_SyncModule = syncModule;
            m_ThresholdMs = EditorPrefs.GetFloat(k_EditorPrefsKey, 0f);
            m_PrevThresholdMs = m_ThresholdMs;
            m_Unit = (TimeUnit)EditorPrefs.GetInt(k_UnitKey, 0);
        }

        public override string DisplayName => "Spike";
        public override Color StripColor => new Color(0.2f, 0.85f, 0.4f, 0.95f);

        public override string StripLabel => m_Unit == TimeUnit.s
            ? $">={m_ThresholdMs / 1000f:G3}s"
            : $">={m_ThresholdMs:F0}ms";

        public override bool IsActive => m_ThresholdMs > 0f;

        public float ThresholdMs => m_ThresholdMs;

        float DisplayValue => m_Unit == TimeUnit.s ? m_ThresholdMs / 1000f : m_ThresholdMs;
        float ToMs(float displayVal) => m_Unit == TimeUnit.s ? displayVal * 1000f : displayVal;

        public override bool DrawToolbarControls()
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label("Spike", EditorStyles.miniLabel, GUILayout.Width(34));
            var newDisplayVal =
                EditorGUILayout.FloatField(DisplayValue, EditorStyles.toolbarTextField, GUILayout.Width(50));

            var newUnit = (TimeUnit)EditorGUILayout.Popup((int)m_Unit, k_TimeUnitLabels, EditorStyles.toolbarDropDown,
                GUILayout.Width(38));
            if (newUnit != m_Unit)
            {
                m_Unit = newUnit;
                EditorPrefs.SetInt(k_UnitKey, (int)m_Unit);
            }

            float newMs = Mathf.Max(0f, ToMs(newDisplayVal));
            if (newMs == m_ThresholdMs) return false;

            m_ThresholdMs = newMs;
            EditorPrefs.SetFloat(k_EditorPrefsKey, m_ThresholdMs);
            m_SyncModule?.Invoke(m_ThresholdMs);
            SetDirty();
            return true;
        }


        bool IsEditorSession()
        {
            int lastFrame = ProfilerDriver.lastFrameIndex;
            if (m_SessionCheckedFrame == lastFrame)
                return m_IsEditorSession;

            int firstFrame = ProfilerDriver.firstFrameIndex;
            if (firstFrame < 0) return m_IsEditorSession;
            var view = ProfilerDriver.GetHierarchyFrameDataView(firstFrame, 0, HierarchyFrameDataView.ViewModes.Default,
                0, false);
            if (view != null && view.valid)
            {
                var data = view.GetSessionMetaData<byte>(
                    LightningProfilerSession.SessionGuid,
                    LightningProfilerSession.SessionInfoTag);
                if (data.IsCreated && data.Length >= 1)
                    m_IsEditorSession = data[0] == 1;
            }

            m_SessionCheckedFrame = lastFrame;
            return m_IsEditorSession;
        }

        /// <summary>
        /// Test a single frame by index. Opens a RawFrameDataView, builds a
        /// <see cref="FrameDataContext"/>, and calls <see cref="IsMatch"/>.
        /// Override for more efficient per-frame checks.
        /// </summary>
        public override bool FrameMatches(int frameIndex)
        {
            float frameTimeMs;
            using (var iter = new ProfilerFrameDataIterator())
            {
                iter.SetRoot(frameIndex, 0);
                frameTimeMs = iter.frameTimeMS;
            }

            if (IsEditorSession())
                frameTimeMs -= GetEditorLoopTimeMs(frameIndex);
            return frameTimeMs >= m_ThresholdMs;
        }

        public override void UpdateMatches()
        {
            if (!IsActive) return;

            bool thresholdChanged = m_ThresholdMs != m_PrevThresholdMs;
            if (!thresholdChanged)
            {
                // No parameter change — use base for incremental new-frame scanning
                base.UpdateMatches();
                return;
            }

            if (IsDirty)
            {
                // Full rescan needed (e.g. profile loaded) — let base handle it
                m_PrevThresholdMs = m_ThresholdMs;
                base.UpdateMatches();
                return;
            }

            int lastFrame = ProfilerDriver.lastFrameIndex;
            int firstFrame = ProfilerDriver.firstFrameIndex;
            if (firstFrame < 0 || lastFrame < 0) return;

            int visibleFirst = Mathf.Max(firstFrame, lastFrame + 1 - ProfilerUserSettings.frameCount);

            if (m_ThresholdMs < m_PrevThresholdMs)
            {
                // Lowered: existing matches still valid, only scan unmatched frames for new matches
                for (int frameIndex = visibleFirst; frameIndex <= lastFrame; frameIndex++)
                {
                    if (!MatchedFramesSet.Contains(frameIndex) && FrameMatches(frameIndex))
                        MatchedFramesSet.Add(frameIndex);
                }
            }
            else
            {
                // Raised: only re-verify currently matched frames
                MatchedFramesSet.RemoveWhere(frameIndex => frameIndex < firstFrame || !FrameMatches(frameIndex));
            }

            m_PrevThresholdMs = m_ThresholdMs;
            CachedLastFrame = lastFrame;
            ClearDirty();
        }

        internal static float GetEditorLoopTimeMs(int frameIndex)
        {
            float totalMs = 0f;
            using (var raw = ProfilerDriver.GetRawFrameDataView(frameIndex, 0))
            {
                if (raw == null || !raw.valid)
                    return 0f;
                int editorLoopId = raw.GetMarkerId("EditorLoop");
                if (editorLoopId == FrameDataView.invalidMarkerId)
                    return 0f;
                for (int i = 0; i < raw.sampleCount; i++)
                {
                    if (raw.GetSampleMarkerId(i) == editorLoopId)
                        totalMs += raw.GetSampleTimeMs(i);
                }
            }

            return totalMs;
        }
    }
}