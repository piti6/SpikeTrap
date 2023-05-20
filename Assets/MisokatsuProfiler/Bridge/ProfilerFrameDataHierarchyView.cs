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

namespace LightningProfiler
{
    public class SimpleSampleNameProvider : IProfilerSampleNameProvider
    {
        public readonly static SimpleSampleNameProvider Instance = new SimpleSampleNameProvider();

        string IProfilerSampleNameProvider.GetItemName(HierarchyFrameDataView frameData, int itemId)
        {
            return frameData.GetItemName(itemId);
        }

        string IProfilerSampleNameProvider.GetMarkerName(HierarchyFrameDataView frameData, int markerId)
        {
            return frameData.GetMarkerName(markerId);
        }

        string IProfilerSampleNameProvider.GetItemName(RawFrameDataView frameData, int itemId)
        {
            return frameData.GetSampleName(itemId);
        }
    }

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

        private IProfilerWindowController m_profilerWindow;

        private bool m_Initialized;

        [SerializeField]
        private int m_FrameIndex = FrameDataView.invalidOrCurrentFrameIndex;

        private HierarchyFrameDataView m_frameDataView;

        [SerializeField]
        TreeViewState m_TreeViewState;

        [SerializeField]
        MultiColumnHeaderState m_MultiColumnHeaderState;


        static readonly GUIContent[] kCPUProfilerViewTypeNames = new GUIContent[]
        {
            EditorGUIUtility.TrTextContent("Hierarchy"),
            EditorGUIUtility.TrTextContent("Raw Hierarchy")
        };

        static readonly int[] kCPUProfilerViewTypes = new int[]
        {
            (int)ProfilerViewType.Hierarchy,
            (int)ProfilerViewType.RawHierarchy
        };

        public event Action<ProfilerViewType> OnChangeViewType = viewType => { };
        public event Action<bool> OnToggleLive = on => { };

        protected void DrawViewTypePopup(ProfilerViewType viewType)
        {
            var newViewType = (ProfilerViewType)EditorGUILayout.IntPopup((int)viewType, kCPUProfilerViewTypeNames, kCPUProfilerViewTypes, BaseStyles.viewTypeToolbarDropDown, GUILayout.Width(BaseStyles.viewTypeToolbarDropDown.fixedWidth));

            if (newViewType != viewType)
            {
                OnChangeViewType.Invoke(newViewType);
                GUIUtility.ExitGUI();
            }
        }

        protected void DrawLiveUpdateToggle(bool updateViewLive)
        {
            using (new EditorGUI.DisabledScope(ProcessService.level != ProcessLevel.Main))
            {
                // This button is only needed in the Master Process
                var newUpdateViewLive = GUILayout.Toggle(updateViewLive, BaseStyles.updateLive, EditorStyles.toolbarButton);

                if (newUpdateViewLive != updateViewLive)
                {
                    OnToggleLive.Invoke(updateViewLive);
                }
            }
        }
        SearchField m_SearchField;
        ProfilerFrameDataTreeView m_TreeView;

        public ProfilerFrameDataTreeView treeView
        {
            get
            {
                InitIfNeeded();
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

        readonly string k_SerializationPrefKeyPrefix;
        string multiColumnHeaderStatePrefKey => k_SerializationPrefKeyPrefix + "MultiColumnHeaderState";

        public ProfilerFrameDataHierarchyView(string serializationPrefKeyPrefix)
        {
            m_Initialized = false;
            k_SerializationPrefKeyPrefix = serializationPrefKeyPrefix;
        }

        void InitIfNeeded()
        {
            if (m_Initialized)
                return;

            var cpuHierarchyColumns = new[]
            {
                HierarchyFrameDataView.columnName,
                HierarchyFrameDataView.columnTotalPercent,
                HierarchyFrameDataView.columnSelfPercent,
                HierarchyFrameDataView.columnCalls,
                HierarchyFrameDataView.columnGcMemory,
                HierarchyFrameDataView.columnTotalTime,
                HierarchyFrameDataView.columnSelfTime,
                HierarchyFrameDataView.columnWarningCount
            };

            var profilerColumns = cpuHierarchyColumns;
            var defaultSortColumn = HierarchyFrameDataView.columnTotalTime;

            var columns = CreateColumns(profilerColumns);

            var multiColumnHeaderStateData = SessionState.GetString(multiColumnHeaderStatePrefKey, "");
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
            var headerState = CreateDefaultMultiColumnHeaderState(columns, defaultSortColumn);
            if (MultiColumnHeaderState.CanOverwriteSerializedFields(m_MultiColumnHeaderState, headerState))
                MultiColumnHeaderState.OverwriteSerializedFields(m_MultiColumnHeaderState, headerState);

            var firstInit = m_MultiColumnHeaderState == null;
            m_MultiColumnHeaderState = headerState;

            var multiColumnHeader = new ProfilerFrameDataMultiColumnHeader(m_MultiColumnHeaderState, columns) { height = 25 };
            if (firstInit)
                multiColumnHeader.ResizeToFit();

            multiColumnHeader.visibleColumnsChanged += OnMultiColumnHeaderChanged;
            multiColumnHeader.sortingChanged += OnMultiColumnHeaderChanged;

            // Check if it already exists (deserialized from window layout file or scriptable object)
            if (m_TreeViewState == null)
                m_TreeViewState = new TreeViewState();
            m_TreeView = new ProfilerFrameDataTreeView(m_TreeViewState, multiColumnHeader, SimpleSampleNameProvider.Instance, m_profilerWindow);
            m_TreeView.selectionChanged += OnMainTreeViewSelectionChanged;
            m_TreeView.searchChanged += OnMainTreeViewSearchChanged;
            m_TreeView.Reload();

            m_SearchField = new SearchField();
            m_SearchField.downOrUpArrowKeyPressed += m_TreeView.SetFocusAndEnsureSelectedItem;

            m_Initialized = true;
        }

        public void OnEnable(IProfilerWindowController profilerWindow)
        {
            m_profilerWindow = profilerWindow;
            m_FrameIndex = FrameDataView.invalidOrCurrentFrameIndex;
            profilerWindow.frameDataViewAboutToBeDisposed += OnFrameDataViewAboutToBeDisposed;
        }

        void OnFrameDataViewAboutToBeDisposed()
        {
            m_TreeView?.OnFrameDataViewAboutToBeDisposed();
        }

        void OnMultiColumnHeaderChanged(MultiColumnHeader header)
        {
            SessionState.SetString(multiColumnHeaderStatePrefKey, JsonUtility.ToJson(header.state));
        }

        public static ProfilerFrameDataMultiColumnHeader.Column[] CreateColumns(int[] profilerColumns)
        {
            var columns = new ProfilerFrameDataMultiColumnHeader.Column[profilerColumns.Length];
            for (var i = 0; i < profilerColumns.Length; ++i)
            {
                var columnName = GetProfilerColumnName(profilerColumns[i]);
                var content = profilerColumns[i] == HierarchyFrameDataView.columnWarningCount
                    ? EditorGUIUtility.IconContent("ProfilerColumn.WarningCount", columnName)
                    : new GUIContent(columnName);
                var column = new ProfilerFrameDataMultiColumnHeader.Column
                {
                    profilerColumn = profilerColumns[i],
                    headerLabel = content
                };
                columns[i] = column;
            }

            return columns;
        }

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
                    return LocalizationDatabase.GetLocalizedString("|Warnings");
                case HierarchyFrameDataView.columnObjectName:
                    return LocalizationDatabase.GetLocalizedString("Object Name");
                case HierarchyFrameDataView.columnStartTime:
                    return LocalizationDatabase.GetLocalizedString("Start ms");
                default:
                    return "ProfilerColumn." + column;
            }
        }

        // Open hyperlinks directing users to specific settings in the Player Settings window.
        static void EditorGUI_OpenSettingsOnHyperlinkClicked(EditorWindow window, HyperLinkClickedEventArgs args)
        {
            if (window.GetType() == typeof(ProfilerWindow) && args.hyperLinkData.ContainsKey("playersettingslink"))
            {
                var settings = SettingsWindow.Show(SettingsScope.Project, "Project/Player");
                if (settings == null)
                {
                    Debug.LogError("Could not find Preferences for 'Project/Player'");
                }
                else
                {
                    string linkString;
                    args.hyperLinkData.TryGetValue("playersettingssearchstring", out linkString);

                    settings.FilterProviders(linkString);
                }
            }
        }

        public void DoGUI(HierarchyFrameDataView frameDataView, bool isLive, ProfilerViewType viewType)
        {
            InitIfNeeded();

            var isDataAvailable = frameDataView != null && frameDataView.valid;

            // Hierarchy view area
            GUILayout.BeginVertical();

            DrawToolbar(frameDataView, isLive, viewType);

            if (!isDataAvailable)
            {
                if (!isLive)
                    GUILayout.Label(BaseStyles.liveUpdateMessage, BaseStyles.label);
                else
                    GUILayout.Label(BaseStyles.noData, BaseStyles.label);
            }
            else
            {
                var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandHeight(true), GUILayout.ExpandHeight(true));

                m_TreeView.SetFrameDataView(frameDataView);
                m_TreeView.OnGUI(rect, isLive);
            }

            GUILayout.EndVertical();


            HandleKeyboardEvents();
        }

        void DrawSearchBar()
        {
            var rect = GUILayoutUtility.GetRect(50f, 300f, EditorGUI.kSingleLineHeight, EditorGUI.kSingleLineHeight, EditorStyles.toolbarSearchField);
            treeView.searchString = m_SearchField.OnToolbarGUI(rect, treeView.searchString);
        }

        void DrawToolbar(HierarchyFrameDataView frameDataView, bool updateViewLive, ProfilerViewType viewType)
        {
            EditorGUILayout.BeginHorizontal(BaseStyles.toolbar);

            DrawViewTypePopup(viewType);

            DrawLiveUpdateToggle(updateViewLive);

            GUILayout.FlexibleSpace();

            if (frameDataView != null && frameDataView.valid)
            {
                var cpuTimeMs = frameDataView.frameTimeMs;
                var cpuTime = cpuTimeMs > 0 ? $"{cpuTimeMs.ToString("0:N2")}" : "--";
                GUILayout.Label($"CPU:{cpuTime}ms", EditorStyles.toolbarLabel);
            }

            GUILayout.FlexibleSpace();

            DrawSearchBar();

            EditorGUILayout.EndHorizontal();
        }

        class ThreadInfo : IComparable<ThreadInfo>, IEquatable<ThreadInfo>
        {
            public int groupOrder;
            public string fullName;
            public int threadIndex;

            public int CompareTo(ThreadInfo other)
            {
                if (this == other)
                    return 0;
                if (groupOrder != other.groupOrder)
                    return groupOrder - other.groupOrder;
                return EditorUtility.NaturalCompare(fullName, other.fullName);
            }

            public static bool operator ==(ThreadInfo lhs, ThreadInfo rhs) => ReferenceEquals(lhs, null) && ReferenceEquals(rhs, null) || !ReferenceEquals(lhs, null) && lhs.Equals(rhs);
            public static bool operator !=(ThreadInfo lhs, ThreadInfo rhs) => ReferenceEquals(lhs, null) ? !ReferenceEquals(rhs, null) : !lhs.Equals(rhs);
            public override int GetHashCode() => (fullName.GetHashCode() * 21 + groupOrder.GetHashCode()) * 21 + threadIndex.GetHashCode();
            public override bool Equals(object other) => other is ThreadInfo && Equals((ThreadInfo)other);
            public bool Equals(ThreadInfo other) => !ReferenceEquals(other, null) && (ReferenceEquals(this, other) || (groupOrder == other.groupOrder && fullName == other.fullName && threadIndex == other.threadIndex));
        }

        class ProfilerThreadSelectionDropdown : AdvancedDropdown
        {
            class ProfilerThreadSelectionDropDownItem : AdvancedDropdownItem
            {
                public ThreadInfo info;
                public ProfilerThreadSelectionDropDownItem(ThreadInfo info) : base(info.fullName)
                {
                    this.info = info;
                    id = info.GetHashCode();
                }
            }

            int m_SelectedThreadIndexInListOfThreadNames;
            List<ThreadInfo> m_ThreadInfos;
            Action<ThreadInfo, int> m_ItemSelectedCallback;
            public ProfilerThreadSelectionDropdown(List<ThreadInfo> threads, int selectedThreadIndexInListOfThreadNames, Action<ThreadInfo, int> itemSelected) : base(new AdvancedDropdownState())
            {
                m_ThreadInfos = threads;
                m_SelectedThreadIndexInListOfThreadNames = selectedThreadIndexInListOfThreadNames;
                m_ItemSelectedCallback = itemSelected;
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                AdvancedDropdownItem root = null;
                if (m_ThreadInfos != null && m_ThreadInfos.Count > 0)
                {
                    if (m_ThreadInfos.Count < m_SelectedThreadIndexInListOfThreadNames || m_SelectedThreadIndexInListOfThreadNames < 0)
                        m_SelectedThreadIndexInListOfThreadNames = 0;

                    root = new ProfilerThreadSelectionDropDownItem(m_ThreadInfos[m_SelectedThreadIndexInListOfThreadNames]);
                    for (int i = 0; i < m_ThreadInfos.Count; i++)
                    {
                        var child = new ProfilerThreadSelectionDropDownItem(m_ThreadInfos[i]);
                        child.elementIndex = i;
                        root.AddChild(child);

                        if (m_SelectedThreadIndexInListOfThreadNames == i)
                            m_DataSource.selectedIDs.Add(child.id);
                    }
                }
                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                base.ItemSelected(item);
                m_ItemSelectedCallback?.Invoke((item as ProfilerThreadSelectionDropDownItem).info, item.elementIndex);
            }
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
            InitIfNeeded();
            m_TreeView.SetSelection(selection, expandSelection);
        }

        public void ClearSelection()
        {
            InitIfNeeded();
            m_TreeView.ClearSelection();
        }

        public void SetFrameDataView(HierarchyFrameDataView frameDataView)
        {
            InitIfNeeded();

            m_TreeView.SetFrameDataView(frameDataView);
            m_frameDataView = frameDataView;
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
