using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling.Editor;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEditorInternal.Profiling;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpikeTrap
{
    public sealed class CpuUsageBridgeDetailsViewController : ProfilerModuleViewController
    {
        internal static volatile CpuUsageBridgeDetailsViewController s_ActiveInstance;

        const string k_SettingsKeyPrefix = "Profiler.CPUProfilerModule.";

        readonly UnityProfilerWindowControllerAdapter m_ProfilerWindowController;

        ProfilerFrameDataHierarchyView m_FrameDataHierarchyView;
        bool m_UpdateViewLive;
        bool m_Initialized;
        readonly StringBuilder m_SpikeLogBuilder = new StringBuilder(4096);
        double m_LastSpikeLogTime;
        const double k_SpikeLogCooldownSeconds = 1.0;
        const int k_MaxLogSamples = 500;

        // --- Pluggable filter system ---
        readonly List<IFrameFilter> m_Filters = new List<IFrameFilter>();
        // Per-filter matched frame sets (managed by controller)
        readonly List<HashSet<int>> m_FilterMatchedFrames = new List<HashSet<int>>();
        // Combined result for All (AND) mode
        readonly HashSet<int> m_CombinedMatchedFrames = new HashSet<int>();
        SpikeFrameFilter m_SpikeFilter;
        SearchFrameFilter m_SearchFilter;
        const float k_StripHeight = 20f;

        // --- Filter combine mode (All = AND, Any = OR) ---
        enum FilterCombineMode { Any, All }
        const string k_FilterCombineModeKey = "SpikeTrap.FilterCombineMode";
        static readonly string[] k_CombineModeLabels = { "Match any", "Match all" };
        FilterCombineMode m_CombineMode;
        int m_PrevFirstFrame = -1;
        int m_PrevLastFrame = -1;

        // --- Cached predicate for RemoveWhere to avoid per-frame delegate allocation ---
        int m_TrimFirstFrame;
        readonly Predicate<int> m_TrimPredicate;
        int m_LastTrimFirstFrame = -1;

        // --- Static frame data cache (survives view controller recreation, stores all sessions) ---
        // Key: (sessionId : high 32 bits, frameIndex : low 32 bits) packed into a long.
        // sessionId is derived from the first frame's frameStartTimeNs — unique per recording,
        // stable across reloads of the same .data file.  A→B→A reuses A's cached data.
        internal static readonly ConcurrentDictionary<long, CachedFrameData> s_FrameDataCache = new ConcurrentDictionary<long, CachedFrameData>();
        internal static readonly ConcurrentDictionary<int, string> s_MarkerNames = new ConcurrentDictionary<int, string>();
        internal static int s_CurrentSessionId;
        static ulong s_SessionStartTimeNs;
        static int s_SessionAnchorFrame = -1;
        static int s_CachedGcAllocMarkerId = -1;
        static int s_CachedEditorLoopMarkerId = -1;
        // Maps frameStartTimeNs → sessionId so reloading the same file reuses the same ID
        static readonly Dictionary<ulong, int> s_TimeNsToSessionId = new Dictionary<ulong, int>();
        static int s_NextSessionId;

        long SessionFrameKey(int frameIndex) => ((long)s_CurrentSessionId << 32) | (uint)frameIndex;

        const int k_ParallelThreshold = 500;
        int m_CachedLastFrame = -1;
        int m_CachedExtractedCount = -1;
        bool m_FiltersDirty = true;

        // Session detection
        static readonly System.Guid k_SessionGuid = new System.Guid("A17B3C4D-E5F6-4789-ABCD-EF0123456789");
        const int k_SessionInfoTag = -100;
        bool m_IsEditorSession = true;
        int m_SessionCheckedFrame = -1;

        static readonly ConcurrentBag<Func<IFrameFilter>> s_CustomFilterFactories = new ConcurrentBag<Func<IFrameFilter>>();

        /// <summary>
        /// Register a factory that creates a custom <see cref="IFrameFilter"/>.
        /// Call from an <c>[InitializeOnLoad]</c> static constructor.
        /// </summary>
        public static void RegisterCustomFilterFactory(Func<IFrameFilter> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            s_CustomFilterFactories.Add(factory);
        }

        // --- View mode: Hierarchy / Timeline ---
        enum DetailsViewMode { Hierarchy, Timeline }
        const string k_ViewModeKey = "SpikeTrap.DetailsViewMode";
        static readonly string[] k_ViewModeLabels = { "Hierarchy", "Timeline" };
        DetailsViewMode m_ViewMode;
        ProfilerTimelineGUI m_TimelineGUI;

        // Unified pause/log on any filter match
        const string k_PauseOnFilterKey = "SpikeTrap.PauseOnFilter";
        const string k_LogOnFilterKey = "SpikeTrap.LogOnFilter";
        bool m_PauseOnFilter;
        bool m_LogOnFilter;
        bool m_PauseCallbackSubscribed;

        // Save-only-marked-frames
        const string k_SaveMarkedOnlyKey = "SpikeTrap.SaveMarkedOnly";
        const string k_DefaultFrameHistoryKey = "SpikeTrap.DefaultFrameHistory";
        bool m_SaveMarkedOnly;
        readonly List<string> m_MarkedFrameTempFiles = new List<string>();
        int m_DefaultFrameHistoryLength;
        const int k_SaveMarkedBufferSize = 1;
        VisualElement m_ChartOverlay;
        Label m_ChartOverlayLabel;
        IMGUIContainer m_IMGUIView;
        private static readonly HierarchyFrameDataView.ViewModes _viewMode =
            HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName |
            HierarchyFrameDataView.ViewModes.HideEditorOnlySamples;

        // Screenshot preview
        static GUIStyle s_CollectingOverlayStyle;
        Texture2D m_ScreenshotTexture;
        long m_ScreenshotFrameIndex = -1;
        const float k_ScreenshotMaxHeight = 150f;

        // Screenshot metadata constants (matches com.utj.screenshot2profiler runtime)
        static readonly System.Guid k_SSMetadataGuid = new System.Guid("4389DCEB-F9B3-4D49-940B-E98482F3A3F8");
        const int k_SSInfoTag = -1;

        public CpuUsageBridgeDetailsViewController(ProfilerWindow profilerWindow)
            : base(profilerWindow)
        {
            m_ProfilerWindowController = new UnityProfilerWindowControllerAdapter(profilerWindow);
            m_TrimPredicate = f => f < m_TrimFirstFrame;
            s_ActiveInstance = this;
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
            m_IMGUIView = imgui;

            // Create chart overlay for collecting state — sits on top of the chart area.
            // Defer to next frame so EditorStyles and the full visual tree are ready.
            imgui.RegisterCallback<AttachToPanelEvent>(_ => EditorApplication.delayCall += AttachChartOverlay);

            return imgui;
        }

        void AttachChartOverlay()
        {
            if (m_ChartOverlay != null) return;

            // EditorStyles throws NRE during domain reload — re-defer until ready
            try { _ = EditorStyles.toolbar; }
            catch
            {
                EditorApplication.delayCall += AttachChartOverlay;
                return;
            }

            if (m_IMGUIView == null) return;

            var root = m_IMGUIView.panel?.visualTree;
            if (root == null) return;

            var chartContainer = root.Q<IMGUIContainer>("toolbar-and-charts__legacy-imgui-container");
            if (chartContainer == null) return;

            m_ChartOverlay = new VisualElement();
            m_ChartOverlay.style.position = Position.Absolute;
            m_ChartOverlay.style.left = 0;
            m_ChartOverlay.style.right = 0;
            m_ChartOverlay.style.bottom = 0;
            m_ChartOverlay.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.85f);
            m_ChartOverlay.style.alignItems = Align.Center;
            m_ChartOverlay.style.justifyContent = Justify.Center;
            m_ChartOverlay.pickingMode = PickingMode.Ignore;

            m_ChartOverlayLabel = new Label("Collecting...");
            m_ChartOverlayLabel.style.fontSize = 16;
            m_ChartOverlayLabel.style.color = new Color(1f, 1f, 1f, 0.6f);
            m_ChartOverlayLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_ChartOverlayLabel.pickingMode = PickingMode.Ignore;
            m_ChartOverlay.Add(m_ChartOverlayLabel);

            m_ChartOverlay.style.top = EditorStyles.toolbar.fixedHeight;

            m_ChartOverlay.visible = m_SaveMarkedOnly;
            chartContainer.Add(m_ChartOverlay);
        }

        protected override void Dispose(bool disposing)
        {
            if (s_ActiveInstance == this)
                s_ActiveInstance = null;

            if (disposing)
            {
                if (m_ScreenshotTexture != null)
                    UnityEngine.Object.DestroyImmediate(m_ScreenshotTexture);

                ProfilerWindow.recordingStateChanged -= OnRecordingStateChanged;

                if (m_PauseCallbackSubscribed)
                {
                    ProfilerDriver.NewProfilerFrameRecorded -= OnNewProfilerFrame;
                    m_PauseCallbackSubscribed = false;
                }

                foreach (var f in m_MarkedFrameTempFiles)
                {
                    try { if (System.IO.File.Exists(f)) System.IO.File.Delete(f); } catch { }
                }
                m_MarkedFrameTempFiles.Clear();

                foreach (var filter in m_Filters)
                    filter.Dispose();
                m_Filters.Clear();

                m_TimelineGUI?.Clear();
                m_TimelineGUI = null;

                if (m_FrameDataHierarchyView != null)
                {
                    m_FrameDataHierarchyView.OnToggleLive -= OnHierarchyLiveToggle;
                    m_FrameDataHierarchyView.userChangedThread -= OnUserChangedThread;
                    m_FrameDataHierarchyView.searchChanged -= OnSearchChanged;
                    m_FrameDataHierarchyView.viewTypeChanged -= OnViewTypeChanged;
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
            m_FrameDataHierarchyView.viewTypeChanged += OnViewTypeChanged;
            m_FrameDataHierarchyView.viewType = (int)m_ViewMode;
            m_FrameDataHierarchyView.hideSearchBar = true;

            ProfilerWindow.recordingStateChanged += OnRecordingStateChanged;

            // --- Create built-in filters ---
            m_SpikeFilter = new SpikeFrameFilter();

            var gcFilter = new GcFrameFilter();

            m_SearchFilter = new SearchFrameFilter(
                () => m_FrameDataHierarchyView.DrawSearchBarExternal());

            m_Filters.Add(m_SpikeFilter);
            m_Filters.Add(gcFilter);
            m_Filters.Add(m_SearchFilter);

            // --- Custom user filters ---
            foreach (var factory in s_CustomFilterFactories)
            {
                var filter = factory();
                if (filter != null)
                    m_Filters.Add(filter);
            }

            // Initialize per-filter matched frame sets (AFTER all filters including custom)
            foreach (var _ in m_Filters)
                m_FilterMatchedFrames.Add(new HashSet<int>());

            m_PauseOnFilter = EditorPrefs.GetBool(k_PauseOnFilterKey, false);
            m_LogOnFilter = EditorPrefs.GetBool(k_LogOnFilterKey, false);
            m_CombineMode = (FilterCombineMode)EditorPrefs.GetInt(k_FilterCombineModeKey, 0);
            m_ViewMode = (DetailsViewMode)EditorPrefs.GetInt(k_ViewModeKey, 0);

            // Initialize Unity's internal timeline view, passing the built-in CPU module for flow events
            m_TimelineGUI = new ProfilerTimelineGUI();
            var cpuModule = ProfilerWindow.GetProfilerModule<CPUProfilerModule>(UnityEngine.Profiling.ProfilerArea.CPU);
            m_TimelineGUI.OnEnable(cpuModule, ProfilerWindow, false);
            m_SaveMarkedOnly = EditorPrefs.GetBool(k_SaveMarkedOnlyKey, false);
            if (m_SaveMarkedOnly)
            {
                m_DefaultFrameHistoryLength = Mathf.Clamp(EditorPrefs.GetInt(k_DefaultFrameHistoryKey, ProfilerUserSettings.frameCount), 1, 2000);
                ProfilerUserSettings.frameCount = k_SaveMarkedBufferSize;
            }
            else
            {
                m_DefaultFrameHistoryLength = ProfilerUserSettings.frameCount;
            }

            string tempDir = System.IO.Path.Combine(Application.temporaryCachePath, "MarkedFrames");
            if (System.IO.Directory.Exists(tempDir))
            {
                var existing = System.IO.Directory.GetFiles(tempDir, "marked_*.data");
                if (existing.Length > 0)
                {
                    System.Array.Sort(existing);
                    m_MarkedFrameTempFiles.AddRange(existing);
                }
            }

            UpdatePauseCallbackSubscription();
            m_Initialized = true;
        }

        void DrawDetailsViewViaLegacyIMGUIMethods()
        {
            var detailsViewContainer = ProfilerWindow.DetailsViewContainer;
            if (detailsViewContainer == null) return;
            var rs = detailsViewContainer.resolvedStyle;
            var rect = new Rect(0f, 0f, rs.width, rs.height);
            rect.yMin += EditorStyles.contentToolbar.CalcHeight(GUIContent.none, 10f);
            OnModuleDetailsGUI(rect);
        }

        void OnModuleDetailsGUI(Rect rect)
        {
            var fetchData = !m_ProfilerWindowController.ProfilerWindowOverheadIsAffectingProfilingRecordingData() || m_UpdateViewLive;

            DrawFilterControls();

            // Keep dropdown in sync with controller's persisted view mode
            if (m_FrameDataHierarchyView.viewType != (int)m_ViewMode)
                m_FrameDataHierarchyView.viewType = (int)m_ViewMode;

            UpdateChartOverlay();

            // Filter strips — always visible so thresholds can be adjusted while collecting
            DrawCombinedHighlightStrip(rect);

            if (m_SaveMarkedOnly)
            {
                DrawCollectingOverlay();
                return;
            }

            // Update shared frame data cache and matched frames
            UpdateFrameDataCacheAndMatches();

            var frameData = fetchData ? GetFrameDataViewForHierarchy() : null;

            if (m_ViewMode == DetailsViewMode.Timeline && m_TimelineGUI != null)
            {
                // Draw the hierarchy toolbar (with view type dropdown) then the timeline below
                m_FrameDataHierarchyView.DrawToolbarOnly(frameData, fetchData, ref m_UpdateViewLive);

                int frameIndex = (int)ProfilerWindow.selectedFrameIndex;
                if (frameIndex < 0) frameIndex = ProfilerDriver.lastFrameIndex;
                // Calculate remaining rect below toolbar for the timeline
                float toolbarBottom = GUILayoutUtility.GetLastRect().yMax;
                var timelineRect = new Rect(rect.x, toolbarBottom, rect.width, rect.yMax - toolbarBottom);
                if (timelineRect.height > 1f)
                    m_TimelineGUI.DoGUI(frameIndex, timelineRect, fetchData, ref m_UpdateViewLive);
            }
            else
            {
                DrawScreenshotPreview();
                m_FrameDataHierarchyView.DoGUI(frameData, fetchData, ref m_UpdateViewLive, null);
            }
        }

        void UpdateChartOverlay()
        {
            if (m_ChartOverlay == null) return;
            m_ChartOverlay.visible = m_SaveMarkedOnly;
            if (m_SaveMarkedOnly && m_ChartOverlayLabel != null)
            {
                int count = m_MarkedFrameTempFiles.Count;
                m_ChartOverlayLabel.text = count > 0
                    ? $"Collecting...  ({count} frames captured)"
                    : "Collecting...";
            }
        }

        void DrawCollectingOverlay()
        {
            var overlayRect = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(overlayRect, new Color(0.15f, 0.15f, 0.15f, 0.85f));

            if (s_CollectingOverlayStyle == null)
            {
                s_CollectingOverlayStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 18,
                    normal = { textColor = new Color(1f, 1f, 1f, 0.6f) }
                };
            }

            int count = m_MarkedFrameTempFiles.Count;
            string text = count > 0
                ? $"Collecting...  ({count} frames captured)"
                : "Collecting...";
            GUI.Label(overlayRect, text, s_CollectingOverlayStyle);

            ProfilerWindow.Repaint();
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

        // ─── Toolbar ────────────────────────────────────────────────────────

        void DrawFilterControls()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // --- Filter combine mode (Any / All) ---
            var newMode = (FilterCombineMode)EditorGUILayout.Popup((int)m_CombineMode, k_CombineModeLabels,
                EditorStyles.toolbarDropDown, GUILayout.Width(85));
            if (newMode != m_CombineMode)
            {
                m_CombineMode = newMode;
                EditorPrefs.SetInt(k_FilterCombineModeKey, (int)m_CombineMode);
                m_FiltersDirty = true;
            }

            // --- Unified Pause / Log ---
            {
                bool anyActive = AnyFilterActive();
                using (new EditorGUI.DisabledScope(!anyActive))
                {
                    var newPause = GUILayout.Toggle(m_PauseOnFilter,
                        EditorGUIUtility.TrTextContent("Pause on match", "Pause play mode when filters match a frame."),
                        EditorStyles.toolbarButton);
                    if (newPause != m_PauseOnFilter)
                    {
                        m_PauseOnFilter = newPause;
                        EditorPrefs.SetBool(k_PauseOnFilterKey, m_PauseOnFilter);
                        UpdatePauseCallbackSubscription();
                    }

                    var newLog = GUILayout.Toggle(m_LogOnFilter,
                        EditorGUIUtility.TrTextContent("Log on match", "Log frame details when filters match."),
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

            // --- Collect / Save marked (state-dependent button) ---
            if (m_SaveMarkedOnly)
            {
                int collected = m_MarkedFrameTempFiles.Count;
                var label = collected > 0
                    ? EditorGUIUtility.TrTextContent($"Save ({collected})", "Stop collecting and save marked frames to a .data file.")
                    : EditorGUIUtility.TrTextContent("Stop", "Stop collecting marked frames.");
                if (GUILayout.Button(label, EditorStyles.toolbarButton))
                {
                    SaveMergedMarkedFrames();
                }
            }
            else
            {
                if (GUILayout.Button(
                    EditorGUIUtility.TrTextContent("Collect", "Start collecting frames matching active filters."),
                    EditorStyles.toolbarButton))
                {
                    m_SaveMarkedOnly = true;
                    EditorPrefs.SetBool(k_SaveMarkedOnlyKey, true);
                    m_DefaultFrameHistoryLength = ProfilerUserSettings.frameCount;
                    EditorPrefs.SetInt(k_DefaultFrameHistoryKey, m_DefaultFrameHistoryLength);
                    ProfilerUserSettings.frameCount = k_SaveMarkedBufferSize;
                    UpdatePauseCallbackSubscription();
                }
            }

            // --- Clear collected temp files ---
            if (m_MarkedFrameTempFiles.Count > 0)
            {
                if (GUILayout.Button(
                    EditorGUIUtility.TrTextContent("Clear", "Delete all collected marked frame temp files."),
                    EditorStyles.toolbarButton))
                {
                    foreach (var f in m_MarkedFrameTempFiles)
                    {
                        try { if (System.IO.File.Exists(f)) System.IO.File.Delete(f); } catch { }
                    }
                    m_MarkedFrameTempFiles.Clear();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        // ─── Screenshot ─────────────────────────────────────────────────────

        void DrawScreenshotPreview()
        {
            long currentFrame = ProfilerWindow.selectedFrameIndex;
            if (currentFrame < 0)
                return;

            if (currentFrame != m_ScreenshotFrameIndex)
            {
                m_ScreenshotFrameIndex = currentFrame;
                var tex = LoadScreenshotFromFrame((int)currentFrame);
                if (tex != null)
                {
                    if (m_ScreenshotTexture != null)
                        UnityEngine.Object.DestroyImmediate(m_ScreenshotTexture);
                    m_ScreenshotTexture = tex;
                }
                // If no screenshot on this frame, keep showing the last valid one
            }

            if (m_ScreenshotTexture == null)
                return;

            float aspect = (float)m_ScreenshotTexture.width / m_ScreenshotTexture.height;
            float availableWidth = EditorGUIUtility.currentViewWidth;
            float displayHeight = Mathf.Min(k_ScreenshotMaxHeight, availableWidth / aspect);
            float displayWidth = displayHeight * aspect;

            var layoutRect = GUILayoutUtility.GetRect(availableWidth, displayHeight);
            var imageRect = new Rect(
                layoutRect.x + (layoutRect.width - displayWidth) * 0.5f,
                layoutRect.y,
                displayWidth,
                displayHeight);

            EditorGUI.DrawRect(layoutRect, new Color(0.1f, 0.1f, 0.1f, 1f));
            GUI.DrawTextureWithTexCoords(imageRect, m_ScreenshotTexture, new Rect(0, 1, 1, -1));
        }

        Texture2D LoadScreenshotFromFrame(int frameIdx)
        {
            using var view = ProfilerDriver.GetHierarchyFrameDataView(frameIdx, 0, HierarchyFrameDataView.ViewModes.Default, 0, false);
            if (view == null || !view.valid)
                return null;

            var tagBytes = view.GetFrameMetaData<byte>(k_SSMetadataGuid, k_SSInfoTag);
            if (!tagBytes.IsCreated || tagBytes.Length < 12)
                return null;

            int id = tagBytes[0] | (tagBytes[1] << 8) | (tagBytes[2] << 16) | (tagBytes[3] << 24);
            int width = tagBytes[4] | (tagBytes[5] << 8);
            int height = tagBytes[6] | (tagBytes[7] << 8);
            byte compressByte = tagBytes.Length > 12 ? tagBytes[12] : (byte)0;
            bool isRaw = compressByte <= 1;
            var texFormat = compressByte == 1 || compressByte == 3 ? TextureFormat.RGB565 : TextureFormat.RGBA32;

            var imgBytes = view.GetFrameMetaData<byte>(k_SSMetadataGuid, id);
            if (!imgBytes.IsCreated || imgBytes.Length <= 16)
                return null;

            var tex = new Texture2D(width, height, texFormat, false);
            try
            {
                if (isRaw)
                {
                    tex.LoadRawTextureData(imgBytes);
                    tex.Apply();
                }
                else
                {
                    tex.LoadImage(imgBytes.ToArray());
                    tex.Apply();
                }
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(tex);
                return null;
            }
            return tex;
        }

        // ─── Recording / Collect ────────────────────────────────────────────

        void OnRecordingStateChanged(bool recording)
        {
            // Clear stale screenshot when a new recording starts
            if (recording)
                ClearScreenshotCache();

            if (!recording && m_SaveMarkedOnly && m_MarkedFrameTempFiles.Count > 0)
            {
                EditorApplication.delayCall += SaveMergedMarkedFrames;
            }
        }

        void ClearScreenshotCache()
        {
            if (m_ScreenshotTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(m_ScreenshotTexture);
                m_ScreenshotTexture = null;
            }
            m_ScreenshotFrameIndex = -1;
        }

        void SaveMergedMarkedFrames()
        {
            m_SaveMarkedOnly = false;
            EditorPrefs.SetBool(k_SaveMarkedOnlyKey, false);
            if (m_PauseCallbackSubscribed)
            {
                ProfilerDriver.NewProfilerFrameRecorded -= OnNewProfilerFrame;
                m_PauseCallbackSubscribed = false;
            }

            ProfilerWindow.SetRecordingEnabled(false);
            if (EditorApplication.isPlaying && !EditorApplication.isPaused)
                EditorApplication.isPaused = true;

            if (m_MarkedFrameTempFiles.Count == 0)
            {
                ProfilerUserSettings.frameCount = m_DefaultFrameHistoryLength;
                return;
            }

            var savePath = EditorUtility.SaveFilePanel("Save Marked Frames", "", "marked_profile", "data");
            if (string.IsNullOrEmpty(savePath))
            {
                ProfilerUserSettings.frameCount = m_DefaultFrameHistoryLength;
                return;
            }

            var tempFiles = new List<string>(m_MarkedFrameTempFiles);

            ProfilerUserSettings.frameCount = 2000;
            ProfilerDriver.ClearAllFrames();

            bool first = true;
            foreach (var tempFile in tempFiles)
            {
                if (!System.IO.File.Exists(tempFile))
                    continue;
                ProfilerDriver.LoadProfile(tempFile, !first);
                first = false;
            }

            ProfilerDriver.SaveProfile(savePath);

            foreach (var tempFile in tempFiles)
            {
                try { System.IO.File.Delete(tempFile); } catch { }
            }
            m_MarkedFrameTempFiles.Clear();

            ProfilerUserSettings.frameCount = 2000;
            ProfilerDriver.LoadProfile(savePath, false);

            int loadedCount = ProfilerDriver.lastFrameIndex - ProfilerDriver.firstFrameIndex + 1;
            ProfilerUserSettings.frameCount = Mathf.Clamp(Mathf.Max(m_DefaultFrameHistoryLength, loadedCount + 10), 1, 2000);

            // Re-enable profiler after ClearAllFrames to restore recording buffer
            ProfilerDriver.enabled = true;

            // Invalidate shared frame data cache
            InvalidateAllCaches();
            ProfilerWindow.Repaint();

            Debug.Log($"[SpikeTrap] Saved {tempFiles.Count} marked frame snapshots to: {savePath}");
        }

        // ─── Shared frame data cache & filter evaluation ────────────────────

        bool AnyFilterActive()
        {
            foreach (var f in m_Filters)
                if (f.IsActive) return true;
            return false;
        }

        bool IsEditorSession()
        {
            int lastFrame = ProfilerDriver.lastFrameIndex;
            if (m_SessionCheckedFrame == lastFrame)
                return m_IsEditorSession;

            int firstFrame = ProfilerDriver.firstFrameIndex;
            if (firstFrame < 0) return m_IsEditorSession;
            using var view = ProfilerDriver.GetHierarchyFrameDataView(firstFrame, 0,
                HierarchyFrameDataView.ViewModes.Default, 0, false);
            if (view != null && view.valid)
            {
                var data = view.GetSessionMetaData<byte>(k_SessionGuid, k_SessionInfoTag);
                if (data.IsCreated && data.Length >= 1)
                    m_IsEditorSession = data[0] == 1;
            }
            m_SessionCheckedFrame = lastFrame;
            return m_IsEditorSession;
        }

        /// <summary>
        /// Extract all filter-relevant data from a single frame in ONE pass.
        /// One GetRawFrameDataView + one sample iteration for all filters.
        /// </summary>
        CachedFrameData ExtractFrameData(int frameIndex)
        {
            UnityEngine.Profiling.Profiler.BeginSample("ExtractFrameData");
            float frameTimeMs;
            using (var iter = new ProfilerFrameDataIterator())
            {
                iter.SetRoot(frameIndex, 0);
                frameTimeMs = iter.frameTimeMS;
            }

            long gcBytes = 0;
            float editorLoopMs = 0f;

            // Only build UniqueMarkerIds when the search filter is active — saves HashSet.Add per sample
            bool needMarkerIds = m_SearchFilter != null && m_SearchFilter.IsActive;
            HashSet<int> markerIds = needMarkerIds ? new HashSet<int>() : null;

            // Collect newly discovered markers during extraction, notify filters after
            List<(int MarkerId, string MarkerName)> newMarkers = null;

            // Per-marker accumulated time for top-N tracking
            var markerTimes = new Dictionary<int, float>();

            bool isEditor = IsEditorSession();

            using (var raw = ProfilerDriver.GetRawFrameDataView(frameIndex, 0))
            {
                if (raw != null && raw.valid)
                {
                    if (s_CachedGcAllocMarkerId == -1)
                        s_CachedGcAllocMarkerId = raw.GetMarkerId("GC.Alloc");
                    int gcMarkerId = s_CachedGcAllocMarkerId;

                    if (isEditor && s_CachedEditorLoopMarkerId == -1)
                        s_CachedEditorLoopMarkerId = raw.GetMarkerId("EditorLoop");
                    int editorLoopId = isEditor
                        ? s_CachedEditorLoopMarkerId
                        : FrameDataView.invalidMarkerId;
                    bool hasEditorLoop = editorLoopId != FrameDataView.invalidMarkerId;

                    for (int i = 0; i < raw.sampleCount; i++)
                    {
                        int markerId = raw.GetSampleMarkerId(i);

                        // Collect unique marker IDs only when search is active
                        bool isNewMarker = markerIds != null ? markerIds.Add(markerId) : !s_MarkerNames.ContainsKey(markerId);
                        if (isNewMarker && s_MarkerNames.TryAdd(markerId, raw.GetSampleName(i)))
                        {
                            if (newMarkers == null)
                                newMarkers = new List<(int MarkerId, string MarkerName)>();
                            newMarkers.Add((markerId, s_MarkerNames[markerId]));
                        }

                        float sampleTimeMs = raw.GetSampleTimeMs(i);

                        // Accumulate per-marker time for top-N
                        if (markerTimes.TryGetValue(markerId, out float existing))
                            markerTimes[markerId] = existing + sampleTimeMs;
                        else
                            markerTimes[markerId] = sampleTimeMs;

                        // EditorLoop time
                        if (hasEditorLoop && markerId == editorLoopId)
                            editorLoopMs += sampleTimeMs;

                        // GC alloc
                        if (markerId == gcMarkerId && raw.GetSampleMetadataCount(i) > 0)
                            gcBytes += raw.GetSampleMetadataAsLong(i, 0);
                    }
                }
            }

            // Notify search filter about new markers — parallelizable, thread-safe
            if (newMarkers != null && newMarkers.Count > 0)
            {
                System.Threading.Tasks.Parallel.ForEach(newMarkers, kvp =>
                    m_SearchFilter.OnMarkerDiscovered(kvp.MarkerId, kvp.MarkerName));
            }

            // Build top-N marker list
            const int k_TopMarkerCount = 10;
            MarkerTimeSample[] topMarkers = null;
            if (markerTimes.Count > 0)
            {
                var sorted = new List<KeyValuePair<int, float>>(markerTimes);
                sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
                int count = Mathf.Min(k_TopMarkerCount, sorted.Count);
                topMarkers = new MarkerTimeSample[count];
                for (int i = 0; i < count; i++)
                    topMarkers[i] = new MarkerTimeSample(sorted[i].Key, sorted[i].Value);
            }

            float effectiveMs = isEditor ? frameTimeMs - editorLoopMs : frameTimeMs;
            var frameData = new CachedFrameData(effectiveMs, gcBytes, markerIds, topMarkers);
            UnityEngine.Profiling.Profiler.EndSample();
            return frameData;
        }

        /// <summary>Returns true if the frame was newly extracted (not cached).</summary>
        bool GetOrExtractFrameData(int frameIndex, out CachedFrameData data)
        {
            long key = SessionFrameKey(frameIndex);
            if (s_FrameDataCache.TryGetValue(key, out data))
                return false;
            data = ExtractFrameData(frameIndex);
            s_FrameDataCache.TryAdd(key, data);
            return true;
        }

        /// <summary>
        /// Collect frames matching a filter from the cache, using Parallel.For for large ranges.
        /// Pure managed, thread-safe.
        /// </summary>
        void CollectMatchingFrames(IFrameFilter filter, int fromFrame, int toFrame, HashSet<int> output)
        {
            int range = toFrame - fromFrame + 1;
            int sid = s_CurrentSessionId;
            if (range >= k_ParallelThreshold)
            {
                var results = new System.Collections.Concurrent.ConcurrentBag<int>();
                var f = filter;
                System.Threading.Tasks.Parallel.For(fromFrame, toFrame + 1, frame =>
                {
                    long key = ((long)sid << 32) | (uint)frame;
                    if (s_FrameDataCache.TryGetValue(key, out var fd) && f.Matches(in fd))
                        results.Add(frame);
                });
                foreach (var frame in results)
                    output.Add(frame);
            }
            else
            {
                for (int frame = fromFrame; frame <= toFrame; frame++)
                {
                    long key = SessionFrameKey(frame);
                    if (s_FrameDataCache.TryGetValue(key, out var data) && filter.Matches(in data))
                        output.Add(frame);
                }
            }
        }

        bool IsFrameMatched(int frameIndex)
        {
            if (!AnyFilterActive())
                return false;

            GetOrExtractFrameData(frameIndex, out var data);
            return EvaluateFilters(in data);
        }

        /// <summary>
        /// Evaluate all active filters against a frame using the current combine mode.
        /// Any = OR (at least one matches). All = AND (all active must match).
        /// Inactive filters are skipped (treated as "don't care").
        /// </summary>
        bool EvaluateFilters(in CachedFrameData data)
        {
            bool MatchAll(in CachedFrameData data)
            {
                foreach (var filter in m_Filters)
                {
                    if (filter.IsActive && !filter.Matches(in data))
                        return false;
                }
                return true;
            }
            
            bool MatchAny(in CachedFrameData data)
            {
                foreach (var filter in m_Filters)
                {
                    if (filter.IsActive && filter.Matches(in data))
                        return true;
                }
                return false;
            }
            
            return m_CombineMode switch
            {
                FilterCombineMode.All => MatchAll(data),
                FilterCombineMode.Any => MatchAny(data),
                _ => throw new ArgumentOutOfRangeException(nameof(m_CombineMode), m_CombineMode, null)
            };
        }

        /// <summary>
        /// Central update: extract new frames, detect resets, re-evaluate matches.
        /// Called each GUI frame from OnModuleDetailsGUI.
        /// </summary>
        void UpdateFrameDataCacheAndMatches()
        {
            int curFirst = ProfilerDriver.firstFrameIndex;
            int curLast = ProfilerDriver.lastFrameIndex;
            if (curFirst < 0 || curLast < 0)
            {
                if (m_PrevFirstFrame >= 0)
                {
                    // Transient empty state (e.g. between LoadProfile calls).
                    // Only reset per-instance state — keep static frame data cache
                    // so A→B→A can reuse A's cached data.
                    s_SessionAnchorFrame = -1;
                    m_CachedLastFrame = -1;
                    m_CachedExtractedCount = -1;
                    m_FiltersDirty = true;
                    foreach (var matched in m_FilterMatchedFrames)
                        matched.Clear();
                    m_CombinedMatchedFrames.Clear();
                    m_PrevFirstFrame = -1;
                    m_PrevLastFrame = -1;
                }
                return;
            }

            // Detect session change via the anchor frame's start timestamp.
            // LoadProfile replaces frame data in-place, so GetFramesBelongToSameProfilerSession
            // can't detect it (frame 0 vs frame 0 always returns true after replacement).
            // Instead, we read frameStartTimeNs at the anchor position — if it changed, data
            // was replaced.  Same .data file reloaded → same timeNs → same sessionId → cache hit.
            bool sessionChanged = false;
            if (s_SessionAnchorFrame < 0)
            {
                // First init
                sessionChanged = true;
            }
            else if (s_SessionAnchorFrame < curFirst)
            {
                // Anchor evicted by ring buffer wrap — live recording, same session.
                // Advance anchor so we can keep detecting future LoadProfile calls.
                s_SessionAnchorFrame = curFirst;
                using (var view = ProfilerDriver.GetRawFrameDataView(curFirst, 0))
                {
                    if (view != null && view.valid)
                        s_SessionStartTimeNs = view.frameStartTimeNs;
                }
            }
            else if (s_SessionAnchorFrame > curLast)
            {
                // Anchor beyond new range — data was fully replaced (LoadProfile with fewer frames)
                sessionChanged = true;
            }
            else
            {
                // Anchor in range — check if data was replaced at that index
                ulong anchorTimeNs = 0;
                using (var view = ProfilerDriver.GetRawFrameDataView(s_SessionAnchorFrame, 0))
                {
                    if (view != null && view.valid)
                        anchorTimeNs = view.frameStartTimeNs;
                }
                if (anchorTimeNs != s_SessionStartTimeNs)
                    sessionChanged = true;
            }

            if (sessionChanged)
            {
                ulong timeNs = 0;
                using (var view = ProfilerDriver.GetRawFrameDataView(curFirst, 0))
                {
                    if (view != null && view.valid)
                        timeNs = view.frameStartTimeNs;
                }
                s_SessionStartTimeNs = timeNs;
                s_SessionAnchorFrame = curFirst;

                // Reuse existing sessionId for this timeNs, or allocate a new one
                if (!s_TimeNsToSessionId.TryGetValue(timeNs, out int sid))
                {
                    sid = s_NextSessionId++;
                    s_TimeNsToSessionId[timeNs] = sid;
                }
                s_CurrentSessionId = sid;

                // Reset per-session state (marker IDs can differ between sessions)
                s_CachedGcAllocMarkerId = -1;
                s_CachedEditorLoopMarkerId = -1;
            }

            if (sessionChanged)
            {
                // Reset instance matching state for new session
                m_FiltersDirty = true;
                m_CachedLastFrame = -1;
                m_CachedExtractedCount = -1;
                foreach (var matched in m_FilterMatchedFrames)
                    matched.Clear();
                m_CombinedMatchedFrames.Clear();
                foreach (var filter in m_Filters)
                    filter.InvalidateCache();
                ClearScreenshotCache();
            }
            m_PrevFirstFrame = curFirst;
            m_PrevLastFrame = curLast;

            int visibleFirst = Mathf.Max(curFirst, curLast + 1 - ProfilerUserSettings.frameCount);

            // Extract all uncached frames at once
            for (int frame = visibleFirst; frame <= curLast; frame++)
                GetOrExtractFrameData(frame, out _);

            // Check if any filter parameter changed or new frames were extracted
            int currentCacheCount = s_FrameDataCache.Count;
            bool anyChanged = m_FiltersDirty || currentCacheCount != m_CachedExtractedCount;

            if (anyChanged || m_CachedLastFrame != curLast)
            {
                for (int i = 0; i < m_Filters.Count; i++)
                {
                    var filter = m_Filters[i];
                    var matched = m_FilterMatchedFrames[i];

                    if (anyChanged)
                    {
                        // Full re-evaluation (pure managed, parallel for large ranges)
                        matched.Clear();
                        if (filter.IsActive)
                            CollectMatchingFrames(filter, visibleFirst, curLast, matched);
                    }
                    else
                    {
                        if (!filter.IsActive)
                        {
                            // Filter was deactivated — clear stale matches entirely
                            matched.Clear();
                        }
                        else
                        {
                            // Incremental: only check new frames (also parallel if range is large)
                            int scanFrom = Mathf.Max(curFirst, m_CachedLastFrame + 1);
                            if (scanFrom <= curLast)
                                CollectMatchingFrames(filter, scanFrom, curLast, matched);
                            if (matched.Count > 0)
                            {
                                m_TrimFirstFrame = curFirst;
                                matched.RemoveWhere(m_TrimPredicate);
                            }
                        }
                    }
                }

                // Compute combined result strip
                m_CombinedMatchedFrames.Clear();
                if (m_CombineMode == FilterCombineMode.All)
                {
                    // AND: intersect all active filter matched sets
                    bool first = true;
                    for (int i = 0; i < m_Filters.Count; i++)
                    {
                        if (!m_Filters[i].IsActive) continue;
                        if (first)
                        {
                            m_CombinedMatchedFrames.UnionWith(m_FilterMatchedFrames[i]);
                            first = false;
                        }
                        else
                        {
                            m_CombinedMatchedFrames.IntersectWith(m_FilterMatchedFrames[i]);
                        }
                    }
                }
                else
                {
                    // OR: union all active filter matched sets
                    for (int i = 0; i < m_Filters.Count; i++)
                    {
                        if (m_Filters[i].IsActive)
                            m_CombinedMatchedFrames.UnionWith(m_FilterMatchedFrames[i]);
                    }
                }

                m_FiltersDirty = false;
                m_CachedLastFrame = curLast;
                m_CachedExtractedCount = currentCacheCount;
            }

            // Trim old cache entries
            TrimFrameDataCache(curFirst);
        }

        void InvalidateAllCaches()
        {
            s_FrameDataCache.Clear();
            s_MarkerNames.Clear();
            s_SessionAnchorFrame = -1;
            s_SessionStartTimeNs = 0;
            m_CachedLastFrame = -1;
            m_CachedExtractedCount = -1;
            m_LastTrimFirstFrame = -1;
            s_CachedGcAllocMarkerId = -1;
            s_CachedEditorLoopMarkerId = -1;
            m_FiltersDirty = true;
            foreach (var matched in m_FilterMatchedFrames)
                matched.Clear();
            m_CombinedMatchedFrames.Clear();
            foreach (var filter in m_Filters)
                filter.InvalidateCache();

            ClearScreenshotCache();
        }

        void TrimFrameDataCache(int firstValidFrame)
        {
            if (firstValidFrame == m_LastTrimFirstFrame) return;
            m_LastTrimFirstFrame = firstValidFrame;

            // Only trim current session's frames that fell out of range (live recording only)
            if (s_FrameDataCache.IsEmpty) return;
            foreach (var key in s_FrameDataCache.Keys)
            {
                // Only trim keys belonging to the current session
                if ((key >> 32) == s_CurrentSessionId && (int)(key & 0xFFFFFFFFL) < firstValidFrame)
                    s_FrameDataCache.TryRemove(key, out _);
            }
        }

        void UpdatePauseCallbackSubscription()
        {
            bool anyActive = AnyFilterActive();
            bool needPauseOrLog = (m_PauseOnFilter || m_LogOnFilter) && anyActive;
            bool shouldSubscribe = needPauseOrLog || m_SaveMarkedOnly;

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
            bool isMarked = IsFrameMatched(frameIndex);

            if (m_SaveMarkedOnly && isMarked)
            {
                string tempDir = System.IO.Path.Combine(Application.temporaryCachePath, "MarkedFrames");
                if (!System.IO.Directory.Exists(tempDir))
                    System.IO.Directory.CreateDirectory(tempDir);
                string tempPath = System.IO.Path.Combine(tempDir, $"marked_{frameIndex}.data");
                ProfilerDriver.SaveProfile(tempPath);
                m_MarkedFrameTempFiles.Add(tempPath);
            }

            if (isMarked && m_LogOnFilter)
                LogMatchedFrame(frameIndex);

            bool isPlaying = EditorApplication.isPlaying && !EditorApplication.isPaused;
            if (!isPlaying)
                return;

            if (isMarked && m_PauseOnFilter)
            {
                var pauseFrame = frameIndex;

                void Pause()
                {
                    EditorApplication.delayCall -= Pause;
                    EditorApplication.isPaused = true;
                    m_ProfilerWindowController.selectedFrameIndex = pauseFrame;
                }

                EditorApplication.delayCall += Pause;
            }
        }

        void OnSearchChanged(string newSearch)
        {
            m_SearchFilter.SetSearchString(newSearch);
            System.Threading.Tasks.Parallel.ForEach(s_MarkerNames, kvp =>
                m_SearchFilter.OnMarkerDiscovered(kvp.Key, kvp.Value));
            m_FiltersDirty = true;
            UpdatePauseCallbackSubscription();
            ProfilerWindow.Repaint();
        }

        void OnViewTypeChanged(int newViewType)
        {
            m_ViewMode = (DetailsViewMode)newViewType;
            EditorPrefs.SetInt(k_ViewModeKey, (int)m_ViewMode);
            ProfilerWindow.Repaint();
        }

        // ─── Highlight strips ───────────────────────────────────────────────

        static GUIContent s_PrevFrameIcon;
        static GUIContent s_NextFrameIcon;

        void DrawCombinedHighlightStrip(Rect containerRect)
        {
            if (s_PrevFrameIcon == null)
                s_PrevFrameIcon = EditorGUIUtility.TrIconContent("Profiler.PrevFrame", "Jump to previous matched frame");
            if (s_NextFrameIcon == null)
                s_NextFrameIcon = EditorGUIUtility.TrIconContent("Profiler.NextFrame", "Jump to next matched frame");

            int frameCount = ProfilerUserSettings.frameCount;
            int firstEmptyFrame = ProfilerDriver.lastFrameIndex + 1 - frameCount;
            float sideWidth = Chart.kSideWidth;
            int selectedFrame = (int)ProfilerWindow.selectedFrameIndex;
            int selectedRelative = selectedFrame - firstEmptyFrame;

            bool anyChanged = false;
            const float btnWidth = 15f;

            for (int i = 0; i < m_Filters.Count; i++)
            {
                var filter = m_Filters[i];
                var matched = m_FilterMatchedFrames[i];
                var rowRect = GUILayoutUtility.GetRect(containerRect.width, k_StripHeight);

                // --- Left side: nav buttons + filter controls (clipped to sideWidth) ---
                var prevRect = new Rect(rowRect.x, rowRect.y, btnWidth, k_StripHeight);
                var nextRect = new Rect(rowRect.x + btnWidth, rowRect.y, btnWidth, k_StripHeight);

                if (GUI.Button(prevRect, s_PrevFrameIcon, EditorStyles.iconButton))
                    JumpToMatchedFrame(matched, selectedFrame, -1);
                if (GUI.Button(nextRect, s_NextFrameIcon, EditorStyles.iconButton))
                    JumpToMatchedFrame(matched, selectedFrame, +1);

                // Filter controls clipped to the remaining side area
                var controlsRect = new Rect(rowRect.x + btnWidth * 2f, rowRect.y,
                    sideWidth - btnWidth * 2f, k_StripHeight);
                GUI.BeginClip(controlsRect);
                GUILayout.BeginArea(new Rect(0, 0, controlsRect.width, controlsRect.height));
                EditorGUILayout.BeginHorizontal(GUILayout.Height(k_StripHeight));
                anyChanged |= filter.DrawToolbarControls();
                EditorGUILayout.EndHorizontal();
                GUILayout.EndArea();
                GUI.EndClip();

                // --- Right side: strip visualization (aligned with chart) ---
                var vizRect = new Rect(rowRect.x + sideWidth, rowRect.y,
                    rowRect.width - sideWidth, k_StripHeight);

                if (Event.current.type == EventType.Repaint && vizRect.width > 0f)
                {
                    float frameWidth = vizRect.width / frameCount;

                    EditorGUI.DrawRect(vizRect, new Color(0.18f, 0.18f, 0.18f, 0.9f));

                    foreach (var frame in matched)
                    {
                        int rel = frame - firstEmptyFrame;
                        if (rel < 0 || rel >= frameCount) continue;
                        EditorGUI.DrawRect(new Rect(vizRect.x + rel * frameWidth, vizRect.y,
                            Mathf.Max(1f, frameWidth), k_StripHeight), filter.HighlightColor);
                    }

                    if (selectedRelative >= 0 && selectedRelative < frameCount)
                        EditorGUI.DrawRect(new Rect(vizRect.x + selectedRelative * frameWidth, vizRect.y,
                            Mathf.Max(2f, frameWidth), k_StripHeight), new Color(1f, 1f, 1f, 0.8f));
                }
            }

            if (anyChanged)
            {
                m_FiltersDirty = true;
                UpdatePauseCallbackSubscription();
            }

            // --- Combined result strip (only in All mode with 2+ active filters) ---
            int activeCount = 0;
            foreach (var f in m_Filters)
                if (f.IsActive) activeCount++;

            if (m_CombineMode == FilterCombineMode.All && activeCount >= 2)
            {
                var rowRect = GUILayoutUtility.GetRect(containerRect.width, k_StripHeight);

                var prevRect = new Rect(rowRect.x, rowRect.y, btnWidth, k_StripHeight);
                var nextRect = new Rect(rowRect.x + btnWidth, rowRect.y, btnWidth, k_StripHeight);

                if (GUI.Button(prevRect, s_PrevFrameIcon, EditorStyles.iconButton))
                    JumpToMatchedFrame(m_CombinedMatchedFrames, selectedFrame, -1);
                if (GUI.Button(nextRect, s_NextFrameIcon, EditorStyles.iconButton))
                    JumpToMatchedFrame(m_CombinedMatchedFrames, selectedFrame, +1);

                // Label
                var labelRect = new Rect(rowRect.x + btnWidth * 2f, rowRect.y,
                    sideWidth - btnWidth * 2f, k_StripHeight);
                GUI.Label(labelRect, "Result", EditorStyles.miniBoldLabel);

                var vizRect = new Rect(rowRect.x + sideWidth, rowRect.y,
                    rowRect.width - sideWidth, k_StripHeight);

                if (Event.current.type == EventType.Repaint && vizRect.width > 0f)
                {
                    float frameWidth = vizRect.width / frameCount;
                    EditorGUI.DrawRect(vizRect, new Color(0.14f, 0.14f, 0.14f, 0.95f));

                    var resultColor = new Color(1f, 1f, 1f, 0.9f);
                    foreach (var frame in m_CombinedMatchedFrames)
                    {
                        int rel = frame - firstEmptyFrame;
                        if (rel < 0 || rel >= frameCount) continue;
                        EditorGUI.DrawRect(new Rect(vizRect.x + rel * frameWidth, vizRect.y,
                            Mathf.Max(1f, frameWidth), k_StripHeight), resultColor);
                    }

                    if (selectedRelative >= 0 && selectedRelative < frameCount)
                        EditorGUI.DrawRect(new Rect(vizRect.x + selectedRelative * frameWidth, vizRect.y,
                            Mathf.Max(2f, frameWidth), k_StripHeight), new Color(1f, 0.9f, 0.2f, 0.9f));
                }
            }
        }

        void JumpToMatchedFrame(HashSet<int> matchedFrames, int currentFrame, int direction)
        {
            int bestFrame = -1;

            foreach (var frame in matchedFrames)
            {
                if (direction > 0 && frame > currentFrame)
                {
                    if (bestFrame < 0 || frame < bestFrame)
                        bestFrame = frame;
                }
                else if (direction < 0 && frame < currentFrame)
                {
                    if (bestFrame < 0 || frame > bestFrame)
                        bestFrame = frame;
                }
            }

            if (bestFrame >= 0)
                m_ProfilerWindowController.selectedFrameIndex = bestFrame;
        }

        // ─── Logging ────────────────────────────────────────────────────────

        void LogMatchedFrame(int frameIndex)
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - m_LastSpikeLogTime < k_SpikeLogCooldownSeconds)
                return;
            m_LastSpikeLogTime = now;

            var sb = m_SpikeLogBuilder;
            sb.Clear();

            using (var iter = new ProfilerFrameDataIterator())
            {
                iter.SetRoot(frameIndex, 0);
                float threshold = m_SpikeFilter != null ? m_SpikeFilter.ThresholdMs : 0f;
                sb.Append($"[Filter] Frame {frameIndex} — CPU: {iter.frameTimeMS:F2}ms, GPU: {iter.frameGpuTimeMS:F2}ms (spike threshold: {threshold:F0}ms)");
            }

            using (var raw = ProfilerDriver.GetRawFrameDataView(frameIndex, 0))
            {
                if (raw != null && raw.valid && raw.sampleCount > 1)
                {
                    int sampleLimit = Mathf.Min(raw.sampleCount, k_MaxLogSamples + 1);

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

        // ─── Internal API for programmatic control ──────────────────────────

        internal bool IsCollectingMarkedFrames => m_SaveMarkedOnly;

        internal void StartCollectingInternal()
        {
            if (m_SaveMarkedOnly) return;
            m_SaveMarkedOnly = true;
            EditorPrefs.SetBool(k_SaveMarkedOnlyKey, true);
            m_DefaultFrameHistoryLength = ProfilerUserSettings.frameCount;
            EditorPrefs.SetInt(k_DefaultFrameHistoryKey, m_DefaultFrameHistoryLength);
            ProfilerUserSettings.frameCount = k_SaveMarkedBufferSize;
            m_FiltersDirty = true;
            UpdatePauseCallbackSubscription();
        }

        internal void StopCollectingInternal()
        {
            if (!m_SaveMarkedOnly) return;
            m_SaveMarkedOnly = false;
            EditorPrefs.SetBool(k_SaveMarkedOnlyKey, false);
            if (m_PauseCallbackSubscribed)
            {
                ProfilerDriver.NewProfilerFrameRecorded -= OnNewProfilerFrame;
                m_PauseCallbackSubscribed = false;
            }
            ProfilerUserSettings.frameCount = m_DefaultFrameHistoryLength;

            // Clean up temp files
            foreach (var f in m_MarkedFrameTempFiles)
            {
                try { if (System.IO.File.Exists(f)) System.IO.File.Delete(f); } catch { }
            }
            m_MarkedFrameTempFiles.Clear();
            ProfilerWindow.Repaint();
        }

        internal bool SaveMergedMarkedFramesToPath(string savePath)
        {
            m_SaveMarkedOnly = false;
            EditorPrefs.SetBool(k_SaveMarkedOnlyKey, false);
            if (m_PauseCallbackSubscribed)
            {
                ProfilerDriver.NewProfilerFrameRecorded -= OnNewProfilerFrame;
                m_PauseCallbackSubscribed = false;
            }

            ProfilerWindow.SetRecordingEnabled(false);
            if (EditorApplication.isPlaying && !EditorApplication.isPaused)
                EditorApplication.isPaused = true;

            if (m_MarkedFrameTempFiles.Count == 0)
            {
                ProfilerUserSettings.frameCount = m_DefaultFrameHistoryLength;
                return false;
            }

            var tempFiles = new List<string>(m_MarkedFrameTempFiles);

            ProfilerUserSettings.frameCount = 2000;
            ProfilerDriver.ClearAllFrames();

            bool first = true;
            foreach (var tempFile in tempFiles)
            {
                if (!System.IO.File.Exists(tempFile))
                    continue;
                ProfilerDriver.LoadProfile(tempFile, !first);
                first = false;
            }

            ProfilerDriver.SaveProfile(savePath);

            foreach (var tempFile in tempFiles)
            {
                try { System.IO.File.Delete(tempFile); } catch { }
            }
            m_MarkedFrameTempFiles.Clear();

            ProfilerUserSettings.frameCount = 2000;
            ProfilerDriver.LoadProfile(savePath, false);

            int loadedCount = ProfilerDriver.lastFrameIndex - ProfilerDriver.firstFrameIndex + 1;
            ProfilerUserSettings.frameCount = Mathf.Clamp(Mathf.Max(m_DefaultFrameHistoryLength, loadedCount + 10), 1, 2000);

            // Re-enable profiler after ClearAllFrames to restore recording buffer
            ProfilerDriver.enabled = true;

            ProfilerWindow.Repaint();
            Debug.Log($"[SpikeTrap] Saved {tempFiles.Count} marked frame snapshots to: {savePath}");
            return true;
        }

        internal void MarkFiltersDirty() => m_FiltersDirty = true;

        internal SpikeFrameFilter SpikeFilter => m_SpikeFilter;
        internal GcFrameFilter GcFilter
        {
            get
            {
                foreach (var f in m_Filters)
                    if (f is GcFrameFilter gc) return gc;
                return null;
            }
        }
    }
}
