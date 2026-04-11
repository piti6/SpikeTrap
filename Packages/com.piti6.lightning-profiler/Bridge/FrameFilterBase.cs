using UnityEngine;

namespace LightningProfiler
{
    /// <summary>
    /// Base class for frame filters.
    /// <para>
    /// <b>Custom filter authors</b> — implement only:
    /// <list type="bullet">
    ///   <item><see cref="DisplayName"/> — filter name</item>
    ///   <item><see cref="StripColor"/> — highlight strip color</item>
    ///   <item><see cref="IsActive"/> — whether the filter is configured</item>
    ///   <item><see cref="Matches"/> — pure managed matching on <see cref="CachedFrameData"/></item>
    /// </list>
    /// The controller handles data extraction, caching, and matched-frame tracking.
    /// </para>
    /// </summary>
    public abstract class FrameFilterBase : IFrameFilter
    {
        // ─── Required (implement these) ─────────────────────────────────────

        public abstract string DisplayName { get; }
        public abstract Color StripColor { get; }
        public abstract bool IsActive { get; }
        public abstract bool Matches(in CachedFrameData frameData);

        // ─── Optional (override if needed) ──────────────────────────────────

        public virtual string StripLabel => DisplayName;
        public virtual bool DrawToolbarControls() => false;
        public virtual void OnMarkerDiscovered(int markerId, string markerName) { }
        public virtual void Dispose() { }
    }
}
