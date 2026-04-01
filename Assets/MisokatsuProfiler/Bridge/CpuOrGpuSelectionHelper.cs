using System;
using Unity.Profiling;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;

namespace LightningProfiler
{
    /// <summary>
    /// Selection integrity checks for frame/time sample APIs (clean-room replacement for Unity internal static helpers).
    /// </summary>
    internal static class CpuOrGpuSelectionHelper
    {
        internal static readonly ProfilerMarker SetSelectionIntegrityCheckMarker =
            new ProfilerMarker($"{nameof(CpuOrGpuSelectionHelper)}.SetSelection.IntegrityCheck");

        internal static readonly ProfilerMarker SetSelectionApplyMarker =
            new ProfilerMarker($"{nameof(CpuOrGpuSelectionHelper)}.SetSelection.Apply");

        internal static int IntegrityCheckFrameAndThreadDataOfSelection(
            long frameIndex,
            string threadGroupName,
            string threadName,
            ref ulong threadId)
        {
            if (string.IsNullOrEmpty(threadName))
                throw new ArgumentException($"{nameof(threadName)} can't be null or empty.");

            if (ProfilerDriver.firstFrameIndex == FrameDataView.invalidOrCurrentFrameIndex)
                throw new Exception("No frame data is loaded, so there's no data to select from.");

            if (frameIndex > ProfilerDriver.lastFrameIndex || frameIndex < ProfilerDriver.firstFrameIndex)
                throw new ArgumentOutOfRangeException(nameof(frameIndex));

            var threadIndex = FrameDataView.invalidThreadIndex;
            using (var frameView = new ProfilerFrameDataIterator())
            {
                var threadCount = frameView.GetThreadCount((int)frameIndex);
                if (threadGroupName == null)
                    threadGroupName = string.Empty;
                threadIndex = ProfilerTimeSampleSelection.GetThreadIndex((int)frameIndex, threadGroupName, threadName, threadId);
                if (threadIndex < 0 || threadIndex >= threadCount)
                    throw new ArgumentException(
                        $"A Thread named: \"{threadName}\" in group \"{threadGroupName}\" could not be found in frame {frameIndex}");
                using (var frameData = ProfilerDriver.GetRawFrameDataView((int)frameIndex, threadIndex))
                {
                    if (threadId != FrameDataView.invalidThreadId && frameData.threadId != threadId)
                        throw new ArgumentException(
                            $"A Thread named: \"{threadName}\" in group \"{threadGroupName}\" was found in frame {frameIndex}, but its thread id {frameData.threadId} did not match the provided {threadId}");
                    threadId = frameData.threadId;
                }
            }

            return threadIndex;
        }
    }
}
