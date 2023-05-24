using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Profiling;
using Unity.Profiling.Editor;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightningProfiler
{
    internal class MisokatsuProfilerViewController : ProfilerModuleViewController
    {
        public event Action<Rect> OnGUI;

        public MisokatsuProfilerViewController(ProfilerWindow profilerWindow) : base(profilerWindow) { }

        protected override VisualElement CreateView()
        {
            IMGUIContainer iMGUIContainer = new IMGUIContainer(DrawDetailsViewViaLegacyIMGUIMethods);
            iMGUIContainer.style.flexGrow = 1f;
            return iMGUIContainer;
        }

        private void DrawDetailsViewViaLegacyIMGUIMethods()
        {
            VisualElement detailsViewContainer = base.ProfilerWindow.DetailsViewContainer;
            IResolvedStyle resolvedStyle = detailsViewContainer.resolvedStyle;
            Rect rect = new Rect(0f, 0f, resolvedStyle.width, resolvedStyle.height);
            rect.yMin += EditorStyles.contentToolbar.CalcHeight(GUIContent.none, 10f);

            OnGUI?.Invoke(rect);
        }
    }

    [ProfilerModuleMetadata("Misokatsu CPU", IconPath = "Profiler.CPU")]
    public class MisokatsuProfilerModule : ProfilerModule
    {
        [SerializeField]
        private ProfilerViewType m_viewType = ProfilerViewType.Hierarchy;
        [SerializeField]
        private int m_currentFrameIndex = FrameDataView.invalidOrCurrentFrameIndex;
        [SerializeField]
        private ulong m_currentThreadId;
        [SerializeField]
        private int m_currentThreadIndex;
        [SerializeField]
        private string m_currentThreadName;
        [SerializeField]
        string m_currentSelectedThreadGroupName;
        [SerializeField]
        private bool m_isLive;
        [SerializeField]
        private ProfilerFrameDataHierarchyView m_FrameDataHierarchyView;

        private static readonly string[] CPUNames = new string[]
        {
            "Rendering",
            "Scripts",
            "Physics",
            "Animation",
            "GarbageCollector",
            "VSync",
            "Global Illumination",
            "UI",
            "Others"
        };

        public MisokatsuProfilerModule() : base(CPUNames.Select(x => new ProfilerCounterDescriptor(x, ProfilerCategory.Scripts.Name)).ToArray(), ProfilerModuleChartType.StackedTimeArea) { }
        public MisokatsuProfilerModule
            (ProfilerCounterDescriptor[] chartCounters, ProfilerModuleChartType defaultChartType = ProfilerModuleChartType.Line, string[] autoEnabledCategoryNames = null) : base(chartCounters, defaultChartType, autoEnabledCategoryNames)
        {
        }

        public override ProfilerModuleViewController CreateDetailsViewController()
        {
            var viewController = new MisokatsuProfilerViewController(ProfilerWindow);

            viewController.OnGUI += OnGUI;

            return viewController;
        }

        internal override void Clear()
        {
            base.Clear();

            m_currentFrameIndex = FrameDataView.invalidOrCurrentFrameIndex;
            m_FrameDataHierarchyView?.Clear();
        }

        internal override void OnEnable()
        {
            base.OnEnable();

            if (m_FrameDataHierarchyView == null)
                m_FrameDataHierarchyView = new ProfilerFrameDataHierarchyView();

            m_FrameDataHierarchyView.OnEnable(ProfilerWindow);

            m_FrameDataHierarchyView.OnChangeViewType -= OnChangeViewType;
            m_FrameDataHierarchyView.OnChangeViewType += OnChangeViewType;

            m_FrameDataHierarchyView.OnToggleLive -= OnToggleLive;
            m_FrameDataHierarchyView.OnToggleLive += OnToggleLive;

            m_FrameDataHierarchyView.selectionChanged -= SetSelectionWithoutIntegrityChecksOnSelectionChangeInDetailedView;
            m_FrameDataHierarchyView.selectionChanged += SetSelectionWithoutIntegrityChecksOnSelectionChangeInDetailedView;

            ProfilerDriver.profileLoaded -= ProfileLoaded;
            ProfilerDriver.profileLoaded += ProfileLoaded;
            ProfilerDriver.profileCleared -= ProfileCleared;
            ProfilerDriver.profileCleared += ProfileCleared;

            m_viewType = (ProfilerViewType)EditorPrefs.GetInt("Profiler.CPUProfilerModule.ViewType", (int)ProfilerViewType.Hierarchy);
        }

        private void OnChangeViewType(ProfilerViewType viewtype)
        {
            if (m_viewType == viewtype)
                return;

            m_viewType = viewtype;

            ApplySelection(true, true);
        }

        private void OnToggleLive(bool isLive)
        {
            m_isLive = isLive;
        }

        internal override void OnDisable()
        {
            base.OnDisable();

            if (m_FrameDataHierarchyView != null)
            {
                m_FrameDataHierarchyView.OnDisable();
                m_FrameDataHierarchyView.OnChangeViewType -= OnChangeViewType;
                m_FrameDataHierarchyView.OnToggleLive -= OnToggleLive;
                m_FrameDataHierarchyView.selectionChanged -= SetSelectionWithoutIntegrityChecksOnSelectionChangeInDetailedView;
            }

            ProfilerDriver.profileLoaded -= ProfileLoaded;
            ProfilerDriver.profileCleared -= ProfileCleared;
            Clear();
        }

        protected void TryRestoringSelection()
        {
            if (selection != null)
            {
                // check that the selection is still valid and wasn't badly deserialized on Domain Reload
                if (selection.markerPathDepth <= 0 || selection.rawSampleIndices == null)
                {
                    m_selection = null;
                    return;
                }

                if (ProfilerDriver.firstFrameIndex >= 0 && ProfilerDriver.lastFrameIndex >= 0)
                {
                    ApplySelection(true, true);
                }
                SetSelectedPropertyPath(selection.legacyMarkerPath);
            }
        }

        void ProfileLoaded()
        {
            if (selection != null)
                selection.frameIndexIsSafe = false;
            Clear();
            TryRestoringSelection();
        }

        void ProfileCleared()
        {
            if (selection != null)
                selection.frameIndexIsSafe = false;
            Clear();
        }

        protected void SetSelectionWithoutIntegrityChecksOnSelectionChangeInDetailedView(ProfilerTimeSampleSelection selection)
        {
            if (selection == null)
            {
                ClearSelection();
                return;
            }
            // trust the internal views to provide a correct frame index
            selection.frameIndexIsSafe = true;
            SetSelectionWithoutIntegrityChecks(selection, null);
        }

        public void ClearSelection()
        {
            SetSelectionWithoutIntegrityChecks(null, null);
            ApplySelection(false, false);
        }

        protected void SetSelectionWithoutIntegrityChecks(ProfilerTimeSampleSelection selectionToSet, List<int> markerIdPath)
        {
            if (selectionToSet != null)
            {
                if (selectionToSet.safeFrameIndex != ProfilerWindow.selectedFrameIndex)
                    ProfilerWindow.SetActiveVisibleFrameIndex(selectionToSet.safeFrameIndex != FrameDataView.invalidOrCurrentFrameIndex ? (int)selectionToSet.safeFrameIndex : ProfilerDriver.lastFrameIndex);
                if (string.IsNullOrEmpty(selectionToSet.legacyMarkerPath))
                {
                    var frameDataView = GetFrameDataView(selectionToSet.threadGroupName, selectionToSet.threadName, selectionToSet.threadId);
                    if (frameDataView == null || !frameDataView.valid)
                        return;
                    selectionToSet.GenerateMarkerNamePath(frameDataView, markerIdPath);
                }
                selection = selectionToSet;
                SetSelectedPropertyPath(selectionToSet.legacyMarkerPath);
            }
            else
            {
                selection = null;
                ClearSelectedPropertyPath();
            }
        }

        private void SetSelectedPropertyPath(string path)
        {
            if (ProfilerDriver.selectedPropertyPath != path)
            {
                ProfilerDriver.selectedPropertyPath = path;
                Update();
            }
        }

        private void ClearSelectedPropertyPath()
        {
            if (ProfilerDriver.selectedPropertyPath != string.Empty)
            {
                ProfilerDriver.selectedPropertyPath = string.Empty;
                Update();
            }
        }

        ProfilerTimeSampleSelection m_selection;
        public ProfilerTimeSampleSelection selection
        {
            get { return m_selection; }
            private set
            {
                if (m_selection != null)
                {
                    m_selection.frameIndexIsSafe = false;
                }

                m_selection = value;
            }
        }

        private void OnGUI(Rect obj)
        {
            m_currentFrameIndex = (int)ProfilerWindow.selectedFrameIndex;
            var frameDataView = GetFrameDataView(m_currentSelectedThreadGroupName, m_currentThreadName, m_currentThreadId);
            m_FrameDataHierarchyView.DoGUI(frameDataView, m_isLive, m_viewType);
        }

        private HierarchyFrameDataView GetFrameDataView(string threadGroupName, string threadName, ulong threadId)
        {
            UnityEngine.Profiling.Profiler.BeginSample("ccc");
            var viewMode = HierarchyFrameDataView.ViewModes.Default;
            if (m_viewType == ProfilerViewType.Hierarchy)
                viewMode |= HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName;
            //return ProfilerWindow.GetFrameDataView(threadIndex, viewMode | GetFilteringMode(), m_FrameDataHierarchyView.sortedProfilerColumn, m_FrameDataHierarchyView.sortedProfilerColumnAscending);
            var frameDataView = ProfilerWindow.GetFrameDataView(threadGroupName, threadName, threadId, viewMode, HierarchyFrameDataView.columnDontSort, false);
            UnityEngine.Profiling.Profiler.EndSample();
            return frameDataView;
        }

        private void ApplySelection(bool viewChanged, bool frameSelection)
        {
            if (selection != null)
            {
                var currentFrame = ProfilerWindow.selectedFrameIndex;
                if (selection.frameIndexIsSafe && selection.safeFrameIndex == currentFrame)
                {
                    var treeViewID = ProfilerFrameDataHierarchyView.invalidTreeViewId;
                    var frameDataView = GetFrameDataView(selection.threadGroupName, selection.threadName, selection.threadId);
                    // avoid Selection Migration happening twice during SetFrameDataView by clearing the old one out first
                    m_FrameDataHierarchyView.ClearSelection();
                    m_FrameDataHierarchyView.SetFrameDataView(frameDataView);
                    if (!frameDataView.valid)
                        return;

                    // GetItemIDFromRawFrameDataViewIndex is a bit expensive so only use that if showing the Raw view (where the raw id is relevant)
                    // or when the cheaper option (setting selection via MarkerIdPath) isn't available
                    if (m_viewType == ProfilerViewType.RawHierarchy || (selection.markerPathDepth <= 0))
                    {
                        treeViewID = m_FrameDataHierarchyView.treeView.GetItemIDFromRawFrameDataViewIndex(frameDataView, selection.rawSampleIndex, selection.markerIdPath);
                    }

                    if (treeViewID == ProfilerFrameDataHierarchyView.invalidTreeViewId)
                    {
                        if (selection.markerPathDepth > 0)
                        {
                            m_FrameDataHierarchyView.SetSelection(selection, viewChanged || frameSelection);
                        }
                    }
                    else
                    {
                        var ids = new List<int>()
                                {
                                    treeViewID
                                };
                        m_FrameDataHierarchyView.treeView.SetSelection(ids, TreeViewSelectionOptions.RevealAndFrame);
                    }
                }
                else if (currentFrame >= 0 && selection.markerPathDepth > 0)
                {
                    var frameDataView = GetFrameDataView(selection.threadGroupName, selection.threadName, selection.threadId);
                    if (!frameDataView.valid)
                        return;
                    // avoid Selection Migration happening twice during SetFrameDataView by clearing the old one out first
                    m_FrameDataHierarchyView.ClearSelection();
                    m_FrameDataHierarchyView.SetFrameDataView(frameDataView);
                    m_FrameDataHierarchyView.SetSelection(selection, (viewChanged || frameSelection));
                }
                // else: the selection was not in the shown frame AND there was no other frame to select it in or the Selection contains no marker path.
                // So either there is no data to apply the selection to, or the selection isn't one that can be applied to another frame because there is no path
                // either way, it is save to not Apply the selection.
            }
            else
            {
                m_FrameDataHierarchyView.ClearSelection();
            }
        }
    }
}