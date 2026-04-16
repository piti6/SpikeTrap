using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor.Profiling;
using UnityEditorInternal.Profiling;
using UnityEngine;

namespace SpikeTrap.Editor
{
    /// <summary>
    /// Clean-room marker / raw-sample path utilities used by hierarchy tree and selection APIs.
    /// Replaces Unity internal <c>ProfilerTimelineGUI</c> static helpers for this project.
    /// </summary>
    internal static class MarkerPathTraversal
    {
        struct RawSampleIterationInfo
        {
            public int partOfThePath;
            public int lastSampleIndexInScope;
        }

        static RawSampleIterationInfo[] s_SkippedScopesCache = new RawSampleIterationInfo[1024];
        static int[] s_LastSampleInScopeOfThePathCache = new int[1024];
        static List<int> s_SampleIndexPathCache = new List<int>(1024);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static List<int> GetCachedSampleIndexPath(int requiredCapacity)
        {
            if (s_SampleIndexPathCache.Capacity < requiredCapacity)
                s_SampleIndexPathCache.Capacity = requiredCapacity;
            s_SampleIndexPathCache.Clear();
            return s_SampleIndexPathCache;
        }

        public static int FindFirstSampleThroughMarkerPath(
            RawFrameDataView iterator,
            IProfilerSampleNameProvider profilerSampleNameProvider,
            IList<int> markerIdPathToMatch,
            int pathLength,
            ref string outName,
            List<int> longestMatchingPath = null)
        {
            var sampleIndexPath = GetCachedSampleIndexPath(pathLength);
            return FindNextSampleThroughMarkerPath(
                iterator,
                profilerSampleNameProvider,
                markerIdPathToMatch,
                pathLength,
                ref outName,
                ref sampleIndexPath,
                longestMatchingPath: longestMatchingPath);
        }

        public static int GetItemMarkerIdPath(
            RawFrameDataView iterator,
            IProfilerSampleNameProvider profilerSampleNameProvider,
            int rawSampleIndex,
            ref string outName,
            ref List<int> markerIdPath)
        {
            var unreachableDepth = iterator.maxDepth + 1;
            var sampleIndexPath = GetCachedSampleIndexPath(unreachableDepth);
            var sampleIdx = FindNextSampleThroughMarkerPath(
                iterator,
                profilerSampleNameProvider,
                markerIdPathToMatch: null,
                unreachableDepth,
                ref outName,
                ref sampleIndexPath,
                specificRawSampleIndexToFind: rawSampleIndex);

            if (sampleIdx != RawFrameDataView.invalidSampleIndex)
            {
                for (int i = 0; i < sampleIndexPath.Count; i++)
                    markerIdPath.Add(iterator.GetSampleMarkerId(sampleIndexPath[i]));
            }

            return sampleIdx;
        }

        public static int FindNextSampleThroughMarkerPath(
            RawFrameDataView iterator,
            IProfilerSampleNameProvider profilerSampleNameProvider,
            IList<int> markerIdPathToMatch,
            int pathLength,
            ref string outName,
            ref List<int> sampleIndexPath,
            List<int> longestMatchingPath = null,
            int specificRawSampleIndexToFind = RawFrameDataView.invalidSampleIndex,
            Func<int, int, RawFrameDataView, bool> sampleIdFitsMarkerPathIndex = null)
        {
            var partOfThePath = sampleIndexPath.Count > 0 ? sampleIndexPath.Count - 1 : 0;
            var sampleIndex = partOfThePath == 0
                ? 1
                : sampleIndexPath[partOfThePath] + iterator.GetSampleChildrenCountRecursive(sampleIndexPath[partOfThePath]) + 1;
            var foundSample = false;
            if (sampleIndexPath.Capacity < pathLength + 1)
                sampleIndexPath.Capacity = pathLength + 1;
            if (s_LastSampleInScopeOfThePathCache.Length < sampleIndexPath.Capacity)
                s_LastSampleInScopeOfThePathCache = new int[sampleIndexPath.Capacity];
            var lastSampleInScopeOfThePath = s_LastSampleInScopeOfThePathCache;
            var lastSampleInScopeOfThePathCount = 0;
            var lastSampleInScope = partOfThePath == 0 ? iterator.sampleCount - 1 : sampleIndex + iterator.GetSampleChildrenCountRecursive(sampleIndex);
            var allowProxySelection = longestMatchingPath != null;
            Debug.Assert(!allowProxySelection || longestMatchingPath.Count <= 0, $"{nameof(longestMatchingPath)} should be empty");
            var longestContiguousMarkerPathMatch = 0;
            var currentlyLongestContiguousMarkerPathMatch = 0;

            if (allowProxySelection && s_SkippedScopesCache.Length < sampleIndexPath.Capacity)
                s_SkippedScopesCache = new RawSampleIterationInfo[sampleIndexPath.Capacity];

            var skippedScopes = s_SkippedScopesCache;
            var skippedScopesCount = 0;
            while (sampleIndex <= lastSampleInScope && partOfThePath < pathLength &&
                   (specificRawSampleIndexToFind <= 0 || sampleIndex <= specificRawSampleIndexToFind))
            {
                if (markerIdPathToMatch == null ||
                    markerIdPathToMatch[partOfThePath + skippedScopesCount] == iterator.GetSampleMarkerId(sampleIndex) ||
                    (sampleIdFitsMarkerPathIndex != null &&
                     sampleIdFitsMarkerPathIndex(sampleIndex, partOfThePath + skippedScopesCount, iterator)))
                {
                    if ((specificRawSampleIndexToFind >= 0 && sampleIndex == specificRawSampleIndexToFind) ||
                        (specificRawSampleIndexToFind < 0 && partOfThePath == pathLength - 1))
                    {
                        foundSample = true;
                        break;
                    }

                    sampleIndexPath.Add(sampleIndex);
                    lastSampleInScopeOfThePath[lastSampleInScopeOfThePathCount++] =
                        sampleIndex + iterator.GetSampleChildrenCountRecursive(sampleIndex);
                    ++sampleIndex;
                    ++partOfThePath;
                    if (skippedScopesCount <= 0)
                        currentlyLongestContiguousMarkerPathMatch = partOfThePath;

                    if (partOfThePath + skippedScopesCount >= pathLength)
                    {
                        if (longestMatchingPath != null &&
                            longestContiguousMarkerPathMatch <= currentlyLongestContiguousMarkerPathMatch &&
                            longestMatchingPath.Count < sampleIndexPath.Count)
                        {
                            longestMatchingPath.Clear();
                            longestMatchingPath.AddRange(sampleIndexPath);
                            longestContiguousMarkerPathMatch = currentlyLongestContiguousMarkerPathMatch;
                        }

                        if (skippedScopesCount > 0)
                        {
                            sampleIndex = lastSampleInScopeOfThePath[--lastSampleInScopeOfThePathCount] + 1;
                            sampleIndexPath.RemoveAt(--partOfThePath);
                        }
                        else
                            break;
                    }
                }
                else if (allowProxySelection && partOfThePath + skippedScopesCount < pathLength - 1 &&
                         longestContiguousMarkerPathMatch <= currentlyLongestContiguousMarkerPathMatch)
                {
                    skippedScopes[skippedScopesCount++] = new RawSampleIterationInfo
                    {
                        partOfThePath = partOfThePath,
                        lastSampleIndexInScope = sampleIndex + iterator.GetSampleChildrenCountRecursive(sampleIndex)
                    };
                }
                else
                {
                    sampleIndex += 1 + iterator.GetSampleChildrenCountRecursive(sampleIndex);
                }

                while (lastSampleInScopeOfThePathCount > 0 &&
                       sampleIndex > lastSampleInScopeOfThePath[lastSampleInScopeOfThePathCount - 1] ||
                       allowProxySelection && skippedScopesCount > 0 &&
                       sampleIndex > skippedScopes[skippedScopesCount - 1].lastSampleIndexInScope)
                {
                    if (skippedScopesCount > 0 && skippedScopes[skippedScopesCount - 1].partOfThePath >= partOfThePath)
                    {
                        sampleIndex = skippedScopes[--skippedScopesCount].lastSampleIndexInScope + 1;
                    }
                    else
                    {
                        if (longestMatchingPath != null &&
                            longestContiguousMarkerPathMatch <= currentlyLongestContiguousMarkerPathMatch &&
                            longestMatchingPath.Count < sampleIndexPath.Count)
                        {
                            longestMatchingPath.Clear();
                            longestMatchingPath.AddRange(sampleIndexPath);
                            longestContiguousMarkerPathMatch = currentlyLongestContiguousMarkerPathMatch;
                        }

                        sampleIndexPath.RemoveAt(--partOfThePath);
                        if (skippedScopesCount <= 0)
                            currentlyLongestContiguousMarkerPathMatch = partOfThePath;
                        sampleIndex = lastSampleInScopeOfThePath[--lastSampleInScopeOfThePathCount] + 1;
                    }
                }
            }

            if (foundSample)
            {
                if (string.IsNullOrEmpty(outName))
                    outName = profilerSampleNameProvider.GetItemName(iterator, sampleIndex);
                sampleIndexPath.Add(sampleIndex);
                if (longestMatchingPath != null)
                {
                    longestMatchingPath.Clear();
                    longestMatchingPath.AddRange(sampleIndexPath);
                }

                return sampleIndex;
            }

            return RawFrameDataView.invalidSampleIndex;
        }
    }
}
