using System;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace LightningProfiler
{
    internal interface IProfilerWindowController
    {
        long selectedFrameIndex
        {
            get;
            set;
        }

        ProfilerModule selectedModule
        {
            get;
            set;
        }

        string ConnectedTargetName
        {
            get;
        }

        bool ConnectedToEditor
        {
            get;
        }

        event Action frameDataViewAboutToBeDisposed;

        event Action<int, bool> currentFrameChanged;

        ProfilerModule GetProfilerModuleByType(Type T);

        void SetClearOnPlay(bool enabled);

        bool GetClearOnPlay();

        HierarchyFrameDataView GetFrameDataView(string groupName, string threadName, ulong threadId, HierarchyFrameDataView.ViewModes viewMode, int profilerSortColumn, bool sortAscending);

        HierarchyFrameDataView GetFrameDataView(int threadIndex, HierarchyFrameDataView.ViewModes viewMode, int profilerSortColumn, bool sortAscending);

        bool IsRecording();

        bool ProfilerWindowOverheadIsAffectingProfilingRecordingData();

        ProfilerProperty CreateProperty();

        ProfilerProperty CreateProperty(int sortType);

        void CloseModule(ProfilerModule module);

        void Repaint();
    }
}
