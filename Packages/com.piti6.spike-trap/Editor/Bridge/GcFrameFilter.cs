using UnityEditor;
using UnityEngine;

namespace SpikeTrap
{
    /// <summary>
    /// Filters frames whose GC allocation exceeds a kilobyte threshold.
    /// Pure managed matching on <see cref="CachedFrameData.GcAllocBytes"/>.
    /// </summary>
    public sealed class GcFrameFilter : FrameFilterBase
    {
        const string k_EditorPrefsKey = "SpikeTrap.GcFilterThresholdKB";
        const string k_UnitKey = "SpikeTrap.GcUnit";

        float m_ThresholdKB;

        enum SizeUnit { KB, MB }
        static readonly string[] k_SizeUnitLabels = { "KB", "MB" };
        SizeUnit m_Unit;

        public GcFrameFilter()
        {
            m_ThresholdKB = EditorPrefs.GetFloat(k_EditorPrefsKey, 0f);
            m_Unit = (SizeUnit)EditorPrefs.GetInt(k_UnitKey, 0);
        }

        /// <summary>Test-only constructor. Accepts threshold directly without EditorPrefs.</summary>
        internal GcFrameFilter(float thresholdKB)
        {
            m_ThresholdKB = thresholdKB;
        }

        public override Color HighlightColor => new Color(0.95f, 0.3f, 0.3f, 0.95f);
        public override bool IsActive => m_ThresholdKB > 0f;

        internal void SetThresholdKB(float thresholdKB)
        {
            m_ThresholdKB = Mathf.Max(0f, thresholdKB);
            EditorPrefs.SetFloat(k_EditorPrefsKey, m_ThresholdKB);
        }

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
            if (Mathf.Approximately(newKB, m_ThresholdKB)) return false;

            m_ThresholdKB = newKB;
            EditorPrefs.SetFloat(k_EditorPrefsKey, m_ThresholdKB);
            return true;
        }

        public override bool Matches(in CachedFrameData frameData)
        {
            if (m_ThresholdKB <= 0f) return false;
            return frameData.GcAllocBytes >= (long)(m_ThresholdKB * 1024.0);
        }
    }
}
