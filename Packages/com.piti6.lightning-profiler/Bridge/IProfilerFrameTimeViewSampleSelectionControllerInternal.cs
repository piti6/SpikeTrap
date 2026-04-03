using System.Collections.Generic;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace LightningProfiler
{
    /// <summary>
    /// Internal hooks used by <see cref="ProfilerEditorUtility"/> for programmatic sample selection.
    /// </summary>
    internal interface IProfilerFrameTimeViewSampleSelectionControllerInternal
    {
        int FindMarkerPathAndRawSampleIndexToFirstMatchingSampleInCurrentView(
            int frameIndex,
            int threadIndex,
            string sampleName,
            out List<int> markerIdPath,
            string markerNamePath = null);

        int FindMarkerPathAndRawSampleIndexToFirstMatchingSampleInCurrentView(
            int frameIndex,
            int threadIndex,
            ref string sampleName,
            ref List<int> markerIdPath,
            int sampleMarkerId);

        void SetSelectionWithoutIntegrityChecks(ProfilerTimeSampleSelection selectionToSet, List<int> markerIdPath);

        IProfilerWindowController profilerWindow { get; }

        int GetActiveVisibleFrameIndexOrLatestFrameForSettingTheSelection();
    }
}
