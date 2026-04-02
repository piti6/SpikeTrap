using System;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using Unity.Profiling.Editor;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightningProfiler
{
    [Serializable]
    [ProfilerModuleMetadata("LightningProfiler CPU Usage", IconPath = "Profiler.CPU")]
    public sealed class CpuUsageProfilerModule : FilterableProfilerModule
    {
        const string k_ChartFilterThresholdKey = "LightningProfiler.ChartFilterThresholdMs";
        static readonly string k_DebugLogPath = "debug-0575cc.log";
        static readonly ProfilerCounterDescriptor[] k_ChartCounters =
        {
            new ProfilerCounterDescriptor("Rendering", ProfilerCategory.Scripts),
            new ProfilerCounterDescriptor("Scripts", ProfilerCategory.Scripts),
            new ProfilerCounterDescriptor("Physics", ProfilerCategory.Scripts),
            new ProfilerCounterDescriptor("Animation", ProfilerCategory.Scripts),
            new ProfilerCounterDescriptor("GarbageCollector", ProfilerCategory.Scripts),
            new ProfilerCounterDescriptor("VSync", ProfilerCategory.Scripts),
            new ProfilerCounterDescriptor("Global Illumination", ProfilerCategory.Scripts),
            new ProfilerCounterDescriptor("UI", ProfilerCategory.Scripts),
            new ProfilerCounterDescriptor("Others", ProfilerCategory.Scripts),
        };

        public CpuUsageProfilerModule()
            : base(k_ChartCounters, ProfilerModuleChartType.StackedTimeArea)
        {
        }

        public override ProfilerModuleViewController CreateDetailsViewController()
        {
            SetChartFilterThreshold(UnityEditor.EditorPrefs.GetFloat(k_ChartFilterThresholdKey, 0f));
            return CpuUsageBridgeDetailsViewController.CreateDetailsViewController(ProfilerWindow);
        }

        static void WriteDebugLog(string runId, string hypothesisId, string location, string message, string dataJson)
        {
            try
            {
                var line =
                    $"{{\"sessionId\":\"0575cc\",\"runId\":\"{EscapeJson(runId)}\",\"hypothesisId\":\"{EscapeJson(hypothesisId)}\",\"location\":\"{EscapeJson(location)}\",\"message\":\"{EscapeJson(message)}\",\"data\":{dataJson},\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}}}";
                File.AppendAllText(k_DebugLogPath, line + Environment.NewLine);
            }
            catch
            {
                // Keep runtime behavior intact if logging fails.
            }
        }

        static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        static string GetAssemblyLocationSafe(System.Reflection.Assembly assembly)
        {
            try
            {
                return assembly.Location ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
