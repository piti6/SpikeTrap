using UnityEngine;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.MPE;
using UnityEditor.Profiling;
using UnityEditorInternal.Profiling;
using UnityEditorInternal;
using System.Linq;

namespace LightningProfiler
{
    [Serializable]
    internal class ProfilerFrameDataHierarchyView
    {
        protected static class BaseStyles
        {
            public static readonly GUIContent noData = EditorGUIUtility.TrTextContent("No frame data available. Select a frame from the charts above to see its details here.");
            public static GUIContent disabledSearchText = EditorGUIUtility.TrTextContent("Showing search results are disabled while recording with deep profiling.\nStop recording to view search results.");
            public static GUIContent cpuGPUTime = EditorGUIUtility.TrTextContent("CPU:{0}ms   GPU:{1}ms");

            public static readonly GUIStyle header = "OL title";
            public static readonly GUIStyle label = "OL label";
            public static readonly GUIStyle toolbar = EditorStyles.toolbar;
            public static readonly GUIStyle selectionExtraInfoArea = EditorStyles.helpBox;
            public static readonly GUIContent warningTriangle = EditorGUIUtility.IconContent("console.infoicon.inactive.sml");
            public static readonly GUIStyle tooltip = new GUIStyle("AnimationEventTooltip");
            public static readonly GUIStyle tooltipText = new GUIStyle("AnimationEventTooltip");
            public static readonly GUIStyle tooltipArrow = "AnimationEventTooltipArrow";
            public static readonly GUIStyle tooltipButton = EditorStyles.miniButton;
            public static readonly GUIStyle tooltipDropdown = new GUIStyle("MiniPopup");
            public static readonly int tooltipButtonAreaControlId = "ProfilerTimelineTooltipButton".GetHashCode();
            public static readonly int timelineTimeAreaControlId = "ProfilerTimelineTimeArea".GetHashCode();

            public static readonly GUIContent tooltipCopyTooltip = EditorGUIUtility.TrTextContent("Copy", "Copy to Clipboard");

            public static readonly GUIContent showDetailsDropdownContent = EditorGUIUtility.TrTextContent("Show");
            public static readonly GUIContent showFullDetailsForCallStacks = EditorGUIUtility.TrTextContent("Full details for Call Stacks");
            public static readonly GUIContent showSelectedSampleStacks = EditorGUIUtility.TrTextContent("Selected Sample Stack ...");
            public static readonly GUIStyle viewTypeToolbarDropDown = new GUIStyle(EditorStyles.toolbarDropDownLeft);
            public static readonly GUIStyle threadSelectionToolbarDropDown = new GUIStyle(EditorStyles.toolbarDropDown);
            public static readonly GUIStyle detailedViewTypeToolbarDropDown = new GUIStyle(EditorStyles.toolbarDropDown);
            public static readonly GUIContent updateLive = EditorGUIUtility.TrTextContent("Live", "Display the current or selected frame while recording Playmode or Editor. This increases the overhead in the EditorLoop when the Profiler Window is repainted.");
            public static readonly GUIContent liveUpdateMessage = EditorGUIUtility.TrTextContent("Displaying of frame data disabled while recording Playmode or Editor. To see the data, pause recording, or toggle \"Live\" display mode on. " +
                "\n \"Live\" display mode increases the overhead in the EditorLoop when the Profiler Window is repainted.");

            public static readonly string selectionExtraInfoHierarhcyView = L10n.Tr("Selection Info: ");
            public static readonly string proxySampleMessage = L10n.Tr("Sample \"{0}\" {1} {2} deeper not found in this frame within the selected Sample Stack.");
            public static readonly string proxySampleMessageScopeSingular = L10n.Tr("scope");
            public static readonly string proxySampleMessageScopePlural = L10n.Tr("scopes");
            public static readonly string proxySampleMessageTooltip = L10n.Tr("Selected Sample Stack: {0}");
            public static readonly string proxySampleMessagePart2TimelineView = L10n.Tr("\nClosest match:\n");

            public static readonly string callstackText = LocalizationDatabase.GetLocalizedString("Call Stack:");

            // 6 seems like a good default value for margins used in quite some places. Do note though, that this is little more than a semi-randomly chosen magic number.
            public const int magicMarginValue = 6;
            const float k_DetailedViewTypeToolbarDropDownWidth = 150f;

            public static readonly Rect tooltipArrowRect = new Rect(-32, 0, 64, 6);

            static BaseStyles()
            {
                viewTypeToolbarDropDown.fixedWidth = Chart.kSideWidth;
                viewTypeToolbarDropDown.stretchWidth = false;

                detailedViewTypeToolbarDropDown.fixedWidth = k_DetailedViewTypeToolbarDropDownWidth;
                tooltip.contentOffset = new Vector2(0, 0);
                tooltip.overflow = new RectOffset(0, 0, 0, 0);
                tooltipText = new GUIStyle(tooltip);
                tooltipText.onNormal.background = null;
                tooltipDropdown.margin.right += magicMarginValue;
            }
        }

        public const int invalidTreeViewId = -1;
        public const int invalidTreeViewDepth = -1;

        public static GUIContent LiveViewDisabledContent => BaseStyles.liveUpdateMessage;
        public static GUIContent NoFrameDataContent => BaseStyles.noData;
        public static GUIStyle ProfilerDetailsLabelStyle => BaseStyles.label;

        private IProfilerWindowController m_profilerWindow;

        [SerializeField]
        private int m_FrameIndex = FrameDataView.invalidOrCurrentFrameIndex;

        private HierarchyFrameDataView m_frameDataView;

        [SerializeField]
        TreeViewState m_TreeViewState;

        [SerializeField]
        MultiColumnHeaderState m_MultiColumnHeaderState;

        [SerializeField]
        string m_GroupName = "";

        [SerializeField]
        string m_ThreadName = "Main Thread";

        [SerializeField]
        string m_FullThreadName = "Main Thread";

        [SerializeField]
        ulong m_ThreadId;

        [SerializeField]
        int m_ThreadIndex = FrameDataView.invalidThreadIndex;

        [SerializeField]
        int m_ThreadIndexInList;

        [NonSerialized]
        List<ThreadMenuEntry> m_ThreadMenuEntries;

        enum DetailedPanelType
        {
            None = 0,
            RelatedData = 1,
            Calls = 2
        }

        [SerializeField]
        DetailedPanelType m_DetailedPanelType;

        static readonly GUIContent[] kDetailedPanelNames =
        {
            EditorGUIUtility.TrTextContent("No Details"),
            EditorGUIUtility.TrTextContent("Related Data"),
            EditorGUIUtility.TrTextContent("Calls")
        };

        static readonly int[] kDetailedPanelValues = { 0, 1, 2 };

        readonly string m_serializationPrefKeyPrefix;
        string MultiColumnHeaderStatePrefKey => m_serializationPrefKeyPrefix + "MultiColumnHeaderState";

        struct ThreadMenuEntry : IComparable<ThreadMenuEntry>
        {
            public int sortKey;
            public string label;
            public int engineThreadIndex;

            public int CompareTo(ThreadMenuEntry other)
            {
                if (sortKey != other.sortKey)
                    return sortKey.CompareTo(other.sortKey);
                var c = string.Compare(label, other.label, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : engineThreadIndex.CompareTo(other.engineThreadIndex);
            }
        }

        public string groupName => m_GroupName ?? string.Empty;
        public string threadName => m_ThreadName ?? string.Empty;
        public ulong threadId => m_ThreadId;
        public int threadIndex => m_ThreadIndex;

        static readonly GUIContent[] kCPUProfilerViewTypeNames = new GUIContent[]
        {
            EditorGUIUtility.TrTextContent("Hierarchy"),
        };
        
        public event Action<bool> OnToggleLive = on => { };
        public event Action<string, string, int> userChangedThread = delegate { };

        protected void DrawLiveUpdateToggle(bool updateViewLive)
        {
            using (new EditorGUI.DisabledScope(ProcessService.level != ProcessLevel.Main))
            {
                // This button is only needed in the Master Process
                var newUpdateViewLive = GUILayout.Toggle(updateViewLive, BaseStyles.updateLive, EditorStyles.toolbarButton);

                if (newUpdateViewLive != updateViewLive)
                {
                    OnToggleLive.Invoke(newUpdateViewLive);
                }
            }
        }
        SearchField m_SearchField;
        ProfilerFrameDataTreeView m_TreeView;

        public ProfilerFrameDataTreeView treeView
        {
            get
            {
                return m_TreeView;
            }
        }

        public int sortedProfilerColumn
        {
            get
            {
                return m_TreeView == null ? HierarchyFrameDataView.columnDontSort : m_TreeView.sortedProfilerColumn;
            }
        }

        public bool sortedProfilerColumnAscending
        {
            get
            {
                return m_TreeView == null ? false : m_TreeView.sortedProfilerColumnAscending;
            }
        }

        public event Action<ProfilerTimeSampleSelection> selectionChanged;

        public delegate void SearchChangedCallback(string newSearch);
        public event SearchChangedCallback searchChanged;

        public ProfilerFrameDataHierarchyView() : this("Profiler.CPUProfilerModule.HierarchyView.") { }

        public ProfilerFrameDataHierarchyView(string serializationPrefKeyPrefix)
        {
            m_serializationPrefKeyPrefix = serializationPrefKeyPrefix ?? "Profiler.CPUProfilerModule.HierarchyView.";
        }

        public void OnEnable(IProfilerWindowController profilerWindow)
        {
            m_profilerWindow = profilerWindow;
            m_profilerWindow.frameDataViewAboutToBeDisposed += OnFrameDataViewAboutToBeDisposed;
            m_FrameIndex = FrameDataView.invalidOrCurrentFrameIndex;

            var multiColumnHeaderStateData = SessionState.GetString(MultiColumnHeaderStatePrefKey, "");
            if (!string.IsNullOrEmpty(multiColumnHeaderStateData))
            {
                try
                {
                    var restoredHeaderState = JsonUtility.FromJson<MultiColumnHeaderState>(multiColumnHeaderStateData);
                    if (restoredHeaderState != null)
                        m_MultiColumnHeaderState = restoredHeaderState;
                }
                catch { } // Nevermind, we'll just fall back to the default
            }
            var headerState = CreateDefaultMultiColumnHeaderState(cpuHierarchyColumns, HierarchyFrameDataView.columnTotalTime);
            if (MultiColumnHeaderState.CanOverwriteSerializedFields(m_MultiColumnHeaderState, headerState))
                MultiColumnHeaderState.OverwriteSerializedFields(m_MultiColumnHeaderState, headerState);

            var firstInit = m_MultiColumnHeaderState == null;
            m_MultiColumnHeaderState = headerState;

            var multiColumnHeader = new ProfilerFrameDataMultiColumnHeader(m_MultiColumnHeaderState, cpuHierarchyColumns) { height = 25 };
            if (firstInit)
                multiColumnHeader.ResizeToFit();

            multiColumnHeader.visibleColumnsChanged += OnMultiColumnHeaderChanged;
            multiColumnHeader.sortingChanged += OnMultiColumnHeaderChanged;

            // Check if it already exists (deserialized from window layout file or scriptable object)
            if (m_TreeViewState == null)
                m_TreeViewState = new TreeViewState();
            m_TreeView = new ProfilerFrameDataTreeView(m_TreeViewState, multiColumnHeader, new SimpleSampleNameProvider(), m_profilerWindow);
            m_TreeView.selectionChanged += OnMainTreeViewSelectionChanged;
            m_TreeView.searchChanged += OnMainTreeViewSearchChanged;
            m_TreeView.Reload();

            m_SearchField = new SearchField();
            m_SearchField.downOrUpArrowKeyPressed += m_TreeView.SetFocusAndEnsureSelectedItem;
        }

        void OnFrameDataViewAboutToBeDisposed()
        {
            m_TreeView?.OnFrameDataViewAboutToBeDisposed();
        }

        void OnMultiColumnHeaderChanged(MultiColumnHeader header)
        {
            SessionState.SetString(MultiColumnHeaderStatePrefKey, JsonUtility.ToJson(header.state));
        }

        private static readonly ProfilerFrameDataMultiColumnHeader.Column[] cpuHierarchyColumns = new[]
        {
            HierarchyFrameDataView.columnName,
            HierarchyFrameDataView.columnTotalPercent,
            HierarchyFrameDataView.columnSelfPercent,
            HierarchyFrameDataView.columnCalls,
            HierarchyFrameDataView.columnGcMemory,
            HierarchyFrameDataView.columnTotalTime,
            HierarchyFrameDataView.columnSelfTime,
            HierarchyFrameDataView.columnWarningCount
        }.Select(x =>
        {
            return new ProfilerFrameDataMultiColumnHeader.Column
            {
                profilerColumn = x,
                headerLabel = new GUIContent(GetProfilerColumnName(x))
            };
        }).ToArray();

        public static MultiColumnHeaderState CreateDefaultMultiColumnHeaderState(ProfilerFrameDataMultiColumnHeader.Column[] columns, int defaultSortColumn)
        {
            var headerColumns = new MultiColumnHeaderState.Column[columns.Length];
            for (var i = 0; i < columns.Length; ++i)
            {
                var width = 80;
                var minWidth = 50;
                var maxWidth = 1000000f;
                var autoResize = false;
                var allowToggleVisibility = true;
                switch (columns[i].profilerColumn)
                {
                    case HierarchyFrameDataView.columnName:
                        width = 200;
                        minWidth = 200;
                        autoResize = true;
                        allowToggleVisibility = false;
                        break;
                    case HierarchyFrameDataView.columnWarningCount:
                        width = 25;
                        minWidth = 25;
                        maxWidth = 25;
                        break;
                }

                var headerColumn = new MultiColumnHeaderState.Column
                {
                    headerContent = columns[i].headerLabel,
                    headerTextAlignment = TextAlignment.Left,
                    sortingArrowAlignment = TextAlignment.Right,
                    width = width,
                    minWidth = minWidth,
                    maxWidth = maxWidth,
                    autoResize = autoResize,
                    allowToggleVisibility = allowToggleVisibility,
                    sortedAscending = i == 0
                };
                headerColumns[i] = headerColumn;
            }

            var state = new MultiColumnHeaderState(headerColumns)
            {
                sortedColumnIndex = ProfilerFrameDataMultiColumnHeader.GetMultiColumnHeaderIndex(columns, defaultSortColumn),
            };
            return state;
        }

        static string GetProfilerColumnName(int column)
        {
            switch (column)
            {
                case HierarchyFrameDataView.columnName:
                    return LocalizationDatabase.GetLocalizedString("Overview");
                case HierarchyFrameDataView.columnTotalPercent:
                    return LocalizationDatabase.GetLocalizedString("Total");
                case HierarchyFrameDataView.columnSelfPercent:
                    return LocalizationDatabase.GetLocalizedString("Self");
                case HierarchyFrameDataView.columnCalls:
                    return LocalizationDatabase.GetLocalizedString("Calls");
                case HierarchyFrameDataView.columnGcMemory:
                    return LocalizationDatabase.GetLocalizedString("GC Alloc");
                case HierarchyFrameDataView.columnTotalTime:
                    return LocalizationDatabase.GetLocalizedString("Time ms");
                case HierarchyFrameDataView.columnSelfTime:
                    return LocalizationDatabase.GetLocalizedString("Self ms");
                case HierarchyFrameDataView.columnDrawCalls:
                    return LocalizationDatabase.GetLocalizedString("DrawCalls");
                case HierarchyFrameDataView.columnTotalGpuTime:
                    return LocalizationDatabase.GetLocalizedString("GPU ms");
                case HierarchyFrameDataView.columnSelfGpuTime:
                    return LocalizationDatabase.GetLocalizedString("Self ms");
                case HierarchyFrameDataView.columnTotalGpuPercent:
                    return LocalizationDatabase.GetLocalizedString("Total");
                case HierarchyFrameDataView.columnSelfGpuPercent:
                    return LocalizationDatabase.GetLocalizedString("Self");
                case HierarchyFrameDataView.columnWarningCount:
                    return LocalizationDatabase.GetLocalizedString("Warnings");
                case HierarchyFrameDataView.columnObjectName:
                    return LocalizationDatabase.GetLocalizedString("Object Name");
                case HierarchyFrameDataView.columnStartTime:
                    return LocalizationDatabase.GetLocalizedString("Start ms");
                default:
                    return "ProfilerColumn." + column;
            }
        }

        public void DoGUI(HierarchyFrameDataView frameDataView, bool isLive)
        {
            bool live = isLive;
            DoGUI(frameDataView, true, ref live, null);
        }

        public void DoGUI(HierarchyFrameDataView frameDataView, bool fetchData, ref bool updateViewLive, Action drawOptionsMenu)
        {
            var isSearchAllowed = string.IsNullOrEmpty(treeView.searchString) ||
                !(m_profilerWindow.ProfilerWindowOverheadIsAffectingProfilingRecordingData() && ProfilerDriver.deepProfiling);
            var isDataAvailable = frameDataView != null && frameDataView.valid;

            GUILayout.BeginVertical();

            if (isDataAvailable && (m_ThreadIndex != frameDataView.threadIndex || m_ThreadName != frameDataView.threadName))
                SyncThreadStateFromFrameData(frameDataView);

            DrawHierarchyToolbar(frameDataView, fetchData, ref updateViewLive, drawOptionsMenu);

            if (!isDataAvailable)
            {
                if (!fetchData && !updateViewLive)
                    GUILayout.Label(BaseStyles.liveUpdateMessage, BaseStyles.label);
                else
                    GUILayout.Label(BaseStyles.noData, BaseStyles.label);
            }
            else if (!isSearchAllowed)
            {
                GUILayout.Label(BaseStyles.disabledSearchText, BaseStyles.label);
            }
            else
            {
                var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandHeight(true), GUILayout.ExpandHeight(true));
                m_TreeView.SetFrameDataView(frameDataView);
                m_TreeView.OnGUI(rect, updateViewLive);
            }

            if (m_DetailedPanelType != DetailedPanelType.None && isDataAvailable)
            {
                EditorGUILayout.BeginHorizontal(BaseStyles.toolbar);
                DrawDetailedPanelPopup();
                drawOptionsMenu?.Invoke();
                EditorGUILayout.EndHorizontal();
                GUILayout.Label(
                    m_DetailedPanelType == DetailedPanelType.RelatedData
                        ? "Related Data panel: use Unity Editor Profiler for full object linkage, or extend LightningProfiler."
                        : "Calls panel: use Unity Editor Profiler for full caller/callee data, or extend LightningProfiler.",
                    EditorStyles.wordWrappedLabel);
            }

            GUILayout.EndVertical();

            HandleKeyboardEvents();
        }

        public bool hideSearchBar { get; set; }

        public void DrawSearchBarExternal()
        {
            DrawSearchBar();
        }

        void DrawSearchBar()
        {
            float w = Chart.kSideWidth - 34f;
            var rect = GUILayoutUtility.GetRect(w, w, EditorGUI.kSingleLineHeight, EditorGUI.kSingleLineHeight, EditorStyles.toolbarSearchField);
            treeView.searchString = m_SearchField.OnToolbarGUI(rect, treeView.searchString);
        }

        void DrawHierarchyToolbar(HierarchyFrameDataView frameDataView, bool fetchData, ref bool updateViewLive, Action drawOptionsMenu)
        {
            EditorGUILayout.BeginHorizontal(BaseStyles.toolbar);

            DrawLiveUpdateToggleRef(ref updateViewLive);

            DrawThreadPopup(frameDataView);

            GUILayout.FlexibleSpace();

            if (frameDataView != null && frameDataView.valid)
            {
                var cpuTimeMs = frameDataView.frameTimeMs;
                var cpuTime = cpuTimeMs > 0 ? $"{cpuTimeMs:N2}" : "--";
                GUILayout.Label($"CPU:{cpuTime}ms", EditorStyles.toolbarLabel);
            }

            GUILayout.FlexibleSpace();

            if (!hideSearchBar)
                DrawSearchBar();

            if (m_DetailedPanelType == DetailedPanelType.None)
            {
                DrawDetailedPanelPopup();
                EditorGUILayout.Space();
                drawOptionsMenu?.Invoke();
            }

            EditorGUILayout.EndHorizontal();
        }

        void DrawLiveUpdateToggleRef(ref bool updateViewLive)
        {
            using (new EditorGUI.DisabledScope(ProcessService.level != ProcessLevel.Main))
            {
                var newUpdateViewLive = GUILayout.Toggle(updateViewLive, BaseStyles.updateLive, EditorStyles.toolbarButton);
                if (newUpdateViewLive != updateViewLive)
                {
                    updateViewLive = newUpdateViewLive;
                    OnToggleLive.Invoke(updateViewLive);
                }
            }
        }

        void DrawDetailedPanelPopup()
        {
            var v = (DetailedPanelType)EditorGUILayout.IntPopup((int)m_DetailedPanelType, kDetailedPanelNames, kDetailedPanelValues, BaseStyles.detailedViewTypeToolbarDropDown, GUILayout.Width(BaseStyles.detailedViewTypeToolbarDropDown.fixedWidth));
            if (v != m_DetailedPanelType)
                m_DetailedPanelType = v;
        }

        static int ThreadSortOrder(string displayName)
        {
            if (displayName.StartsWith("Job", StringComparison.Ordinal))
                return 2;
            if (displayName.StartsWith("Loading", StringComparison.Ordinal))
                return 3;
            if (displayName.IndexOf("Scripting", StringComparison.Ordinal) >= 0)
                return 4;
            if (displayName.StartsWith("Main Thread", StringComparison.Ordinal))
                return 0;
            if (displayName.StartsWith("Render Thread", StringComparison.Ordinal))
                return 1;
            return 10;
        }

        void RefreshThreadMenu(int frameIndex)
        {
            if (frameIndex < 0)
                return;
            if (m_ThreadMenuEntries == null)
                m_ThreadMenuEntries = new List<ThreadMenuEntry>();
            m_ThreadMenuEntries.Clear();
            using (var it = new ProfilerFrameDataIterator())
            {
                int n = it.GetThreadCount(frameIndex);
                for (int i = 0; i < n; i++)
                {
                    it.SetRoot(frameIndex, i);
                    var g = it.GetGroupName() ?? "";
                    var tn = it.GetThreadName() ?? "";
                    var label = string.IsNullOrEmpty(g) ? tn : g + "." + tn;
                    m_ThreadMenuEntries.Add(new ThreadMenuEntry
                    {
                        sortKey = ThreadSortOrder(label),
                        label = label,
                        engineThreadIndex = i
                    });
                }
            }

            m_ThreadMenuEntries.Sort();
            m_ThreadIndexInList = 0;
            for (int i = 0; i < m_ThreadMenuEntries.Count; i++)
            {
                if (m_ThreadMenuEntries[i].engineThreadIndex == m_ThreadIndex)
                {
                    m_ThreadIndexInList = i;
                    break;
                }
            }
        }

        void DrawThreadPopup(HierarchyFrameDataView frameDataView)
        {
            var style = BaseStyles.threadSelectionToolbarDropDown;
            var content = new GUIContent(string.IsNullOrEmpty(m_FullThreadName) ? "Main Thread" : m_FullThreadName);
            float minW, maxW;
            style.CalcMinMaxWidth(content, out minW, out maxW);
            var r = GUILayoutUtility.GetRect(content, style, GUILayout.MinWidth(Math.Max(BaseStyles.detailedViewTypeToolbarDropDown.fixedWidth, minW)));
            var disabled = frameDataView == null || !frameDataView.valid;
            using (new EditorGUI.DisabledScope(disabled))
            {
                if (EditorGUI.DropdownButton(r, content, FocusType.Keyboard, style))
                {
                    RefreshThreadMenu(frameDataView.frameIndex);
                    var menu = new GenericMenu();
                    for (int i = 0; i < m_ThreadMenuEntries.Count; i++)
                    {
                        int idx = i;
                        var e = m_ThreadMenuEntries[i];
                        menu.AddItem(new GUIContent(e.label), idx == m_ThreadIndexInList, () => OnThreadMenuPick(e, idx));
                    }

                    menu.DropDown(r);
                }
            }
        }

        void OnThreadMenuPick(ThreadMenuEntry entry, int listIndex)
        {
            m_ThreadIndexInList = listIndex;
            m_FullThreadName = entry.label;
            var dot = m_FullThreadName.IndexOf('.');
            if (dot > 0)
            {
                m_GroupName = m_FullThreadName.Substring(0, dot);
                m_ThreadName = m_FullThreadName.Substring(dot + 1);
            }
            else
            {
                m_GroupName = string.Empty;
                m_ThreadName = m_FullThreadName;
            }

            userChangedThread(m_GroupName, m_ThreadName, entry.engineThreadIndex);
            m_profilerWindow.Repaint();
        }

        void SyncThreadStateFromFrameData(HierarchyFrameDataView fd)
        {
            m_GroupName = fd.threadGroupName ?? string.Empty;
            m_ThreadName = fd.threadName ?? string.Empty;
            m_ThreadId = fd.threadId;
            m_ThreadIndex = fd.threadIndex;
            m_FullThreadName = string.IsNullOrEmpty(m_GroupName) ? m_ThreadName : m_GroupName + "." + m_ThreadName;
            RefreshThreadMenu(fd.frameIndex);
        }

        void HandleKeyboardEvents()
        {
            if (!m_TreeView.HasFocus() || !m_TreeView.HasSelection())
                return;

            var evt = Event.current;
            if (evt.type == EventType.KeyDown && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter))
                SelectObjectsInHierarchyView();
        }

        void SelectObjectsInHierarchyView()
        {
            var instanceIds = m_TreeView.GetSelectedInstanceIds();
            if (instanceIds == null || instanceIds.Count == 0)
                return;

            var selection = new List<UnityEngine.Object>();

            foreach (int t in instanceIds)
            {
                var obj = EditorUtility.InstanceIDToObject(t);
                var com = obj as Component;
                if (com != null)
                    selection.Add(com.gameObject);
                else if (obj != null)
                    selection.Add(obj);
            }

            if (selection.Count != 0)
                Selection.objects = selection.ToArray();
        }

        void OnMainTreeViewSelectionChanged(ProfilerTimeSampleSelection selection)
        {
            if (selectionChanged != null)
                selectionChanged.Invoke(selection);
        }

        void OnMainTreeViewSearchChanged(string newSearch)
        {
            if (searchChanged != null)
                searchChanged.Invoke(newSearch);
            if (m_TreeView.HasSelection())
            {
                var selection = m_TreeView.GetSelection();
                if (selection != null && selection.Count > 0 && selection[0] > 0)
                {
                    m_TreeView.SetSelection(selection);
                    m_TreeView.FrameItem(selection[0]);
                }
            }
        }

        public void SetSelection(ProfilerTimeSampleSelection selection, bool expandSelection)
        {
            if (selection.markerIdPath == null)
            {
                throw new ArgumentNullException(nameof(selection.markerIdPath));
            }
            if (selection.markerNamePath == null)
            {
                throw new ArgumentNullException(nameof(selection.markerNamePath));
            }
            if (selection.markerIdPath.Count != selection.markerPathDepth)
            {
                throw new ArgumentException($"ProfilerFrameDataHierarchyView.SetSelectionFromMarkerIDPath needs to be called with {nameof(selection)} having {nameof(selection.markerIdPath)} and {nameof(selection.markerNamePath)} with the same amount of elements.");
            }
            m_TreeView.SetSelection(selection, expandSelection);
        }

        public void ClearSelection()
        {
            m_TreeView.ClearSelection();
        }

        public void SetFrameDataView(HierarchyFrameDataView frameDataView)
        {
            if (frameDataView != null && frameDataView.valid)
                SyncThreadStateFromFrameData(frameDataView);
            if (m_TreeView != null)
                m_TreeView.SetFrameDataView(frameDataView);
            m_frameDataView = frameDataView;
            if (frameDataView != null && frameDataView.valid)
                m_FrameIndex = frameDataView.frameIndex;
        }

        public void Clear()
        {
            if (m_TreeView != null)
            {
                if (m_TreeView.multiColumnHeader != null)
                {
                    m_TreeView.multiColumnHeader.visibleColumnsChanged -= OnMultiColumnHeaderChanged;
                    m_TreeView.multiColumnHeader.sortingChanged -= OnMultiColumnHeaderChanged;
                }
                m_TreeView.Clear();
            }
        }

        public void OnDisable()
        {
            m_profilerWindow.frameDataViewAboutToBeDisposed -= OnFrameDataViewAboutToBeDisposed;
            if (m_TreeView != null)
            {
                if (m_TreeView.multiColumnHeader != null)
                {
                    m_TreeView.multiColumnHeader.visibleColumnsChanged -= OnMultiColumnHeaderChanged;
                    m_TreeView.multiColumnHeader.sortingChanged -= OnMultiColumnHeaderChanged;
                }
            }
        }
    }
}
