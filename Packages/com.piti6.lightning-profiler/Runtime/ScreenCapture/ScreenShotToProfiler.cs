using UnityEngine;
using System;
using UnityEngine.Rendering;
using UnityEngine.Profiling;

namespace UTJ.SS2Profiler
{
    public class ScreenShotToProfiler
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

        public Action<RenderTexture> captureBehaviour
        {
            set
            {
#if DEBUG
                if (renderTextureBuffer != null)
                    renderTextureBuffer.captureBehaviour = value;
#endif
            }
        }

        /// How many frames to skip between captures. 0 = every frame, 1 = every other frame, etc.
        public int captureInterval { get; set; } = 0;

#if DEBUG
        private const string CAPTURE_CMD_SAMPLE = "ScreenToRt";

        private CommandBuffer commandBuffer;
        private ScreenShotLogic renderTextureBuffer;
        private GameObject behaviourGmo;
        private int frameIdx = 0;
        private int lastRequestIdx = -1;
        private int framesSinceCapture = 0;

        private CustomSampler captureSampler;
        private CustomSampler updateSampler;

        private bool isInitialize = false;
#endif

        public bool Initialize()
        {
            if (Screen.width > Screen.height)
                return Initialize(192, 128, true);
            else
                return Initialize(128, 192, true);
        }

        public bool Initialize(int width, int height, bool allowSync = false)
        {
#if DEBUG
            Initialize(width, height, TextureCompress.RGB_565, allowSync);
#endif
            return true;
        }

        public bool Initialize(int width, int height, TextureCompress compress, bool allowSync)
        {
#if DEBUG
            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                if (!allowSync)
                    return false;
                Debug.LogWarning("SystemInfo.supportsAsyncGPUReadback is false! Profiler Screenshot is very slow...");
                compress = ScreenShotProfilerUtil.FallbackAtNoGPUAsync(compress);
            }
            if (renderTextureBuffer != null) { return false; }
            InitializeLogic(width, height, compress);
#endif
            return true;
        }

        private void InitializeLogic(int width, int height, TextureCompress compress)
        {
#if DEBUG
            if (isInitialize) return;
            if (width == 0 || height == 0) return;

            renderTextureBuffer = new ScreenShotLogic(width, height, compress);
            renderTextureBuffer.captureBehaviour = this.DefaultCaptureBehaviour;

            behaviourGmo = new GameObject("LightningProfiler_ScreenCapture");
            behaviourGmo.hideFlags = HideFlags.HideAndDontSave;
            GameObject.DontDestroyOnLoad(behaviourGmo);
            var behaviour = behaviourGmo.AddComponent<BehaviourProxy>();

            captureSampler = CustomSampler.Create("ScreenshotToProfiler.Capture");
            updateSampler = CustomSampler.Create("ScreenshotToProfiler.Update");
            behaviour.captureFunc += this.Capture;
            behaviour.updateFunc += this.Update;
            isInitialize = true;
#endif
        }

        public void Destroy()
        {
#if DEBUG
            if (behaviourGmo)
                GameObject.Destroy(behaviourGmo);
            if (renderTextureBuffer != null)
                renderTextureBuffer.Dispose();
            renderTextureBuffer = null;
            isInitialize = false;
#endif
        }

#if DEBUG
        private void Update()
        {
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
            // Skip frames based on interval
            if (captureInterval > 0)
            {
                framesSinceCapture++;
                if (framesSinceCapture <= captureInterval)
                    return;
                framesSinceCapture = 0;
            }

            captureSampler.Begin();
            lastRequestIdx = renderTextureBuffer.CaptureScreen(frameIdx);
            renderTextureBuffer.UpdateAsyncRequest();
            ++frameIdx;
            captureSampler.End();
        }
#endif

        public void DefaultCaptureBehaviour(RenderTexture target)
        {
#if DEBUG
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
#endif
        }
    }
}
