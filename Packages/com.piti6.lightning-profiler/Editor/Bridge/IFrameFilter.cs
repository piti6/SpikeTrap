using System.Collections.Generic;
using UnityEngine;

namespace LightningProfiler
{
    /// <summary>
    /// Defines a pluggable frame filter for the Lightning Profiler.
    /// Implement this interface (or extend <see cref="FrameFilterBase"/>) to create custom filters.
    /// Register via <see cref="CpuUsageBridgeDetailsViewController.RegisterCustomFilterFactory"/>.
    /// <para>
    /// Filters receive pre-extracted <see cref="CachedFrameData"/> — no native API access needed.
    /// The controller handles data extraction, caching, matched-frame tracking, and incremental updates.
    /// </para>
    /// </summary>
    public interface IFrameFilter
    {
        /// <summary>Display name used in logs and tooltips.</summary>
        string DisplayName { get; }

        /// <summary>Color used for the highlight strip.</summary>
        Color StripColor { get; }

        /// <summary>Label shown on the left side of the strip.</summary>
        string StripLabel { get; }

        /// <summary>Whether this filter has a non-trivial parameter set.</summary>
        bool IsActive { get; }

        /// <summary>
        /// Draw filter-specific toolbar controls (IMGUI).
        /// Return true if the filter parameter changed (triggers matched frames re-evaluation).
        /// </summary>
        bool DrawToolbarControls();

        /// <summary>
        /// Test a single frame against this filter using pre-extracted managed data.
        /// No native API calls — pure managed, thread-safe.
        /// </summary>
        bool Matches(in CachedFrameData frameData);

        /// <summary>
        /// Called when new marker names are discovered during frame extraction.
        /// Allows filters (e.g. search) to update their matching marker ID sets.
        /// </summary>
        void OnMarkerDiscovered(int markerId, string markerName);

        /// <summary>Clean up resources when the filter is removed.</summary>
        void Dispose();
    }
}
