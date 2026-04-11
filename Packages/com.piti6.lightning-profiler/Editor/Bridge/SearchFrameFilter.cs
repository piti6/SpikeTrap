using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace LightningProfiler
{
    /// <summary>
    /// Filters frames containing a profiler sample whose name matches a search term (case-insensitive).
    /// Thread-safe: <see cref="OnMarkerDiscovered"/> and <see cref="Matches"/> can be called from any thread.
    /// </summary>
    public sealed class SearchFrameFilter : FrameFilterBase
    {
        volatile string m_SearchString;
        readonly Action m_DrawSearchBar;

        // Thread-safe sets using ConcurrentDictionary as ConcurrentHashSet
        readonly ConcurrentDictionary<int, byte> m_MatchingMarkerIds = new ConcurrentDictionary<int, byte>();
        readonly ConcurrentDictionary<int, byte> m_CheckedMarkerIds = new ConcurrentDictionary<int, byte>();

        public SearchFrameFilter(Action drawSearchBar)
        {
            m_DrawSearchBar = drawSearchBar;
        }

        public override string DisplayName => "Search";
        public override Color StripColor => new Color(1f, 0.75f, 0.1f, 0.95f);
        public override string StripLabel => string.IsNullOrEmpty(m_SearchString) ? "search" : m_SearchString;
        public override bool IsActive => !string.IsNullOrEmpty(m_SearchString);

        public void SetSearchString(string search)
        {
            m_SearchString = search;
            m_MatchingMarkerIds.Clear();
            m_CheckedMarkerIds.Clear();
        }

        public override bool DrawToolbarControls()
        {
            m_DrawSearchBar?.Invoke();
            return false;
        }

        /// <summary>
        /// Thread-safe matching. Checks if any marker in the frame is in the matching set.
        /// </summary>
        public override bool Matches(in CachedFrameData frameData)
        {
            if (m_MatchingMarkerIds.IsEmpty) return false;
            if (frameData.UniqueMarkerIds == null) return false;

            foreach (var id in frameData.UniqueMarkerIds)
            {
                if (m_MatchingMarkerIds.ContainsKey(id))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Thread-safe marker discovery. Can be called from any thread.
        /// Uses ConcurrentDictionary.TryAdd for atomic check-and-insert.
        /// </summary>
        public override void OnMarkerDiscovered(int markerId, string markerName)
        {
            if (!m_CheckedMarkerIds.TryAdd(markerId, 0))
                return; // already checked

            if (!string.IsNullOrEmpty(m_SearchString) &&
                markerName.IndexOf(m_SearchString, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                m_MatchingMarkerIds.TryAdd(markerId, 0);
            }
        }
    }
}
