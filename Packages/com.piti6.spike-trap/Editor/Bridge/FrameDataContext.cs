using System.Collections.Generic;

namespace SpikeTrap
{
    /// <summary>
    /// Time attributed to a single profiler marker within a frame.
    /// </summary>
    public readonly struct MarkerTimeSample
    {
        public readonly int MarkerId;
        public readonly float TimeMs;

        public MarkerTimeSample(int markerId, float timeMs)
        {
            MarkerId = markerId;
            TimeMs = timeMs;
        }
    }

    /// <summary>
    /// Managed frame data extracted once from native profiler APIs.
    /// Thread-safe and reusable — no native references.
    /// </summary>
    public readonly struct CachedFrameData
    {
        /// <summary>Effective CPU time in ms (total frame time minus EditorLoop if editor session).</summary>
        public readonly float EffectiveTimeMs;

        /// <summary>Total GC.Alloc bytes in the frame (excluding EditorLoop samples if editor session).</summary>
        public readonly long GcAllocBytes;

        /// <summary>Set of unique profiler marker IDs present in this frame.</summary>
        public readonly IReadOnlyCollection<int> UniqueMarkerIds;

        /// <summary>Top-N costliest markers by inclusive time, sorted descending. Null if not collected.</summary>
        public readonly IReadOnlyList<MarkerTimeSample> TopMarkers;

        public CachedFrameData(float effectiveTimeMs, long gcAllocBytes, IReadOnlyCollection<int> uniqueMarkerIds)
        {
            EffectiveTimeMs = effectiveTimeMs;
            GcAllocBytes = gcAllocBytes;
            UniqueMarkerIds = uniqueMarkerIds;
            TopMarkers = null;
        }

        public CachedFrameData(float effectiveTimeMs, long gcAllocBytes, IReadOnlyCollection<int> uniqueMarkerIds,
            IReadOnlyList<MarkerTimeSample> topMarkers)
        {
            EffectiveTimeMs = effectiveTimeMs;
            GcAllocBytes = gcAllocBytes;
            UniqueMarkerIds = uniqueMarkerIds;
            TopMarkers = topMarkers;
        }
    }
}
