using PlasticGui;
using System;
using System.Linq;
using Unity.Profiling;
using Unity.Profiling.Editor;
using UnityEditor;
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

    private void OnGUI(Rect obj)
    {
        m_currentFrameIndex = (int)ProfilerWindow.selectedFrameIndex;
        var frameDataView = GetFrameDataView(m_currentSelectedThreadGroupName, m_currentThreadName, m_currentThreadId);
        m_FrameDataHierarchyView.DoGUI(frameDataView, ref updateViewLive, m_viewType);
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
}
