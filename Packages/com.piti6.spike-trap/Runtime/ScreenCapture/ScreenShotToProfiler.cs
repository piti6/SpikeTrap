using UnityEngine;
using System;
using UnityEngine.Rendering;
using UnityEngine.Profiling;

namespace UTJ.SS2Profiler
{
    internal class ScreenShotToProfiler
    {
        public static readonly Guid MetadataGuid = new Guid("4389DCEB-F9B3-4D49-940B-E98482F3A3F8");
        public static readonly int InfoTag = -1;

        public static ScreenShotToProfiler Instance { get; private set; } = new ScreenShotToProfiler();

        public enum TextureCompress : byte
        {
            None = 0,
            RGB_565 = 1,
            PNG = 2,
            JPG_BufferRGB565 = 3,
            JPG_BufferRGBA = 4,
        }

        private const string CAPTURE_CMD_SAMPLE = "ScreenToRt";

        private CommandBuffer commandBuffer;
        private ScreenShotLogic renderTextureBuffer;
        private GameObject behaviourGmo;
        private int frameIdx = 0;
        private int lastRequestIdx = -1;

        private CustomSampler captureSampler;
        private CustomSampler updateSampler;

        private bool isInitialize = false;

        /// <summary>
        /// Initialize screenshot capture with a resolution scale relative to the current screen size.
        /// Aspect ratio is preserved automatically.
        /// </summary>
        /// <param name="resolutionScale">Scale factor (0.25 = quarter resolution, 0.5 = half). Default: 0.25.</param>
        /// <param name="compress">Texture compression mode.</param>
        /// <param name="allowSync">Allow synchronous GPU readback as fallback.</param>
        public bool Initialize(float resolutionScale = 0.25f, TextureCompress compress = TextureCompress.RGB_565,
            bool allowSync = true, Action<RenderTexture> captureBehaviour = null)
        {
            int w = Mathf.Max(1, Mathf.RoundToInt(Screen.width * resolutionScale));
            int h = Mathf.Max(1, Mathf.RoundToInt(Screen.height * resolutionScale));
            return InitializeInternal(w, h, compress, allowSync, captureBehaviour);
        }

        private bool InitializeInternal(int width, int height, TextureCompress compress, bool allowSync,
            Action<RenderTexture> captureBehaviour)
        {
            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                if (!allowSync)
                    return false;
                Debug.LogWarning("SystemInfo.supportsAsyncGPUReadback is false! Profiler Screenshot is very slow...");
                compress = ScreenShotProfilerUtil.FallbackAtNoGPUAsync(compress);
            }
            if (renderTextureBuffer != null) { return false; }
            InitializeLogic(width, height, compress, captureBehaviour);
            return true;
        }

        private void InitializeLogic(int width, int height, TextureCompress compress,
            Action<RenderTexture> captureBehaviour)
        {
            if (isInitialize) return;
            if (width == 0 || height == 0) return;

            renderTextureBuffer = new ScreenShotLogic(width, height, compress);
            renderTextureBuffer.captureBehaviour = captureBehaviour ?? this.DefaultCaptureBehaviour;

            behaviourGmo = new GameObject("SpikeTrap_ScreenCapture");
            behaviourGmo.hideFlags = HideFlags.HideAndDontSave;
            GameObject.DontDestroyOnLoad(behaviourGmo);
            var behaviour = behaviourGmo.AddComponent<BehaviourProxy>();

            captureSampler = CustomSampler.Create("ScreenshotToProfiler.Capture");
            updateSampler = CustomSampler.Create("ScreenshotToProfiler.Update");
            behaviour.captureFunc += this.Capture;
            behaviour.updateFunc += this.Update;
            isInitialize = true;
        }

        public void Destroy()
        {
            if (behaviourGmo)
                GameObject.Destroy(behaviourGmo);
            if (renderTextureBuffer != null)
                renderTextureBuffer.Dispose();
            renderTextureBuffer = null;
            if (commandBuffer != null)
            {
                commandBuffer.Release();
                commandBuffer = null;
            }
            isInitialize = false;
        }

        private void Update()
        {
            SpikeTrap.Runtime.SpikeTrapSession.EmitSessionInfoIfNeeded();
            updateSampler.Begin();
            if (SystemInfo.supportsAsyncGPUReadback)
            {
                renderTextureBuffer.AsyncReadbackRequestAtIdx(lastRequestIdx);
                renderTextureBuffer.UpdateAsyncRequest();
            }
            else
            {
                renderTextureBuffer.ReadBackSyncAtIdx(lastRequestIdx);
            }
            updateSampler.End();
        }

        private void Capture()
        {
            captureSampler.Begin();
            lastRequestIdx = renderTextureBuffer.CaptureScreen(frameIdx);
            renderTextureBuffer.UpdateAsyncRequest();
            ++frameIdx;
            captureSampler.End();
        }

        private void DefaultCaptureBehaviour(RenderTexture target)
        {
            if (commandBuffer == null)
            {
                commandBuffer = new CommandBuffer();
                commandBuffer.name = "ScreenCapture";
            }
            commandBuffer.Clear();
            var rt = RenderTexture.GetTemporary(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
            ScreenCapture.CaptureScreenshotIntoRenderTexture(rt);
            commandBuffer.BeginSample(CAPTURE_CMD_SAMPLE);
            commandBuffer.Blit(rt, target);
            commandBuffer.EndSample(CAPTURE_CMD_SAMPLE);
            Graphics.ExecuteCommandBuffer(commandBuffer);
            RenderTexture.ReleaseTemporary(rt);
            commandBuffer.Clear();
        }
    }
}
