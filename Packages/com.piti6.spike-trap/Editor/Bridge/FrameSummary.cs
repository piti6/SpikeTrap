using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpikeTrap.Editor
{
    /// <summary>
    /// Resolved frame summary for external consumption (AI analysis, scripting).
    /// All marker IDs are resolved to names — no further lookups needed.
    /// </summary>
    public readonly struct FrameSummary
    {
        public readonly int FrameIndex;
        public readonly int SessionId;
        public readonly float EffectiveTimeMs;
        public readonly long GcAllocBytes;
        /// <summary>Top markers sorted by inclusive time descending. Null if not available.</summary>
        public readonly string[] TopMarkerNames;
        /// <summary>Corresponding times for <see cref="TopMarkerNames"/>.</summary>
        public readonly float[] TopMarkerTimesMs;

        public FrameSummary(int frameIndex, int sessionId, float effectiveTimeMs, long gcAllocBytes,
            string[] topMarkerNames, float[] topMarkerTimesMs)
        {
            FrameIndex = frameIndex;
            SessionId = sessionId;
            EffectiveTimeMs = effectiveTimeMs;
            GcAllocBytes = gcAllocBytes;
            TopMarkerNames = topMarkerNames;
            TopMarkerTimesMs = topMarkerTimesMs;
        }

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"Frame {FrameIndex}: {EffectiveTimeMs:F2}ms, GC {GcAllocBytes / 1024f:F1}KB");
            if (TopMarkerNames != null && TopMarkerNames.Length > 0)
            {
                sb.Append(" | ");
                for (int i = 0; i < TopMarkerNames.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append($"{TopMarkerNames[i]}={TopMarkerTimesMs[i]:F2}ms");
                }
            }
            return sb.ToString();
        }
    }
}
