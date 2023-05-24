using System;
using UnityEngine.UIElements;

namespace LightningProfiler
{
    //
    // 요약:
    //     Provides a single view of content for a ProfilerModule displayed in the Profiler
    //     window.
    public abstract class ProfilerModuleViewController : IDisposable
    {
        private VisualElement m_View;

        public bool Disposed
        {
            get;
            private set;
        }

        internal VisualElement View
        {
            get
            {
                if (m_View == null)
                {
                    m_View = CreateView();
                }

                return m_View;
            }
        }

        //
        // 요약:
        //     The Profiler window that the view controller belongs to.
        protected ProfilerWindow ProfilerWindow
        {
            get;
        }

        protected ProfilerModuleViewController(ProfilerWindow profilerWindow)
        {
            ProfilerWindow = profilerWindow;
        }

        //
        // 요약:
        //     Disposes the view controller. Unity calls this method automatically when the
        //     view controller is no longer required, and its view will be removed from the
        //     window hierarchy.
        //
        // 매개 변수:
        //   disposing:
        //     The flag to indicate whether the method call comes from a Dispose method or from
        //     a finalizer. A bool. When the value is true, the method call comes from a Dispose
        //     method. Otherwise, the method call comes from a finalizer.
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        //
        // 요약:
        //     Creates the view controller’s view. Unity calls this method automatically when
        //     it is about to display the view controller’s view for the first time.
        //
        // 반환 값:
        //     Returns the view controller’s view. A UIElements.VisualElement.
        protected abstract VisualElement CreateView();

        protected virtual void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    m_View?.RemoveFromHierarchy();
                }

                Disposed = true;
            }
        }
    }
}
