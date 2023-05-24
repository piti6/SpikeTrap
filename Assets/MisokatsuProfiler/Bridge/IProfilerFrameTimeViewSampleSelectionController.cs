using System;
using UnityEditor.Profiling;

namespace LightningProfiler
{
    public interface IProfilerFrameTimeViewSampleSelectionController
    {
        //
        // 요약:
        //     Get the current selection in a frame time sample based.
        ProfilerTimeSampleSelection selection
        {
            get;
        }

        //
        // 요약:
        //     This filters the samples displayed in Hierarchy view to only include the names
        //     that include this string.
        string sampleNameSearchFilter
        {
            get;
            set;
        }

        //
        // 요약:
        //     The index of the the thread selected to be displayed in the.
        int focusedThreadIndex
        {
            get;
            set;
        }

        event Action<IProfilerFrameTimeViewSampleSelectionController, ProfilerTimeSampleSelection> selectionChanged;

        //
        // 요약:
        //     Set the current selection in a frame time sample based Profiler Module, such
        //     as the.
        //
        // 매개 변수:
        //   selection:
        //     A fully described selection created via a the ProfilerTimeSampleSelection constructor
        //     or previously retrieved via ProfilerWindow.selection.
        //
        // 반환 값:
        //     Returns true if the selection was successfully set, false if it was rejected
        //     because no fitting sample could be found.
        bool SetSelection(ProfilerTimeSampleSelection selection);

        //
        // 요약:
        //     Call this method to clear the current selection in this frame time view based.
        void ClearSelection();
    }
}
