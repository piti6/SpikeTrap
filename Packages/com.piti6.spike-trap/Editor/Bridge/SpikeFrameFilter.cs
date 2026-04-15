using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SpikeTrap
{
    /// <summary>
    /// Filters frames whose effective CPU time exceeds a millisecond threshold.
    /// Pure managed matching on <see cref="CachedFrameData.EffectiveTimeMs"/>.
    /// </summary>
    public sealed class SpikeFrameFilter : FrameFilterBase
    {
        private enum TimeUnit
        {
            MS,
            S
        }
        
        private const string EditorPrefsKey = "SpikeTrap.ChartFilterThresholdMs";
        private const string UnitKey = "SpikeTrap.SpikeUnit";

        private float _thresholdMs;
        
        private readonly string[] _timeUnitLabels;
        private TimeUnit _unit;

        public SpikeFrameFilter()
        {
            _timeUnitLabels = System.Enum.GetNames(typeof(TimeUnit))
                .Select(x => x.ToLower())
                .ToArray();
            
            _thresholdMs = EditorPrefs.GetFloat(EditorPrefsKey, 0f);
            _unit = (TimeUnit)EditorPrefs.GetInt(UnitKey, 0);
        }

        /// <summary>Test-only constructor. Accepts threshold directly without EditorPrefs.</summary>
        internal SpikeFrameFilter(float thresholdMs)
        {
            _thresholdMs = thresholdMs;
        }

        public override Color HighlightColor => new Color(0.2f, 0.85f, 0.4f, 0.95f);
        public override bool IsActive => _thresholdMs > 0f;
        public float ThresholdMs => _thresholdMs;

        internal void SetThresholdMs(float thresholdMs)
        {
            _thresholdMs = Mathf.Max(0f, thresholdMs);
            EditorPrefs.SetFloat(EditorPrefsKey, _thresholdMs);
        }

        private float ToMs(float displayVal)
        {
            return _unit == TimeUnit.S ? displayVal * 1000f : displayVal;
        }

        public override bool DrawToolbarControls()
        {
            var displayValue = _unit switch
            {
                TimeUnit.S => _thresholdMs / 1000f,
                TimeUnit.MS => _thresholdMs,
                _ => 0f
            };
            
            GUILayout.FlexibleSpace();
            GUILayout.Label("Spike", EditorStyles.miniLabel, GUILayout.Width(34));
            var newDisplayVal =
                EditorGUILayout.FloatField(displayValue, EditorStyles.toolbarTextField, GUILayout.Width(50));

            var newUnit = (TimeUnit)EditorGUILayout.Popup((int)_unit, _timeUnitLabels,
                EditorStyles.toolbarDropDown, GUILayout.Width(38));
            if (newUnit != _unit)
            {
                _unit = newUnit;
                EditorPrefs.SetInt(UnitKey, (int)_unit);
            }

            float newMs = Mathf.Max(0f, ToMs(newDisplayVal));
            if (Mathf.Approximately(newMs, _thresholdMs)) return false;

            _thresholdMs = newMs;
            EditorPrefs.SetFloat(EditorPrefsKey, _thresholdMs);
            return true;
        }

        public override bool Matches(in CachedFrameData frameData)
        {
            if (_thresholdMs <= 0f) return false;
            return frameData.EffectiveTimeMs >= _thresholdMs;
        }
    }
}
