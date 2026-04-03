using System.Collections.Generic;
using Unity.Profiling.LowLevel;
using UnityEditor.Profiling;
using UnityEditorInternal.Profiling;
using UnityEngine;

namespace LightningProfiler
{
    /// <summary>
    /// Hierarchy-view marker path resolution for programmatic selection (clean-room).
    /// </summary>
    internal static class CpuUsageHierarchyMarkerSearch
    {
        struct HierarchySampleIterationInfo
        {
            public int sampleId;
            public int sampleDepth;
        }

        public static int FindMarkerPathAndRawSampleIndex(
            HierarchyFrameDataView frameData,
            IProfilerSampleNameProvider nameProvider,
            ref string sampleName,
            ref List<int> markerIdPath,
            string markerNamePath,
            int sampleMarkerId)
        {
            if (frameData == null || !frameData.valid)
            {
                markerIdPath = new List<int>();
                return RawFrameDataView.invalidSampleIndex;
            }

            var sampleIdPath = new List<int>();
            var children = new List<int>();
            frameData.GetItemChildren(frameData.GetRootItemID(), children);
            var yetToVisit = new Stack<HierarchySampleIterationInfo>();
            var rawIds = new List<int>();
            int foundSampleIndex = RawFrameDataView.invalidSampleIndex;

            if (sampleMarkerId == FrameDataView.invalidMarkerId)
                sampleMarkerId = frameData.GetMarkerId(sampleName);

            if (markerIdPath != null && markerIdPath.Count > 0)
            {
                int enclosingScopeId = FindNextMatchingSampleIdInScope(frameData, nameProvider, null, sampleIdPath, children, yetToVisit, false, markerIdPath[sampleIdPath.Count]);
                while (enclosingScopeId != RawFrameDataView.invalidSampleIndex && sampleIdPath.Count <= markerIdPath.Count)
                {
                    if (sampleIdPath.Count == markerIdPath.Count)
                    {
                        var sampleId = enclosingScopeId;
                        if ((sampleMarkerId != FrameDataView.invalidMarkerId && sampleMarkerId != markerIdPath[sampleIdPath.Count - 1]) ||
                            (sampleName != null && frameData.GetMarkerName(markerIdPath[sampleIdPath.Count - 1]) != sampleName))
                        {
                            if (sampleMarkerId == FrameDataView.invalidMarkerId)
                                sampleId = FindNextMatchingSampleIdInScope(frameData, nameProvider, sampleName, sampleIdPath, children, yetToVisit, true);
                            else
                                sampleId = FindNextMatchingSampleIdInScope(frameData, nameProvider, null, sampleIdPath, children, yetToVisit, true, sampleMarkerId);
                        }

                        if (sampleId != RawFrameDataView.invalidSampleIndex)
                        {
                            foundSampleIndex = sampleId;
                            for (int i = markerIdPath.Count; i < sampleIdPath.Count; i++)
                                markerIdPath.Add(frameData.GetItemMarkerID(sampleIdPath[i]));
                            break;
                        }

                        while (sampleIdPath.Count >= markerIdPath.Count && sampleIdPath.Count > 0)
                            sampleIdPath.RemoveAt(sampleIdPath.Count - 1);
                    }

                    enclosingScopeId = FindNextMatchingSampleIdInScope(frameData, nameProvider, null, sampleIdPath, children, yetToVisit, false, markerIdPath[sampleIdPath.Count]);
                }
            }
            else if (!string.IsNullOrEmpty(markerNamePath))
            {
                var path = markerNamePath.Split('/');
                if (path != null && path.Length > 0)
                {
                    int enclosingScopeId = FindNextMatchingSampleIdInScope(frameData, nameProvider, path[sampleIdPath.Count], sampleIdPath, children, yetToVisit, false);
                    while (enclosingScopeId != RawFrameDataView.invalidSampleIndex && sampleIdPath.Count <= path.Length)
                    {
                        if (sampleIdPath.Count == path.Length)
                        {
                            var sampleId = enclosingScopeId;
                            if (path[sampleIdPath.Count - 1] != sampleName)
                                sampleId = FindNextMatchingSampleIdInScope(frameData, nameProvider, sampleName, sampleIdPath, children, yetToVisit, true);
                            if (sampleId != RawFrameDataView.invalidSampleIndex)
                            {
                                foundSampleIndex = sampleId;
                                break;
                            }

                            while (sampleIdPath.Count >= path.Length && sampleIdPath.Count > 0)
                                sampleIdPath.RemoveAt(sampleIdPath.Count - 1);
                        }

                        enclosingScopeId = FindNextMatchingSampleIdInScope(frameData, nameProvider, path[sampleIdPath.Count], sampleIdPath, children, yetToVisit, false);
                    }
                }
            }
            else
            {
                if (sampleMarkerId == FrameDataView.invalidMarkerId)
                    foundSampleIndex = FindNextMatchingSampleIdInScope(frameData, nameProvider, sampleName, sampleIdPath, children, yetToVisit, true);
                else
                    foundSampleIndex = FindNextMatchingSampleIdInScope(frameData, nameProvider, null, sampleIdPath, children, yetToVisit, true, sampleMarkerId);
            }

            if (foundSampleIndex != RawFrameDataView.invalidSampleIndex)
            {
                if (string.IsNullOrEmpty(sampleName))
                    sampleName = nameProvider.GetItemName(frameData, foundSampleIndex);
                if (markerIdPath == null)
                    markerIdPath = new List<int>();
                if (markerIdPath.Count == 0)
                    ProfilerTimeSampleSelection.GetCleanMarkerIdsFromSampleIds(frameData, sampleIdPath, markerIdPath);
                frameData.GetItemRawFrameDataViewIndices(foundSampleIndex, rawIds);
                Debug.Assert(rawIds.Count > 0, "Frame data is Invalid");
                return rawIds[0];
            }

            markerIdPath = new List<int>();
            return RawFrameDataView.invalidSampleIndex;
        }

        static int FindNextMatchingSampleIdInScope(
            HierarchyFrameDataView frameData,
            IProfilerSampleNameProvider nameProvider,
            string sampleName,
            List<int> sampleIdPath,
            List<int> children,
            Stack<HierarchySampleIterationInfo> yetToVisit,
            bool searchRecursively,
            int markerId = FrameDataView.invalidMarkerId)
        {
            if (markerId == FrameDataView.invalidMarkerId)
                markerId = frameData.GetMarkerId(sampleName);
            if (children.Count > 0)
            {
                for (int i = children.Count - 1; i >= 0; i--)
                    yetToVisit.Push(new HierarchySampleIterationInfo { sampleId = children[i], sampleDepth = sampleIdPath.Count });
                children.Clear();
            }

            while (yetToVisit.Count > 0)
            {
                var sample = yetToVisit.Pop();
                int higherlevelScopeSampleToReturnTo = RawFrameDataView.invalidSampleIndex;
                while (sample.sampleDepth < sampleIdPath.Count && sampleIdPath.Count > 0)
                {
                    higherlevelScopeSampleToReturnTo = sampleIdPath[sampleIdPath.Count - 1];
                    sampleIdPath.RemoveAt(sampleIdPath.Count - 1);
                }

                if (!searchRecursively && higherlevelScopeSampleToReturnTo >= 0)
                {
                    yetToVisit.Push(sample);
                    return higherlevelScopeSampleToReturnTo;
                }

                var isEditorOnlySample = (frameData.GetItemMarkerFlags(sample.sampleId) & MarkerFlags.AvailabilityEditor) != 0;
                var itemName = nameProvider.GetItemName(frameData, sample.sampleId);
                var editorOnlyMatch = isEditorOnlySample && (
                    (sampleName != null && itemName.Contains(sampleName))
                    || (sampleName == null && itemName.Contains(frameData.GetMarkerName(markerId))));
                var nameEqualsWhenUnscoped = markerId == FrameDataView.invalidMarkerId && itemName == sampleName;
                var markerIdMatch = markerId == frameData.GetItemMarkerID(sample.sampleId);
                var found = editorOnlyMatch || nameEqualsWhenUnscoped || markerIdMatch;
                if (found || searchRecursively)
                {
                    sampleIdPath.Add(sample.sampleId);
                    frameData.GetItemChildren(sample.sampleId, children);
                    for (int i = children.Count - 1; i >= 0; i--)
                        yetToVisit.Push(new HierarchySampleIterationInfo { sampleId = children[i], sampleDepth = sampleIdPath.Count });
                    children.Clear();
                    if (found)
                        return sample.sampleId;
                }
            }

            return RawFrameDataView.invalidSampleIndex;
        }
    }
}
