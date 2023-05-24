using System;
using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.Editor;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;

namespace LightningProfiler
{
    //
    // 요약:
    //     Represents a Profiler module in the Profiler window.
    [Serializable]
    public abstract class ProfilerModule
    {
        internal readonly struct InitializationArgs
        {
            public string Identifier
            {
                get;
            }

            public string DisplayName
            {
                get;
            }

            public string IconPath
            {
                get;
            }

            public ProfilerWindow ProfilerWindow
            {
                get;
            }

            public InitializationArgs(string identifier, string name, string iconPath, ProfilerWindow profilerWindow)
            {
                Identifier = identifier;
                DisplayName = name;
                IconPath = iconPath;
                ProfilerWindow = profilerWindow;
            }
        }

        internal class LocalizationResource : ProfilerModuleMetadataAttribute.IResource
        {
            string ProfilerModuleMetadataAttribute.IResource.GetLocalizedString(string key)
            {
                return LocalizationDatabase.GetLocalizedString(key);
            }
        }

        private static class Markers
        {
            public static readonly ProfilerMarker updateModule = new ProfilerMarker("ProfilerModule.Update");

            public static readonly ProfilerMarker drawChartView = new ProfilerMarker("ProfilerModule.DrawChartView");
        }

        [SerializeField]
        private string m_Identifier;

        private ProfilerModuleViewController m_DetailsViewController;

        internal const int k_UndefinedOrderIndex = int.MaxValue;

        private const string k_ProfilerModuleActiveStatePreferenceKeyFormat = "ProfilerModule.{0}.Active";

        private const string k_ProfilerModuleOrderIndexPreferenceKeyFormat = "ProfilerModule.{0}.OrderIndex";

        private const int k_NoFrameIndex = int.MinValue;

        private protected ProfilerChart m_Chart;

        [NonSerialized]
        private bool m_Active = false;

        private int m_LastUpdatedFrameIndex = int.MinValue;

        //
        // 요약:
        //     The module’s display name.
        public string DisplayName
        {
            get;
            private set;
        }

        internal string Identifier => m_Identifier;

        [field: NonSerialized]
        internal ProfilerCounterDescriptor[] ChartCounters
        {
            get;
            private set;
        }

        //
        // 요약:
        //     The Profiler window that the module instance belongs to.
        protected ProfilerWindow ProfilerWindow
        {
            get;
            private set;
        }

        private protected string IconPath
        {
            get;
            private set;
        }

        [field: NonSerialized]
        private ProfilerModuleChartType ChartType
        {
            get;
        }

        [field: NonSerialized]
        private string[] AutoEnabledCategoryNames
        {
            get;
            set;
        }

        internal virtual ProfilerArea area => (ProfilerArea)(-1);

        internal bool active
        {
            get
            {
                return m_Active;
            }
            set
            {
                if (value != active)
                {
                    m_Active = value;
                    ApplyActiveState();
                    SaveActiveState();
                    if (!active && Chart != null)
                    {
                        Chart.Close();
                    }
                }
            }
        }

        internal int orderIndex
        {
            get
            {
                return EditorPrefs.GetInt(orderIndexPreferenceKey, defaultOrderIndex);
            }
            set
            {
                EditorPrefs.SetInt(orderIndexPreferenceKey, value);
            }
        }

        internal ProfilerChart Chart => m_Chart;

        private protected virtual string activeStatePreferenceKey => $"ProfilerModule.{Identifier}.Active";

        private protected string orderIndexPreferenceKey => $"ProfilerModule.{Identifier}.OrderIndex";

        private protected int firstFrameIndexWithHistoryOffset => ProfilerDriver.lastFrameIndex + 1 - ProfilerUserSettings.frameCount;

        private protected virtual string legacyPreferenceKey => null;

        private protected virtual int defaultOrderIndex => int.MaxValue;

        private protected string[] GetAutoEnabledCategoryNames => AutoEnabledCategoryNames;

        protected ProfilerModule(ProfilerCounterDescriptor[] chartCounters, ProfilerModuleChartType defaultChartType = ProfilerModuleChartType.Line, string[] autoEnabledCategoryNames = null)
        {
            ChartCounters = chartCounters;
            ChartType = defaultChartType;
            if (autoEnabledCategoryNames == null || autoEnabledCategoryNames.Length == 0)
            {
                autoEnabledCategoryNames = UniqueCategoryNamesInCounters(chartCounters);
            }

            AutoEnabledCategoryNames = autoEnabledCategoryNames;
        }

        //
        // 요약:
        //     Creates a View Controller object that draws the Profiler module’s Details View
        //     in the Profiler window. Unity calls this method automatically when the module
        //     is selected in the Profiler window.
        //
        // 반환 값:
        //     Returns a ProfilerModuleViewController derived object that draws the module’s
        //     Details View in the Profiler window. The default value is a view controller that
        //     displays a list of the module’s chart counters alongside their current values
        //     in the selected frame.
        public virtual ProfilerModuleViewController CreateDetailsViewController()
        {
            return new StandardDetailsViewController(ProfilerWindow, ChartCounters);
        }

        internal void Initialize(InitializationArgs args)
        {
            m_Identifier = args.Identifier;
            DisplayName = args.DisplayName;
            IconPath = args.IconPath;
            ProfilerWindow = args.ProfilerWindow;
            LegacyModuleInitialize();
            AssertIsValid();
        }

        internal virtual void LegacyModuleInitialize()
        {
        }

        internal void AssertIsValid()
        {
            if (string.IsNullOrEmpty(Identifier))
            {
                throw new InvalidOperationException("The Profiler module '" + DisplayName + "' has an invalid identifier.");
            }

            if (string.IsNullOrEmpty(DisplayName))
            {
                throw new InvalidOperationException("The Profiler module '" + DisplayName + "' has an invalid name.");
            }

            if (string.IsNullOrEmpty(IconPath))
            {
                throw new InvalidOperationException("The Profiler module '" + DisplayName + "' has an invalid icon path.");
            }

            if (ProfilerWindow == null)
            {
                throw new InvalidOperationException("The Profiler module '" + DisplayName + "' has an invalid Profiler window reference.");
            }

            if (ChartCounters == null || ChartCounters.Length == 0)
            {
                throw new InvalidOperationException("The Profiler module '" + DisplayName + "' cannot have no chart counters.");
            }

            if (ChartCounters.Length > 10)
            {
                throw new InvalidOperationException($"The Profiler module '{DisplayName}' cannot have more than {10} chart counters.");
            }
        }

        internal VisualElement CreateDetailsView()
        {
            OnSelected();
            if (m_DetailsViewController != null)
            {
                throw new InvalidOperationException("A new details view was requested for the module '" + DisplayName + "' but the previous one has not been destroyed.");
            }

            m_DetailsViewController = CreateDetailsViewController();
            if (m_DetailsViewController == null)
            {
                throw new InvalidOperationException("A new details view controller was requested for the module '" + DisplayName + "' but none was provided.");
            }

            return m_DetailsViewController.View;
        }

        internal void CloseDetailsView()
        {
            OnDeselected();
            if (m_DetailsViewController != null)
            {
                m_DetailsViewController.Dispose();
                m_DetailsViewController = null;
            }
        }

        private string[] UniqueCategoryNamesInCounters(ProfilerCounterDescriptor[] counters)
        {
            HashSet<string> hashSet = new HashSet<string>();
            if (counters != null)
            {
                foreach (ProfilerCounterDescriptor profilerCounterDescriptor in counters)
                {
                    hashSet.Add(profilerCounterDescriptor.CategoryName);
                }
            }

            string[] array = new string[hashSet.Count];
            hashSet.CopyTo(array);
            return array;
        }

        internal virtual void OnEnable()
        {
            BuildChartIfNecessary();
            active = ReadActiveState();
        }

        internal virtual void OnDisable()
        {
            SaveViewSettings();
        }

        internal float GetMinimumChartHeight()
        {
            return m_Chart.GetMinimumHeight();
        }

        internal int DrawChartView(Rect chartRect, int currentFrame, bool isSelected, int lastVisibleFrameIndex)
        {
            using (Markers.drawChartView.Auto())
            {
                bool flag = m_LastUpdatedFrameIndex != lastVisibleFrameIndex;
                if (Event.current.type == EventType.Repaint && flag)
                {
                    Update();
                }

                currentFrame = m_Chart.DoChartGUI(chartRect, currentFrame, isSelected);
                if (isSelected)
                {
                    DrawChartOverlay(m_Chart.lastChartRect);
                }

                return currentFrame;
            }
        }

        internal virtual void Update()
        {
            using (Markers.updateModule.Auto())
            {
                UpdateChart();
                m_LastUpdatedFrameIndex = ProfilerDriver.lastFrameIndex;
            }
        }

        internal virtual void Rebuild()
        {
            RebuildChart();
        }

        internal void OnLostFocus()
        {
            m_Chart.OnLostFocus();
        }

        internal virtual void Clear()
        {
            m_LastUpdatedFrameIndex = int.MinValue;
            m_Chart?.ResetChartState();
        }

        internal virtual void OnNativePlatformSupportModuleChanged()
        {
        }

        internal virtual void SaveViewSettings()
        {
        }

        internal void ToggleActive()
        {
            active = !active;
        }

        internal void ResetOrderIndexToDefault()
        {
            EditorPrefs.DeleteKey(orderIndexPreferenceKey);
        }

        internal void DeleteAllPreferences()
        {
            EditorPrefs.DeleteKey(activeStatePreferenceKey);
            EditorPrefs.DeleteKey(orderIndexPreferenceKey);
            m_Chart.DeleteSettings();
        }

        internal void InternalSetChartCounters(ProfilerCounterDescriptor[] chartCounters)
        {
            ChartCounters = chartCounters;
        }

        internal void InternalSetAutoEnabledCategoryNames(string[] autoEnabledCategoryNames)
        {
            AutoEnabledCategoryNames = autoEnabledCategoryNames;
        }

        private protected virtual void OnSelected()
        {
        }

        private protected virtual void OnDeselected()
        {
        }

        private protected virtual void ApplyActiveState()
        {
            ProfilerWindow.SetCategoriesInUse(AutoEnabledCategoryNames, active);
        }

        private protected virtual bool ReadActiveState()
        {
            return EditorPrefs.GetBool(activeStatePreferenceKey, defaultValue: true);
        }

        private protected virtual void SaveActiveState()
        {
            EditorPrefs.SetBool(activeStatePreferenceKey, active);
        }

        private protected virtual ProfilerChart InstantiateChart(float defaultChartScale, float chartMaximumScaleInterpolationValue)
        {
            m_Chart = new ProfilerChart(area, ChartType, defaultChartScale, chartMaximumScaleInterpolationValue, ChartCounters.Length, Identifier, DisplayName, IconPath);
            return m_Chart;
        }

        private protected virtual void UpdateChartOverlay(int firstEmptyFrame, int firstFrame, int frameCount)
        {
        }

        private protected virtual void DrawChartOverlay(Rect chartRect)
        {
        }

        private protected void RebuildChart()
        {
            bool forceRebuild = true;
            BuildChartIfNecessary(forceRebuild);
        }

        private protected void SetName(string name)
        {
            DisplayName = name;
        }

        private void BuildChartIfNecessary(bool forceRebuild = false)
        {
            if (forceRebuild || m_Chart == null)
            {
                InitializeChart();
                UpdateChart();
            }

            m_Chart.LoadAndBindSettings(legacyPreferenceKey);
        }

        private void InitializeChart()
        {
            bool flag = ChartType == ProfilerModuleChartType.StackedTimeArea;
            float defaultChartScale = flag ? 0.001f : 1f;
            float chartMaximumScaleInterpolationValue = flag ? (-1f) : 0f;
            m_Chart = InstantiateChart(defaultChartScale, chartMaximumScaleInterpolationValue);
            m_Chart.ConfigureChartSeries(ProfilerUserSettings.frameCount, ChartCounters);
            ConfigureChartSelectionCallbacks();
        }

        private void ConfigureChartSelectionCallbacks()
        {
            m_Chart.selected += OnChartSelected;
            m_Chart.closed += OnChartClosed;
        }

        private void UpdateChart()
        {
            BuildChartIfNecessary();
            int frameCount = ProfilerUserSettings.frameCount;
            int firstFrameIndexWithHistoryOffset = this.firstFrameIndexWithHistoryOffset;
            int firstFrame = Mathf.Max(ProfilerDriver.firstFrameIndex, firstFrameIndexWithHistoryOffset);
            m_Chart.UpdateData(firstFrameIndexWithHistoryOffset, firstFrame, frameCount);
            UpdateChartOverlay(firstFrameIndexWithHistoryOffset, firstFrame, frameCount);
            m_Chart.UpdateScaleValuesIfNecessary(firstFrameIndexWithHistoryOffset, firstFrame, frameCount);
        }

        private void OnChartSelected(Chart chart)
        {
            ProfilerWindow.selectedModule = this;
        }

        private void OnChartClosed(Chart chart)
        {
            ProfilerWindow.CloseModule(this);
        }
    }
}
