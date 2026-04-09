using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
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
        SpikeFrameFilter m_SpikeFilter;
        SearchFrameFilter m_SearchFilter;
        const float k_StripHeight = 20f;

        static readonly List<Func<IFrameFilter>> s_CustomFilterFactories = new List<Func<IFrameFilter>>();

        /// <summary>
        /// Register a factory that creates a custom <see cref="IFrameFilter"/>.
        /// Call from an <c>[InitializeOnLoad]</c> static constructor.
        /// </summary>
        public static void RegisterCustomFilterFactory(Func<IFrameFilter> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            s_CustomFilterFactories.Add(factory);
        }

        // Unified pause/log on any filter match
        const string k_PauseOnFilterKey = "LightningProfiler.PauseOnFilter";
        const string k_LogOnFilterKey = "LightningProfiler.LogOnFilter";
        bool m_PauseOnFilter;
        bool m_LogOnFilter;
        bool m_PauseCallbackSubscribed;

        // Save-only-marked-frames
        const string k_SaveMarkedOnlyKey = "LightningProfiler.SaveMarkedOnly";
        const string k_DefaultFrameHistoryKey = "LightningProfiler.DefaultFrameHistory";
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
        Texture2D m_ScreenshotTexture;
        long m_ScreenshotFrameIndex = -1;
        const float k_ScreenshotMaxHeight = 150f;

        // Screenshot metadata constants (matches com.utj.screenshot2profiler runtime)
        static readonly System.Guid k_SSMetadataGuid = new System.Guid("4389DCEB-F9B3-4D49-940B-E98482F3A3F8");
        const int k_SSInfoTag = -1;

        // Session metadata — cached per profiler session
        static readonly System.Guid k_SessionGuid = new System.Guid("A17B3C4D-E5F6-4789-ABCD-EF0123456789");
        const int k_SessionInfoTag = -100;
        bool m_IsEditorSession = true;
        int m_SessionCheckedFrame = -1;


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
            m_IMGUIView = imgui;

            // Create chart overlay for collecting state — sits on top of the chart area.
            // Defer to next frame so EditorStyles and the full visual tree are ready.
            imgui.RegisterCallback<AttachToPanelEvent>(_ => EditorApplication.delayCall += AttachChartOverlay);

            return imgui;
        }

        void AttachChartOverlay()
        {
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

            float toolbarHeight = EditorStyles.toolbar != null ? EditorStyles.toolbar.fixedHeight : 21f;
            m_ChartOverlay.style.top = toolbarHeight;

            m_ChartOverlay.visible = m_SaveMarkedOnly;
            chartContainer.Add(m_ChartOverlay);
        }

        protected override void Dispose(bool disposing)
        {
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

            ProfilerWindow.recordingStateChanged += OnRecordingStateChanged;

            // --- Create built-in filters ---
            m_SpikeFilter = new SpikeFrameFilter(
                () => IsEditorSession(),
                threshold =>
                {
                    var module = ProfilerWindow.selectedModule as FilterableProfilerModule;
                    if (module != null)
                        module.SetChartFilterThreshold(threshold);
                });

            var gcFilter = new GcFrameFilter(() => IsEditorSession());

            m_SearchFilter = new SearchFrameFilter(
                () => m_FrameDataHierarchyView.threadIndex,
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

            m_PauseOnFilter = EditorPrefs.GetBool(k_PauseOnFilterKey, false);
            m_LogOnFilter = EditorPrefs.GetBool(k_LogOnFilterKey, false);
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
            var rs = detailsViewContainer.resolvedStyle;
            var rect = new Rect(0f, 0f, rs.width, rs.height);
            rect.yMin += EditorStyles.contentToolbar.CalcHeight(GUIContent.none, 10f);
            OnModuleDetailsGUI(rect);
        }

        void OnModuleDetailsGUI(Rect rect)
        {
            var fetchData = !m_ProfilerWindowController.ProfilerWindowOverheadIsAffectingProfilingRecordingData() || m_UpdateViewLive;

            DrawFilterControls();

            UpdateChartOverlay();

            if (m_SaveMarkedOnly)
            {
                DrawCollectingOverlay();
                return;
            }

            // Update and draw filter strips
            foreach (var filter in m_Filters)
                filter.UpdateMatches();

            DrawCombinedHighlightStrip(rect);

            // Screenshot preview
            DrawScreenshotPreview();

            var frameData = fetchData ? GetFrameDataViewForHierarchy() : null;
            m_FrameDataHierarchyView.DoGUI(frameData, fetchData, ref m_UpdateViewLive, null);
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

            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                normal = { textColor = new Color(1f, 1f, 1f, 0.6f) }
            };

            int count = m_MarkedFrameTempFiles.Count;
            string text = count > 0
                ? $"Collecting...  ({count} frames captured)"
                : "Collecting...";
            GUI.Label(overlayRect, text, style);

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

            // --- Unified Pause / Log ---
            {
                bool anyActive = AnyFilterActive();
                using (new EditorGUI.DisabledScope(!anyActive))
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
            var view = ProfilerDriver.GetHierarchyFrameDataView(frameIdx, 0, HierarchyFrameDataView.ViewModes.Default, 0, false);
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
            return tex;
        }

        // ─── Recording / Collect ────────────────────────────────────────────

        void OnRecordingStateChanged(bool recording)
        {
            if (!recording && m_SaveMarkedOnly && m_MarkedFrameTempFiles.Count > 0)
            {
                EditorApplication.delayCall += SaveMergedMarkedFrames;
            }
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

            // Invalidate all filter caches
            foreach (var filter in m_Filters)
                filter.InvalidateCache();
            ProfilerWindow.Repaint();

            Debug.Log($"[LightningProfiler] Saved {tempFiles.Count} marked frame snapshots to: {savePath}");
        }

        // ─── Filter evaluation ──────────────────────────────────────────────

        bool AnyFilterActive()
        {
            foreach (var f in m_Filters)
                if (f.IsActive) return true;
            return false;
        }

        FrameDataContext BuildFrameDataContext(int frame, RawFrameDataView raw)
        {
            float frameTimeMs;
            using (var iter = new ProfilerFrameDataIterator())
            {
                iter.SetRoot(frame, 0);
                frameTimeMs = iter.frameTimeMS;
            }

            bool isEditor = IsEditorSession();
            float editorLoopMs = 0f;
            if (isEditor && raw != null && raw.valid)
            {
                for (int i = 0; i < raw.sampleCount; i++)
                {
                    if (raw.GetSampleName(i) == "EditorLoop")
                        editorLoopMs += raw.GetSampleTimeMs(i);
                }
            }

            return new FrameDataContext(frame, frameTimeMs, isEditor, raw, editorLoopMs);
        }

        bool IsFrameMarked(int frame)
        {
            if (!AnyFilterActive())
                return false;

            using (var raw = ProfilerDriver.GetRawFrameDataView(frame, 0))
            {
                var ctx = BuildFrameDataContext(frame, raw);
                foreach (var filter in m_Filters)
                {
                    if (filter.IsActive && filter.IsMatch(in ctx))
                        return true;
                }
            }
            return false;
        }

        bool IsEditorSession()
        {
            int lastFrame = ProfilerDriver.lastFrameIndex;
            if (m_SessionCheckedFrame == lastFrame)
                return m_IsEditorSession;

            int firstFrame = ProfilerDriver.firstFrameIndex;
            if (firstFrame < 0) return m_IsEditorSession;
            var view = ProfilerDriver.GetHierarchyFrameDataView(firstFrame, 0, HierarchyFrameDataView.ViewModes.Default, 0, false);
            if (view != null && view.valid)
            {
                var data = view.GetSessionMetaData<byte>(k_SessionGuid, k_SessionInfoTag);
                if (data.IsCreated && data.Length >= 1)
                    m_IsEditorSession = data[0] == 1;
            }
            m_SessionCheckedFrame = lastFrame;
            return m_IsEditorSession;
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
            // --- Save-marked-only ---
            if (m_SaveMarkedOnly)
            {
                if (IsFrameMarked(frameIndex))
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

            // Check if frame matches any active filter
            bool isMarked = false;
            using (var raw = ProfilerDriver.GetRawFrameDataView(checkFrame, 0))
            {
                var ctx = BuildFrameDataContext(checkFrame, raw);
                foreach (var filter in m_Filters)
                {
                    if (filter.IsActive && filter.IsMatch(in ctx))
                    {
                        isMarked = true;
                        break;
                    }
                }
            }

            if (isMarked && m_LogOnFilter)
                LogSpikeFrame(checkFrame);

            if (!isPlaying)
                return;

            if (isMarked && m_PauseOnFilter)
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

        void OnSearchChanged(string newSearch)
        {
            m_SearchFilter.SetSearchString(newSearch);
            UpdatePauseCallbackSubscription();
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

            foreach (var filter in m_Filters)
            {
                // Reserve full row, then split at sideWidth using absolute rects
                var rowRect = GUILayoutUtility.GetRect(containerRect.width, k_StripHeight);

                // --- Left side: nav buttons + filter controls (clipped to sideWidth) ---
                var prevRect = new Rect(rowRect.x, rowRect.y, btnWidth, k_StripHeight);
                var nextRect = new Rect(rowRect.x + btnWidth, rowRect.y, btnWidth, k_StripHeight);

                if (GUI.Button(prevRect, s_PrevFrameIcon, EditorStyles.iconButton))
                    JumpToMatchedFrame(filter, selectedFrame, -1);
                if (GUI.Button(nextRect, s_NextFrameIcon, EditorStyles.iconButton))
                    JumpToMatchedFrame(filter, selectedFrame, +1);

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

                    foreach (var frame in filter.MatchedFrames)
                    {
                        int rel = frame - firstEmptyFrame;
                        if (rel < 0 || rel >= frameCount) continue;
                        EditorGUI.DrawRect(new Rect(vizRect.x + rel * frameWidth, vizRect.y,
                            Mathf.Max(1f, frameWidth), k_StripHeight), filter.StripColor);
                    }

                    if (selectedRelative >= 0 && selectedRelative < frameCount)
                        EditorGUI.DrawRect(new Rect(vizRect.x + selectedRelative * frameWidth, vizRect.y,
                            Mathf.Max(2f, frameWidth), k_StripHeight), new Color(1f, 1f, 1f, 0.8f));
                }
            }

            if (anyChanged)
                UpdatePauseCallbackSubscription();
        }

        void JumpToMatchedFrame(IFrameFilter filter, int currentFrame, int direction)
        {
            int bestFrame = -1;

            foreach (var frame in filter.MatchedFrames)
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

        void LogSpikeFrame(int frameIndex)
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
    }
}
