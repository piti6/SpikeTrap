using System.Collections.Generic;
using UnityEngine;

namespace LightningProfiler
{
    /// <summary>
    /// Defines a pluggable frame filter for the Lightning Profiler.
    /// Implement this interface (or extend <see cref="FrameFilterBase"/>) to create custom filters.
    /// Register via <see cref="CpuUsageBridgeDetailsViewController.RegisterCustomFilterFactory"/>.
    /// </summary>
    public interface IFrameFilter
    {
        /// <summary>Display name used in logs and tooltips.</summary>
        string DisplayName { get; }

        /// <summary>Color used for the highlight strip.</summary>
        Color StripColor { get; }

        /// <summary>Label shown on the left side of the strip.</summary>
        string StripLabel { get; }

        /// <summary>Whether this filter has a non-trivial parameter set (threshold > 0, search term non-empty, etc.).</summary>
        bool IsActive { get; }

        /// <summary>The set of frame indices that currently match this filter.</summary>
        IReadOnlyCollection<int> MatchedFrames { get; }

        /// <summary>
        /// Draw filter-specific toolbar controls (IMGUI).
        /// Return true if the filter parameter changed this frame (triggers subscription update).
        /// </summary>
        bool DrawToolbarControls();

        /// <summary>
        /// Update the matched-frames cache for the visible frame range.
        /// Called each GUI frame. Uses incremental scanning when the parameter hasn't changed.
        /// </summary>
        void UpdateMatches();

        /// <summary>
        /// Test a single frame against this filter using pre-fetched data.
        /// Used by OnNewProfilerFrame and IsFrameMarked for real-time pause/log/collect.
        /// The <paramref name="context"/> must not be stored beyond this call.
        /// </summary>
        bool IsMatch(in FrameDataContext context);

        /// <summary>Reset all cached state (matched frames, cached parameters).</summary>
        void InvalidateCache();

        /// <summary>Clean up resources when the filter is removed.</summary>
        void Dispose();
    }
}
