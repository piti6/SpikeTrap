#region 어셈블리 UnityEditor.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// 위치를 알 수 없음
// Decompiled with ICSharpCode.Decompiler 6.1.0.5902
#endregion

using System;
using System.Collections.Generic;
using UnityEditor.Profiling;
using UnityEditorInternal.Profiling;

namespace LightningProfiler
{
    //
    // 요약:
    //     A Utility class for Profiler tooling in the Unity Editor.
    public static class ProfilerEditorUtility
    {
        internal static int GetActiveVisibleFrameIndex(this IProfilerWindowController controller)
        {
            return (int)controller.selectedFrameIndex;
        }

        internal static void SetActiveVisibleFrameIndex(this IProfilerWindowController controller, int frame)
        {
            controller.selectedFrameIndex = frame;
        }

        internal static T GetProfilerModuleByType<T>(this IProfilerWindowController controller) where T : ProfilerModule
        {
            return controller.GetProfilerModuleByType(typeof(T)) as T;
        }

        //
        // 요약:
        //     Set the current selection in a frame time sample based Profiler Module, such
        //     as the.
        //
        // 매개 변수:
        //   controller:
        //     The controller object of the Profiler module whose selection you want to set.
        //     When the value is null, Unity throws a NullArgumentException.
        //
        //   frameIndex:
        //     The 0 based frame index. Note that the Profiler Window UI shows the frame index
        //     as n+1. When this value is outside of the range described by ProfilerWindow.firstAvailableFrameIndex
        //     and ProfilerWindow.lastAvailableFrameIndex, or smaller than 0, Unity throws an
        //     ArgumentOutOfRangeException.
        //
        //   threadGroupName:
        //     The name of the thread group. Null or an empty string signify that the thread
        //     isn't part of a thread group. "Job", "Loading" and "Scripting Threads" are examples
        //     of such thread group names.
        //
        //   threadName:
        //     The Name of the thread, e.g. "Main Thread", "Render Thread" or "Worker 0". When
        //     this value is null or an empty string, Unity throws an ArgumentException.
        //
        //   sampleName:
        //     The name of the sample to select. If Unity cannot find a sample that matches
        //     this name, it does not set a selection and this method returns false. When this
        //     value is null or an empty string, Unity throws an ArgumentNullException or ArgumentException
        //     respectively.
        //
        //   markerNamePath:
        //     The names of all samples in the sample stack, each separated by a , that define
        //     the base path for the search. Similar to a file folder structure, this base path
        //     defines where Unity looks for a sample which matches the sampleName. The searched
        //     sampleName can be the last item in that marker path or any child sample of it.
        //     Do not add a trailing . If no sample can be found matching this sample stack
        //     path and the sampleName, no selection is set and this method returns false. This
        //     defaults to null which means no requirement is set on the sample's sample stack
        //     and the first sample fitting the sampleName is selected.
        //
        //   threadId:
        //     The ID of the thread. When the default value of FrameDataView.invalidThreadId
        //     is passed, Unity searches for the sample in the first thread matching the provided
        //     threadGroupName and threadName. Specify this threadId if there are multiple threads
        //     with the same name. Use a RawFrameDataView.threadId or HierarchyFrameDataView.threadId
        //     to retrieve the ID to a specific thread, if you need it to be specific.
        //
        //   sampleMarkerId:
        //     Use HierarchyFrameDataView or RawFrameDataView to get the Marker Ids. When no
        //     sample can be found matching this sample stack path and the sampleMarkerId, no
        //     selection is set and this method returns false.
        //
        //   markerIdPath:
        //     A list of Profiler marker IDs for all samples in the sample stack, that define
        //     the base path for the search. Similar to a file folder structure, this base path
        //     defines where Unity looks for a sample which matches the sampleMarkerId. The
        //     searched sampleMarkerId can be the last item in that marker path or any child
        //     sample of it. If no sample can be found matching this sample stack path and the
        //     sampleMarkerId, no selection is set and this method returns false. This defaults
        //     to null which means no requirement is set on the sample's sample stack and the
        //     first sample fitting the sampleMarkerId is selected.
        //
        // 반환 값:
        //     Returns true if the selection was successfully set, false if it was rejected
        //     because no fitting sample could be found.
        public static bool SetSelection(this IProfilerFrameTimeViewSampleSelectionController controller, long frameIndex, string threadGroupName, string threadName, string sampleName, string markerNamePath = null, ulong threadId = 0uL)
        {
            IProfilerFrameTimeViewSampleSelectionControllerInternal profilerFrameTimeViewSampleSelectionControllerInternal = controller as IProfilerFrameTimeViewSampleSelectionControllerInternal;
            if (controller == null || profilerFrameTimeViewSampleSelectionControllerInternal == null)
            {
                throw new ArgumentNullException("controller", "The IProfilerFrameTimeViewSampleSelectionController you are setting a selection on can't be null.");
            }

            List<int> markerIdPath;
            ProfilerTimeSampleSelection profilerTimeSampleSelection;
            using (CPUOrGPUProfilerModule.setSelectionIntegrityCheckMarker.Auto())
            {
                if (string.IsNullOrEmpty(sampleName))
                {
                    throw new ArgumentException("sampleName can't be null or empty. Hint: To clear a selection, use ClearSelection instead.");
                }

                int threadIndex = CPUOrGPUProfilerModule.IntegrityCheckFrameAndThreadDataOfSelection(frameIndex, threadGroupName, threadName, ref threadId);
                int num = profilerFrameTimeViewSampleSelectionControllerInternal.FindMarkerPathAndRawSampleIndexToFirstMatchingSampleInCurrentView((int)frameIndex, threadIndex, sampleName, out markerIdPath, markerNamePath);
                if (num < 0)
                {
                    return false;
                }

                profilerTimeSampleSelection = new ProfilerTimeSampleSelection(frameIndex, threadGroupName, threadName, threadId, num, sampleName);
            }

            using (CPUOrGPUProfilerModule.setSelectionApplyMarker.Auto())
            {
                profilerTimeSampleSelection.frameIndexIsSafe = true;
                profilerFrameTimeViewSampleSelectionControllerInternal.SetSelectionWithoutIntegrityChecks(profilerTimeSampleSelection, markerIdPath);
                return true;
            }
        }

        public static bool SetSelection(this IProfilerFrameTimeViewSampleSelectionController controller, long frameIndex, string threadGroupName, string threadName, int sampleMarkerId, List<int> markerIdPath = null, ulong threadId = 0uL)
        {
            IProfilerFrameTimeViewSampleSelectionControllerInternal profilerFrameTimeViewSampleSelectionControllerInternal = controller as IProfilerFrameTimeViewSampleSelectionControllerInternal;
            if (controller == null || profilerFrameTimeViewSampleSelectionControllerInternal == null)
            {
                throw new ArgumentNullException("controller", "The IProfilerFrameTimeViewSampleSelectionController you are setting a selection on can't be null.");
            }

            ProfilerTimeSampleSelection profilerTimeSampleSelection;
            using (CPUOrGPUProfilerModule.setSelectionIntegrityCheckMarker.Auto())
            {
                if (sampleMarkerId == -1)
                {
                    throw new ArgumentException(string.Format("{0} can't invalid ({1}). Hint: To clear a selection, use {2} instead.", "sampleMarkerId", -1, "ClearSelection"));
                }

                int threadIndex = CPUOrGPUProfilerModule.IntegrityCheckFrameAndThreadDataOfSelection(frameIndex, threadGroupName, threadName, ref threadId);
                string sampleName = null;
                int num = profilerFrameTimeViewSampleSelectionControllerInternal.FindMarkerPathAndRawSampleIndexToFirstMatchingSampleInCurrentView((int)frameIndex, threadIndex, ref sampleName, ref markerIdPath, sampleMarkerId);
                if (num < 0)
                {
                    return false;
                }

                profilerTimeSampleSelection = new ProfilerTimeSampleSelection(frameIndex, threadGroupName, threadName, threadId, num, sampleName);
            }

            using (CPUOrGPUProfilerModule.setSelectionApplyMarker.Auto())
            {
                profilerTimeSampleSelection.frameIndexIsSafe = true;
                profilerFrameTimeViewSampleSelectionControllerInternal.SetSelectionWithoutIntegrityChecks(profilerTimeSampleSelection, markerIdPath);
                return true;
            }
        }

        //
        // 요약:
        //     Set the current selection in a frame time sample based Profiler Module, such
        //     as the.
        //
        // 매개 변수:
        //   controller:
        //     The controller object of the Profiler module whose selection you want to set.
        //     When the value is null, Unity throws a NullArgumentException.
        //
        //   markerNameOrMarkerNamePath:
        //     The name of the sample to be selected, or the names of all samples in the sample
        //     stack. Separate each name with a , ending on the sample that should be selected.
        //     Do not add a trailing . If Unity cannot find a sample that matches this name
        //     or sample stack, it does not set a selection and this method returns false. When
        //     this value is null or an empty string, Unity throws an ArgumentException.
        //
        //   frameIndex:
        //     The 0 based frame index. This value defaults to -1 which means the selection
        //     is set on the currently shown frame. Note that the Profiler Window UI shows the
        //     frame index as n+1. When this value is outside of the range described by ProfilerWindow.firstAvailableFrameIndex
        //     and ProfilerWindow.lastAvailableFrameIndex, or not -1, Unity throws an ArgumentOutOfRangeException.
        //
        //   threadGroupName:
        //     The name of the thread group. The parameter defaults to an empty string. Null
        //     or an empty string signify that the thread isn't part of a thread group. "Job",
        //     "Loading" and "Scripting Threads" are examples of such thread group names.
        //
        //   threadName:
        //     The Name of the thread, e.g. "Main Thread", "Render Thread" or "Worker 0". This
        //     parameter defaults to "Main Thread". When this value is null or an empty string,
        //     Unity throws an ArgumentException.
        //
        //   threadId:
        //     The ID of the thread. When the default value of FrameDataView.invalidThreadId
        //     is passed, Unity searches for the sample in the first thread matching the provided
        //     threadGroupName and threadName. Specify this threadId if there are multiple threads
        //     with the same name. Use a RawFrameDataView.threadId or HierarchyFrameDataView.threadId
        //     to retrieve the ID to a specific thread, if you need it to be specific.
        //
        // 반환 값:
        //     Returns true if the selection was successfully set, false if it was rejected
        //     because no fitting sample could be found.
        public static bool SetSelection(this IProfilerFrameTimeViewSampleSelectionController controller, string markerNameOrMarkerNamePath, long frameIndex = -1L, string threadGroupName = "", string threadName = "Main Thread", ulong threadId = 0uL)
        {
            IProfilerFrameTimeViewSampleSelectionControllerInternal profilerFrameTimeViewSampleSelectionControllerInternal = controller as IProfilerFrameTimeViewSampleSelectionControllerInternal;
            if (controller == null || profilerFrameTimeViewSampleSelectionControllerInternal == null)
            {
                throw new ArgumentNullException("controller", "The IProfilerFrameTimeViewSampleSelectionController you are setting a selection on can't be null.");
            }

            List<int> markerIdPath;
            ProfilerTimeSampleSelection profilerTimeSampleSelection;
            using (CPUOrGPUProfilerModule.setSelectionIntegrityCheckMarker.Auto())
            {
                if (string.IsNullOrEmpty(markerNameOrMarkerNamePath))
                {
                    throw new ArgumentException("markerNameOrMarkerNamePath can't be null or empty. Hint: To clear a selection, use ClearSelection instead.");
                }

                if (frameIndex == -1)
                {
                    frameIndex = profilerFrameTimeViewSampleSelectionControllerInternal.GetActiveVisibleFrameIndexOrLatestFrameForSettingTheSelection();
                }

                int num = CPUOrGPUProfilerModule.IntegrityCheckFrameAndThreadDataOfSelection(frameIndex, threadGroupName, threadName, ref threadId);
                int num2 = markerNameOrMarkerNamePath.LastIndexOf('/');
                string sampleName = (num2 == -1) ? markerNameOrMarkerNamePath : markerNameOrMarkerNamePath.Substring(num2 + 1, markerNameOrMarkerNamePath.Length - (num2 + 1));
                if (num2 == -1)
                {
                    markerNameOrMarkerNamePath = null;
                }

                int num3 = profilerFrameTimeViewSampleSelectionControllerInternal.FindMarkerPathAndRawSampleIndexToFirstMatchingSampleInCurrentView((int)frameIndex, 0, sampleName, out markerIdPath, markerNameOrMarkerNamePath);
                if (num3 < 0)
                {
                    return false;
                }

                profilerTimeSampleSelection = new ProfilerTimeSampleSelection(frameIndex, threadGroupName, threadName, threadId, num3, sampleName);
            }

            using (CPUOrGPUProfilerModule.setSelectionApplyMarker.Auto())
            {
                profilerTimeSampleSelection.frameIndexIsSafe = true;
                profilerFrameTimeViewSampleSelectionControllerInternal.SetSelectionWithoutIntegrityChecks(profilerTimeSampleSelection, markerIdPath);
                return true;
            }
        }
    }
}
