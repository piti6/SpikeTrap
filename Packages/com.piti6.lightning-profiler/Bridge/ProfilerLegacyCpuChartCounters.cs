using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.Editor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine.Profiling;

namespace LightningProfiler
{
    /// <summary>
    /// Builds chart counter descriptors the same way Unity's legacy CPU module does
    /// (<see cref="ProfilerDriver.GetGraphStatisticsPropertiesForArea"/> + CPU category mapping).
    /// </summary>
    internal static class ProfilerLegacyCpuChartCounters
    {
        const string CpuCategoryName = "Scripts";

        public static ProfilerCounterDescriptor[] BuildDescriptorsForCpuArea()
        {
            var legacyStats = ProfilerDriver.GetGraphStatisticsPropertiesForArea(ProfilerArea.CPU);
            if (legacyStats == null || legacyStats.Length == 0)
                return new[] { new ProfilerCounterDescriptor("CPU", ProfilerCategory.Scripts.Name) };

            var list = new List<ProfilerCounterDescriptor>(legacyStats.Length);
            foreach (var statName in legacyStats)
            {
                if (string.IsNullOrEmpty(statName))
                    continue;
                var category = LegacyProfilerAreaUtility.ProfilerAreaToCategoryName(ProfilerArea.CPU) ?? CpuCategoryName;
                list.Add(new ProfilerCounterDescriptor(statName, category));
            }

            return list.Count > 0 ? list.ToArray() : new[] { new ProfilerCounterDescriptor("CPU", ProfilerCategory.Scripts.Name) };
        }
    }

    /// <summary>
    /// Mirrors Unity <c>LegacyProfilerAreaUtility</c> category mapping (reference behavior only).
    /// </summary>
    internal static class LegacyProfilerAreaUtility
    {
        static readonly Dictionary<ProfilerArea, string> s_Map = new Dictionary<ProfilerArea, string>
        {
            { ProfilerArea.CPU, ProfilerCategory.Scripts.Name },
            { ProfilerArea.GPU, ProfilerCategory.Render.Name },
            { ProfilerArea.Rendering, ProfilerCategory.Render.Name },
            { ProfilerArea.Memory, ProfilerCategory.Memory.Name },
            { ProfilerArea.Audio, ProfilerCategory.Audio.Name },
            { ProfilerArea.Video, ProfilerCategory.Video.Name },
            { ProfilerArea.Physics, ProfilerCategory.Physics.Name },
            { ProfilerArea.Physics2D, ProfilerCategory.Physics.Name },
            { ProfilerArea.NetworkMessages, ProfilerCategory.Network.Name },
            { ProfilerArea.NetworkOperations, ProfilerCategory.Network.Name },
            { ProfilerArea.UI, ProfilerCategory.Gui.Name },
            { ProfilerArea.UIDetails, ProfilerCategory.Gui.Name },
            { ProfilerArea.GlobalIllumination, ProfilerCategory.Lighting.Name },
            { ProfilerArea.VirtualTexturing, ProfilerCategory.VirtualTexturing.Name },
        };

        public static string ProfilerAreaToCategoryName(ProfilerArea area)
        {
            return s_Map.TryGetValue(area, out var categoryName) ? categoryName : null;
        }
    }
}
