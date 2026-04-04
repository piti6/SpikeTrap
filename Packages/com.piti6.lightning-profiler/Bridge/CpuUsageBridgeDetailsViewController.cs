using System;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling.Editor;
using UnityEditor;
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

    public sealed class CpuUsageBridgeDetailsViewController : ProfilerModuleViewController
    {
        const string k_SettingsKeyPrefix = "Profiler.CPUProfilerModule.";
        const string k_ChartFilterThresholdKey = "LightningProfiler.ChartFilterThresholdMs";

        readonly UnityProfilerWindowControllerAdapter m_ProfilerWindowController;

        ProfilerFrameDataHierarchyView m_FrameDataHierarchyView;
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

        // Unified pause/log on any filter match
        const string k_PauseOnFilterKey = "LightningProfiler.PauseOnFilter";
        const string k_LogOnFilterKey = "LightningProfiler.LogOnFilter";
        bool m_PauseOnFilter;
        bool m_LogOnFilter;
        bool m_PauseCallbackSubscribed;

        // Ignore EditorLoop frames
        const string k_IgnoreEditorLoopKey = "LightningProfiler.IgnoreEditorLoop";
        bool m_IgnoreEditorLoop;

        // GC filter settings
        const string k_GcFilterThresholdKey = "LightningProfiler.GcFilterThresholdKB";
        float m_GcFilterThresholdKB;

        // GC frame highlight state
        readonly HashSet<int> m_GcHotFrames = new HashSet<int>();
        float m_GcCachedThreshold;
        int m_GcCachedLastFrame = -1;
        const float k_GcStripHeight = 12f;

        // Save-only-marked-frames
        const string k_SaveMarkedOnlyKey = "LightningProfiler.SaveMarkedOnly";
        const string k_DefaultFrameHistoryKey = "LightningProfiler.DefaultFrameHistory";
        bool m_SaveMarkedOnly;
        readonly List<string> m_MarkedFrameTempFiles = new List<string>();
        int m_DefaultFrameHistoryLength;
        const int k_SaveMarkedBufferSize = 1;
        private static readonly HierarchyFrameDataView.ViewModes _viewMode =
            HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName |
            HierarchyFrameDataView.ViewModes.HideEditorOnlySamples;


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

                // Clean up any unsaved marked frame temp files
                foreach (var f in m_MarkedFrameTempFiles)
                {
                    try { if (System.IO.File.Exists(f)) System.IO.File.Delete(f); } catch { }
                }
                m_MarkedFrameTempFiles.Clear();

                if (m_FrameDataHierarchyView != null)
                {
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
            m_FrameDataHierarchyView.OnToggleLive += OnHierarchyLiveToggle;
            m_FrameDataHierarchyView.userChangedThread += OnUserChangedThread;
            m_FrameDataHierarchyView.searchChanged += OnSearchChanged;
            m_FrameDataHierarchyView.hideSearchBar = true;

            m_ChartFilterThresholdMs = EditorPrefs.GetFloat(k_ChartFilterThresholdKey, 0f);
            m_PauseOnFilter = EditorPrefs.GetBool(k_PauseOnFilterKey, false);
            m_LogOnFilter = EditorPrefs.GetBool(k_LogOnFilterKey, false);
            m_IgnoreEditorLoop = EditorPrefs.GetBool(k_IgnoreEditorLoopKey, true);
            m_GcFilterThresholdKB = EditorPrefs.GetFloat(k_GcFilterThresholdKey, 0f);
            m_SaveMarkedOnly = EditorPrefs.GetBool(k_SaveMarkedOnlyKey, false);
            if (m_SaveMarkedOnly)
            {
                // Restore the original frame count from before collect mode was enabled
                m_DefaultFrameHistoryLength = EditorPrefs.GetInt(k_DefaultFrameHistoryKey, ProfilerUserSettings.frameCount);
                // Re-apply the shrink (domain reload may have reset it)
                ProfilerUserSettings.frameCount = k_SaveMarkedBufferSize;
            }
            else
            {
                m_DefaultFrameHistoryLength = ProfilerUserSettings.frameCount;
            }
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
            var fetchData = !m_ProfilerWindowController.ProfilerWindowOverheadIsAffectingProfilingRecordingData() || m_UpdateViewLive;

            // Draw combined filter controls (spike + GC) in one toolbar
            DrawFilterControls();

            // Update match data
            if (m_ChartFilterThresholdMs > 0f)
                UpdateThresholdFrameMatches();
            if (m_GcFilterThresholdKB > 0f)
                UpdateGcFrameMatches();
            if (!string.IsNullOrEmpty(m_SearchString))
                UpdateSearchFrameMatches();

            // Draw combined highlight strips (spike / GC / search)
            if (m_ChartFilterThresholdMs > 0f || m_GcFilterThresholdKB > 0f || !string.IsNullOrEmpty(m_SearchString))
                DrawCombinedHighlightStrip(rect);

            var frameData = fetchData ? GetFrameDataViewForHierarchy() : null;
            m_FrameDataHierarchyView.DoGUI(frameData, fetchData, ref m_UpdateViewLive, null);
        }

        HierarchyFrameDataView GetFrameDataViewForHierarchy()
        {
            return GetFrameDataView(m_FrameDataHierarchyView.groupName, m_FrameDataHierarchyView.threadName, m_FrameDataHierarchyView.threadId);
        }

        HierarchyFrameDataView GetFrameDataView(string threadGroupName, string threadName, ulong threadId)
        {
            return m_ProfilerWindowController.GetFrameDataView(
                threadGroupName,
                threadName,
                threadId,
                _viewMode,
                m_FrameDataHierarchyView.sortedProfilerColumn,
                m_FrameDataHierarchyView.sortedProfilerColumnAscending);
        }

        HierarchyFrameDataView GetFrameDataView(int threadIndex)
        {
            return m_ProfilerWindowController.GetFrameDataView(
                threadIndex,
                _viewMode,
                m_FrameDataHierarchyView.sortedProfilerColumn,
                m_FrameDataHierarchyView.sortedProfilerColumnAscending);
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

            // --- Filters ---
            GUILayout.Label("Spike", EditorStyles.miniLabel, GUILayout.Width(34));
            var newThreshold = EditorGUILayout.FloatField(m_ChartFilterThresholdMs, EditorStyles.toolbarTextField, GUILayout.Width(50));
            GUILayout.Label("ms", EditorStyles.miniLabel, GUILayout.Width(18));

            if (newThreshold != m_ChartFilterThresholdMs)
            {
                m_ChartFilterThresholdMs = Mathf.Max(0f, newThreshold);
                EditorPrefs.SetFloat(k_ChartFilterThresholdKey, m_ChartFilterThresholdMs);

                var module = ProfilerWindow.selectedModule as FilterableProfilerModule;
                if (module != null)
                    module.SetChartFilterThreshold(m_ChartFilterThresholdMs);

                UpdatePauseCallbackSubscription();
            }

            GUILayout.Label("GC", EditorStyles.miniLabel, GUILayout.Width(18));
            var newGcThreshold = EditorGUILayout.FloatField(m_GcFilterThresholdKB, EditorStyles.toolbarTextField, GUILayout.Width(50));
            GUILayout.Label("KB", EditorStyles.miniLabel, GUILayout.Width(18));

            if (newGcThreshold != m_GcFilterThresholdKB)
            {
                m_GcFilterThresholdKB = Mathf.Max(0f, newGcThreshold);
                EditorPrefs.SetFloat(k_GcFilterThresholdKey, m_GcFilterThresholdKB);
                UpdatePauseCallbackSubscription();
            }

            m_FrameDataHierarchyView.DrawSearchBarExternal();

            GUILayout.Space(4);

            // --- Unified Pause / Log ---
            {
                bool anyFilter = m_ChartFilterThresholdMs > 0f || m_GcFilterThresholdKB > 0f || !string.IsNullOrEmpty(m_SearchString);
                using (new EditorGUI.DisabledScope(!anyFilter))
                {
                    var newPause = GUILayout.Toggle(m_PauseOnFilter,
                        EditorGUIUtility.TrTextContent("Pause", "Pause play mode when any active filter matches a frame."),
                        EditorStyles.toolbarButton);
                    if (newPause != m_PauseOnFilter)
                    {
                        m_PauseOnFilter = newPause;
                        EditorPrefs.SetBool(k_PauseOnFilterKey, m_PauseOnFilter);
                        UpdatePauseCallbackSubscription();
                    }

                    var newLog = GUILayout.Toggle(m_LogOnFilter,
                        EditorGUIUtility.TrTextContent("Log", "Log frame details when any active filter matches."),
                        EditorStyles.toolbarButton);
                    if (newLog != m_LogOnFilter)
                    {
                        m_LogOnFilter = newLog;
                        EditorPrefs.SetBool(k_LogOnFilterKey, m_LogOnFilter);
                        UpdatePauseCallbackSubscription();
                    }
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

            // --- Save marked only ---
            var newSaveMarked = GUILayout.Toggle(m_SaveMarkedOnly,
                EditorGUIUtility.TrTextContent($"Collect marked ({m_MarkedFrameTempFiles.Count})",
                    "When enabled, each frame matching spike/GC/search is saved to a temp file. Use the Save button to merge them into a single .data file."),
                EditorStyles.toolbarButton);
            if (newSaveMarked != m_SaveMarkedOnly)
            {
                m_SaveMarkedOnly = newSaveMarked;
                EditorPrefs.SetBool(k_SaveMarkedOnlyKey, m_SaveMarkedOnly);
                if (m_SaveMarkedOnly)
                {
                    // Persist the original frame count so it survives domain reloads
                    m_DefaultFrameHistoryLength = ProfilerUserSettings.frameCount;
                    EditorPrefs.SetInt(k_DefaultFrameHistoryKey, m_DefaultFrameHistoryLength);
                    ProfilerUserSettings.frameCount = k_SaveMarkedBufferSize;
                }
                else
                {
                    // Restore normal buffer size
                    ProfilerUserSettings.frameCount = m_DefaultFrameHistoryLength;
                }
                UpdatePauseCallbackSubscription();
            }

            using (new EditorGUI.DisabledScope(m_MarkedFrameTempFiles.Count == 0))
            {
                if (GUILayout.Button(
                    EditorGUIUtility.TrTextContent("Save marked", "Merge all collected marked frames into a single .data file."),
                    EditorStyles.toolbarButton))
                {
                    SaveMergedMarkedFrames();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        void SaveMergedMarkedFrames()
        {
            // Stop profiler recording via the ProfilerWindow (ProfilerDriver.enabled gets re-enabled by the window)
            ProfilerWindow.SetRecordingEnabled(false);
            if (EditorApplication.isPlaying && !EditorApplication.isPaused)
                EditorApplication.isPaused = true;

            // Disable collect mode so the callback stops saving temp files during merge
            m_SaveMarkedOnly = false;
            EditorPrefs.SetBool(k_SaveMarkedOnlyKey, false);
            if (m_PauseCallbackSubscribed)
            {
                ProfilerDriver.NewProfilerFrameRecorded -= OnNewProfilerFrame;
                m_PauseCallbackSubscribed = false;
            }

            var savePath = EditorUtility.SaveFilePanel("Save Marked Frames", "", "marked_profile", "data");
            if (string.IsNullOrEmpty(savePath))
                return;

            // Remember current state
            var tempFiles = new List<string>(m_MarkedFrameTempFiles);

            // Restore large buffer so all merged frames fit
            ProfilerUserSettings.frameCount = Mathf.Max(m_DefaultFrameHistoryLength, tempFiles.Count * k_SaveMarkedBufferSize + 10);
            ProfilerDriver.ClearAllFrames();

            // Load each temp file, appending frames
            bool first = true;
            foreach (var tempFile in tempFiles)
            {
                if (!System.IO.File.Exists(tempFile))
                    continue;
                ProfilerDriver.LoadProfile(tempFile, !first);
                first = false;
            }

            // Save merged result
            ProfilerDriver.SaveProfile(savePath);

            // Cleanup temp files
            foreach (var tempFile in tempFiles)
            {
                try { System.IO.File.Delete(tempFile); } catch { }
            }
            m_MarkedFrameTempFiles.Clear();

            // Reload the merged file so user can see the result
            ProfilerDriver.LoadProfile(savePath, false);

            // Invalidate highlight caches so strips re-evaluate the loaded frames
            m_ThresholdCachedLastFrame = -1;
            m_ThresholdCachedValue = -1f;
            m_ThresholdHotFrames.Clear();
            m_GcCachedLastFrame = -1;
            m_GcCachedThreshold = -1f;
            m_GcHotFrames.Clear();
            m_SearchMatchCachedLastFrame = -1;
            m_SearchMatchCachedQuery = null;
            m_SearchMatchFrames.Clear();
            ProfilerWindow.Repaint();

            // Restore normal buffer size (collect mode was disabled above)
            ProfilerUserSettings.frameCount = m_DefaultFrameHistoryLength;

            Debug.Log($"[LightningProfiler] Saved {tempFiles.Count} marked frame snapshots to: {savePath}");
        }

        bool IsFrameMarked(int frame)
        {
            if (m_ChartFilterThresholdMs > 0f)
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
                    return true;
            }

            if (m_GcFilterThresholdKB > 0f)
            {
                long gcBytes = GetFrameGcAllocBytes(frame, 0, m_IgnoreEditorLoop);
                if (gcBytes >= (long)(m_GcFilterThresholdKB * 1024f))
                    return true;
            }

            if (!string.IsNullOrEmpty(m_SearchString))
            {
                int threadIndex = m_FrameDataHierarchyView.threadIndex;
                if (threadIndex < 0) threadIndex = 0;
                if (FrameContainsSearchTerm(frame, threadIndex, m_SearchString))
                    return true;
            }

            return false;
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
            bool anyFilter = m_ChartFilterThresholdMs > 0f || m_GcFilterThresholdKB > 0f || !string.IsNullOrEmpty(m_SearchString);
            bool needPauseOrLog = (m_PauseOnFilter || m_LogOnFilter) && anyFilter;
            bool needSaveFilter = m_SaveMarkedOnly;
            bool shouldSubscribe = needPauseOrLog || needSaveFilter;

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
            // --- Save-marked-only: check frameIndex directly (buffer=1, frameIndex-1 is gone) ---
            if (m_SaveMarkedOnly)
            {
                bool saveIsMarked = IsFrameMarked(frameIndex);
                if (saveIsMarked)
                {
                    string tempDir = System.IO.Path.Combine(Application.temporaryCachePath, "MarkedFrames");
                    if (!System.IO.Directory.Exists(tempDir))
                        System.IO.Directory.CreateDirectory(tempDir);
                    string tempPath = System.IO.Path.Combine(tempDir, $"marked_{frameIndex}.data");
                    ProfilerDriver.SaveProfile(tempPath);
                    m_MarkedFrameTempFiles.Add(tempPath);
                }
            }

            int checkFrame = frameIndex - 1;
            if (checkFrame < ProfilerDriver.firstFrameIndex)
                return;

            bool isPlaying = EditorApplication.isPlaying && !EditorApplication.isPaused;

            // --- Determine if frame is "marked" by any active filter (for pause/log/highlight) ---

            // Check spike threshold.
            bool isSpike = false;
            if (m_ChartFilterThresholdMs > 0f)
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

            // Check GC threshold.
            bool isGcSpike = false;
            if (m_GcFilterThresholdKB > 0f)
            {
                long gcBytes = GetFrameGcAllocBytes(checkFrame, 0, m_IgnoreEditorLoop);
                if (gcBytes >= (long)(m_GcFilterThresholdKB * 1024f))
                    isGcSpike = true;
            }

            // Check search term match.
            bool isSearchMatch = false;
            if (!string.IsNullOrEmpty(m_SearchString))
            {
                int threadIndex = m_FrameDataHierarchyView.threadIndex;
                if (threadIndex < 0) threadIndex = 0;
                if (FrameContainsSearchTerm(checkFrame, threadIndex, m_SearchString))
                    isSearchMatch = true;
            }

            bool isMarked = isSpike || isGcSpike || isSearchMatch;

            if (isMarked && m_LogOnFilter)
                LogSpikeFrame(checkFrame);

            // --- Pause logic (play mode only) ---
            if (!isPlaying)
                return;

            bool shouldPause = isMarked && m_PauseOnFilter;

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
            bool hasSearch = !string.IsNullOrEmpty(m_SearchString);

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

            // --- Search match strip (below GC) ---
            if (hasSearch && m_SearchMatchFrames.Count > 0)
            {
                var stripRect = GUILayoutUtility.GetRect(containerRect.width, k_SearchStripHeight);
                if (Event.current.type == EventType.Repaint)
                {
                    var drawRect = new Rect(stripRect.x + sideWidth, stripRect.y, stripRect.width - sideWidth, k_SearchStripHeight);
                    float frameWidth = drawRect.width / frameCount;

                    EditorGUI.DrawRect(drawRect, new Color(0.12f, 0.12f, 0.12f, 0.8f));

                    var matchColor = new Color(1f, 0.75f, 0.1f, 0.95f);
                    foreach (var frame in m_SearchMatchFrames)
                    {
                        int rel = frame - firstEmptyFrame;
                        if (rel < 0 || rel >= frameCount) continue;
                        EditorGUI.DrawRect(new Rect(drawRect.x + rel * frameWidth, drawRect.y, Mathf.Max(1f, frameWidth), k_SearchStripHeight), matchColor);
                    }

                    if (selectedRelative >= 0 && selectedRelative < frameCount)
                        EditorGUI.DrawRect(new Rect(drawRect.x + selectedRelative * frameWidth, drawRect.y, Mathf.Max(2f, frameWidth), k_SearchStripHeight), new Color(1f, 1f, 1f, 0.8f));

                    var labelRect = new Rect(stripRect.x + 2f, stripRect.y, sideWidth - 4f, k_SearchStripHeight);
                    GUI.Label(labelRect, "search", EditorStyles.miniLabel);
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

    }
}
