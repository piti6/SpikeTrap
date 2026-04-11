using UnityEditor;
using UnityEngine;

namespace LightningProfiler
{
    /// <summary>
    /// Filters frames whose GC allocation exceeds a kilobyte threshold.
    /// Pure managed matching on <see cref="CachedFrameData.GcAllocBytes"/>.
    /// </summary>
    public sealed class GcFrameFilter : FrameFilterBase
    {
        const string k_EditorPrefsKey = "LightningProfiler.GcFilterThresholdKB";
        const string k_UnitKey = "LightningProfiler.GcUnit";

        float m_ThresholdKB;

        enum SizeUnit { KB, MB }
        static readonly string[] k_SizeUnitLabels = { "KB", "MB" };
        SizeUnit m_Unit;

        public GcFrameFilter()
        {
            m_ThresholdKB = EditorPrefs.GetFloat(k_EditorPrefsKey, 0f);
            m_Unit = (SizeUnit)EditorPrefs.GetInt(k_UnitKey, 0);
        }

        public override string DisplayName => "GC";
        public override Color StripColor => new Color(0.95f, 0.3f, 0.3f, 0.95f);
        public override string StripLabel => m_Unit == SizeUnit.MB
            ? $">={m_ThresholdKB / 1024f:G3}MB"
            : $">={m_ThresholdKB:F0}KB";
        public override bool IsActive => m_ThresholdKB > 0f;

        float DisplayValue => m_Unit == SizeUnit.MB ? m_ThresholdKB / 1024f : m_ThresholdKB;
        float ToKB(float displayVal) => m_Unit == SizeUnit.MB ? displayVal * 1024f : displayVal;

        public override bool DrawToolbarControls()
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label("GC", EditorStyles.miniLabel, GUILayout.Width(18));
            var newDisplayVal = EditorGUILayout.FloatField(DisplayValue,
                EditorStyles.toolbarTextField, GUILayout.Width(50));

            var newUnit = (SizeUnit)EditorGUILayout.Popup((int)m_Unit, k_SizeUnitLabels,
                EditorStyles.toolbarDropDown, GUILayout.Width(38));
            if (newUnit != m_Unit)
            {
                m_Unit = newUnit;
                EditorPrefs.SetInt(k_UnitKey, (int)m_Unit);
            }

            float newKB = Mathf.Max(0f, ToKB(newDisplayVal));
            if (newKB == m_ThresholdKB) return false;

            m_ThresholdKB = newKB;
            EditorPrefs.SetFloat(k_EditorPrefsKey, m_ThresholdKB);
            return true;
        }

        public override bool Matches(in CachedFrameData frameData)
        {
            return frameData.GcAllocBytes >= (long)(m_ThresholdKB * 1024f);
        }
    }
}
