using System;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling.Editor;
using UnityEditor;
using UnityEditor.Graphs;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightningProfiler
{
    internal sealed class UnityProfilerWindowControllerAdapter : IProfilerWindowController
    {
        readonly ProfilerWindow m_Window;

        public UnityProfilerWindowControllerAdapter(ProfilerWindow window)
        {
            m_Window = window ?? throw new ArgumentNullException(nameof(window));
        }

        public long selectedFrameIndex
        {
            get => m_Window.selectedFrameIndex;
            set => m_Window.selectedFrameIndex = value;
        }

        public ProfilerModule selectedModule
        {
            get => m_Window.selectedModule;
            set => m_Window.selectedModule = value;
        }

        public ProfilerModule GetProfilerModuleByType(Type type) => m_Window.GetProfilerModuleByType(type);

        public event Action frameDataViewAboutToBeDisposed
        {
            add => m_Window.frameDataViewAboutToBeDisposed += value;
            remove => m_Window.frameDataViewAboutToBeDisposed -= value;
        }

        public event Action<int, bool> currentFrameChanged
        {
            add => m_Window.currentFrameChanged += value;
            remove => m_Window.currentFrameChanged -= value;
        }

        public void SetClearOnPlay(bool enabled) => m_Window.SetClearOnPlay(enabled);
        public bool GetClearOnPlay() => m_Window.GetClearOnPlay();

        public HierarchyFrameDataView GetFrameDataView(string groupName, string threadName, ulong threadId, HierarchyFrameDataView.ViewModes viewMode, int profilerSortColumn, bool sortAscending)
            => m_Window.GetFrameDataView(groupName, threadName, threadId, viewMode, profilerSortColumn, sortAscending);

        public HierarchyFrameDataView GetFrameDataView(int threadIndex, HierarchyFrameDataView.ViewModes viewMode, int profilerSortColumn, bool sortAscending)
            => m_Window.GetFrameDataView(threadIndex, viewMode, profilerSortColumn, sortAscending);

        public bool IsRecording() => m_Window.IsRecording();
        public bool ProfilerWindowOverheadIsAffectingProfilingRecordingData() => m_Window.ProfilerWindowOverheadIsAffectingProfilingRecordingData();

        public string ConnectedTargetName => m_Window.ConnectedTargetName;
        public bool ConnectedToEditor => m_Window.ConnectedToEditor;

        public ProfilerProperty CreateProperty() => m_Window.CreateProperty();
        public ProfilerProperty CreateProperty(int sortType) => m_Window.CreateProperty(sortType);

        public void CloseModule(ProfilerModule module) => m_Window.CloseModule(module);
        public void Repaint() => m_Window.Repaint();
    }

    public sealed class CpuUsageBridgeDetailsViewController : ProfilerModuleViewController, ICpuProfilerTimelineViewHost
    {
        const string k_SettingsKeyPrefix = "Profiler.CPUProfilerModule.";
        const string k_ChartFilterThresholdKey = "LightningProfiler.ChartFilterThresholdMs";

        readonly UnityProfilerWindowControllerAdapter m_ProfilerWindowController;

        ProfilerFrameDataHierarchyView m_FrameDataHierarchyView;
        CpuProfilerTimelineView m_TimelineView;
        CpuProfilerViewType m_ViewType = CpuProfilerViewType.Timeline;
        bool m_UpdateViewLive;
        bool m_Initialized;
        float m_ChartFilterThresholdMs;
        readonly StringBuilder m_SpikeLogBuilder = new StringBuilder(4096);
        double m_LastSpikeLogTime;
        const double k_SpikeLogCooldownSeconds = 1.0;
        const int k_MaxLogSamples = 500;

        // Search frame highlight state
        string m_SearchString;
        readonly HashSet<int> m_SearchMatchFrames = new HashSet<int>();
        string m_SearchMatchCachedQuery;
        int m_SearchMatchCachedLastFrame = -1;
        const float k_SearchStripHeight = 10f;

        // Threshold frame highlight state
        readonly HashSet<int> m_ThresholdHotFrames = new HashSet<int>();
        float m_ThresholdCachedValue;
        int m_ThresholdCachedLastFrame = -1;
        const float k_ThresholdStripHeight = 12f;

        // Pause-on-spike / pause-on-match / log-on-spike
        const string k_PauseOnSpikeKey = "LightningProfiler.PauseOnSpike";
        const string k_PauseOnMatchKey = "LightningProfiler.PauseOnMatch";
        const string k_LogOnSpikeKey = "LightningProfiler.LogOnSpike";
        bool m_PauseOnSpike;
        bool m_PauseOnMatch;
        bool m_LogOnSpike;
        bool m_PauseCallbackSubscribed;

        // Ignore EditorLoop frames
        const string k_IgnoreEditorLoopKey = "LightningProfiler.IgnoreEditorLoop";
        bool m_IgnoreEditorLoop;

        // GC filter settings
        const string k_GcFilterThresholdKey = "LightningProfiler.GcFilterThresholdKB";
        const string k_PauseOnGcKey = "LightningProfiler.PauseOnGC";
        float m_GcFilterThresholdKB;
        bool m_PauseOnGc;

        // GC frame highlight state
        readonly HashSet<int> m_GcHotFrames = new HashSet<int>();
        float m_GcCachedThreshold;
        int m_GcCachedLastFrame = -1;
        const float k_GcStripHeight = 12f;


        public CpuUsageBridgeDetailsViewController(ProfilerWindow profilerWindow)
            : base(profilerWindow)
        {
            m_ProfilerWindowController = new UnityProfilerWindowControllerAdapter(profilerWindow);
        }

        public static ProfilerModuleViewController CreateDetailsViewController(ProfilerWindow profilerWindow)
        {
            return new CpuUsageBridgeDetailsViewController(profilerWindow);
        }

        protected override VisualElement CreateView()
        {
            EnsureInitialized();

            var imgui = new IMGUIContainer(DrawDetailsViewViaLegacyIMGUIMethods);
            imgui.style.flexGrow = 1f;

            // Create chart overlay for threshold greyout — sits on top of the chart area.
            imgui.RegisterCallback<AttachToPanelEvent>(_ => AttachChartOverlay());

            return imgui;
        }

        void AttachChartOverlay()
        {
            // Chart overlay removed — using the threshold strip below the chart instead.
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (m_PauseCallbackSubscribed)
                {
                    ProfilerDriver.NewProfilerFrameRecorded -= OnNewProfilerFrame;
                    m_PauseCallbackSubscribed = false;
                }

                if (m_FrameDataHierarchyView != null)
                {
                    m_FrameDataHierarchyView.OnChangeViewType -= OnViewTypeChanged;
                    m_FrameDataHierarchyView.OnToggleLive -= OnHierarchyLiveToggle;
                    m_FrameDataHierarchyView.userChangedThread -= OnUserChangedThread;
                    m_FrameDataHierarchyView.searchChanged -= OnSearchChanged;
                    m_FrameDataHierarchyView.OnDisable();
                }
            }

            base.Dispose(disposing);
        }

        void EnsureInitialized()
        {
            if (m_Initialized)
                return;

            m_FrameDataHierarchyView = new ProfilerFrameDataHierarchyView(k_SettingsKeyPrefix + "HierarchyView.");
            m_FrameDataHierarchyView.OnEnable(m_ProfilerWindowController);
            m_FrameDataHierarchyView.OnChangeViewType += OnViewTypeChanged;
            m_FrameDataHierarchyView.OnToggleLive += OnHierarchyLiveToggle;
            m_FrameDataHierarchyView.userChangedThread += OnUserChangedThread;
            m_FrameDataHierarchyView.searchChanged += OnSearchChanged;

            m_TimelineView = new CpuProfilerTimelineView();
            m_TimelineView.OnEnable(this, ProfilerWindow);

            m_ViewType = (CpuProfilerViewType)EditorPrefs.GetInt(k_SettingsKeyPrefix + "ViewType", (int)CpuProfilerViewType.Timeline);
            m_ChartFilterThresholdMs = EditorPrefs.GetFloat(k_ChartFilterThresholdKey, 0f);
            m_PauseOnSpike = EditorPrefs.GetBool(k_PauseOnSpikeKey, false);
            m_PauseOnMatch = EditorPrefs.GetBool(k_PauseOnMatchKey, false);
            m_LogOnSpike = EditorPrefs.GetBool(k_LogOnSpikeKey, false);
            m_IgnoreEditorLoop = EditorPrefs.GetBool(k_IgnoreEditorLoopKey, true);
            m_GcFilterThresholdKB = EditorPrefs.GetFloat(k_GcFilterThresholdKey, 0f);
            m_PauseOnGc = EditorPrefs.GetBool(k_PauseOnGcKey, false);
            UpdatePauseCallbackSubscription();
            m_Initialized = true;
        }

        void DrawDetailsViewViaLegacyIMGUIMethods()
        {
            var detailsViewContainer = ProfilerWindow.DetailsViewContainer;
            var rs = detailsViewContainer.resolvedStyle;
            var rect = new Rect(0f, 0f, rs.width, rs.height);
            rect.yMin += EditorStyles.contentToolbar.CalcHeight(GUIContent.none, 10f);
            OnModuleDetailsGUI(rect);
        }

        void OnModuleDetailsGUI(Rect rect)
        {
            var currentFrameIndex = (int)ProfilerWindow.selectedFrameIndex;
            var fetchData = !m_ProfilerWindowController.ProfilerWindowOverheadIsAffectingProfilingRecordingData() || m_UpdateViewLive;

            if (m_ViewType == CpuProfilerViewType.Timeline)
            {
                m_TimelineView?.DoGUI(currentFrameIndex, rect, fetchData, ref m_UpdateViewLive);
                return;
            }

            // Draw combined filter controls (spike + GC) in one toolbar
            DrawFilterControls();

            // Update match data
            if (m_ChartFilterThresholdMs > 0f)
                UpdateThresholdFrameMatches();
            if (m_GcFilterThresholdKB > 0f)
                UpdateGcFrameMatches();

            // Draw combined highlight strips side by side
            if (m_ChartFilterThresholdMs > 0f || m_GcFilterThresholdKB > 0f)
                DrawCombinedHighlightStrip(rect);

            // Draw search match indicator strip
            if (!string.IsNullOrEmpty(m_SearchString))
            {
                UpdateSearchFrameMatches();
                DrawSearchFrameHighlightStrip(rect);
            }

            var frameData = fetchData ? GetFrameDataViewForHierarchy() : null;
            m_FrameDataHierarchyView.DoGUI(frameData, fetchData, ref m_UpdateViewLive, m_ViewType, null);
        }

        HierarchyFrameDataView GetFrameDataViewForHierarchy()
        {
            return GetFrameDataView(m_FrameDataHierarchyView.groupName, m_FrameDataHierarchyView.threadName, m_FrameDataHierarchyView.threadId);
        }

        HierarchyFrameDataView GetFrameDataView(string threadGroupName, string threadName, ulong threadId)
        {
            var viewMode = HierarchyFrameDataView.ViewModes.Default;
            if (m_ViewType == CpuProfilerViewType.Hierarchy)
                viewMode |= HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName;

            return m_ProfilerWindowController.GetFrameDataView(
                threadGroupName,
                threadName,
                threadId,
                viewMode,
                m_FrameDataHierarchyView.sortedProfilerColumn,
                m_FrameDataHierarchyView.sortedProfilerColumnAscending);
        }

        HierarchyFrameDataView GetFrameDataView(int threadIndex)
        {
            var viewMode = HierarchyFrameDataView.ViewModes.Default;
            if (m_ViewType == CpuProfilerViewType.Hierarchy)
                viewMode |= HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName;

            return m_ProfilerWindowController.GetFrameDataView(
                threadIndex,
                viewMode,
                m_FrameDataHierarchyView.sortedProfilerColumn,
                m_FrameDataHierarchyView.sortedProfilerColumnAscending);
        }

        void OnViewTypeChanged(CpuProfilerViewType viewType)
        {
            m_ViewType = viewType;
            EditorPrefs.SetInt(k_SettingsKeyPrefix + "ViewType", (int)m_ViewType);
            ProfilerWindow.Repaint();
        }

        void OnHierarchyLiveToggle(bool live)
        {
            m_UpdateViewLive = live;
        }

        void OnUserChangedThread(string groupName, string threadName, int threadIndex)
        {
            var frameData = threadIndex >= 0
                ? GetFrameDataView(threadIndex)
                : GetFrameDataView(groupName, threadName, FrameDataView.invalidThreadId);

            m_FrameDataHierarchyView.SetFrameDataView(frameData);
            ProfilerWindow.Repaint();
        }

        void DrawFilterControls()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // --- Spike filter ---
            GUILayout.Label("Spike", EditorStyles.miniLabel, GUILayout.Width(34));
            var newThreshold = EditorGUILayout.FloatField(m_ChartFilterThresholdMs, EditorStyles.toolbarTextField, GUILayout.Width(50));
            GUILayout.Label("ms", EditorStyles.miniLabel, GUILayout.Width(20));

            if (newThreshold != m_ChartFilterThresholdMs)
            {
                m_ChartFilterThresholdMs = Mathf.Max(0f, newThreshold);
                EditorPrefs.SetFloat(k_ChartFilterThresholdKey, m_ChartFilterThresholdMs);

                var module = ProfilerWindow.selectedModule as FilterableProfilerModule;
                if (module != null)
                    module.SetChartFilterThreshold(m_ChartFilterThresholdMs);

                UpdatePauseCallbackSubscription();
            }

            using (new EditorGUI.DisabledScope(m_ChartFilterThresholdMs <= 0f))
            {
                var newPause = GUILayout.Toggle(m_PauseOnSpike,
                    EditorGUIUtility.TrTextContent("Pause on spike", "Pause play mode when a frame exceeds the spike threshold."),
                    EditorStyles.toolbarButton);
                if (newPause != m_PauseOnSpike)
                {
                    m_PauseOnSpike = newPause;
                    EditorPrefs.SetBool(k_PauseOnSpikeKey, m_PauseOnSpike);
                    UpdatePauseCallbackSubscription();
                }

                var newLog = GUILayout.Toggle(m_LogOnSpike,
                    EditorGUIUtility.TrTextContent("Log on spike", "Log spike frame details to the console."),
                    EditorStyles.toolbarButton);
                if (newLog != m_LogOnSpike)
                {
                    m_LogOnSpike = newLog;
                    EditorPrefs.SetBool(k_LogOnSpikeKey, m_LogOnSpike);
                    UpdatePauseCallbackSubscription();
                }
            }

            GUILayout.Space(6);

            // --- GC filter ---
            GUILayout.Label("GC", EditorStyles.miniLabel, GUILayout.Width(20));
            var newGcThreshold = EditorGUILayout.FloatField(m_GcFilterThresholdKB, EditorStyles.toolbarTextField, GUILayout.Width(50));
            GUILayout.Label("KB", EditorStyles.miniLabel, GUILayout.Width(20));

            if (newGcThreshold != m_GcFilterThresholdKB)
            {
                m_GcFilterThresholdKB = Mathf.Max(0f, newGcThreshold);
                EditorPrefs.SetFloat(k_GcFilterThresholdKey, m_GcFilterThresholdKB);
                UpdatePauseCallbackSubscription();
            }

            using (new EditorGUI.DisabledScope(m_GcFilterThresholdKB <= 0f))
            {
                var newPauseGc = GUILayout.Toggle(m_PauseOnGc,
                    EditorGUIUtility.TrTextContent("Pause on GC", "Pause play mode when a frame's GC allocations exceed the threshold."),
                    EditorStyles.toolbarButton);
                if (newPauseGc != m_PauseOnGc)
                {
                    m_PauseOnGc = newPauseGc;
                    EditorPrefs.SetBool(k_PauseOnGcKey, m_PauseOnGc);
                    UpdatePauseCallbackSubscription();
                }
            }

            GUILayout.Space(6);

            // --- Pause on match ---
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(m_SearchString)))
            {
                var newMatch = GUILayout.Toggle(m_PauseOnMatch,
                    EditorGUIUtility.TrTextContent("Pause on match", "Pause play mode when a frame contains a sample matching the search term."),
                    EditorStyles.toolbarButton);
                if (newMatch != m_PauseOnMatch)
                {
                    m_PauseOnMatch = newMatch;
                    EditorPrefs.SetBool(k_PauseOnMatchKey, m_PauseOnMatch);
                    UpdatePauseCallbackSubscription();
                }
            }

            GUILayout.FlexibleSpace();

            // --- Ignore EditorLoop ---
            var newIgnore = GUILayout.Toggle(m_IgnoreEditorLoop,
                EditorGUIUtility.TrTextContent("Ignore EditorLoop", "Skip EditorLoop frames for spike/GC detection and highlighting."),
                EditorStyles.toolbarButton);
            if (newIgnore != m_IgnoreEditorLoop)
            {
                m_IgnoreEditorLoop = newIgnore;
                EditorPrefs.SetBool(k_IgnoreEditorLoopKey, m_IgnoreEditorLoop);
                // Force rescan of highlight strips
                m_ThresholdCachedLastFrame = -1;
                m_ThresholdCachedValue = -1f;
                m_ThresholdHotFrames.Clear();
                m_GcCachedLastFrame = -1;
                m_GcCachedThreshold = -1f;
                m_GcHotFrames.Clear();
            }

            EditorGUILayout.EndHorizontal();
        }

        /// Returns the total EditorLoop time in ms for the given frame (sums all EditorLoop samples).
        static float GetEditorLoopTimeMs(int frameIndex)
        {
            float totalMs = 0f;
            using (var raw = ProfilerDriver.GetRawFrameDataView(frameIndex, 0))
            {
                if (raw == null || !raw.valid)
                    return 0f;
                for (int i = 0; i < raw.sampleCount; i++)
                {
                    if (raw.GetSampleName(i) == "EditorLoop")
                        totalMs += raw.GetSampleTimeMs(i);
                }
            }
            return totalMs;
        }

        /// Collects the sample indices of all EditorLoop samples (they have no children).
        static HashSet<int> FindEditorLoopIndices(RawFrameDataView raw)
        {
            var indices = new HashSet<int>();
            for (int i = 0; i < raw.sampleCount; i++)
            {
                if (raw.GetSampleName(i) == "EditorLoop")
                    indices.Add(i);
            }
            return indices;
        }

        void UpdatePauseCallbackSubscription()
        {
            bool needSpike = (m_PauseOnSpike || m_LogOnSpike) && m_ChartFilterThresholdMs > 0f;
            bool needMatch = m_PauseOnMatch && !string.IsNullOrEmpty(m_SearchString);
            bool needGc = m_PauseOnGc && m_GcFilterThresholdKB > 0f;
            bool shouldSubscribe = needSpike || needMatch || needGc;

            if (shouldSubscribe && !m_PauseCallbackSubscribed)
            {
                ProfilerDriver.NewProfilerFrameRecorded += OnNewProfilerFrame;
                m_PauseCallbackSubscribed = true;
            }
            else if (!shouldSubscribe && m_PauseCallbackSubscribed)
            {
                ProfilerDriver.NewProfilerFrameRecorded -= OnNewProfilerFrame;
                m_PauseCallbackSubscribed = false;
            }
        }

        void OnNewProfilerFrame(int connectionId, int frameIndex)
        {
            int checkFrame = frameIndex - 1;
            if (checkFrame < ProfilerDriver.firstFrameIndex)
                return;

            bool isPlaying = EditorApplication.isPlaying && !EditorApplication.isPaused;

            // Check spike threshold (works in both editor and play mode).
            bool isSpike = false;
            if ((m_PauseOnSpike || m_LogOnSpike) && m_ChartFilterThresholdMs > 0f)
            {
                float frameTimeMs;
                using (var iter = new ProfilerFrameDataIterator())
                {
                    iter.SetRoot(checkFrame, 0);
                    frameTimeMs = iter.frameTimeMS;
                }
                if (m_IgnoreEditorLoop)
                    frameTimeMs -= GetEditorLoopTimeMs(checkFrame);
                if (frameTimeMs >= m_ChartFilterThresholdMs)
                    isSpike = true;
            }

            if (isSpike && m_LogOnSpike)
                LogSpikeFrame(checkFrame);

            // Check GC threshold.
            bool isGcSpike = false;
            if (m_PauseOnGc && m_GcFilterThresholdKB > 0f)
            {
                long gcBytes = GetFrameGcAllocBytes(checkFrame, 0, m_IgnoreEditorLoop);
                if (gcBytes >= (long)(m_GcFilterThresholdKB * 1024f))
                    isGcSpike = true;
            }

            // Pause logic only applies during play mode.
            if (!isPlaying)
                return;

            bool shouldPause = false;

            if (isSpike && m_PauseOnSpike)
                shouldPause = true;

            if (!shouldPause && isGcSpike && m_PauseOnGc)
                shouldPause = true;

            // Check search term match.
            if (!shouldPause && m_PauseOnMatch && !string.IsNullOrEmpty(m_SearchString))
            {
                int threadIndex = m_FrameDataHierarchyView.threadIndex;
                if (threadIndex < 0) threadIndex = 0;
                if (FrameContainsSearchTerm(checkFrame, threadIndex, m_SearchString))
                    shouldPause = true;
            }

            if (shouldPause)
            {
                var pauseFrame = checkFrame;

                void Pause()
                {
                    EditorApplication.delayCall -= Pause;
                    EditorApplication.isPaused = true;
                    m_ProfilerWindowController.selectedFrameIndex = pauseFrame;
                }

                EditorApplication.delayCall += Pause;
            }
        }

        void LogSpikeFrame(int frameIndex)
        {
            // Rate-limit to prevent feedback loop when profiling the editor.
            double now = EditorApplication.timeSinceStartup;
            if (now - m_LastSpikeLogTime < k_SpikeLogCooldownSeconds)
                return;
            m_LastSpikeLogTime = now;

            var sb = m_SpikeLogBuilder;
            sb.Clear();

            using (var iter = new ProfilerFrameDataIterator())
            {
                iter.SetRoot(frameIndex, 0);
                sb.Append($"[Spike] Frame {frameIndex} — CPU: {iter.frameTimeMS:F2}ms, GPU: {iter.frameGpuTimeMS:F2}ms (threshold: {m_ChartFilterThresholdMs:F0}ms)");
            }

            // Dump sample hierarchy from the main thread (capped to avoid OOM).
            using (var raw = ProfilerDriver.GetRawFrameDataView(frameIndex, 0))
            {
                if (raw != null && raw.valid && raw.sampleCount > 1)
                {
                    int sampleLimit = Mathf.Min(raw.sampleCount, k_MaxLogSamples + 1);

                    // Build depth array via pre-order walk using children counts.
                    var depths = new int[raw.sampleCount];
                    var depthStack = new Stack<(int remaining, int depth)>();
                    depthStack.Push((raw.GetSampleChildrenCount(0), 0));

                    for (int i = 1; i < raw.sampleCount; i++)
                    {
                        var top = depthStack.Pop();
                        depths[i] = top.depth + 1;
                        int remaining = top.remaining - 1;
                        if (remaining > 0)
                            depthStack.Push((remaining, top.depth));

                        int childCount = raw.GetSampleChildrenCount(i);
                        if (childCount > 0)
                            depthStack.Push((childCount, depths[i]));
                    }

                    // Print each sample indented by depth.
                    for (int i = 1; i < sampleLimit; i++)
                    {
                        float timeMs = raw.GetSampleTimeMs(i);
                        string name = raw.GetSampleName(i);
                        int depth = depths[i];

                        sb.AppendLine();
                        sb.Append(' ', depth * 2);
                        sb.Append($"{name}: {timeMs:F2}ms");
                    }

                    if (raw.sampleCount > sampleLimit)
                    {
                        sb.AppendLine();
                        sb.Append($"... ({raw.sampleCount - sampleLimit} more samples truncated)");
                    }
                }
            }

            Debug.Log(sb.ToString());
        }

        void UpdateThresholdFrameMatches()
        {
            int lastFrame = ProfilerDriver.lastFrameIndex;
            bool thresholdChanged = m_ChartFilterThresholdMs != m_ThresholdCachedValue;

            if (!thresholdChanged && m_ThresholdCachedLastFrame == lastFrame)
                return;

            int firstFrame = ProfilerDriver.firstFrameIndex;
            if (firstFrame < 0 || lastFrame < 0)
                return;

            if (thresholdChanged)
            {
                // Full rescan needed — but limit to visible range only.
                m_ThresholdHotFrames.Clear();
                m_ThresholdCachedValue = m_ChartFilterThresholdMs;

                int visibleFirst = Mathf.Max(firstFrame, lastFrame + 1 - ProfilerUserSettings.frameCount);
                for (int frame = visibleFirst; frame <= lastFrame; frame++)
                    CheckAndAddFrame(frame);
            }
            else
            {
                // Incremental — only scan new frames since last check.
                int scanFrom = Mathf.Max(firstFrame, m_ThresholdCachedLastFrame + 1);
                for (int frame = scanFrom; frame <= lastFrame; frame++)
                    CheckAndAddFrame(frame);

                // Trim frames that fell out of the available range.
                if (m_ThresholdHotFrames.Count > 0)
                    m_ThresholdHotFrames.RemoveWhere(f => f < firstFrame);
            }

            m_ThresholdCachedLastFrame = lastFrame;
        }

        void CheckAndAddFrame(int frame)
        {
            float frameTimeMs;
            using (var iter = new ProfilerFrameDataIterator())
            {
                iter.SetRoot(frame, 0);
                frameTimeMs = iter.frameTimeMS;
            }
            if (m_IgnoreEditorLoop)
                frameTimeMs -= GetEditorLoopTimeMs(frame);
            if (frameTimeMs >= m_ChartFilterThresholdMs)
                m_ThresholdHotFrames.Add(frame);
        }

        void DrawCombinedHighlightStrip(Rect containerRect)
        {
            bool hasSpike = m_ChartFilterThresholdMs > 0f;
            bool hasGc = m_GcFilterThresholdKB > 0f;

            int frameCount = ProfilerUserSettings.frameCount;
            int firstEmptyFrame = ProfilerDriver.lastFrameIndex + 1 - frameCount;
            float sideWidth = Chart.kSideWidth;
            int selectedFrame = (int)ProfilerWindow.selectedFrameIndex;
            int selectedRelative = selectedFrame - firstEmptyFrame;

            // --- Spike strip (top) ---
            if (hasSpike)
            {
                var stripRect = GUILayoutUtility.GetRect(containerRect.width, k_ThresholdStripHeight);
                if (Event.current.type == EventType.Repaint)
                {
                    var drawRect = new Rect(stripRect.x + sideWidth, stripRect.y, stripRect.width - sideWidth, k_ThresholdStripHeight);
                    float frameWidth = drawRect.width / frameCount;

                    EditorGUI.DrawRect(drawRect, new Color(0.18f, 0.18f, 0.18f, 0.9f));

                    var hotColor = new Color(0.2f, 0.85f, 0.4f, 0.95f);
                    foreach (var frame in m_ThresholdHotFrames)
                    {
                        int rel = frame - firstEmptyFrame;
                        if (rel < 0 || rel >= frameCount) continue;
                        EditorGUI.DrawRect(new Rect(drawRect.x + rel * frameWidth, drawRect.y, Mathf.Max(1f, frameWidth), k_ThresholdStripHeight), hotColor);
                    }

                    if (selectedRelative >= 0 && selectedRelative < frameCount)
                        EditorGUI.DrawRect(new Rect(drawRect.x + selectedRelative * frameWidth, drawRect.y, Mathf.Max(2f, frameWidth), k_ThresholdStripHeight), new Color(1f, 1f, 1f, 0.8f));

                    var labelRect = new Rect(stripRect.x + 2f, stripRect.y, sideWidth - 4f, k_ThresholdStripHeight);
                    GUI.Label(labelRect, $">={m_ChartFilterThresholdMs:F0}ms", EditorStyles.miniLabel);
                }
            }

            // --- GC strip (below spike) ---
            if (hasGc)
            {
                var stripRect = GUILayoutUtility.GetRect(containerRect.width, k_GcStripHeight);
                if (Event.current.type == EventType.Repaint)
                {
                    var drawRect = new Rect(stripRect.x + sideWidth, stripRect.y, stripRect.width - sideWidth, k_GcStripHeight);
                    float frameWidth = drawRect.width / frameCount;

                    EditorGUI.DrawRect(drawRect, new Color(0.18f, 0.18f, 0.18f, 0.9f));

                    var gcColor = new Color(0.95f, 0.3f, 0.3f, 0.95f);
                    foreach (var frame in m_GcHotFrames)
                    {
                        int rel = frame - firstEmptyFrame;
                        if (rel < 0 || rel >= frameCount) continue;
                        EditorGUI.DrawRect(new Rect(drawRect.x + rel * frameWidth, drawRect.y, Mathf.Max(1f, frameWidth), k_GcStripHeight), gcColor);
                    }

                    if (selectedRelative >= 0 && selectedRelative < frameCount)
                        EditorGUI.DrawRect(new Rect(drawRect.x + selectedRelative * frameWidth, drawRect.y, Mathf.Max(2f, frameWidth), k_GcStripHeight), new Color(1f, 1f, 1f, 0.8f));

                    var labelRect = new Rect(stripRect.x + 2f, stripRect.y, sideWidth - 4f, k_GcStripHeight);
                    GUI.Label(labelRect, $">={m_GcFilterThresholdKB:F0}KB", EditorStyles.miniLabel);
                }
            }
        }

        static long GetFrameGcAllocBytes(int frameIndex, int threadIndex, bool excludeEditorLoop = false)
        {
            long totalGcBytes = 0;
            using (var raw = ProfilerDriver.GetRawFrameDataView(frameIndex, threadIndex))
            {
                if (raw == null || !raw.valid)
                    return 0;
                int gcMarkerId = raw.GetMarkerId("GC.Alloc");
                if (gcMarkerId == FrameDataView.invalidMarkerId)
                    return 0;

                HashSet<int> editorIndices = null;
                if (excludeEditorLoop)
                    editorIndices = FindEditorLoopIndices(raw);

                for (int i = 0; i < raw.sampleCount; i++)
                {
                    if (editorIndices != null && editorIndices.Contains(i))
                        continue;
                    if (raw.GetSampleMarkerId(i) == gcMarkerId && raw.GetSampleMetadataCount(i) > 0)
                        totalGcBytes += raw.GetSampleMetadataAsLong(i, 0);
                }
            }
            return totalGcBytes;
        }

        void UpdateGcFrameMatches()
        {
            int lastFrame = ProfilerDriver.lastFrameIndex;
            bool thresholdChanged = m_GcFilterThresholdKB != m_GcCachedThreshold;

            if (!thresholdChanged && m_GcCachedLastFrame == lastFrame)
                return;

            int firstFrame = ProfilerDriver.firstFrameIndex;
            if (firstFrame < 0 || lastFrame < 0)
                return;

            if (thresholdChanged)
            {
                m_GcHotFrames.Clear();
                m_GcCachedThreshold = m_GcFilterThresholdKB;

                int visibleFirst = Mathf.Max(firstFrame, lastFrame + 1 - ProfilerUserSettings.frameCount);
                for (int frame = visibleFirst; frame <= lastFrame; frame++)
                    CheckAndAddGcFrame(frame);
            }
            else
            {
                int scanFrom = Mathf.Max(firstFrame, m_GcCachedLastFrame + 1);
                for (int frame = scanFrom; frame <= lastFrame; frame++)
                    CheckAndAddGcFrame(frame);

                if (m_GcHotFrames.Count > 0)
                    m_GcHotFrames.RemoveWhere(f => f < firstFrame);
            }

            m_GcCachedLastFrame = lastFrame;
        }

        void CheckAndAddGcFrame(int frame)
        {
            long gcBytes = GetFrameGcAllocBytes(frame, 0, m_IgnoreEditorLoop);
            if (gcBytes >= (long)(m_GcFilterThresholdKB * 1024f))
                m_GcHotFrames.Add(frame);
        }

        void OnSearchChanged(string newSearch)
        {
            m_SearchString = newSearch;
            m_SearchMatchFrames.Clear();
            m_SearchMatchCachedQuery = null;
            m_SearchMatchCachedLastFrame = -1;
            UpdatePauseCallbackSubscription();
            ProfilerWindow.Repaint();
        }

        void UpdateSearchFrameMatches()
        {
            if (string.IsNullOrEmpty(m_SearchString))
            {
                m_SearchMatchFrames.Clear();
                return;
            }

            int lastFrame = ProfilerDriver.lastFrameIndex;
            bool queryChanged = m_SearchString != m_SearchMatchCachedQuery;

            if (!queryChanged && m_SearchMatchCachedLastFrame == lastFrame)
                return;

            int firstFrame = ProfilerDriver.firstFrameIndex;
            if (firstFrame < 0 || lastFrame < 0)
                return;

            int threadIndex = m_FrameDataHierarchyView.threadIndex;
            if (threadIndex < 0)
                threadIndex = 0;

            if (queryChanged)
            {
                // Full rescan — limit to visible range.
                m_SearchMatchFrames.Clear();
                m_SearchMatchCachedQuery = m_SearchString;

                int visibleFirst = Mathf.Max(firstFrame, lastFrame + 1 - ProfilerUserSettings.frameCount);
                for (int frame = visibleFirst; frame <= lastFrame; frame++)
                {
                    if (FrameContainsSearchTerm(frame, threadIndex, m_SearchString))
                        m_SearchMatchFrames.Add(frame);
                }
            }
            else
            {
                // Incremental — only scan new frames.
                int scanFrom = Mathf.Max(firstFrame, m_SearchMatchCachedLastFrame + 1);
                for (int frame = scanFrom; frame <= lastFrame; frame++)
                {
                    if (FrameContainsSearchTerm(frame, threadIndex, m_SearchString))
                        m_SearchMatchFrames.Add(frame);
                }
                m_SearchMatchFrames.RemoveWhere(f => f < firstFrame);
            }

            m_SearchMatchCachedLastFrame = lastFrame;
        }

        static bool FrameContainsSearchTerm(int frameIndex, int threadIndex, string search)
        {
            using (var raw = ProfilerDriver.GetRawFrameDataView(frameIndex, threadIndex))
            {
                if (raw == null || !raw.valid)
                    return false;
                for (int i = 1; i < raw.sampleCount; i++)
                {
                    var name = raw.GetSampleName(i);
                    if (name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            return false;
        }

        void DrawSearchFrameHighlightStrip(Rect containerRect)
        {
            if (m_SearchMatchFrames.Count == 0 && !string.IsNullOrEmpty(m_SearchMatchCachedQuery))
            {
                // No matches — show empty strip with a label
                var noMatchRect = GUILayoutUtility.GetRect(containerRect.width, k_SearchStripHeight);
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(noMatchRect, new Color(0.15f, 0.15f, 0.15f, 0.8f));
                return;
            }

            if (m_SearchMatchFrames.Count == 0)
                return;

            int frameCount = ProfilerUserSettings.frameCount;
            int firstEmptyFrame = ProfilerDriver.lastFrameIndex + 1 - frameCount;

            var stripRect = GUILayoutUtility.GetRect(containerRect.width, k_SearchStripHeight);

            if (Event.current.type != EventType.Repaint)
                return;

            float sideWidth = Chart.kSideWidth;
            var drawRect = new Rect(stripRect.x + sideWidth, stripRect.y, stripRect.width - sideWidth, k_SearchStripHeight);

            // Background
            EditorGUI.DrawRect(drawRect, new Color(0.12f, 0.12f, 0.12f, 0.8f));

            float frameWidth = drawRect.width / frameCount;
            var highlightColor = new Color(1f, 0.75f, 0.1f, 0.95f);

            foreach (var frame in m_SearchMatchFrames)
            {
                int relativeFrame = frame - firstEmptyFrame;
                if (relativeFrame < 0 || relativeFrame >= frameCount)
                    continue;

                var barRect = new Rect(
                    drawRect.x + relativeFrame * frameWidth,
                    drawRect.y,
                    Mathf.Max(1f, frameWidth),
                    k_SearchStripHeight);

                EditorGUI.DrawRect(barRect, highlightColor);
            }

            // Draw selected frame indicator on top
            int selectedFrame = (int)ProfilerWindow.selectedFrameIndex;
            int selectedRelative = selectedFrame - firstEmptyFrame;
            if (selectedRelative >= 0 && selectedRelative < frameCount)
            {
                var selRect = new Rect(
                    drawRect.x + selectedRelative * frameWidth,
                    drawRect.y,
                    Mathf.Max(2f, frameWidth),
                    k_SearchStripHeight);
                EditorGUI.DrawRect(selRect, new Color(1f, 1f, 1f, 0.7f));
            }
        }

        public void DrawTimelineToolbar(ProfilerFrameDataIterator iter, ref bool updateViewLive)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            m_FrameDataHierarchyView.DrawViewTypePopup(m_ViewType, OnViewTypeChanged);

            using (new EditorGUI.DisabledScope(UnityEditor.MPE.ProcessService.level != UnityEditor.MPE.ProcessLevel.Main))
            {
                var newUpdateViewLive = GUILayout.Toggle(updateViewLive, EditorGUIUtility.TrTextContent("Live", "Display the current or selected frame while recording Playmode or Editor."), EditorStyles.toolbarButton);
                if (newUpdateViewLive != updateViewLive)
                {
                    updateViewLive = newUpdateViewLive;
                    m_UpdateViewLive = newUpdateViewLive;
                }
            }

            GUILayout.FlexibleSpace();
            if (iter != null)
            {
                var content = EditorGUIUtility.TrTextContent("CPU:{0}ms   GPU:{1}ms");
                GUILayout.Label(string.Format(content.text, iter.frameTimeMS.ToString("N2"), iter.frameGpuTimeMS.ToString("N2")), EditorStyles.toolbarLabel);
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
