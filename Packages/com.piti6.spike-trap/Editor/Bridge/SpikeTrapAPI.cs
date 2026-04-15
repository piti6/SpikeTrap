using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpikeTrap
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

    /// <summary>
    /// Public static API for programmatic profiling automation.
    /// Designed for use via dynamic code execution (uLoop MCP) or editor scripts.
    /// All methods must be called on the main thread (Unity API constraint).
    /// </summary>
    public static class SpikeTrapAPI
    {
        /// <summary>Is the Profiler module view currently active?</summary>
        public static bool IsViewActive => CpuUsageBridgeDetailsViewController.s_ActiveInstance != null;

        /// <summary>Is the controller currently in Collect mode?</summary>
        public static bool IsCollecting
        {
            get
            {
                var inst = CpuUsageBridgeDetailsViewController.s_ActiveInstance;
                return inst != null && inst.IsCollectingMarkedFrames;
            }
        }

        /// <summary>
        /// Start collecting frames matching filters.
        /// Optionally set spike threshold and/or GC threshold before starting.
        /// </summary>
        /// <param name="spikeThresholdMs">Spike filter threshold in ms. 0 = disabled.</param>
        /// <param name="gcThresholdKB">GC alloc filter threshold in KB. 0 = disabled.</param>
        /// <returns>True if collecting started successfully.</returns>
        public static bool StartCollecting(float spikeThresholdMs = 0f, float gcThresholdKB = 0f)
        {
            var inst = CpuUsageBridgeDetailsViewController.s_ActiveInstance;
            if (inst == null)
            {
                Debug.LogWarning("[SpikeTrapAPI] No active profiler module view. Open the Profiler window first.");
                return false;
            }

            if (spikeThresholdMs > 0f)
                inst.SpikeFilter?.SetThresholdMs(spikeThresholdMs);

            if (gcThresholdKB > 0f)
                inst.GcFilter?.SetThresholdKB(gcThresholdKB);

            inst.MarkFiltersDirty();
            inst.StartCollectingInternal();
            return true;
        }

        /// <summary>
        /// Update filter thresholds while collecting (or any time).
        /// Pass 0 to disable a filter. Pass -1 to leave unchanged.
        /// </summary>
        public static bool SetFilterThresholds(float spikeThresholdMs = -1f, float gcThresholdKB = -1f)
        {
            var inst = CpuUsageBridgeDetailsViewController.s_ActiveInstance;
            if (inst == null)
            {
                Debug.LogWarning("[SpikeTrapAPI] No active profiler module view.");
                return false;
            }

            if (spikeThresholdMs >= 0f)
                inst.SpikeFilter?.SetThresholdMs(spikeThresholdMs);

            if (gcThresholdKB >= 0f)
                inst.GcFilter?.SetThresholdKB(gcThresholdKB);

            inst.MarkFiltersDirty();
            return true;
        }

        /// <summary>
        /// Stop collecting and save matched frames to a .data file.
        /// </summary>
        /// <param name="savePath">Absolute file path to save the .data file.</param>
        /// <returns>True if frames were saved. False if no frames collected or no active instance.</returns>
        public static bool StopCollectingAndSave(string savePath)
        {
            if (string.IsNullOrEmpty(savePath))
            {
                Debug.LogWarning("[SpikeTrapAPI] savePath is null or empty.");
                return false;
            }

            var inst = CpuUsageBridgeDetailsViewController.s_ActiveInstance;
            if (inst == null)
            {
                Debug.LogWarning("[SpikeTrapAPI] No active profiler module view.");
                return false;
            }

            return inst.SaveMergedMarkedFramesToPath(savePath);
        }

        /// <summary>
        /// Get all cached frame summaries for the current session.
        /// Does not require an active instance — reads from the static cache.
        /// </summary>
        public static FrameSummary[] GetCachedFrameSummaries()
        {
            var cache = CpuUsageBridgeDetailsViewController.s_FrameDataCache;
            var markerNames = CpuUsageBridgeDetailsViewController.s_MarkerNames;
            int sid = CpuUsageBridgeDetailsViewController.s_CurrentSessionId;

            var results = new List<FrameSummary>();
            foreach (var kv in cache)
            {
                int keySid = (int)(kv.Key >> 32);
                if (keySid != sid) continue;
                int frameIndex = (int)(kv.Key & 0xFFFFFFFFL);
                results.Add(ToSummary(frameIndex, keySid, kv.Value, markerNames));
            }

            results.Sort((a, b) => a.FrameIndex.CompareTo(b.FrameIndex));
            return results.ToArray();
        }

        /// <summary>
        /// Get frame summaries where effective CPU time exceeds the threshold.
        /// Sorted by EffectiveTimeMs descending (worst frames first).
        /// Does not require an active instance.
        /// </summary>
        public static FrameSummary[] GetSpikeFrames(float thresholdMs)
        {
            var cache = CpuUsageBridgeDetailsViewController.s_FrameDataCache;
            var markerNames = CpuUsageBridgeDetailsViewController.s_MarkerNames;
            int sid = CpuUsageBridgeDetailsViewController.s_CurrentSessionId;

            var results = new List<FrameSummary>();
            foreach (var kv in cache)
            {
                int keySid = (int)(kv.Key >> 32);
                if (keySid != sid) continue;
                if (kv.Value.EffectiveTimeMs < thresholdMs) continue;
                int frameIndex = (int)(kv.Key & 0xFFFFFFFFL);
                results.Add(ToSummary(frameIndex, keySid, kv.Value, markerNames));
            }

            results.Sort((a, b) => b.EffectiveTimeMs.CompareTo(a.EffectiveTimeMs));
            return results.ToArray();
        }

        /// <summary>
        /// Resolve a marker ID to its name.
        /// </summary>
        public static string GetMarkerName(int markerId)
        {
            CpuUsageBridgeDetailsViewController.s_MarkerNames.TryGetValue(markerId, out string name);
            return name;
        }

        /// <summary>
        /// Get the number of cached frames across all sessions.
        /// </summary>
        public static int CachedFrameCount => CpuUsageBridgeDetailsViewController.s_FrameDataCache.Count;

        static FrameSummary ToSummary(int frameIndex, int sessionId, in CachedFrameData data,
            System.Collections.Concurrent.ConcurrentDictionary<int, string> markerNames)
        {
            string[] names = null;
            float[] times = null;

            if (data.TopMarkers != null && data.TopMarkers.Count > 0)
            {
                names = new string[data.TopMarkers.Count];
                times = new float[data.TopMarkers.Count];
                for (int i = 0; i < data.TopMarkers.Count; i++)
                {
                    var m = data.TopMarkers[i];
                    markerNames.TryGetValue(m.MarkerId, out string n);
                    names[i] = n ?? $"Marker#{m.MarkerId}";
                    times[i] = m.TimeMs;
                }
            }

            return new FrameSummary(frameIndex, sessionId, data.EffectiveTimeMs, data.GcAllocBytes, names, times);
        }
    }
}
