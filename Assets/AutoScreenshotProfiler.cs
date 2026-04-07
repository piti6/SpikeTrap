using UnityEngine;
using UTJ.SS2Profiler;

/// Auto-initializes screenshot capture for the profiler. No prefab needed.
public static class AutoScreenshotProfiler
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        ScreenShotToProfiler.Instance.Initialize();
    }
}
