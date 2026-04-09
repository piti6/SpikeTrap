using System;
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

        float m_ThresholdMs;
        float m_CachedThreshold;
        readonly Func<bool> m_IsEditorSession;
        readonly Action<float> m_SyncModule;

        enum TimeUnit { ms, s }
        static readonly string[] k_TimeUnitLabels = { "ms", "s" };
        TimeUnit m_Unit;

        public SpikeFrameFilter(Func<bool> isEditorSession, Action<float> syncModule)
        {
            m_IsEditorSession = isEditorSession;
            m_SyncModule = syncModule;
            m_ThresholdMs = EditorPrefs.GetFloat(k_EditorPrefsKey, 0f);
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
            var newDisplayVal = EditorGUILayout.FloatField(DisplayValue, EditorStyles.toolbarTextField, GUILayout.Width(50));

            var newUnit = (TimeUnit)EditorGUILayout.Popup((int)m_Unit, k_TimeUnitLabels, EditorStyles.toolbarDropDown, GUILayout.Width(38));
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
            return true;
        }

        public override bool IsMatch(in FrameDataContext ctx)
        {
            if (m_ThresholdMs <= 0f) return false;
            float effectiveTime = ctx.FrameTimeMs;
            if (ctx.IsEditorSession)
                effectiveTime -= ctx.EditorLoopTimeMs;
            return effectiveTime >= m_ThresholdMs;
        }

        protected override bool HasParameterChanged() => m_ThresholdMs != m_CachedThreshold;
        protected override void SnapshotParameter() { m_CachedThreshold = m_ThresholdMs; }

        protected override bool TestFrame(int frameIndex)
        {
            float frameTimeMs;
            using (var iter = new ProfilerFrameDataIterator())
            {
                iter.SetRoot(frameIndex, 0);
                frameTimeMs = iter.frameTimeMS;
            }
            if (m_IsEditorSession())
                frameTimeMs -= GetEditorLoopTimeMs(frameIndex);
            return frameTimeMs >= m_ThresholdMs;
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
