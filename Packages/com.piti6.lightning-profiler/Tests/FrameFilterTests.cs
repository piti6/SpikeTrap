using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using LightningProfiler;

namespace LightningProfiler.Tests
{
    public class SpikeFrameFilterTests
    {
        static CachedFrameData MakeFrame(float effectiveMs)
        {
            return new CachedFrameData(0, effectiveMs, 0, null);
        }

        [Test]
        public void Matches_AboveThreshold_ReturnsTrue()
        {
            var filter = new SpikeFrameFilter(33f);
            var data = MakeFrame(50f);
            Assert.IsTrue(filter.Matches(in data));
        }

        [Test]
        public void Matches_BelowThreshold_ReturnsFalse()
        {
            var filter = new SpikeFrameFilter(33f);
            var data = MakeFrame(16f);
            Assert.IsFalse(filter.Matches(in data));
        }

        [Test]
        public void Matches_ExactlyAtThreshold_ReturnsTrue()
        {
            var filter = new SpikeFrameFilter(33f);
            var data = MakeFrame(33f);
            Assert.IsTrue(filter.Matches(in data));
        }

        [Test]
        public void Matches_ZeroThreshold_IsInactive()
        {
            var filter = new SpikeFrameFilter(0f);
            Assert.IsFalse(filter.IsActive);
        }

        [Test]
        public void DisplayName_IsSpike()
        {
            var filter = new SpikeFrameFilter(10f);
            Assert.AreEqual("Spike", filter.DisplayName);
        }
    }

    public class GcFrameFilterTests
    {
        static CachedFrameData MakeFrame(long gcBytes)
        {
            return new CachedFrameData(0, 0f, gcBytes, null);
        }

        [Test]
        public void Matches_AboveThreshold_ReturnsTrue()
        {
            var filter = new GcFrameFilter(64f); // 64 KB
            var data = MakeFrame(65536); // 64 * 1024
            Assert.IsTrue(filter.Matches(in data));
        }

        [Test]
        public void Matches_BelowThreshold_ReturnsFalse()
        {
            var filter = new GcFrameFilter(64f);
            var data = MakeFrame(1024); // 1 KB
            Assert.IsFalse(filter.Matches(in data));
        }

        [Test]
        public void Matches_ExactlyAtThreshold_ReturnsTrue()
        {
            var filter = new GcFrameFilter(1f); // 1 KB = 1024 bytes
            var data = MakeFrame(1024);
            Assert.IsTrue(filter.Matches(in data));
        }

        [Test]
        public void Matches_ZeroThreshold_IsInactive()
        {
            var filter = new GcFrameFilter(0f);
            Assert.IsFalse(filter.IsActive);
        }

        [Test]
        public void DisplayName_IsGC()
        {
            var filter = new GcFrameFilter(10f);
            Assert.AreEqual("GC", filter.DisplayName);
        }

        [Test]
        public void Matches_ZeroThreshold_ReturnsFalse()
        {
            // GcFrameFilter(0f) is IsActive==false, and Matches also returns false
            // due to the early guard (m_ThresholdKB <= 0f). Safe even if caller forgets IsActive.
            var filter = new GcFrameFilter(0f);
            var data = new CachedFrameData(0, 0f, 0, null);
            Assert.IsFalse(filter.IsActive);
            Assert.IsFalse(filter.Matches(in data));
        }
    }

    public class SearchFrameFilterTests
    {
        static CachedFrameData MakeFrame(int frameIndex, HashSet<int> markerIds)
        {
            return new CachedFrameData(frameIndex, 0f, 0, markerIds);
        }

        SearchFrameFilter CreateFilter(string searchTerm, params (int id, string name)[] markers)
        {
            var filter = new SearchFrameFilter(() => { });
            filter.SetSearchString(searchTerm);
            foreach (var (id, name) in markers)
                filter.OnMarkerDiscovered(id, name);
            return filter;
        }

        [Test]
        public void Matches_FrameContainsMatchingMarker_ReturnsTrue()
        {
            var filter = CreateFilter("Network", (1, "NetworkSync"), (2, "PlayerLoop"));
            var data = MakeFrame(0, new HashSet<int> { 1, 2 });
            Assert.IsTrue(filter.Matches(in data));
        }

        [Test]
        public void Matches_FrameDoesNotContainMatchingMarker_ReturnsFalse()
        {
            var filter = CreateFilter("Network", (1, "NetworkSync"), (2, "PlayerLoop"));
            var data = MakeFrame(0, new HashSet<int> { 2 }); // only PlayerLoop
            Assert.IsFalse(filter.Matches(in data));
        }

        [Test]
        public void Matches_CaseInsensitive()
        {
            var filter = CreateFilter("network", (1, "NetworkSync"));
            var data = MakeFrame(0, new HashSet<int> { 1 });
            Assert.IsTrue(filter.Matches(in data));
        }

        [Test]
        public void Matches_SubstringMatch()
        {
            var filter = CreateFilter("Save", (1, "SaveCheckpoint"), (2, "LoadAsset"));
            var data = MakeFrame(0, new HashSet<int> { 1 });
            Assert.IsTrue(filter.Matches(in data));
        }

        [Test]
        public void Matches_EmptySearch_IsInactive()
        {
            var filter = new SearchFrameFilter(() => { });
            filter.SetSearchString("");
            Assert.IsFalse(filter.IsActive);
        }

        [Test]
        public void Matches_NullMarkerIds_ReturnsFalse()
        {
            var filter = CreateFilter("Network", (1, "NetworkSync"));
            var data = MakeFrame(0, null);
            Assert.IsFalse(filter.Matches(in data));
        }

        [Test]
        public void Matches_NoMatchingMarkers_ReturnsFalse()
        {
            var filter = CreateFilter("ZZZ_NotExist", (1, "PlayerLoop"), (2, "Rendering"));
            var data = MakeFrame(0, new HashSet<int> { 1, 2 });
            Assert.IsFalse(filter.Matches(in data));
        }

        [Test]
        public void SetSearchString_ClearsOldMatches()
        {
            var filter = CreateFilter("Network", (1, "NetworkSync"));
            var data = MakeFrame(0, new HashSet<int> { 1 });
            Assert.IsTrue(filter.Matches(in data));

            // Change search — old marker IDs should be re-evaluated
            filter.SetSearchString("Player");
            filter.OnMarkerDiscovered(1, "NetworkSync");
            filter.OnMarkerDiscovered(2, "PlayerLoop");
            Assert.IsFalse(filter.Matches(in data)); // frame only has marker 1 (NetworkSync)

            var data2 = MakeFrame(0, new HashSet<int> { 2 });
            Assert.IsTrue(filter.Matches(in data2));
        }

        [Test]
        public void OnMarkerDiscovered_DuplicateMarker_IgnoredSafely()
        {
            var filter = CreateFilter("Network", (1, "NetworkSync"));
            // Call again with same ID — should not crash or change state
            filter.OnMarkerDiscovered(1, "NetworkSync");
            var data = MakeFrame(0, new HashSet<int> { 1 });
            Assert.IsTrue(filter.Matches(in data));
        }
    }

    public class SearchFrameFilterThreadSafetyTests
    {
        [Test]
        public void OnMarkerDiscovered_ConcurrentCalls_NoCorruption()
        {
            var filter = new SearchFrameFilter(() => { });
            filter.SetSearchString("Test");

            // Feed 10,000 markers from parallel threads
            Parallel.For(0, 10000, i =>
            {
                string name = i % 100 == 0 ? $"TestMarker_{i}" : $"OtherMarker_{i}";
                filter.OnMarkerDiscovered(i, name);
            });

            // Verify: markers divisible by 100 should match "Test"
            int matchCount = 0;
            for (int i = 0; i < 10000; i++)
            {
                var data = new CachedFrameData(0, 0f, 0, new HashSet<int> { i });
                if (filter.Matches(in data))
                    matchCount++;
            }
            Assert.AreEqual(100, matchCount); // 0, 100, 200, ... 9900
        }

        [Test]
        public void Matches_ConcurrentReads_NoCorruption()
        {
            var filter = new SearchFrameFilter(() => { });
            filter.SetSearchString("Hit");
            filter.OnMarkerDiscovered(1, "HitMarker");
            filter.OnMarkerDiscovered(2, "MissMarker");

            var hitFrame = new CachedFrameData(0, 0f, 0, new HashSet<int> { 1 });
            var missFrame = new CachedFrameData(1, 0f, 0, new HashSet<int> { 2 });

            // Read from 1000 parallel threads
            var hitResults = new ConcurrentBag<bool>();
            var missResults = new ConcurrentBag<bool>();

            Parallel.For(0, 1000, _ =>
            {
                hitResults.Add(filter.Matches(in hitFrame));
                missResults.Add(filter.Matches(in missFrame));
            });

            Assert.AreEqual(1000, hitResults.Count);
            Assert.AreEqual(1000, missResults.Count);
            foreach (var r in hitResults) Assert.IsTrue(r);
            foreach (var r in missResults) Assert.IsFalse(r);
        }

        [Test]
        public void OnMarkerDiscovered_WhileMatching_NoCorruption()
        {
            var filter = new SearchFrameFilter(() => { });
            filter.SetSearchString("Concurrent");

            // Seed some initial markers
            for (int i = 0; i < 100; i++)
                filter.OnMarkerDiscovered(i, $"Marker_{i}");

            // Simultaneously: discover new markers AND match frames
            var matchResults = new ConcurrentBag<bool>();

            Parallel.For(0, 5000, i =>
            {
                if (i % 2 == 0)
                {
                    // Writer: discover new markers
                    string name = i == 2000 ? "ConcurrentHit" : $"Other_{i}";
                    filter.OnMarkerDiscovered(100 + i, name);
                }
                else
                {
                    // Reader: check match
                    var data = new CachedFrameData(0, 0f, 0, new HashSet<int> { 100 + 2000 }); // the ConcurrentHit marker
                    matchResults.Add(filter.Matches(in data));
                }
            });

            // Should not have thrown. Results may vary (marker might not be discovered yet when read),
            // but no corruption should occur.
            Assert.IsTrue(matchResults.Count > 0);

            // Deterministic end-state check: after the parallel block, marker 2100 ("ConcurrentHit")
            // must be fully discovered. Verify the filter matches it and rejects non-matching markers.
            var hitFrame = new CachedFrameData(0, 0f, 0, new HashSet<int> { 2100 });
            Assert.IsTrue(filter.Matches(in hitFrame), "After concurrent chaos, marker 2100 (ConcurrentHit) must match");

            var missFrame = new CachedFrameData(0, 0f, 0, new HashSet<int> { 101, 103, 105 });
            Assert.IsFalse(filter.Matches(in missFrame), "Frame with only non-matching markers must not match");
        }

        [Test]
        public void SetSearchString_WhileOnMarkerDiscovered_NoCorruption()
        {
            var filter = new SearchFrameFilter(() => { });
            filter.SetSearchString("Alpha");

            // Two threads race: one flips the search string, the other discovers markers.
            Parallel.Invoke(
                () =>
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        string term = i % 2 == 0 ? "Alpha" : "Beta";
                        filter.SetSearchString(term);
                    }
                },
                () =>
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        // Alternate between names that match "Alpha" and "Beta"
                        string name = i % 2 == 0 ? $"AlphaMarker_{i}" : $"BetaMarker_{i}";
                        filter.OnMarkerDiscovered(i, name);
                    }
                }
            );

            // After both threads finish, the filter must be in a consistent state.
            // The last SetSearchString wins (iteration 999 writes "Beta").
            // Re-discover all markers so the current search string evaluates them.
            string currentSearch = filter.IsActive ? "active" : "inactive";
            // We cannot know the exact interleaving, but we can re-seed markers and verify consistency.
            filter.SetSearchString("Beta");
            for (int i = 0; i < 1000; i++)
            {
                string name = i % 2 == 0 ? $"AlphaMarker_{i}" : $"BetaMarker_{i}";
                filter.OnMarkerDiscovered(i, name);
            }

            // Now verify: only "Beta*" markers should match
            for (int i = 0; i < 1000; i++)
            {
                var data = new CachedFrameData(0, 0f, 0, new HashSet<int> { i });
                bool matches = filter.Matches(in data);
                if (i % 2 == 1) // BetaMarker_N
                    Assert.IsTrue(matches, $"Marker {i} (BetaMarker) should match search 'Beta'");
                else // AlphaMarker_N
                    Assert.IsFalse(matches, $"Marker {i} (AlphaMarker) should not match search 'Beta'");
            }
        }
    }

    public class CachedFrameDataTests
    {
        [Test]
        public void Constructor_StoresAllValues()
        {
            var markers = new HashSet<int> { 1, 2, 3 };
            var data = new CachedFrameData(42, 16.5f, 1024, markers);

            Assert.AreEqual(42, data.FrameIndex);
            Assert.AreEqual(16.5f, data.EffectiveTimeMs, 0.001f);
            Assert.AreEqual(1024, data.GcAllocBytes);
            Assert.AreSame(markers, data.UniqueMarkerIds);
        }

        [Test]
        public void Constructor_NullMarkers_Allowed()
        {
            var data = new CachedFrameData(0, 0f, 0, null);
            Assert.IsNull(data.UniqueMarkerIds);
        }
    }

    /// <summary>
    /// Tests for a minimal custom filter to verify the FrameFilterBase API is easy to use.
    /// </summary>
    public class CustomFilterTests
    {
        class HighGpuFilter : FrameFilterBase
        {
            readonly float m_ThresholdMs;
            public HighGpuFilter(float threshold) { m_ThresholdMs = threshold; }

            public override string DisplayName => "HighGPU";
            public override Color HighlightColor => Color.yellow;
            public override bool IsActive => m_ThresholdMs > 0f;

            public override bool Matches(in CachedFrameData frameData)
            {
                // Custom filter: match frames where effective time > threshold
                // (simulating a GPU filter using CPU time as proxy for test)
                return frameData.EffectiveTimeMs > m_ThresholdMs;
            }
        }

        [Test]
        public void CustomFilter_ImplementsInterfaceCorrectly()
        {
            IFrameFilter filter = new HighGpuFilter(10f);
            Assert.AreEqual("HighGPU", filter.DisplayName);
            Assert.IsTrue(filter.IsActive);

            var data = new CachedFrameData(0, 20f, 0, null);
            Assert.IsTrue(filter.Matches(in data));

            var lowData = new CachedFrameData(0, 5f, 0, null);
            Assert.IsFalse(filter.Matches(in lowData));
        }

        [Test]
        public void CustomFilter_InactiveWhenZeroThreshold()
        {
            var filter = new HighGpuFilter(0f);
            Assert.IsFalse(filter.IsActive);
        }

        [Test]
        public void CustomFilter_DisposeDoesNotThrow()
        {
            var filter = new HighGpuFilter(10f);
            // Should not throw
            filter.Dispose();
        }
    }

    public class EdgeCaseTests
    {
        [Test]
        public void SpikeFilter_NegativeFrameTime_DoesNotMatch()
        {
            var filter = new SpikeFrameFilter(10f);
            var data = new CachedFrameData(0, -5f, 0, null);
            Assert.IsFalse(filter.Matches(in data));
        }

        [Test]
        public void SpikeFilter_ZeroThreshold_MatchesReturnsFalse()
        {
            // threshold=0 means IsActive is false, and Matches also returns false
            // due to the early guard (m_ThresholdMs <= 0f). Safe even if caller forgets IsActive.
            var filter = new SpikeFrameFilter(0f);
            var data = new CachedFrameData(0, 0f, 0, null);
            Assert.IsFalse(filter.IsActive);
            Assert.IsFalse(filter.Matches(in data));
        }

        [Test]
        public void GcFilter_FractionalKBThreshold_512Bytes()
        {
            // 0.5 KB = 512 bytes
            var filter = new GcFrameFilter(0.5f);

            var below = new CachedFrameData(0, 0f, 511, null);
            Assert.IsFalse(filter.Matches(in below));

            var exact = new CachedFrameData(0, 0f, 512, null);
            Assert.IsTrue(filter.Matches(in exact));
        }

        [Test]
        public void GcFilter_LargeValues_1GB()
        {
            // 1 GB = 1048576 KB = 1073741824 bytes
            var filter = new GcFrameFilter(1048576f);
            var data = new CachedFrameData(0, 0f, 1073741824L, null);
            Assert.IsTrue(filter.Matches(in data));
        }

        [Test]
        public void SearchFilter_EmptyHashSet_ReturnsFalse()
        {
            var filter = new SearchFrameFilter(() => { });
            filter.SetSearchString("Hit");
            filter.OnMarkerDiscovered(1, "HitMarker");

            // Frame has an empty marker set (not null) — no marker ID to match against
            var data = new CachedFrameData(0, 0f, 0, new HashSet<int>());
            Assert.IsFalse(filter.Matches(in data));
        }

        [Test]
        public void SearchFilter_NullMarkerName_DoesNotThrow()
        {
            var filter = new SearchFrameFilter(() => { });
            filter.SetSearchString("Test");

            // Current code may NRE on null markerName — this test documents the gap.
            Assert.DoesNotThrow(() => filter.OnMarkerDiscovered(1, null));
        }
    }

    public class MultiFilterCompositionTests
    {
        [Test]
        public void MultipleFilters_ORSemantics()
        {
            // Create filters
            var spike = new SpikeFrameFilter(30f);
            var gc = new GcFrameFilter(1f); // 1 KB = 1024 bytes
            var search = new SearchFrameFilter(() => { });
            search.SetSearchString("Network");
            search.OnMarkerDiscovered(1, "NetworkSync");
            search.OnMarkerDiscovered(99, "PlayerLoop");

            var filters = new IFrameFilter[] { spike, gc, search };

            // Frame A: 5ms, 10240 bytes (10KB), markers={99} (PlayerLoop, no NetworkSync)
            // GC matches (10240 >= 1024), spike does not (5 < 30), search does not (99 is PlayerLoop)
            var frameA = new CachedFrameData(0, 5f, 10240, new HashSet<int> { 99 });

            bool anyMatchA = false;
            bool spikeMatchA = spike.Matches(in frameA);
            bool gcMatchA = gc.Matches(in frameA);
            bool searchMatchA = search.Matches(in frameA);

            Assert.IsFalse(spikeMatchA, "Spike should not match frame A (5ms < 30ms)");
            Assert.IsTrue(gcMatchA, "GC should match frame A (10240 bytes >= 1024 bytes)");
            Assert.IsFalse(searchMatchA, "Search should not match frame A (marker 99 is PlayerLoop)");

            foreach (var f in filters)
            {
                if (f.IsActive && f.Matches(in frameA))
                {
                    anyMatchA = true;
                    break;
                }
            }
            Assert.IsTrue(anyMatchA, "At least one active filter should match frame A (OR semantics)");

            // Frame B: 5ms, 100 bytes, markers={99} (PlayerLoop) — none match
            var frameB = new CachedFrameData(1, 5f, 100, new HashSet<int> { 99 });

            bool anyMatchB = false;
            Assert.IsFalse(spike.Matches(in frameB), "Spike should not match frame B");
            Assert.IsFalse(gc.Matches(in frameB), "GC should not match frame B");
            Assert.IsFalse(search.Matches(in frameB), "Search should not match frame B");

            foreach (var f in filters)
            {
                if (f.IsActive && f.Matches(in frameB))
                {
                    anyMatchB = true;
                    break;
                }
            }
            Assert.IsFalse(anyMatchB, "No active filter should match frame B");
        }
    }
}
