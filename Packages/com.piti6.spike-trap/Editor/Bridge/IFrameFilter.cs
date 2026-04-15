using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpikeTrap
{
    /// <summary>
    /// Defines a pluggable frame filter for the SpikeTrap.
    /// Implement this interface (or extend <see cref="FrameFilterBase"/>) to create custom filters.
    /// Register via <see cref="CpuUsageBridgeDetailsViewController.RegisterCustomFilterFactory"/>.
    /// <para>
    /// Filters receive pre-extracted <see cref="CachedFrameData"/> — no native API access needed.
    /// The controller handles data extraction, caching, matched-frame tracking, and incremental updates.
    /// </para>
    /// </summary>
    public interface IFrameFilter : IDisposable
    {
        /// <summary>Color used for the highlight strip.</summary>
        Color HighlightColor { get; }

        /// <summary>Whether this filter has a non-trivial parameter set.</summary>
        bool IsActive { get; }

        /// <summary>
        /// Draw filter-specific toolbar controls (IMGUI).
        /// Return true if the filter parameter changed (triggers matched frames re-evaluation).
        /// </summary>
        bool DrawToolbarControls();

        /// <summary>
        /// Test a single frame against this filter using pre-extracted managed data.
        /// Must be thread-safe — the controller may call this from background threads via Parallel.For.
        /// </summary>
        bool Matches(in CachedFrameData frameData);

        /// <summary>
        /// Reset internal state. Called by the controller on session boundaries (file load, clear).
        /// </summary>
        void InvalidateCache();
    }
}
