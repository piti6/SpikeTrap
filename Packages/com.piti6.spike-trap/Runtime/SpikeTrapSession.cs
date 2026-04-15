using System;
using UnityEngine;
using UnityEngine.Profiling;

namespace SpikeTrap.Runtime
{
    /// Emits session metadata into profiler so the editor knows the source platform.
    public static class SpikeTrapSession
    {
        public static readonly Guid SessionGuid = new Guid("A17B3C4D-E5F6-4789-ABCD-EF0123456789");
        public const int SessionInfoTag = -100;

        static bool s_Emitted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            s_Emitted = false;
        }

        /// Call early in the session. Uses EmitSessionMetaData (once per session, not per frame).
        public static void EmitSessionInfoIfNeeded()
        {
            if (s_Emitted) return;
            s_Emitted = true;

            var data = new byte[4];
#if UNITY_EDITOR
            data[0] = 1; // editor
#else
            data[0] = 0; // device
#endif
            Profiler.EmitSessionMetaData(SessionGuid, SessionInfoTag, data);
        }
    }
}
