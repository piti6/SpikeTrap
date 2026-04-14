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
        /// <summary>
        /// Immutable snapshot of search state. A single volatile reference swap guarantees
        /// that concurrent readers always see a consistent (search string + dictionaries) tuple.
        /// </summary>
        class SearchState
        {
            public readonly string SearchString;
            public readonly ConcurrentDictionary<int, byte> MatchingMarkerIds = new ConcurrentDictionary<int, byte>();
            public readonly ConcurrentDictionary<int, byte> CheckedMarkerIds = new ConcurrentDictionary<int, byte>();
            public SearchState(string search) { SearchString = search; }
        }

        volatile SearchState m_State = new SearchState(null);
        readonly Action m_DrawSearchBar;

        public SearchFrameFilter(Action drawSearchBar)
        {
            m_DrawSearchBar = drawSearchBar;
        }

        public override Color HighlightColor => new Color(1f, 0.75f, 0.1f, 0.95f);
        public override bool IsActive => !string.IsNullOrEmpty(m_State.SearchString);

        public void SetSearchString(string search)
        {
            // Single atomic reference swap — concurrent readers see either the old complete
            // state or the new empty state; never a mix of old string with new dictionaries.
            m_State = new SearchState(search);
        }

        public override bool DrawToolbarControls()
        {
            m_DrawSearchBar?.Invoke();
            return false;
        }

        /// <summary>
        /// Thread-safe matching. Checks if any marker in the frame is in the matching set.
        /// Captures <see cref="m_State"/> once into a local to avoid TOCTOU issues.
        /// </summary>
        public override bool Matches(in CachedFrameData frameData)
        {
            var state = m_State;
            if (state.MatchingMarkerIds.IsEmpty) return false;
            if (frameData.UniqueMarkerIds == null) return false;

            foreach (var id in frameData.UniqueMarkerIds)
            {
                if (state.MatchingMarkerIds.ContainsKey(id))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Thread-safe marker discovery. Can be called from any thread.
        /// Captures <see cref="m_State"/> once into a local to avoid TOCTOU issues.
        /// Uses ConcurrentDictionary.TryAdd for atomic check-and-insert.
        /// </summary>
        public void OnMarkerDiscovered(int markerId, string markerName)
        {
            var state = m_State;
            if (!state.CheckedMarkerIds.TryAdd(markerId, 0))
                return; // already checked

            if (!string.IsNullOrEmpty(state.SearchString) && markerName != null &&
                markerName.IndexOf(state.SearchString, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                state.MatchingMarkerIds.TryAdd(markerId, 0);
            }
        }

        /// <summary>
        /// Clears cached marker data while preserving the current search term.
        /// Called by the controller on session boundaries (file load, clear).
        /// </summary>
        public override void InvalidateCache()
        {
            m_State = new SearchState(m_State.SearchString);
        }
    }
}
