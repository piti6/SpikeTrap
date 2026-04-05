using System.Collections;
using UnityEngine;

namespace UTJ.SS2Profiler
{
    internal class BehaviourProxy : MonoBehaviour
    {
        public System.Action captureFunc { get; set; }
        public System.Action updateFunc { get; set; }
        private WaitForEndOfFrame waitForEndOfFrame;

        void Start()
        {
            StartCoroutine(Execute());
        }

        private void Update()
        {
            updateFunc?.Invoke();
        }

        IEnumerator Execute()
        {
            while (true)
            {
                if (waitForEndOfFrame == null)
                    waitForEndOfFrame = new WaitForEndOfFrame();
                yield return waitForEndOfFrame;
                captureFunc?.Invoke();
            }
        }
    }
}
