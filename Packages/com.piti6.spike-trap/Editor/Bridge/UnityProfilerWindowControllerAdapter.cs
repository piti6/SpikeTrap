using System;
using Unity.Profiling.Editor;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace SpikeTrap.Editor
{
    internal sealed class UnityProfilerWindowControllerAdapter
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
}
