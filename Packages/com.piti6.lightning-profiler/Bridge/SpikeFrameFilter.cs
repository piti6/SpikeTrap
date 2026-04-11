using System;
using UnityEditor;
using UnityEngine;

namespace LightningProfiler
{
    /// <summary>
    /// Filters frames whose effective CPU time exceeds a millisecond threshold.
    /// Pure managed matching on <see cref="CachedFrameData.EffectiveTimeMs"/>.
    /// </summary>
    public sealed class SpikeFrameFilter : FrameFilterBase
    {
        const string k_EditorPrefsKey = "LightningProfiler.ChartFilterThresholdMs";
        const string k_UnitKey = "LightningProfiler.SpikeUnit";

        float m_ThresholdMs;
        readonly Action<float> m_SyncModule;

        enum TimeUnit { ms, s }
        static readonly string[] k_TimeUnitLabels = { "ms", "s" };
        TimeUnit m_Unit;

        public SpikeFrameFilter(Action<float> syncModule)
        {
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
            var newDisplayVal =
                EditorGUILayout.FloatField(DisplayValue, EditorStyles.toolbarTextField, GUILayout.Width(50));

            var newUnit = (TimeUnit)EditorGUILayout.Popup((int)m_Unit, k_TimeUnitLabels,
                EditorStyles.toolbarDropDown, GUILayout.Width(38));
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

        public override bool Matches(in CachedFrameData frameData)
        {
            return frameData.EffectiveTimeMs >= m_ThresholdMs;
        }
    }
}
