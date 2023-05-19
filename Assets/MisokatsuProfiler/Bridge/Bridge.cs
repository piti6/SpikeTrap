using PlasticGui;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Profiling;
using Unity.Profiling.Editor;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEditorInternal.Profiling;
using UnityEngine;
using UnityEngine.UIElements;

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

    public MisokatsuProfilerModule() : base(CPUNames.Select(x => new ProfilerCounterDescriptor(x, ProfilerCategory.Scripts.Name)).ToArray()) { }
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

    internal override void OnEnable()
    {
        base.OnEnable();
        if (m_FrameDataHierarchyView == null)
            m_FrameDataHierarchyView = new ProfilerFrameDataHierarchyView("Profiler.CPUProfilerModule.HierarchyView.");

        m_viewType = (ProfilerViewType)EditorPrefs.GetInt("Profiler.CPUProfilerModule.ViewType", (int)ProfilerViewType.Hierarchy);

        m_FrameDataHierarchyView.OnEnable(this, ProfilerWindow);

        m_FrameDataHierarchyView.OnThreadSelectionChange -= OnThreadSelectionChange;
        m_FrameDataHierarchyView.OnThreadSelectionChange += OnThreadSelectionChange;

        void OnThreadSelectionChange()
        {
            ProfilerWindow.Repaint();
        }

        m_FrameDataHierarchyView.OnChangeViewType -= OnChangeViewType;
        m_FrameDataHierarchyView.OnChangeViewType += OnChangeViewType;

        void OnChangeViewType(ProfilerViewType viewtype)
        {
            if (m_viewType == viewtype)
                return;

            m_viewType = viewtype;

            ApplySelection(true, true);
        }

        m_FrameDataHierarchyView.OnToggleLive -= OnToggleLive;
        m_FrameDataHierarchyView.OnToggleLive += OnToggleLive;

        void OnToggleLive(bool isLive)
        {
            m_isLive = isLive;
        }

        m_FrameDataHierarchyView.selectionChanged -= SetSelectionWithoutIntegrityChecksOnSelectionChangeInDetailedView;
        m_FrameDataHierarchyView.selectionChanged += SetSelectionWithoutIntegrityChecksOnSelectionChangeInDetailedView;
        m_FrameDataHierarchyView.userChangedThread -= ThreadSelectionInHierarchyViewChanged;
        m_FrameDataHierarchyView.userChangedThread += ThreadSelectionInHierarchyViewChanged;
        if (!string.IsNullOrEmpty(sampleNameSearchFilter))
            m_FrameDataHierarchyView.treeView.searchString = sampleNameSearchFilter;
        m_FrameDataHierarchyView.searchChanged -= SearchFilterInHierarchyViewChanged;
        m_FrameDataHierarchyView.searchChanged += SearchFilterInHierarchyViewChanged;
        ProfilerDriver.profileLoaded -= ProfileLoaded;
        ProfilerDriver.profileLoaded += ProfileLoaded;
        ProfilerDriver.profileCleared -= ProfileCleared;
        ProfilerDriver.profileCleared += ProfileCleared;

        m_ViewType = (ProfilerViewType)EditorPrefs.GetInt(ViewTypeSettingsKey, (int)DefaultViewTypeSetting);
        m_ProfilerViewFilteringOptions = SessionState.GetInt(ProfilerViewFilteringOptionsKey, m_ProfilerViewFilteringOptions);
    }

    private void OnGUI(Rect obj)
    {
        m_currentFrameIndex = (int)ProfilerWindow.selectedFrameIndex;
        var frameDataView = GetFrameDataView(m_currentSelectedThreadGroupName, m_currentThreadName, m_currentThreadId);
        m_FrameDataHierarchyView.DoGUI(frameDataView, m_isLive, m_viewType);
    }

    private HierarchyFrameDataView GetFrameDataView(string threadGroupName, string threadName, ulong threadId)
    {
        var viewMode = HierarchyFrameDataView.ViewModes.Default;
        if (m_viewType == ProfilerViewType.Hierarchy)
            viewMode |= HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName;
        //return ProfilerWindow.GetFrameDataView(threadIndex, viewMode | GetFilteringMode(), m_FrameDataHierarchyView.sortedProfilerColumn, m_FrameDataHierarchyView.sortedProfilerColumnAscending);
        return ProfilerWindow.GetFrameDataView(threadGroupName, threadName, threadId, viewMode, HierarchyFrameDataView.columnDontSort, false);
    }

    private HierarchyFrameDataView GetFrameDataView(int threadIndex)
    {
        var viewMode = HierarchyFrameDataView.ViewModes.Default;
        if (m_viewType == ProfilerViewType.Hierarchy)
            viewMode |= HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName;
        //return ProfilerWindow.GetFrameDataView(threadIndex, viewMode | GetFilteringMode(), m_FrameDataHierarchyView.sortedProfilerColumn, m_FrameDataHierarchyView.sortedProfilerColumnAscending);
        return ProfilerWindow.GetFrameDataView(threadIndex, viewMode, HierarchyFrameDataView.columnDontSort, false);
    }

    private void ApplySelection(bool viewChanged, bool frameSelection)
    {
        if (selection != null)
        {
            using (k_ApplyValidSelectionMarker.Auto())
            {
                var currentFrame = ProfilerWindow.selectedFrameIndex;
                if (selection.frameIndexIsSafe && selection.safeFrameIndex == currentFrame)
                {
                    var treeViewID = ProfilerFrameDataHierarchyView.invalidTreeViewId;
                    if (fetchData)
                    {
                        var frameDataView = m_HierarchyOverruledThreadFromSelection ? GetFrameDataView() : GetFrameDataView(selection.threadGroupName, selection.threadName, selection.threadId);
                        // avoid Selection Migration happening twice during SetFrameDataView by clearing the old one out first
                        m_FrameDataHierarchyView.ClearSelection();
                        m_FrameDataHierarchyView.SetFrameDataView(frameDataView);
                        if (!frameDataView.valid)
                            return;

                        // GetItemIDFromRawFrameDataViewIndex is a bit expensive so only use that if showing the Raw view (where the raw id is relevant)
                        // or when the cheaper option (setting selection via MarkerIdPath) isn't available
                        if (ViewType == ProfilerViewType.RawHierarchy || (selection.markerPathDepth <= 0))
                        {
                            treeViewID = m_FrameDataHierarchyView.treeView.GetItemIDFromRawFrameDataViewIndex(frameDataView, selection.rawSampleIndex, selection.markerIdPath);
                        }
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
                    if (fetchData)
                    {
                        var frameDataView = m_HierarchyOverruledThreadFromSelection ? GetFrameDataView() : GetFrameDataView(selection.threadGroupName, selection.threadName, selection.threadId);
                        if (!frameDataView.valid)
                            return;
                        // avoid Selection Migration happening twice during SetFrameDataView by clearing the old one out first
                        m_FrameDataHierarchyView.ClearSelection();
                        m_FrameDataHierarchyView.SetFrameDataView(frameDataView);
                    }
                    m_FrameDataHierarchyView.SetSelection(selection, (viewChanged || frameSelection));
                }
                // else: the selection was not in the shown frame AND there was no other frame to select it in or the Selection contains no marker path.
                // So either there is no data to apply the selection to, or the selection isn't one that can be applied to another frame because there is no path
                // either way, it is save to not Apply the selection.
            }
        }
        else
        {
            using (k_ApplySelectionClearMarker.Auto())
            {
                m_FrameDataHierarchyView.ClearSelection();
            }
        }
    }
}
