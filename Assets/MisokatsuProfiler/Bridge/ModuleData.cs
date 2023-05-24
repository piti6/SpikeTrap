using System;
using System.Collections.Generic;
using UnityEditor.Profiling;
using UnityEngine;

namespace LightningProfiler
{
    [Serializable]
    internal class ModuleData
    {
        public enum EditedState
        {
            NoChanges,
            Updated,
            Created
        }

        public const int k_MaximumChartCountersCount = 10;

        [SerializeField]
        private string m_Name;

        [SerializeField]
        private List<ProfilerCounterData> m_ChartCounters = new List<ProfilerCounterData>();

        [SerializeField]
        private bool m_IsEditable;

        [SerializeField]
        private EditedState m_EditedState;

        public bool isEditable => m_IsEditable;

        public EditedState editedState => m_EditedState;

        public string name => m_Name;

        public string localizedName => name;

        public List<ProfilerCounterData> chartCounters => m_ChartCounters;

        public List<ProfilerCounterData> detailCounters => m_ChartCounters;

        public bool hasMaximumChartCounters => m_ChartCounters.Count >= 10;

        public string identifier
        {
            get;
            private set;
        }

        public string currentProfilerModuleIdentifier
        {
            get;
            private set;
        }

        public ModuleData(string identifier, string name, bool isEditable, bool newlyCreatedModule = false)
        {
            this.identifier = identifier;
            currentProfilerModuleIdentifier = identifier;
            m_Name = name;
            m_IsEditable = isEditable;
            m_EditedState = (newlyCreatedModule ? EditedState.Created : EditedState.NoChanges);
        }

        public static List<ModuleData> CreateDataRepresentationOfProfilerModules(List<ProfilerModule> modules)
        {
            List<ModuleData> list = new List<ModuleData>(modules.Count);
            for (int i = 0; i < modules.Count; i++)
            {
                ProfilerModule module = modules[i];
                ModuleData item = CreateWithProfilerModule(module);
                list.Add(item);
            }

            return list;
        }

        private static ModuleData CreateWithProfilerModule(ProfilerModule module)
        {
            bool isEditable = module is DynamicProfilerModule;
            ModuleData moduleData = new ModuleData(module.Identifier, module.DisplayName, isEditable);
            List<ProfilerCounterData> list = moduleData.m_ChartCounters = new List<ProfilerCounterData>(ProfilerCounterDataUtility.ConvertToLegacyCounterDatas(module.ChartCounters));
            return moduleData;
        }

        public void SetName(string name)
        {
            m_Name = name;
            identifier = name;
            SetUpdatedEditedStateIfNoChanges();
        }

        public void AddChartCounter(ProfilerCounterData counter)
        {
            m_ChartCounters.Add(counter);
            SetUpdatedEditedStateIfNoChanges();
        }

        public void RemoveChartCounterAtIndex(int index)
        {
            m_ChartCounters.RemoveAt(index);
            SetUpdatedEditedStateIfNoChanges();
        }

        public void SetUpdatedEditedStateForOrderIndexChange()
        {
            SetUpdatedEditedStateIfNoChanges();
        }

        public bool ContainsChartCounter(ProfilerCounterData counter)
        {
            bool result = false;
            foreach (ProfilerCounterData chartCounter in m_ChartCounters)
            {
                if (chartCounter.m_Category.Equals(counter.m_Category) && chartCounter.m_Name.Equals(counter.m_Name))
                {
                    result = true;
                    break;
                }
            }

            return result;
        }

        public bool ContainsChartCounter(string counter, string category)
        {
            return ContainsChartCounter(new ProfilerCounterData
            {
                m_Name = counter,
                m_Category = category
            });
        }

        private void SetUpdatedEditedStateIfNoChanges()
        {
            if (m_EditedState == EditedState.NoChanges)
            {
                m_EditedState = EditedState.Updated;
            }
        }
    }
}
