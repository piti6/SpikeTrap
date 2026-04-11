using System.Collections.Generic;

namespace LightningProfiler
{
    /// <summary>
    /// Managed frame data extracted once from native profiler APIs.
    /// Thread-safe and reusable — no native references.
    /// </summary>
    public readonly struct CachedFrameData
    {
        /// <summary>Frame index.</summary>
        public readonly int FrameIndex;

        /// <summary>Effective CPU time in ms (total frame time minus EditorLoop if editor session).</summary>
        public readonly float EffectiveTimeMs;

        /// <summary>Total GC.Alloc bytes in the frame (excluding EditorLoop samples if editor session).</summary>
        public readonly long GcAllocBytes;

        /// <summary>Set of unique profiler marker IDs present in this frame.</summary>
        public readonly IReadOnlyCollection<int> UniqueMarkerIds;

        public CachedFrameData(int frameIndex, float effectiveTimeMs, long gcAllocBytes, IReadOnlyCollection<int> uniqueMarkerIds)
        {
            FrameIndex = frameIndex;
            EffectiveTimeMs = effectiveTimeMs;
            GcAllocBytes = gcAllocBytes;
            UniqueMarkerIds = uniqueMarkerIds;
        }
    }
}
