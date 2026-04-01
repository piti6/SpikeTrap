using System;
using Unity.Profiling.Editor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine.Profiling;

namespace LightningProfiler
{
    internal interface IProfilerWindowController
    {
        long selectedFrameIndex { get; set; }
        ProfilerModule selectedModule { get; set; }
        ProfilerModule GetProfilerModuleByType(Type type);

        event Action frameDataViewAboutToBeDisposed;
        event Action<int, bool> currentFrameChanged;

        void SetClearOnPlay(bool enabled);
        bool GetClearOnPlay();

        HierarchyFrameDataView GetFrameDataView(string groupName, string threadName, ulong threadId, HierarchyFrameDataView.ViewModes viewMode, int profilerSortColumn, bool sortAscending);
        HierarchyFrameDataView GetFrameDataView(int threadIndex, HierarchyFrameDataView.ViewModes viewMode, int profilerSortColumn, bool sortAscending);

        bool IsRecording();
        bool ProfilerWindowOverheadIsAffectingProfilingRecordingData();

        string ConnectedTargetName { get; }
        bool ConnectedToEditor { get; }

        ProfilerProperty CreateProperty();
        ProfilerProperty CreateProperty(int sortType);

        void CloseModule(ProfilerModule module);
        void Repaint();
    }
}
