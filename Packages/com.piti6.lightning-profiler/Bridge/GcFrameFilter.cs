using LightningProfiler.Runtime;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace LightningProfiler
{
    /// <summary>
    /// Filters frames whose GC allocation exceeds a kilobyte threshold.
    /// </summary>
    public sealed class GcFrameFilter : FrameFilterBase
    {
        const string k_EditorPrefsKey = "LightningProfiler.GcFilterThresholdKB";
        const string k_UnitKey = "LightningProfiler.GcUnit";
        
        bool m_IsEditorSession = true;
        int m_SessionCheckedFrame = -1;

        float m_ThresholdKB;
        float m_PrevThresholdKB;

        enum SizeUnit { KB, MB }
        static readonly string[] k_SizeUnitLabels = { "KB", "MB" };
        SizeUnit m_Unit;

        public GcFrameFilter()
        {
            m_ThresholdKB = EditorPrefs.GetFloat(k_EditorPrefsKey, 0f);
            m_PrevThresholdKB = m_ThresholdKB;
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
            var newDisplayVal = EditorGUILayout.FloatField(DisplayValue, EditorStyles.toolbarTextField, GUILayout.Width(50));

            var newUnit = (SizeUnit)EditorGUILayout.Popup((int)m_Unit, k_SizeUnitLabels, EditorStyles.toolbarDropDown, GUILayout.Width(38));
            if (newUnit != m_Unit)
            {
                m_Unit = newUnit;
                EditorPrefs.SetInt(k_UnitKey, (int)m_Unit);
            }

            float newKB = Mathf.Max(0f, ToKB(newDisplayVal));
            if (newKB == m_ThresholdKB) return false;

            m_ThresholdKB = newKB;
            EditorPrefs.SetFloat(k_EditorPrefsKey, m_ThresholdKB);
            SetDirty();
            return true;
        }

        public override void UpdateMatches()
        {
            if (!IsActive) return;

            bool thresholdChanged = m_ThresholdKB != m_PrevThresholdKB;
            if (!thresholdChanged)
            {
                base.UpdateMatches();
                return;
            }

            if (IsDirty)
            {
                m_PrevThresholdKB = m_ThresholdKB;
                base.UpdateMatches();
                return;
            }

            int lastFrame = ProfilerDriver.lastFrameIndex;
            int firstFrame = ProfilerDriver.firstFrameIndex;
            if (firstFrame < 0 || lastFrame < 0) return;

            int visibleFirst = Mathf.Max(firstFrame, lastFrame + 1 - ProfilerUserSettings.frameCount);

            if (m_ThresholdKB < m_PrevThresholdKB)
            {
                // Lowered: existing matches still valid, only scan unmatched frames
                for (int frame = visibleFirst; frame <= lastFrame; frame++)
                {
                    if (!MatchedFramesSet.Contains(frame) && FrameMatches(frame))
                        MatchedFramesSet.Add(frame);
                }
            }
            else
            {
                // Raised: only re-verify currently matched frames
                MatchedFramesSet.RemoveWhere(frame => frame < firstFrame || !FrameMatches(frame));
            }

            m_PrevThresholdKB = m_ThresholdKB;
            CachedLastFrame = lastFrame;
            ClearDirty();
        }

        public override bool FrameMatches(int frameIndex)
        {
            long thresholdBytes = (long)(m_ThresholdKB * 1024f);
            return FrameGcExceedsThreshold(frameIndex, 0, IsEditorSession(), thresholdBytes);
        }
        
        bool IsEditorSession()
        {
            int lastFrame = ProfilerDriver.lastFrameIndex;
            if (ProfilerDriver.GetFramesBelongToSameProfilerSession(m_SessionCheckedFrame, lastFrame))
            {
                m_SessionCheckedFrame = lastFrame;
                return m_IsEditorSession;
            }

            int firstFrame = ProfilerDriver.firstFrameIndex;
            if (firstFrame < 0) return m_IsEditorSession;
            var view = ProfilerDriver.GetHierarchyFrameDataView(firstFrame, 0, HierarchyFrameDataView.ViewModes.Default, 0, false);
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


        static bool FrameGcExceedsThreshold(int frameIndex, int threadIndex, bool excludeEditorLoop, long thresholdBytes)
        {
            using (var raw = ProfilerDriver.GetRawFrameDataView(frameIndex, threadIndex))
            {
                if (raw == null || !raw.valid)
                    return false;
                return FrameGcExceedsThreshold(raw, thresholdBytes, excludeEditorLoop);
            }
        }

        static bool FrameGcExceedsThreshold(RawFrameDataView raw, long thresholdBytes, bool excludeEditorLoop = false)
        {
            int gcMarkerId = raw.GetMarkerId("GC.Alloc");
            if (gcMarkerId == FrameDataView.invalidMarkerId)
                return false;

            int editorLoopId = excludeEditorLoop
                ? raw.GetMarkerId("EditorLoop")
                : FrameDataView.invalidMarkerId;
            bool hasEditorLoop = editorLoopId != FrameDataView.invalidMarkerId;

            long gcBytes = 0;
            for (int i = 0; i < raw.sampleCount; i++)
            {
                int markerId = raw.GetSampleMarkerId(i);
                if (hasEditorLoop && markerId == editorLoopId)
                    continue;
                if (markerId == gcMarkerId && raw.GetSampleMetadataCount(i) > 0)
                {
                    gcBytes += raw.GetSampleMetadataAsLong(i, 0);
                    if (gcBytes >= thresholdBytes)
                        return true;
                }
            }
            return false;
        }
    }
}
