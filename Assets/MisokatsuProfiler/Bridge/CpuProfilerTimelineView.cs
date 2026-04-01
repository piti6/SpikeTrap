using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Graphs;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace LightningProfiler
{
    /// <summary>
    /// Managed CPU timeline visualization (clean-room alternative to Unity's native timeline implementation).
    /// </summary>
    [Serializable]
    internal sealed class CpuProfilerTimelineView
    {
        const float kLineHeight = 14f;
        const float kThreadPad = 3f;

        [NonSerialized] ICpuProfilerTimelineViewHost m_Module;
        [NonSerialized] Vector2 m_Scroll;
        [NonSerialized] float m_HorizontalZoom = 1f;

        readonly List<int> m_ThreadOrder = new List<int>();
        readonly List<string> m_ThreadLabels = new List<string>();

        [NonSerialized] int m_ProgrammaticHighlightThread = FrameDataView.invalidThreadIndex;
        [NonSerialized] int m_ProgrammaticHighlightRawSample = RawFrameDataView.invalidSampleIndex;

        readonly Color[] m_BarColors =
        {
            new Color(0.45f, 0.55f, 0.95f, 0.9f),
            new Color(0.45f, 0.85f, 0.55f, 0.9f),
            new Color(0.95f, 0.65f, 0.45f, 0.9f),
            new Color(0.85f, 0.55f, 0.85f, 0.9f),
        };

        public void OnEnable(ICpuProfilerTimelineViewHost module, ProfilerWindow window)
        {
            m_Module = module;
        }

        public void Clear()
        {
            m_ProgrammaticHighlightThread = FrameDataView.invalidThreadIndex;
            m_ProgrammaticHighlightRawSample = RawFrameDataView.invalidSampleIndex;
        }

        /// <summary>
        /// Keeps managed timeline bars in sync with API/chart selection (no native flow lanes yet).
        /// </summary>
        public void ApplyProgrammaticSelection(ProfilerTimeSampleSelection sel, int frameIndex)
        {
            if (sel == null || frameIndex < 0)
            {
                m_ProgrammaticHighlightThread = FrameDataView.invalidThreadIndex;
                m_ProgrammaticHighlightRawSample = RawFrameDataView.invalidSampleIndex;
                return;
            }

            m_ProgrammaticHighlightThread = sel.GetThreadIndex(frameIndex);
            m_ProgrammaticHighlightRawSample = sel.rawSampleIndex;
        }

        public void ReInitialize()
        {
            // Show Flow Events and similar options need extra rendering; not implemented in this managed view.
        }

        public void DoGUI(int frameIndex, Rect position, bool fetchData, ref bool updateViewLive)
        {
            using (var iter = fetchData ? new ProfilerFrameDataIterator() : null)
            {
                int threadCount = fetchData ? iter.GetThreadCount(frameIndex) : 0;
                if (fetchData && threadCount > 0)
                    iter.SetRoot(frameIndex, 0);

                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                m_Module.DrawTimelineToolbar(iter, ref updateViewLive);
                EditorGUILayout.EndHorizontal();

                if (!fetchData && !updateViewLive)
                {
                    GUILayout.Label(ProfilerFrameDataHierarchyView.LiveViewDisabledContent, ProfilerFrameDataHierarchyView.ProfilerDetailsLabelStyle);
                    return;
                }

                if (threadCount == 0)
                {
                    GUILayout.Label(ProfilerFrameDataHierarchyView.NoFrameDataContent, ProfilerFrameDataHierarchyView.ProfilerDetailsLabelStyle);
                    return;
                }

                double frameMsD = iter != null ? iter.frameTimeMS : 16.67;
                if (frameMsD <= 0.0)
                    frameMsD = 16.67;
                float frameMs = (float)frameMsD;

                BuildThreadOrderAndLabels(frameIndex, threadCount);
                float side = Chart.kSideWidth;
                float contentHeight = m_ThreadOrder.Count * (kLineHeight + kThreadPad) + 8f;
                float drawWidth = Mathf.Max(320f, (position.width - side - 24f) * m_HorizontalZoom);

                m_Scroll = GUILayout.BeginScrollView(m_Scroll, GUILayout.ExpandHeight(true));
                var area = GUILayoutUtility.GetRect(drawWidth + side, contentHeight);
                if (Event.current.type == EventType.Repaint)
                    ProfilerWindow.Styles.profilerGraphBackground.Draw(area, false, false, false, false);

                float y = area.y + 4f;
                for (var i = 0; i < m_ThreadOrder.Count; i++)
                {
                    var t = m_ThreadOrder[i];
                    var rowRect = new Rect(area.x, y, area.width, kLineHeight);
                    DrawThreadRow(frameIndex, t, rowRect, side, drawWidth, frameMs, m_ThreadLabels[i]);
                    y += kLineHeight + kThreadPad;
                }

                GUILayout.EndScrollView();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Zoom", EditorStyles.miniLabel, GUILayout.Width(40));
                m_HorizontalZoom = GUILayout.HorizontalSlider(m_HorizontalZoom, 0.25f, 4f, GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();
            }
        }

        void BuildThreadOrderAndLabels(int frameIndex, int threadCount)
        {
            m_ThreadOrder.Clear();
            m_ThreadLabels.Clear();
            var names = new List<(int idx, string name)>(threadCount);
            for (int i = 0; i < threadCount; i++)
                names.Add((i, GetThreadLabel(frameIndex, i)));
            names.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            foreach (var n in names)
            {
                m_ThreadOrder.Add(n.idx);
                m_ThreadLabels.Add(n.name);
            }
        }

        static string GetThreadLabel(int frameIndex, int threadIndex)
        {
            using (var it = new ProfilerFrameDataIterator())
            {
                it.SetRoot(frameIndex, threadIndex);
                var g = it.GetGroupName();
                var n = it.GetThreadName();
                return string.IsNullOrEmpty(g) ? n : g + "." + n;
            }
        }

        void DrawThreadRow(int frameIndex, int threadIndex, Rect rowRect, float sideWidth, float drawWidth, float frameMs, string threadLabel)
        {
            var labelRect = new Rect(rowRect.x + 4f, rowRect.y, sideWidth - 8f, kLineHeight);
            var chartRect = new Rect(rowRect.x + sideWidth, rowRect.y, drawWidth, kLineHeight);
            GUI.Label(labelRect, threadLabel, EditorStyles.miniLabel);

            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(chartRect, new Color(0.08f, 0.08f, 0.08f, 0.6f));

            var highlightThisThread = m_ProgrammaticHighlightThread == threadIndex;
            using (var raw = ProfilerDriver.GetRawFrameDataView(frameIndex, threadIndex))
            {
                if (raw == null || !raw.valid || raw.sampleCount <= 1)
                    return;
                VisitSamples(raw, 1, frameMs, chartRect, 0, highlightThisThread, m_ProgrammaticHighlightRawSample);
            }
        }

        void VisitSamples(RawFrameDataView raw, int sampleIndex, float frameMs, Rect chartRect, int depth, bool highlightThread, int highlightRawSampleIndex)
        {
            if (sampleIndex <= 0 || sampleIndex >= raw.sampleCount)
                return;

            double startMs = raw.GetSampleStartTimeMs(sampleIndex);
            double durMs = raw.GetSampleTimeMs(sampleIndex);
            float x0 = (float)(startMs / frameMs) * chartRect.width;
            float w = Mathf.Max(1f, (float)(durMs / frameMs) * chartRect.width);
            float yOff = Mathf.Min(depth * 2f, kLineHeight * 0.35f);
            var bar = new Rect(chartRect.x + x0, chartRect.y + yOff, w, Mathf.Max(2f, kLineHeight - yOff));
            if (bar.xMax > chartRect.xMin && bar.xMin < chartRect.xMax && Event.current.type == EventType.Repaint)
            {
                var markerId = raw.GetSampleMarkerId(sampleIndex);
                var isProgrammaticHit = highlightThread && highlightRawSampleIndex >= 0 && sampleIndex == highlightRawSampleIndex;
                var c = isProgrammaticHit
                    ? new Color(1f, 0.92f, 0.2f, 0.95f)
                    : m_BarColors[Mathf.Abs(markerId) % m_BarColors.Length];
                EditorGUI.DrawRect(bar, c);
                if (isProgrammaticHit)
                    EditorGUI.DrawRect(new Rect(bar.x - 1f, bar.y - 1f, bar.width + 2f, bar.height + 2f), new Color(1f, 1f, 0.4f, 0.45f));
            }

            int subtreeEnd = sampleIndex + raw.GetSampleChildrenCountRecursive(sampleIndex);
            int child = sampleIndex + 1;
            while (child < subtreeEnd && child < raw.sampleCount)
            {
                var childRecursive = raw.GetSampleChildrenCountRecursive(child);
                VisitSamples(raw, child, frameMs, chartRect, depth + 1, highlightThread, highlightRawSampleIndex);
                child = child + childRecursive + 1;
            }
        }
    }
}
