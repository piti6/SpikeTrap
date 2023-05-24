using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditor.Profiling.ModuleEditor;
using UnityEngine.UIElements;

namespace LightningProfiler
{
    internal class ModuleDetailsViewController : ViewController
    {
        private static class ProfilerMarkers
        {
            public static readonly ProfilerMarker k_LoadAllCounters = new ProfilerMarker("ModuleEditor.LoadAllCounters");

            public static readonly ProfilerMarker k_FetchCounters = new ProfilerMarker("ModuleEditor.FetchCounters");

            public static readonly ProfilerMarker k_FormatCountersForDisplay = new ProfilerMarker("ModuleEditor.FormatCountersForDisplay");

            public static readonly ProfilerMarker k_RebuildCountersUI = new ProfilerMarker("ModuleEditor.RebuildCountersUI");
        }

        private class TreeViewItemData : TreeViewItem<string>
        {
            private static int m_NextId;

            public TreeViewItemData(string data, List<TreeViewItem<string>> children = null)
                : base(m_NextId++, data, children)
            {
            }

            public static void ResetNextId()
            {
                m_NextId = 0;
            }
        }

        private class GroupTreeViewItemData : TreeViewItemData
        {
            public GroupTreeViewItemData(string group)
                : base(group)
            {
            }
        }

        private class CategoryTreeViewItemData : TreeViewItemData
        {
            public CategoryTreeViewItemData(string category)
                : base(category)
            {
            }
        }

        private class CounterTreeViewItemData : TreeViewItemData
        {
            public string category
            {
                get
                {
                    CategoryTreeViewItemData categoryTreeViewItemData = base.parent as CategoryTreeViewItemData;
                    return categoryTreeViewItemData.data;
                }
            }

            public CounterTreeViewItemData(string counter)
                : base(counter)
            {
            }
        }

        private abstract class CounterItem : VisualElement
        {
            private const string k_UssClass_Label = "counter-list-view-item__label";

            public Label titleLabel
            {
                get;
            }

            public CounterItem()
            {
                titleLabel = new Label();
                titleLabel.AddToClassList("counter-list-view-item__label");
                Add(titleLabel);
            }
        }

        private class CounterListViewItem : CounterItem
        {
            private const string k_UssClass = "counter-list-view-item";

            public CounterListViewItem()
            {
                AddToClassList("counter-list-view-item");
                DragIndicator dragIndicator = new DragIndicator();
                Add(dragIndicator);
                dragIndicator.SendToBack();
            }
        }

        private class CounterTreeViewItem : CounterItem
        {
            private const string k_UssClass = "counter-tree-view-item";

            private const string k_UssClass_Unselectable = "counter-tree-view-item__unselectable";

            public CounterTreeViewItem()
            {
                AddToClassList("counter-tree-view-item");
            }

            public void SetSelectable(bool selectable)
            {
                EnableInClassList("counter-tree-view-item__unselectable", !selectable);
            }
        }

        private const string k_UssSelector_CurrentModuleDetailsTitleTextField = "current-module-details__title-text-field";

        private const string k_UssSelector_CurrentModuleDetailsChartCountersTitleLabel = "current-module-details__chart-counters__title-label";

        private const string k_UssSelector_CurrentModuleDetailsChartCountersDescriptionLabel = "current-module-details__chart-counters__description-label";

        private const string k_UssSelector_CurrentModuleDetailsChartCountersCountLabel = "current-module-details__chart-counters__count-label";

        private const string k_UssSelector_CurrentModuleDetailsChartCountersRemoveSelectedToolbarButton = "current-module-details__chart-counters__remove-selected-toolbar-button";

        private const string k_UssSelector_CurrentModuleDetailsChartCountersSelectAllToolbarButton = "current-module-details__chart-counters__select-all-toolbar-button";

        private const string k_UssSelector_CurrentModuleDetailsChartCountersDeselectAllToolbarButton = "current-module-details__chart-counters__deselect-all-toolbar-button";

        private const string k_UssSelector_CurrentModuleDetailsChartCountersListView = "current-module-details__chart-counters__list-view";

        private const string k_UssSelector_CurrentModuleDetailsDeleteButton = "current-module-details__delete-button";

        private const string k_UssSelector_AllCountersTitleLabel = "all-counters__title-label";

        private const string k_UssSelector_AllCountersDescriptionLabel = "all-counters__description-label";

        private const string k_UssSelector_AllCountersTreeView = "all-counters__tree-view";

        private const string k_UssSelector_AllCountersAddSelectedToolbarButton = "all-counters__add-selected-toolbar-button";

        private const string k_UssSelector_ModuleDetailsConfirmButton = "module-details__confirm-button";

        private const string k_UssSelector_ModuleDetailsNoModuleSelectedLabel = "module-details__no-module-selected-label";

        private const string k_UssSelector_DragAndDropTargetHover = "drag-and-drop__drop-target--hover";

        private const string k_AllCountersTreeViewDataKey = "all-counters__tree-view__data-key";

        private const int k_MaximumTitleLength = 40;

        private ModuleData m_Module;

        private List<ITreeViewItem> m_TreeDataItems;

        private TextField m_TitleTextField;

        private Label m_ChartCountersTitleLabel;

        private Label m_ChartCountersDescriptionLabel;

        private Label m_ChartCountersCountLabel;

        private Button m_ChartCountersRemoveSelectedToolbarButton;

        private Button m_ChartCountersSelectAllToolbarButton;

        private Button m_ChartCountersDeselectAllToolbarButton;

        private ListView m_ChartCountersListView;

        private Button m_DeleteModuleButton;

        private Label m_AllCountersTitleLabel;

        private Label m_AllCountersDescriptionLabel;

        private InternalTreeView m_AllCountersTreeView;

        private Button m_AllCountersAddSelectedButton;

        private Button m_ConfirmButton;

        private Label m_NoModuleSelectedLabel;

        private bool m_isConnectedToEditor;

        public event Action<ModuleData> onDeleteModule;

        public event Action onConfirmChanges;

        public event Action onModuleNameChanged;

        public ModuleDetailsViewController(bool isConnectedToEditor)
        {
            m_isConnectedToEditor = isConnectedToEditor;
        }

        public override void ConfigureView(VisualElement root)
        {
            base.ConfigureView(root);
            m_TitleTextField.RegisterValueChangedCallback(OnTitleChanged);
            m_ChartCountersTitleLabel.text = "Counters";
            m_ChartCountersDescriptionLabel.text = "Add counters to be displayed by the module.";
            m_ChartCountersRemoveSelectedToolbarButton.text = "Remove Selected";
            m_ChartCountersRemoveSelectedToolbarButton.clicked += RemoveSelectedCountersFromModule;
            m_ChartCountersSelectAllToolbarButton.text = "Select All";
            m_ChartCountersSelectAllToolbarButton.clicked += SelectAllChartCounters;
            m_ChartCountersDeselectAllToolbarButton.text = "Deselect All";
            m_ChartCountersDeselectAllToolbarButton.clicked += DeselectAllChartCounters;
            m_ChartCountersListView.makeItem = MakeListViewItem;
            m_ChartCountersListView.bindItem = BindListViewItem;
            m_ChartCountersListView.selectionType = SelectionType.Multiple;
            m_ChartCountersListView.reorderable = true;
            m_ChartCountersListView.itemIndexChanged += OnListViewItemMoved;
            m_DeleteModuleButton.text = "Delete Module";
            m_DeleteModuleButton.clicked += DeleteModule;
            m_AllCountersTitleLabel.text = "Available Counters";
            m_AllCountersDescriptionLabel.text = "Select counters in the list below to add them to the selected module's counters. This list includes all built-in Unity counters, as well as any User-defined counters present upon load in the Profiler's data stream.";
            m_AllCountersTreeView.makeItem = MakeTreeViewItem;
            m_AllCountersTreeView.bindItem = BindTreeViewItem;
            m_AllCountersTreeView.viewDataKey = "all-counters__tree-view__data-key";
            m_AllCountersTreeView.selectionType = SelectionType.Multiple;
            m_AllCountersTreeView.onSelectionChange += OnTreeViewSelectionChanged;
            m_AllCountersTreeView.onItemsChosen += OnTreeViewSelectionChosen;
            m_AllCountersAddSelectedButton.text = "Add Selected";
            m_AllCountersAddSelectedButton.clicked += AddSelectedTreeViewCountersToModule;
            m_ConfirmButton.text = "Save Changes";
            m_ConfirmButton.clicked += ConfirmChanges;
            m_NoModuleSelectedLabel.text = "Select a custom module from the list or add a new custom module.";
            LoadAllCounters();
        }

        public void SetModule(ModuleData module)
        {
            m_Module = module;
            m_ChartCountersListView.ClearSelection();
            if (m_Module.isEditable)
            {
                m_TitleTextField.SetValueWithoutNotify(m_Module.localizedName);
                UpdateChartCountersCountLabel();
                m_ChartCountersListView.itemsSource = m_Module.chartCounters;
                m_ChartCountersListView.Rebuild();
                m_AllCountersTreeView.Rebuild();
                m_NoModuleSelectedLabel.visible = false;
            }
            else
            {
                m_NoModuleSelectedLabel.visible = true;
            }
        }

        public void SetNoModuleSelected()
        {
            m_NoModuleSelectedLabel.visible = true;
        }

        protected override void CollectViewElements(VisualElement root)
        {
            base.CollectViewElements(root);
            m_TitleTextField = root.Q<TextField>("current-module-details__title-text-field");
            m_ChartCountersTitleLabel = root.Q<Label>("current-module-details__chart-counters__title-label");
            m_ChartCountersDescriptionLabel = root.Q<Label>("current-module-details__chart-counters__description-label");
            m_ChartCountersCountLabel = root.Q<Label>("current-module-details__chart-counters__count-label");
            m_ChartCountersRemoveSelectedToolbarButton = root.Q<Button>("current-module-details__chart-counters__remove-selected-toolbar-button");
            m_ChartCountersSelectAllToolbarButton = root.Q<Button>("current-module-details__chart-counters__select-all-toolbar-button");
            m_ChartCountersDeselectAllToolbarButton = root.Q<Button>("current-module-details__chart-counters__deselect-all-toolbar-button");
            m_ChartCountersListView = root.Q<ListView>("current-module-details__chart-counters__list-view");
            m_DeleteModuleButton = root.Q<Button>("current-module-details__delete-button");
            m_AllCountersTitleLabel = root.Q<Label>("all-counters__title-label");
            m_AllCountersDescriptionLabel = root.Q<Label>("all-counters__description-label");
            m_AllCountersTreeView = root.Q<InternalTreeView>("all-counters__tree-view");
            m_AllCountersAddSelectedButton = root.Q<Button>("all-counters__add-selected-toolbar-button");
            m_ConfirmButton = root.Q<Button>("module-details__confirm-button");
            m_NoModuleSelectedLabel = root.Q<Label>("module-details__no-module-selected-label");
        }

        private void LoadAllCounters()
        {
            using (ProfilerMarkers.k_LoadAllCounters.Auto())
            {
                ProfilerMarkers.k_FetchCounters.Begin();
                CounterCollector counterCollector = new CounterCollector();
                SortedDictionary<string, List<string>> systemCounters;
                SortedDictionary<string, List<string>> userCounters;
                if (m_isConnectedToEditor)
                {
                    counterCollector.LoadEditorCounters(out systemCounters, out userCounters);
                }
                else
                {
                    counterCollector.LoadCounters(out systemCounters, out userCounters);
                }

                ProfilerMarkers.k_FetchCounters.End();
                ProfilerMarkers.k_FormatCountersForDisplay.Begin();
                m_TreeDataItems = new List<ITreeViewItem>();
                TreeViewItemData.ResetNextId();
                AddCounterGroupToTreeDataItems(systemCounters, "Unity", m_TreeDataItems);
                AddCounterGroupToTreeDataItems(userCounters, "User", m_TreeDataItems);
                ProfilerMarkers.k_FormatCountersForDisplay.End();
                ProfilerMarkers.k_RebuildCountersUI.Begin();
                m_AllCountersTreeView.rootItems = m_TreeDataItems;
                m_AllCountersTreeView.Rebuild();
                ProfilerMarkers.k_RebuildCountersUI.End();
            }
        }

        private void AddCounterGroupToTreeDataItems(SortedDictionary<string, List<string>> counterDictionary, string groupName, List<ITreeViewItem> treeDataItems)
        {
            if (counterDictionary.Count == 0)
            {
                return;
            }

            GroupTreeViewItemData groupTreeViewItemData = new GroupTreeViewItemData(groupName);
            foreach (string key in counterDictionary.Keys)
            {
                CategoryTreeViewItemData categoryTreeViewItemData = new CategoryTreeViewItemData(key);
                List<ITreeViewItem> list = new List<ITreeViewItem>();
                foreach (string item in counterDictionary[key])
                {
                    list.Add(new CounterTreeViewItemData(item));
                }

                categoryTreeViewItemData.AddChildren(list);
                groupTreeViewItemData.AddChild(categoryTreeViewItemData);
            }

            treeDataItems.Add(groupTreeViewItemData);
        }

        private VisualElement MakeListViewItem()
        {
            return new CounterListViewItem();
        }

        private void BindListViewItem(VisualElement element, int index)
        {
            ProfilerCounterData profilerCounterData = m_Module.chartCounters[index];
            CounterListViewItem counterListViewItem = element as CounterListViewItem;
            Label titleLabel = counterListViewItem.titleLabel;
            titleLabel.text = profilerCounterData.m_Name;
        }

        private VisualElement MakeTreeViewItem()
        {
            return new CounterTreeViewItem();
        }

        private void BindTreeViewItem(VisualElement element, ITreeViewItem item)
        {
            TreeViewItemData treeViewItemData = item as TreeViewItemData;
            CounterTreeViewItem counterTreeViewItem = element as CounterTreeViewItem;
            Label titleLabel = counterTreeViewItem.titleLabel;
            titleLabel.text = treeViewItemData.data;
            bool flag = false;
            CounterTreeViewItemData counterTreeViewItemData = default(CounterTreeViewItemData);
            int num;
            if (m_Module != null)
            {
                counterTreeViewItemData = (treeViewItemData as CounterTreeViewItemData);
                num = ((counterTreeViewItemData != null) ? 1 : 0);
            }
            else
            {
                num = 0;
            }

            if (num != 0)
            {
                string category = counterTreeViewItemData.category;
                string data = counterTreeViewItemData.data;
                flag = m_Module.ContainsChartCounter(data, category);
            }

            counterTreeViewItem.SetSelectable(!flag);
        }

        private void OnTitleChanged(ChangeEvent<string> evt)
        {
            string newValue = evt.newValue;
            if (newValue.Length > 40)
            {
                m_TitleTextField.SetValueWithoutNotify(evt.previousValue);
                return;
            }

            m_Module.SetName(evt.newValue);
            this.onModuleNameChanged?.Invoke();
        }

        private void OnTreeViewSelectionChanged(IEnumerable<ITreeViewItem> selectedItems)
        {
            List<int> list = new List<int>();
            foreach (ITreeViewItem selectedItem in selectedItems)
            {
                if (!selectedItem.hasChildren)
                {
                    list.Add(selectedItem.id);
                    continue;
                }

                int id = selectedItem.id;
                if (m_AllCountersTreeView.IsExpanded(id))
                {
                    m_AllCountersTreeView.CollapseItem(id);
                }
                else
                {
                    m_AllCountersTreeView.ExpandItem(id);
                }
            }

            m_AllCountersTreeView.SetSelectionWithoutNotify(list);
        }

        private void OnTreeViewSelectionChosen(IEnumerable<ITreeViewItem> selectedItems)
        {
            AddSelectedTreeViewCountersToModule();
        }

        private void AddSelectedTreeViewCountersToModule()
        {
            IEnumerable<ITreeViewItem> selectedItems = m_AllCountersTreeView.selectedItems;
            foreach (ITreeViewItem item in selectedItems)
            {
                CounterTreeViewItemData counterTreeViewItemData = item as CounterTreeViewItemData;
                if (counterTreeViewItemData != null)
                {
                    ProfilerCounterData profilerCounterData = default(ProfilerCounterData);
                    profilerCounterData.m_Category = counterTreeViewItemData.category;
                    profilerCounterData.m_Name = counterTreeViewItemData.data;
                    ProfilerCounterData counter = profilerCounterData;
                    AddCounterToModuleWithoutUIRefresh(counter);
                }
            }

            m_AllCountersTreeView.ClearSelection();
            m_ChartCountersListView.Rebuild();
            m_AllCountersTreeView.Rebuild();
            UpdateChartCountersCountLabel();
        }

        private void AddCounterToModuleWithoutUIRefresh(ProfilerCounterData counter)
        {
            if (!m_Module.hasMaximumChartCounters && !m_Module.ContainsChartCounter(counter))
            {
                m_Module.AddChartCounter(counter);
            }
        }

        private void RemoveSelectedCountersFromModule()
        {
            List<int> list = m_ChartCountersListView.selectedIndices.ToList();
            list.Sort((int a, int b) => b.CompareTo(a));
            for (int i = 0; i < list.Count; i++)
            {
                int index = list[i];
                m_Module.RemoveChartCounterAtIndex(index);
            }

            m_ChartCountersListView.ClearSelection();
            m_ChartCountersListView.Rebuild();
            m_AllCountersTreeView.Rebuild();
            UpdateChartCountersCountLabel();
        }

        private void ConfirmChanges()
        {
            this.onConfirmChanges?.Invoke();
        }

        private void DeleteModule()
        {
            string localizedString = "Delete Module";
            string localizedString2 = "Are you sure you want to delete the module '{0}'?";
            string message = string.Format(localizedString2, m_Module.localizedName);
            string localizedString3 = "Delete";
            string localizedString4 = "Cancel";
            if (EditorUtility.DisplayDialog(localizedString, message, localizedString3, localizedString4))
            {
                this.onDeleteModule(m_Module);
            }
        }

        private void SelectAllChartCounters()
        {
            m_ChartCountersListView.SelectAll();
        }

        private void DeselectAllChartCounters()
        {
            m_ChartCountersListView.ClearSelection();
        }

        private void UpdateChartCountersCountLabel()
        {
            m_ChartCountersCountLabel.text = $"{m_Module.chartCounters.Count}/{10}";
        }

        private void OnListViewItemMoved(int previousIndex, int newIndex)
        {
            m_Module.SetUpdatedEditedStateForOrderIndexChange();
        }
    }
}
