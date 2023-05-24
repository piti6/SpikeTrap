using System;
using System.Collections.Generic;
using UnityEditor.Profiling.ModuleEditor;
using UnityEngine.UIElements;

namespace LightningProfiler
{
    internal class ModuleListViewController : ViewController
    {
        private class ModuleListViewItem : VisualElement
        {
            private const string k_UssClass = "module-list-view-item";

            private const string k_UssClass_Label = "module-list-view-item__label";

            private const string k_UssClass_Unselectable = "module-list-view-item__unselectable";

            public Label titleLabel
            {
                get;
            }

            public ModuleListViewItem()
            {
                AddToClassList("module-list-view-item");
                DragIndicator child = new DragIndicator();
                Add(child);
                titleLabel = new Label();
                titleLabel.AddToClassList("module-list-view-item__label");
                Add(titleLabel);
            }

            public void SetSelectable(bool selectable)
            {
                EnableInClassList("module-list-view-item__unselectable", !selectable);
            }
        }

        private const string k_UssSelector_TitleLabel = "modules__title-label";

        private const string k_UssSelector_ListView = "modules__list-view";

        private const string k_UssSelector_CreateButton = "modules__create-button";

        private readonly List<ModuleData> m_Modules;

        private Label m_TitleLabel;

        private ListView m_ListView;

        private Button m_CreateButton;

        public event Action onCreateModule;

        public event Action<ModuleData, int> onModuleAtIndexSelected;

        public ModuleListViewController(List<ModuleData> modules)
        {
            m_Modules = modules;
        }

        public override void ConfigureView(VisualElement root)
        {
            base.ConfigureView(root);
            m_TitleLabel.text = "Profiler Modules";
            m_ListView.makeItem = MakeListViewItem;
            m_ListView.bindItem = BindListViewItem;
            m_ListView.selectionType = SelectionType.Single;
            m_ListView.reorderable = true;
            m_ListView.itemIndexChanged += OnListViewItemMoved;
            m_ListView.onSelectionChange += OnListViewSelectionChange;
            m_ListView.itemsSource = m_Modules;
            m_CreateButton.text = "Add";
            m_CreateButton.clicked += AddModule;
        }

        public void SelectModuleAtIndex(int index)
        {
            m_ListView.SetSelection(index);
        }

        public void ClearSelection()
        {
            m_ListView.ClearSelection();
        }

        public void Refresh()
        {
            m_ListView.Rebuild();
        }

        public void RefreshSelectedListItem()
        {
            int selectedIndex = m_ListView.selectedIndex;
            m_ListView.RefreshItem(selectedIndex);
        }

        protected override void CollectViewElements(VisualElement root)
        {
            base.CollectViewElements(root);
            m_TitleLabel = root.Q<Label>("modules__title-label");
            m_ListView = root.Q<ListView>("modules__list-view");
            m_CreateButton = root.Q<Button>("modules__create-button");
        }

        private VisualElement MakeListViewItem()
        {
            return new ModuleListViewItem();
        }

        private void BindListViewItem(VisualElement element, int index)
        {
            ModuleData moduleData = m_Modules[index];
            ModuleListViewItem moduleListViewItem = element as ModuleListViewItem;
            Label titleLabel = moduleListViewItem.titleLabel;
            titleLabel.text = moduleData.localizedName;
            bool isEditable = moduleData.isEditable;
            moduleListViewItem.SetSelectable(isEditable);
        }

        private void OnListViewSelectionChange(IEnumerable<object> selectedItems)
        {
            int selectedIndex = m_ListView.selectedIndex;
            ModuleData arg = (selectedIndex != -1) ? m_Modules[selectedIndex] : null;
            this.onModuleAtIndexSelected(arg, selectedIndex);
        }

        private void OnListViewItemMoved(int previousIndex, int newIndex)
        {
            foreach (ModuleData module in m_Modules)
            {
                module.SetUpdatedEditedStateForOrderIndexChange();
            }
        }

        private void AddModule()
        {
            this.onCreateModule();
        }
    }
}
