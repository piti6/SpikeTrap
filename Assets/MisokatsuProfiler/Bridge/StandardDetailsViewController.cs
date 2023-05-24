using System;
using System.Text;
using Unity.Profiling.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine.UIElements;

namespace LightningProfiler
{
    internal class StandardDetailsViewController : ProfilerModuleViewController
    {
        private const string k_UxmlResourceName = "StandardDetailsView.uxml";

        private const string k_UssSelector_StandardDetailsView__Label = "standard-details-view__label";

        private ProfilerCounterDescriptor[] m_Counters;

        private SelectableLabel m_Label;

        public StandardDetailsViewController(ProfilerWindow profilerWindow, ProfilerCounterDescriptor[] counters)
            : base(profilerWindow)
        {
            m_Counters = counters;
        }

        protected override VisualElement CreateView()
        {
            VisualTreeAsset visualTreeAsset = EditorGUIUtility.Load("StandardDetailsView.uxml") as VisualTreeAsset;
            TemplateContainer templateContainer = visualTreeAsset.Instantiate();
            m_Label = templateContainer.Q<SelectableLabel>("standard-details-view__label");
            ReloadData(base.ProfilerWindow.selectedFrameIndex);
            SubscribeToExternalEvents();
            return templateContainer;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UnsubscribeFromExternalEvents();
                base.Dispose(disposing);
            }
        }

        private void SubscribeToExternalEvents()
        {
            base.ProfilerWindow.SelectedFrameIndexChanged += OnSelectedFrameIndexChanged;
        }

        private void UnsubscribeFromExternalEvents()
        {
            base.ProfilerWindow.SelectedFrameIndexChanged -= OnSelectedFrameIndexChanged;
        }

        private void OnSelectedFrameIndexChanged(long selectedFrameIndex)
        {
            ReloadData(selectedFrameIndex);
        }

        private void ReloadData(long selectedFrameIndex)
        {
            m_Label.value = ConstructTextSummaryOfCounters(selectedFrameIndex);
        }

        private string ConstructTextSummaryOfCounters(long selectedFrameIndex)
        {
            int frame = Convert.ToInt32(selectedFrameIndex);
            StringBuilder stringBuilder = new StringBuilder();
            ProfilerCounterDescriptor[] counters = m_Counters;
            for (int i = 0; i < counters.Length; i++)
            {
                ProfilerCounterDescriptor profilerCounterDescriptor = counters[i];
                string formattedCounterValue = ProfilerDriver.GetFormattedCounterValue(frame, profilerCounterDescriptor.CategoryName, profilerCounterDescriptor.Name);
                stringBuilder.AppendLine($"{profilerCounterDescriptor}: {formattedCounterValue}");
            }

            return stringBuilder.ToString();
        }
    }
}
