using System;

namespace SpikeTrap.Editor
{
    /// <summary>
    /// Public static API for SpikeTrap profiling automation.
    /// Designed for use via dynamic code execution (uLoop MCP) or editor scripts.
    /// All methods must be called on the main thread (Unity API constraint).
    /// </summary>
    public static class SpikeTrapApi
    {
        // ─── Profiling ──────────────────────────────────────────────────────

        /// <summary>Is the Profiler module view currently active?</summary>
        public static bool IsViewActive => SpikeTrapAPIBridge.IsViewActive;

        /// <summary>
        /// Open the Profiler window and select the SpikeTrap module. Idempotent.
        /// Requires a display — fails in <c>-batchmode -nographics</c>.
        /// </summary>
        public static bool EnsureProfilerWindowOpen()
            => SpikeTrapAPIBridge.EnsureProfilerWindowOpen(typeof(SpikeTrapProfilerModule));

        /// <summary>Is the controller currently in Collect mode?</summary>
        public static bool IsCollecting => SpikeTrapAPIBridge.IsCollecting;

        /// <summary>
        /// Start collecting frames matching filters.
        /// </summary>
        /// <param name="spikeThresholdMs">Spike filter threshold in ms. 0 = disabled.</param>
        /// <param name="gcThresholdKB">GC alloc filter threshold in KB. 0 = disabled.</param>
        public static bool StartCollecting(float spikeThresholdMs = 0f, float gcThresholdKB = 0f)
            => SpikeTrapAPIBridge.StartCollecting(spikeThresholdMs, gcThresholdKB);

        /// <summary>
        /// Update filter thresholds while collecting (or any time).
        /// Pass 0 to disable a filter. Pass -1 to leave unchanged.
        /// </summary>
        public static bool SetFilterThresholds(float spikeThresholdMs = -1f, float gcThresholdKB = -1f)
            => SpikeTrapAPIBridge.SetFilterThresholds(spikeThresholdMs, gcThresholdKB);

        /// <summary>
        /// Stop collecting without saving. Discards captured frames and restores profiler buffer.
        /// </summary>
        public static bool StopCollecting()
            => SpikeTrapAPIBridge.StopCollecting();

        /// <summary>
        /// Stop collecting and save matched frames to a .data file (fire-and-forget).
        /// The save is deferred — the file is NOT on disk when this returns.
        /// Use <see cref="StopCollectingAndSaveAsync"/> when completion matters.
        /// </summary>
        /// <param name="savePath">Absolute file path to save the .data file.</param>
        public static void StopCollectingAndSave(string savePath)
            => SpikeTrapAPIBridge.StopCollectingAndSave(savePath);

        /// <summary>
        /// Stop collecting and save matched frames to a .data file.
        /// Task completes true when the file has been written, false if there
        /// was nothing to save or no active view.
        /// </summary>
        /// <param name="savePath">Absolute file path to save the .data file.</param>
        public static System.Threading.Tasks.Task<bool> StopCollectingAndSaveAsync(string savePath)
            => SpikeTrapAPIBridge.StopCollectingAndSaveAsync(savePath);

        // ─── Analysis ───────────────────────────────────────────────────────

        /// <summary>
        /// Get all cached frame summaries for the current session.
        /// Does not require an active instance — reads from the static cache.
        /// </summary>
        public static FrameSummary[] GetCachedFrameSummaries()
            => SpikeTrapAPIBridge.GetCachedFrameSummaries();

        /// <summary>
        /// Get frame summaries where effective CPU time exceeds the threshold.
        /// Sorted by EffectiveTimeMs descending (worst frames first).
        /// </summary>
        public static FrameSummary[] GetSpikeFrames(float thresholdMs)
            => SpikeTrapAPIBridge.GetSpikeFrames(thresholdMs);

        /// <summary>
        /// Resolve a marker ID to its name.
        /// </summary>
        public static string GetMarkerName(int markerId)
            => SpikeTrapAPIBridge.GetMarkerName(markerId);

        /// <summary>
        /// Get the number of cached frames across all sessions.
        /// </summary>
        public static int CachedFrameCount => SpikeTrapAPIBridge.CachedFrameCount;

        // ─── Custom Filters ─────────────────────────────────────────────────

        /// <summary>
        /// Register a factory that creates a custom <see cref="IFrameFilter"/>.
        /// Call from an <c>[InitializeOnLoad]</c> static constructor.
        /// </summary>
        public static void RegisterCustomFilterFactory(Func<IFrameFilter> factory)
            => SpikeTrapAPIBridge.RegisterCustomFilterFactory(factory);

    }
}
