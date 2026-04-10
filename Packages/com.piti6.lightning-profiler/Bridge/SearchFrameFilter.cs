using System;
using System.Collections.Generic;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace LightningProfiler
{
    /// <summary>
    /// Filters frames containing a profiler sample whose name matches a search term (case-insensitive).
    /// Uses marker-ID caching so each unique marker name is checked only once per frame.
    /// </summary>
    public sealed class SearchFrameFilter : FrameFilterBase
    {
        string m_SearchString;
        readonly Func<int> m_GetThreadIndex;
        readonly Action m_DrawSearchBar;

        // Marker ID → matches search term. Reused across frames while the search term is unchanged.
        readonly Dictionary<int, bool> m_MarkerMatchCache = new Dictionary<int, bool>();

        public SearchFrameFilter(Func<int> getThreadIndex, Action drawSearchBar)
        {
            m_GetThreadIndex = getThreadIndex;
            m_DrawSearchBar = drawSearchBar;
        }

        public override string DisplayName => "Search";
        public override Color StripColor => new Color(1f, 0.75f, 0.1f, 0.95f);
        public override string StripLabel => string.IsNullOrEmpty(m_SearchString) ? "search" : m_SearchString;
        public override bool IsActive => !string.IsNullOrEmpty(m_SearchString);

        /// <summary>Called when the hierarchy view's search bar changes.</summary>
        public void SetSearchString(string search)
        {
            m_SearchString = search;
            m_MarkerMatchCache.Clear();
            InvalidateCache();
        }

        public override bool DrawToolbarControls()
        {
            m_DrawSearchBar?.Invoke();
            return false;
        }

        public override bool FrameMatches(int frameIndex)
        {
            if (string.IsNullOrEmpty(m_SearchString)) return false;
            
            int threadIndex = m_GetThreadIndex();
            if (threadIndex < 0) threadIndex = 0;
            using (var raw = ProfilerDriver.GetRawFrameDataView(frameIndex, threadIndex))
            {
                if (raw == null || !raw.valid)
                    return false;
                return RawDataContainsSearch(raw, m_SearchString, m_MarkerMatchCache);
            }
        }

        static bool RawDataContainsSearch(RawFrameDataView raw, string search, Dictionary<int, bool> markerCache)
        {
            for (int i = 1; i < raw.sampleCount; i++)
            {
                int markerId = raw.GetSampleMarkerId(i);
                if (!markerCache.TryGetValue(markerId, out bool matches))
                {
                    var name = raw.GetSampleName(i);
                    matches = name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
                    markerCache[markerId] = matches;
                }
                if (matches)
                    return true;
            }
            return false;
        }

        public override void InvalidateCache()
        {
            m_MarkerMatchCache.Clear();
            base.InvalidateCache();
        }
    }
}
