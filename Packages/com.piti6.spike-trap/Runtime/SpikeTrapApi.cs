using System;
using UnityEngine;

namespace SpikeTrap.Runtime
{
    /// <summary>
    /// Texture compression mode for profiler screenshot capture.
    /// </summary>
    public enum TextureCompress : byte
    {
        None = 0,
        RGB_565 = 1,
        PNG = 2,
        JPG_BufferRGB565 = 3,
        JPG_BufferRGBA = 4,
    }

    /// <summary>
    /// Public static API for SpikeTrap runtime features.
    /// Provides per-frame screenshot capture for the profiler.
    /// </summary>
    public static class SpikeTrapApi
    {
        // ─── Screenshot Capture ─────────────────────────────────────────────

        /// <summary>
        /// Initialize per-frame screenshot capture for the profiler.
        /// Screenshots are embedded in profiler frame metadata and displayed in the SpikeTrap detail view.
        /// </summary>
        /// <param name="resolutionScale">Scale factor relative to screen size (default 0.25 = quarter resolution).</param>
        /// <param name="compress">Texture compression mode. Default: RGB_565.</param>
        /// <param name="allowSync">Allow synchronous GPU readback as fallback when async is unavailable.</param>
        /// <param name="captureBehaviour">Custom capture routine. Receives the target RenderTexture to blit into. Null = default screen capture.</param>
        public static bool InitializeScreenshotCapture(
            float resolutionScale = 0.25f,
            TextureCompress compress = TextureCompress.RGB_565,
            bool allowSync = true,
            Action<RenderTexture> captureBehaviour = null)
            => UTJ.SS2Profiler.ScreenShotToProfiler.Instance.Initialize(
                resolutionScale,
                (UTJ.SS2Profiler.ScreenShotToProfiler.TextureCompress)compress,
                allowSync,
                captureBehaviour);

        /// <summary>
        /// Stop screenshot capture and release resources.
        /// </summary>
        public static void DestroyScreenshotCapture()
            => UTJ.SS2Profiler.ScreenShotToProfiler.Instance.Destroy();
    }
}
