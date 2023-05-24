using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEditor;
using UnityEditor.Profiling.ModuleEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightningProfiler
{
    internal class ModuleEditorWindow : EditorWindow
    {
        private const string k_UxmlResourceName = "ModuleEditorWindow.uxml";

        private const string k_UssSelectorModuleEditorWindowDark = "module-editor-window__dark";

        private const string k_UssSelectorModuleEditorWindowLight = "module-editor-window__light";

        private const int k_InvalidIndex = -1;

        private const string k_NewProfilerModuleDefaultName = "New Profiler Module {0}";

        private static readonly Vector2 k_MinimumWindowSize = new Vector2(720f, 405f);

        private const int kMaxModuleIndex = 256;

        private bool m_IsInitialized;

        [SerializeField]
        private List<ModuleData> m_Modules;

        [SerializeField]
        private int m_SelectedIndex;

        [SerializeField]
        private List<ModuleData> m_CreatedModules = new List<ModuleData>();

        [SerializeField]
        private List<ModuleData> m_DeletedModules = new List<ModuleData>();

        private bool m_ChangesHaveBeenConfirmed;

        [SerializeField]
        private int m_LastModuleNameIndex = 1;

        private bool m_isConnectedToEditor;

        private ModuleListViewController m_ModuleListViewController;

        private ModuleDetailsViewController m_ModuleDetailsViewController;

        internal List<ModuleData> Modules => m_Modules;

        public event Action<ReadOnlyCollection<ModuleData>, ReadOnlyCollection<ModuleData>> onChangesConfirmed;

        public static ModuleEditorWindow Present(List<ProfilerModule> modules, bool isConnectedToEditor)
        {
            ModuleEditorWindow windowDontShow = EditorWindow.GetWindowDontShow<ModuleEditorWindow>();
            windowDontShow.Initialize(modules, isConnectedToEditor);
            windowDontShow.ShowUtility();
            return windowDontShow;
        }

        public static bool TryGetOpenInstance(out ModuleEditorWindow moduleEditorWindow)
        {
            moduleEditorWindow = null;
            if (EditorWindow.HasOpenInstances<ModuleEditorWindow>())
            {
                moduleEditorWindow = EditorWindow.GetWindowDontShow<ModuleEditorWindow>();
            }

            return moduleEditorWindow != null;
        }

        private void Initialize(List<ProfilerModule> modules, bool isConnectedToEditor)
        {
            if (!m_IsInitialized)
            {
                base.minSize = k_MinimumWindowSize;
                base.titleContent = new GUIContent(LocalizationDatabase.GetLocalizedString("Profiler Module Editor"));
                m_Modules = ModuleData.CreateDataRepresentationOfProfilerModules(modules);
                m_SelectedIndex = IndexOfFirstEditableModule();
                m_IsInitialized = true;
                m_isConnectedToEditor = isConnectedToEditor;
                BuildWindow();
            }
        }

        private void OnEnable()
        {
            if (m_IsInitialized)
            {
                BuildWindow();
            }
        }

        private void OnGUI()
        {
            Event current = Event.current;
            if (current.type == EventType.Repaint)
            {
                base.hasUnsavedChanges = HasUnsavedChanges();
            }
        }

        public override void SaveChanges()
        {
            base.SaveChanges();
            ConfirmChanges(closeWindow: false);
        }

        private void BuildWindow()
        {
            VisualTreeAsset visualTreeAsset = EditorGUIUtility.Load("ModuleEditorWindow.uxml") as VisualTreeAsset;
            visualTreeAsset.CloneTree(base.rootVisualElement);
            string className = EditorGUIUtility.isProSkin ? "module-editor-window__dark" : "module-editor-window__light";
            base.rootVisualElement.AddToClassList(className);
            m_ModuleListViewController = new ModuleListViewController(m_Modules);
            m_ModuleListViewController.ConfigureView(base.rootVisualElement);
            m_ModuleListViewController.onCreateModule += CreateModule;
            m_ModuleListViewController.onModuleAtIndexSelected += OnModuleAtIndexSelected;
            m_ModuleDetailsViewController = new ModuleDetailsViewController(m_isConnectedToEditor);
            m_ModuleDetailsViewController.ConfigureView(base.rootVisualElement);
            m_ModuleDetailsViewController.onDeleteModule += DeleteModule;
            m_ModuleDetailsViewController.onConfirmChanges += ConfirmChanges;
            m_ModuleDetailsViewController.onModuleNameChanged += OnModuleNameChanged;
            base.saveChangesMessage = LocalizationDatabase.GetLocalizedString("Do you want to save the changes you made before closing?");
            if (m_SelectedIndex != -1)
            {
                m_ModuleListViewController.SelectModuleAtIndex(m_SelectedIndex);
            }
        }

        private void OnModuleAtIndexSelected(ModuleData module, int index)
        {
            m_SelectedIndex = index;
            if (index != -1)
            {
                m_ModuleDetailsViewController.SetModule(module);
            }
            else
            {
                m_ModuleDetailsViewController.SetNoModuleSelected();
            }
        }

        private void CreateModule()
        {
            IEnumerable<string> source = m_Modules.Select((ModuleData x) => x.name).Distinct();
            string text = string.Format("New Profiler Module {0}", "-");
            while (m_LastModuleNameIndex < 256)
            {
                string text2 = $"New Profiler Module {m_LastModuleNameIndex}";
                if (!source.Contains(text2))
                {
                    text = text2;
                    break;
                }

                m_LastModuleNameIndex++;
            }

            string identifier = text;
            ModuleData item = new ModuleData(identifier, text, isEditable: true, newlyCreatedModule: true);
            m_Modules.Add(item);
            m_CreatedModules.Add(item);
            m_ModuleListViewController.Refresh();
            int index = m_Modules.Count - 1;
            m_ModuleListViewController.SelectModuleAtIndex(index);
        }

        private void DeleteModule(ModuleData module)
        {
            m_Modules.Remove(module);
            int num = IndexOfModuleInCollection(module, m_CreatedModules);
            if (num != -1)
            {
                m_CreatedModules.RemoveAt(num);
            }
            else
            {
                m_DeletedModules.Add(module);
            }

            m_ModuleListViewController.Refresh();
            int num2 = IndexOfFirstEditableModule();
            if (num2 != -1)
            {
                m_ModuleListViewController.SelectModuleAtIndex(num2);
            }
            else
            {
                m_ModuleListViewController.ClearSelection();
            }
        }

        private void ConfirmChanges()
        {
            ConfirmChanges(closeWindow: true);
        }

        private void ConfirmChanges(bool closeWindow)
        {
            if (ValidateChanges(out string localizedErrorDescription))
            {
                this.onChangesConfirmed?.Invoke(m_Modules.AsReadOnly(), m_DeletedModules.AsReadOnly());
                m_ChangesHaveBeenConfirmed = true;
                if (closeWindow)
                {
                    Close();
                }

                return;
            }

            if (closeWindow)
            {
                string localizedString = LocalizationDatabase.GetLocalizedString("Save Changes Failed");
                string message = localizedErrorDescription;
                string localizedString2 = LocalizationDatabase.GetLocalizedString("OK");
                EditorUtility.DisplayDialog(localizedString, message, localizedString2);
                return;
            }

            throw new InvalidOperationException(localizedErrorDescription);
        }

        private void OnModuleNameChanged()
        {
            m_ModuleListViewController.RefreshSelectedListItem();
        }

        private int IndexOfFirstEditableModule()
        {
            int result = -1;
            for (int i = 0; i < m_Modules.Count; i++)
            {
                ModuleData moduleData = m_Modules[i];
                if (moduleData.isEditable)
                {
                    result = i;
                    break;
                }
            }

            return result;
        }

        private int IndexOfModuleInCollection(ModuleData module, List<ModuleData> modules)
        {
            int result = -1;
            for (int i = 0; i < modules.Count; i++)
            {
                ModuleData moduleData = modules[i];
                if (moduleData.Equals(module))
                {
                    result = i;
                    break;
                }
            }

            return result;
        }

        private bool HasUnsavedChanges()
        {
            if (m_ChangesHaveBeenConfirmed)
            {
                return false;
            }

            if (m_DeletedModules.Count > 0)
            {
                return true;
            }

            bool result = false;
            foreach (ModuleData module in m_Modules)
            {
                if (module.editedState != 0)
                {
                    result = true;
                    break;
                }
            }

            return result;
        }

        private bool ValidateChanges(out string localizedErrorDescription)
        {
            localizedErrorDescription = null;
            List<string> list = new List<string>(m_Modules.Count);
            foreach (ModuleData module in m_Modules)
            {
                string identifier = module.identifier;
                if (!list.Contains(identifier))
                {
                    list.Add(identifier);
                    if (string.IsNullOrEmpty(module.name))
                    {
                        localizedErrorDescription = LocalizationDatabase.GetLocalizedString("All modules must have a name.");
                        break;
                    }

                    if (module.chartCounters.Count == 0)
                    {
                        localizedErrorDescription = LocalizationDatabase.GetLocalizedString("The module '" + module.name + "' has no counters. All modules must have at least one counter.");
                    }

                    continue;
                }

                localizedErrorDescription = LocalizationDatabase.GetLocalizedString("There are two modules called '" + module.name + "'. Module names must be unique.");
                break;
            }

            return localizedErrorDescription == null;
        }
    }
}
