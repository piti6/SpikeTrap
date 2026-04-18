using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace SpikeTrap.Editor
{
    /// <summary>
    /// Public static API for SpikeTrap profiling automation.
    /// Designed for use via dynamic code execution (uLoop MCP) or editor scripts.
    /// All methods must be called on the main thread (Unity API constraint).
    /// </summary>
    internal static class SpikeTrapAPIBridge
    {
        // ─── Profiling ──────────────────────────────────────────────────────

        /// <summary>Is the Profiler module view currently active?</summary>
        public static bool IsViewActive => SpikeTrapViewController.s_ActiveInstance != null;

        /// <summary>
        /// Open the Profiler window and select the module identified by <paramref name="moduleType"/>.
        /// Idempotent. Requires a display — fails in <c>-batchmode -nographics</c>.
        /// </summary>
        public static bool EnsureProfilerWindowOpen(Type moduleType)
        {
            if (moduleType == null)
                return false;

            var window = EditorWindow.GetWindow<ProfilerWindow>();
            if (window == null)
                return false;

            var module = window.GetProfilerModuleByType(moduleType);
            if (module == null)
                return false;

            window.selectedModule = module;
            window.Show();
            return true;
        }

        /// <summary>Is the controller currently in Collect mode?</summary>
        public static bool IsCollecting
        {
            get
            {
                var inst = SpikeTrapViewController.s_ActiveInstance;
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
            var inst = SpikeTrapViewController.s_ActiveInstance;
            if (inst == null)
            {
                Debug.LogWarning("[SpikeTrap] No active profiler module view. Open the Profiler window first.");
                return false;
            }

            if (spikeThresholdMs > 0f)
                inst.SpikeFilter?.SetThresholdMs(spikeThresholdMs);

            if (gcThresholdKB > 0f)
                inst.GcFilter?.SetThresholdKB(gcThresholdKB);

            inst.MarkFiltersDirty();
            return inst.StartCollectingInternal();
        }

        /// <summary>
        /// Update filter thresholds while collecting (or any time).
        /// Pass 0 to disable a filter. Pass -1 to leave unchanged.
        /// </summary>
        public static bool SetFilterThresholds(float spikeThresholdMs = -1f, float gcThresholdKB = -1f)
        {
            var inst = SpikeTrapViewController.s_ActiveInstance;
            if (inst == null)
            {
                Debug.LogWarning("[SpikeTrap] No active profiler module view.");
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
        /// Stop collecting without saving. Discards captured frames and restores profiler buffer.
        /// </summary>
        /// <returns>True if stopped. False if not collecting or no active instance.</returns>
        public static bool StopCollecting()
        {
            var inst = SpikeTrapViewController.s_ActiveInstance;
            if (inst == null)
            {
                Debug.LogWarning("[SpikeTrap] No active profiler module view.");
                return false;
            }

            if (!inst.IsCollectingMarkedFrames)
                return false;

            inst.StopCollectingInternal();
            return true;
        }

        /// <summary>
        /// Stop collecting and save matched frames to a .data file.
        /// Fire-and-forget — callers that need to know when the file is on disk
        /// should use <see cref="StopCollectingAndSaveAsync"/> instead.
        /// </summary>
        public static void StopCollectingAndSave(string savePath)
        {
            _ = StopCollectingAndSaveAsync(savePath);
        }

        /// <summary>
        /// Stop collecting and save matched frames to a .data file.
        /// Task completes true when the file is written to disk, false if
        /// there was nothing to save or the view was not active.
        /// </summary>
        public static System.Threading.Tasks.Task<bool> StopCollectingAndSaveAsync(string savePath)
        {
            if (string.IsNullOrEmpty(savePath))
            {
                Debug.LogWarning("[SpikeTrap] savePath is null or empty.");
                return System.Threading.Tasks.Task.FromResult(false);
            }

            var inst = SpikeTrapViewController.s_ActiveInstance;
            if (inst == null)
            {
                Debug.LogWarning("[SpikeTrap] No active profiler module view.");
                return System.Threading.Tasks.Task.FromResult(false);
            }

            return inst.SaveMergedMarkedFramesToPathAsync(savePath);
        }

        // ─── Analysis ───────────────────────────────────────────────────────

        /// <summary>
        /// Get all cached frame summaries for the current session.
        /// Does not require an active instance — reads from the static cache.
        /// </summary>
        public static FrameSummary[] GetCachedFrameSummaries()
        {
            var cache = SpikeTrapViewController.s_FrameDataCache;
            var markerNames = SpikeTrapViewController.s_MarkerNames;
            int sid = SpikeTrapViewController.s_CurrentSessionId;

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
            var cache = SpikeTrapViewController.s_FrameDataCache;
            var markerNames = SpikeTrapViewController.s_MarkerNames;
            int sid = SpikeTrapViewController.s_CurrentSessionId;

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
            SpikeTrapViewController.s_MarkerNames.TryGetValue(markerId, out string name);
            return name;
        }

        /// <summary>
        /// Get the number of cached frames across all sessions.
        /// </summary>
        public static int CachedFrameCount => SpikeTrapViewController.s_FrameDataCache.Count;

        // ─── Custom Filters ─────────────────────────────────────────────────

        /// <summary>
        /// Register a factory that creates a custom <see cref="IFrameFilter"/>.
        /// Call from an <c>[InitializeOnLoad]</c> static constructor.
        /// </summary>
        public static void RegisterCustomFilterFactory(Func<IFrameFilter> factory)
        {
            SpikeTrapViewController.RegisterCustomFilterFactory(factory);
        }

        // ─── Internal ───────────────────────────────────────────────────────

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
