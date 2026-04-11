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
            public override Color StripColor => Color.yellow;
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
            Assert.AreEqual("HighGPU", filter.StripLabel); // defaults to DisplayName

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
        public void CustomFilter_OnMarkerDiscovered_DefaultDoesNothing()
        {
            var filter = new HighGpuFilter(10f);
            // Should not throw
            filter.OnMarkerDiscovered(1, "SomeMarker");
            filter.Dispose();
        }
    }
}
