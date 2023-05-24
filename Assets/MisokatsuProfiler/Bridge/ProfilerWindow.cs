using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Unity.Profiling.Editor;
using UnityEditor;
using UnityEditor.Accessibility;
using UnityEditor.MPE;
using UnityEditor.Networking.PlayerConnection;
using UnityEditor.Profiling;
using UnityEditor.ShortcutManagement;
using UnityEditor.StyleSheets;
using UnityEditorInternal;
using UnityEditorInternal.Profiling;
using UnityEngine;
using UnityEngine.Networking.PlayerConnection;
using UnityEngine.Profiling;
using UnityEngine.UIElements;

namespace LightningProfiler
{
    //
    // 요약:
    //     Use the ProfilerWindow class for interactions with the Modules.
    [EditorWindowTitle(title = "Profiler", icon = "UnityEditor.ProfilerWindow")]
    public sealed class ProfilerWindow : EditorWindow, IHasCustomMenu, IProfilerWindowController, ProfilerModulesDropdownWindow.IResponder
    {
        internal static class Styles
        {
            public static readonly GUIContent addArea;

            public static readonly GUIContent deepProfile;

            public static readonly GUIContent deepProfileNotSupported;

            public static readonly GUIContent noData;

            public static readonly GUIContent noActiveModules;

            public static readonly string enableDeepProfilingWarningDialogTitle;

            public static readonly string enableDeepProfilingWarningDialogContent;

            public static readonly string disableDeepProfilingWarningDialogTitle;

            public static readonly string disableDeepProfilingWarningDialogContent;

            public static readonly string domainReloadWarningDialogButton;

            public static readonly string cancelDialogButton;

            public static readonly GUIContent recordCallstacks;

            public static readonly string[] recordCallstacksOptions;

            public static readonly string[] recordCallstacksDevelopmentOptions;

            public static readonly ProfilerMemoryRecordMode[] recordCallstacksEnumValues;

            public static readonly GUIContent profilerRecordOff;

            public static readonly GUIContent profilerRecordOn;

            public static SVC<Color> borderColor;

            public static readonly GUIContent prevFrame;

            public static readonly GUIContent nextFrame;

            public static readonly GUIContent currentFrame;

            public static readonly GUIContent frame;

            public static readonly GUIContent clearOnPlay;

            public static readonly GUIContent clearData;

            public static readonly GUIContent saveWindowTitle;

            public static readonly GUIContent saveProfilingData;

            public static readonly GUIContent loadWindowTitle;

            public static readonly GUIContent loadProfilingData;

            public static readonly string[] loadProfilingDataFileFilters;

            public static readonly GUIContent optionsButtonContent;

            public static readonly GUIContent helpButtonContent;

            public static readonly GUIContent preferencesButtonContent;

            public static readonly GUIContent accessibilityModeLabel;

            public static readonly GUIContent showStatsLabelsOnCurrentFrameLabel;

            public static readonly GUIStyle background;

            public static readonly GUIStyle header;

            public static readonly GUIStyle label;

            public static readonly GUIStyle entryEven;

            public static readonly GUIStyle entryOdd;

            public static readonly GUIStyle profilerGraphBackground;

            public static readonly GUIStyle profilerDetailViewBackground;

            public static readonly GUILayoutOption chartWidthOption;

            static Styles()
            {
                addArea = EditorGUIUtility.TrTextContent("Profiler Modules", "Add and remove profiler modules");
                deepProfile = EditorGUIUtility.TrTextContent("Deep Profile", "Instrument all scripting method calls to investigate scripts");
                deepProfileNotSupported = EditorGUIUtility.TrTextContent("Deep Profile", "Build a Player with Deep Profiling Support to be able to enable instrumentation of all scripting methods in a Player.");
                noData = EditorGUIUtility.TrTextContent("No frame data available");
                noActiveModules = EditorGUIUtility.TrTextContent("No Profiler Modules are active. Activate modules from the top left-hand drop-down.");
                enableDeepProfilingWarningDialogTitle = L10n.Tr("Enable deep script profiling");
                enableDeepProfilingWarningDialogContent = L10n.Tr("Enabling deep profiling requires reloading scripts.");
                disableDeepProfilingWarningDialogTitle = L10n.Tr("Disable deep script profiling");
                disableDeepProfilingWarningDialogContent = L10n.Tr("Disabling deep profiling requires reloading all scripts.");
                domainReloadWarningDialogButton = L10n.Tr("Reload");
                cancelDialogButton = L10n.Tr("Cancel");
                recordCallstacks = EditorGUIUtility.TrTextContent("Call Stacks", "Record call stacks for special samples such as \"GC.Alloc\". To see the call stacks, select a sample in the CPU Usage module, e.g. in Timeline view. To also see call stacks in Hierarchy view, switch from \"No Details\" to \"Related Data\", select a \"GC.Alloc\" sample and select \"N/A\" items from the list.");
                recordCallstacksOptions = new string[3]
                {
                    L10n.Tr("GC.Alloc"),
                    L10n.Tr("UnsafeUtility.Malloc(Persistent)"),
                    L10n.Tr("JobHandle.Complete")
                };
                recordCallstacksDevelopmentOptions = new string[4]
                {
                    L10n.Tr("GC.Alloc"),
                    L10n.Tr("UnsafeUtility.Malloc(Persistent)"),
                    L10n.Tr("JobHandle.Complete"),
                    L10n.Tr("Native Allocations (Editor Only)")
                };
                recordCallstacksEnumValues = new ProfilerMemoryRecordMode[4]
                {
                    ProfilerMemoryRecordMode.GCAlloc,
                    ProfilerMemoryRecordMode.UnsafeUtilityMalloc,
                    ProfilerMemoryRecordMode.JobHandleComplete,
                    ProfilerMemoryRecordMode.NativeAlloc
                };
                profilerRecordOff = EditorGUIUtility.TrIconContent("Record Off", "Record profiling information");
                profilerRecordOn = EditorGUIUtility.TrIconContent("Record On", "Record profiling information");
                borderColor = new SVC<Color>("--theme-profiler-border-color-darker", Color.black);
                prevFrame = EditorGUIUtility.TrIconContent("Animation.PrevKey", "Previous frame");
                nextFrame = EditorGUIUtility.TrIconContent("Animation.NextKey", "Next frame");
                currentFrame = EditorGUIUtility.TrIconContent("Animation.LastKey", "Current frame");
                frame = EditorGUIUtility.TrTextContent("Frame: ", "Selected frame / Total number of frames");
                clearOnPlay = EditorGUIUtility.TrTextContent("Clear on Play", "Clear the captured data on entering Play Mode, or connecting to a new Player");
                clearData = EditorGUIUtility.TrTextContent("Clear", "Clear the captured data");
                saveWindowTitle = EditorGUIUtility.TrTextContent("Save Window");
                saveProfilingData = EditorGUIUtility.TrIconContent("SaveAs", "Save current profiling information to a binary file");
                loadWindowTitle = EditorGUIUtility.TrTextContent("Load Window");
                loadProfilingData = EditorGUIUtility.TrIconContent("Profiler.Open", "Load binary profiling information from a file. Shift click to append to the existing data");
                loadProfilingDataFileFilters = new string[4]
                {
                    L10n.Tr("Profiler files"),
                    "data,raw",
                    L10n.Tr("All files"),
                    "*"
                };
                optionsButtonContent = EditorGUIUtility.TrIconContent("_Menu", "Additional Options");
                helpButtonContent = EditorGUIUtility.TrIconContent("_Help", "Open Manual (in a web browser)");
                preferencesButtonContent = EditorGUIUtility.TrTextContent("Preferences", "Open User Preferences for the Profiler");
                accessibilityModeLabel = EditorGUIUtility.TrTextContent("Color Blind Mode", "Switch the color scheme to color blind safe colors");
                showStatsLabelsOnCurrentFrameLabel = EditorGUIUtility.TrTextContent("Show Stats for 'current frame'", "Show stats labels when the 'current frame' toggle is on.");
                background = "OL box flat";
                header = "OL title";
                label = "OL label";
                entryEven = "OL EntryBackEven";
                entryOdd = "OL EntryBackOdd";
                profilerGraphBackground = "ProfilerScrollviewBackground";
                profilerDetailViewBackground = "ProfilerDetailViewBackground";
                chartWidthOption = GUILayout.Width(179f);
                profilerGraphBackground.overflow.left = -180;
            }
        }

        [Serializable]
        internal class ProfilerWindowControllerProxy
        {
        }

        private static List<ProfilerWindow> s_ProfilerWindows = new List<ProfilerWindow>();

        private const string k_UxmlResourceName = "ProfilerWindow.uxml";

        private const string k_UssSelector_ProfilerWindowDark = "profiler-window--dark";

        private const string k_UssSelector_ProfilerWindowLight = "profiler-window--light";

        private const string k_UssSelector_MainSplitView = "main-split-view";

        private const string k_UssSelector_ToolbarAndChartsLegacyIMGUIContainer = "toolbar-and-charts__legacy-imgui-container";

        private const string k_UssSelector_ModuleDetailsView_Container = "module-details-view__container";

        private const string k_MainSplitViewFixedPaneSizePreferenceKey = "ProfilerWindow.MainSplitView.FixedPaneSize";

        private const int k_NoModuleSelected = -1;

        private const string k_SelectedModuleIndexPreferenceKey = "ProfilerWindow.SelectedModuleIndex";

        private const string k_DynamicModulesPreferenceKey = "ProfilerWindow.DynamicModules";

        private static readonly Vector2 k_MinimumWindowSize = new Vector2(880f, 216f);

        [NonSerialized]
        private float m_FrameCountLabelMinWidth = 0f;

        [SerializeField]
        private bool m_Recording;

        private IConnectionStateInternal m_AttachProfilerState;

        private Vector2 m_GraphPos = Vector2.zero;

        [SerializeField]
        private string m_ActiveNativePlatformSupportModuleName;

        [NonSerialized]
        private int m_SelectedModuleIndex = -1;

        private int m_CurrentFrame = -1;

        private int m_LastFrameFromTick = -1;

        private bool m_CurrentFrameEnabled = false;

        private const int k_MainThreadIndex = 0;

        private HierarchyFrameDataView m_FrameDataView;

        //
        // 요약:
        //     Deprecated: Use ProfilerWindow.cpuModuleIdentifier instead. The name of the.
        [Obsolete("cpuModuleName is deprecated. Use cpuModuleIdentifier instead. (UnityUpgradable) -> cpuModuleIdentifier")]
        public const string cpuModuleName = "UnityEditorInternal.Profiling.CPUProfilerModule, UnityEditor.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";

        //
        // 요약:
        //     Deprecated: Use ProfilerWindow.gpuModuleIdentifier instead. The name of the.
        [Obsolete("gpuModuleName is deprecated. Use gpuModuleIdentifier instead. (UnityUpgradable) -> gpuModuleIdentifier")]
        public const string gpuModuleName = "UnityEditorInternal.Profiling.GPUProfilerModule, UnityEditor.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";

        //
        // 요약:
        //     The identifier of the.
        public const string cpuModuleIdentifier = "UnityEditorInternal.Profiling.CPUProfilerModule, UnityEditor.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";

        //
        // 요약:
        //     The identifier of the.
        public const string gpuModuleIdentifier = "UnityEditorInternal.Profiling.GPUProfilerModule, UnityEditor.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";

        [SerializeReference]
        private List<ProfilerModule> m_AllModules;

        internal ProfilerCategoryActivator m_CategoryActivator;

        private ProfilerMemoryRecordMode m_CurrentCallstackRecordMode = ProfilerMemoryRecordMode.None;

        [SerializeField]
        private ProfilerMemoryRecordMode m_CallstackRecordMode = ProfilerMemoryRecordMode.None;

        [SerializeField]
        private bool m_ClearOnPlay;

        private IMGUIContainer m_ToolbarAndChartsIMGUIContainer;

        private VisualElement m_DetailsViewContainer;

        private const string kProfilerRecentSaveLoadProfilePath = "ProfilerRecentSaveLoadProfilePath";

        private const string kProfilerEnabledSessionKey = "ProfilerEnabled";

        private const string kProfilerEditorTargetModeEnabledSessionKey = "ProfilerTargetMode";

        private const string kProfilerDeepProfilingWarningSessionKey = "ProfilerDeepProfilingWarning";

        private int m_LastReportedSelectedFrameIndex;

        internal IEnumerable<ProfilerModule> Modules => m_AllModules;

        internal bool IgnoreRepaintAllProfilerWindowsTick
        {
            get;
            set;
        }

        internal string ConnectedTargetName => m_AttachProfilerState.connectionName;

        internal bool ConnectedToEditor => m_AttachProfilerState.connectedToTarget == ConnectionTarget.Editor;

        internal TwoPaneSplitView MainSplitView
        {
            get;
            private set;
        }

        internal VisualElement DetailsViewContainer => m_DetailsViewContainer;

        //
        // 요약:
        //     Deprecated: Use ProfilerWindow.selectedModuleIdentifier instead. The name of
        //     the that is currently selected in the Profiler Window, or null if no Module is
        //     currently selected.
        [Obsolete("selectedModuleName is deprecated. Use selectedModuleIdentifier instead. (UnityUpgradable) -> selectedModuleIdentifier")]
        public string selectedModuleName => selectedModuleIdentifier;

        //
        // 요약:
        //     The identifier of the that is currently selected in the Profiler Window, or null
        //     if no Module is currently selected.
        public string selectedModuleIdentifier => selectedModule?.Identifier ?? null;

        internal ProfilerModule selectedModule
        {
            get
            {
                return ModuleAtIndex(m_SelectedModuleIndex);
            }
            set
            {
                if (selectedModule != value)
                {
                    SelectModule(value);
                }
            }
        }

        //
        // 요약:
        //     The zero-based index of the frame currently selected in the Profiler Window.
        public long selectedFrameIndex
        {
            get
            {
                return GetActiveVisibleFrameIndex();
            }
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException("value", "Can't set a value < 0 for the selectedFrameIndex.");
                }

                if (value < firstAvailableFrameIndex)
                {
                    throw new ArgumentOutOfRangeException("value", string.Format("Can't set a value smaller than {0} which is currently {1}.", "firstAvailableFrameIndex", firstAvailableFrameIndex));
                }

                if (value > lastAvailableFrameIndex)
                {
                    throw new ArgumentOutOfRangeException("value", string.Format("Can't set a value greater than {0} which is currently {1}.", "lastAvailableFrameIndex", lastAvailableFrameIndex));
                }

                SetActiveVisibleFrameIndex((int)value);
            }
        }

        //
        // 요약:
        //     The index of the first frame available in the Profiler Window, or -1 if no frames
        //     are available.
        public long firstAvailableFrameIndex => ProfilerDriver.firstFrameIndex;

        //
        // 요약:
        //     The index of the last frame available in the Profiler Window, or -1 if no frames
        //     are available.
        public long lastAvailableFrameIndex => ProfilerDriver.lastFrameIndex;

        long IProfilerWindowController.selectedFrameIndex
        {
            get
            {
                return selectedFrameIndex;
            }
            set
            {
                selectedFrameIndex = value;
            }
        }

        ProfilerModule IProfilerWindowController.selectedModule
        {
            get
            {
                return selectedModule;
            }
            set
            {
                selectedModule = value;
            }
        }

        string IProfilerWindowController.ConnectedTargetName => ConnectedTargetName;

        bool IProfilerWindowController.ConnectedToEditor => ConnectedToEditor;

        internal event Action<int, bool> currentFrameChanged = delegate
        {
        };

        internal event Action frameDataViewAboutToBeDisposed = delegate
        {
        };

        public event Action<long> SelectedFrameIndexChanged;

        internal event Action<bool> recordingStateChanged = delegate
        {
        };

        internal event Action<bool> deepProfileChanged = delegate
        {
        };

        internal event Action<ProfilerMemoryRecordMode> memoryRecordingModeChanged = delegate
        {
        };

        event Action<int, bool> IProfilerWindowController.currentFrameChanged
        {
            add
            {
                currentFrameChanged += value;
            }
            remove
            {
                currentFrameChanged -= value;
            }
        }

        event Action IProfilerWindowController.frameDataViewAboutToBeDisposed
        {
            add
            {
                frameDataViewAboutToBeDisposed += value;
            }
            remove
            {
                frameDataViewAboutToBeDisposed -= value;
            }
        }

        internal ProfilerProperty CreateProperty()
        {
            return CreateProperty(-1);
        }

        internal ProfilerProperty CreateProperty(int sortType)
        {
            int num = GetActiveVisibleFrameIndex();
            if (num < 0)
            {
                num = ProfilerDriver.lastFrameIndex;
            }

            if (num < Math.Max(0, ProfilerDriver.firstFrameIndex))
            {
                return null;
            }

            ProfilerProperty profilerProperty = new ProfilerProperty();
            profilerProperty.SetRoot(num, sortType, 0);
            profilerProperty.onlyShowGPUSamples = (selectedModule is GPUProfilerModule);
            return profilerProperty;
        }

        internal int GetActiveVisibleFrameIndex()
        {
            return (m_CurrentFrame == -1) ? m_LastFrameFromTick : m_CurrentFrame;
        }

        internal bool ProfilerWindowOverheadIsAffectingProfilingRecordingData()
        {
            return ProcessService.level == ProcessLevel.UMP_MASTER && IsSetToRecord() && ProfilerDriver.IsConnectionEditor() && ((EditorApplication.isPlaying && !EditorApplication.isPaused) || ProfilerDriver.profileEditor);
        }

        internal bool IsRecording()
        {
            return IsSetToRecord() && ((EditorApplication.isPlaying && !EditorApplication.isPaused) || ProfilerDriver.profileEditor || !ProfilerDriver.IsConnectionEditor());
        }

        internal bool IsSetToRecord()
        {
            return m_Recording;
        }


        internal T GetProfilerModule<T>(ProfilerArea area) where T : ProfilerModuleBase
        {
            foreach (ProfilerModule allModule in m_AllModules)
            {
                if (allModule.area == area)
                {
                    return allModule as T;
                }
            }

            return null;
        }

        internal ProfilerModule GetProfilerModuleByType(Type type)
        {
            ProfilerModule profilerModule = null;
            if (type.IsAbstract)
            {
                throw new ArgumentException("type can't be abstract.", "type");
            }

            foreach (ProfilerModule allModule in m_AllModules)
            {
                if (type == allModule.GetType())
                {
                    profilerModule = allModule;
                }
            }

            if (profilerModule == null)
            {
                throw new ArgumentException("A type of " + type.Name + " is not a type that describes an existing Profiler Module.", "type");
            }

            return profilerModule;
        }

        internal void SetProfilerModuleActiveState(ProfilerModule module, bool active)
        {
            if (module == null)
            {
                throw new ArgumentNullException("module");
            }

            int num = IndexOfModule(module);
            if (num == -1)
            {
                throw new ArgumentException("The " + module.DisplayName + " module is not registered with the Profiler Window.", "module");
            }

            m_AllModules[num].active = active;
        }

        internal bool GetProfilerModuleActiveState(ProfilerModule module)
        {
            if (module == null)
            {
                throw new ArgumentNullException("module");
            }

            int num = IndexOfModule(module);
            if (num == -1)
            {
                throw new ArgumentException("The " + module.DisplayName + " module is not registered with the Profiler Window.", "module");
            }

            return m_AllModules[num].active;
        }

        private void OnEnable()
        {
            Initialize();
            ConstructVisualTree();
            SubscribeToGlobalEvents();
            if (ModuleEditorWindow.TryGetOpenInstance(out ModuleEditorWindow moduleEditorWindow))
            {
                moduleEditorWindow.onChangesConfirmed += OnModuleEditorChangesConfirmed;
            }

            foreach (ProfilerModule allModule in m_AllModules)
            {
                allModule.OnEnable();
            }

            int @int = SessionState.GetInt("ProfilerWindow.SelectedModuleIndex", -1);
            if (@int != -1)
            {
                SelectModuleAtIndex(@int);
            }
            else
            {
                SelectFirstActiveModule();
            }
        }

        private void OnDisable()
        {
            SaveViewSettings();
            m_AttachProfilerState.Dispose();
            m_AttachProfilerState = null;
            s_ProfilerWindows.Remove(this);
            DeselectSelectedModuleIfNecessary();
            foreach (ProfilerModule allModule in m_AllModules)
            {
                allModule.OnDisable();
            }

            UnsubscribeFromGlobalEvents();
        }

        private void Initialize()
        {
            base.minSize = k_MinimumWindowSize;
            base.titleContent = GetLocalizedTitleContent();
            s_ProfilerWindows.Add(this);
            List<ProfilerModule> allModules = m_AllModules;
            m_AllModules = InitializeAllModules(allModules);
            m_AttachProfilerState = (PlayerConnectionGUIUtility.GetConnectionState(this, OnTargetedEditorConnectionChanged, IsEditorConnectionTargeted, OnConnectedToPlayer) as IConnectionStateInternal);
            m_CategoryActivator = new ProfilerCategoryActivator();
            m_ActiveNativePlatformSupportModuleName = EditorUtility.GetActiveNativePlatformSupportModuleName();
        }

        private List<ProfilerModule> InitializeAllModules(List<ProfilerModule> existingModules)
        {
            List<ProfilerModule> modules = new List<ProfilerModule>();
            InitializeAllCompileTimeDefinedProfilerModulesIntoCollection(ref modules, existingModules);
            SortModuleCollectionInPlace(ref modules);
            return modules;
        }

        private void InitializeAllCompileTimeDefinedProfilerModulesIntoCollection(ref List<ProfilerModule> modules, List<ProfilerModule> existingModules)
        {
            foreach (Type item in TypeCache.GetTypesDerivedFrom<ProfilerModule>())
            {
                if (!ProfilerModuleTypeValidator.IsValidModuleTypeDefinition(item, out ProfilerModuleMetadataAttribute moduleMetadata, out string errorDescription))
                {
                    if (!string.IsNullOrEmpty(errorDescription))
                    {
                        Debug.LogError(errorDescription);
                    }

                    continue;
                }

                string assemblyQualifiedName = item.AssemblyQualifiedName;
                ProfilerModule module;
                bool flag = TryGetModuleInCollection(assemblyQualifiedName, existingModules, out module);
                try
                {
                    if (!flag)
                    {
                        module = (Activator.CreateInstance(item) as ProfilerModule);
                    }

                    ProfilerModule.InitializationArgs args = new ProfilerModule.InitializationArgs(assemblyQualifiedName, moduleMetadata.DisplayName, moduleMetadata.IconPath, this);
                    module.Initialize(args);
                    modules.Add(module);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Unable to create Profiler module of type {item}. {ex.Message}");
                }
            }
        }

        private bool TryGetModuleInCollection<T>(string identifier, IEnumerable<ProfilerModule> collection, out T module) where T : ProfilerModule
        {
            module = null;
            if (collection == null)
            {
                return false;
            }

            foreach (ProfilerModule item in collection)
            {
                if (item != null && identifier.Equals(item.Identifier))
                {
                    module = (item as T);
                    return module != null;
                }
            }

            return false;
        }

        private void SortModuleCollectionInPlace(ref List<ProfilerModule> modules)
        {
            modules.Sort(delegate (ProfilerModule a, ProfilerModule b)
            {
                int num = a.orderIndex.CompareTo(b.orderIndex);
                if (num == 0)
                {
                    num = a.DisplayName.CompareTo(b.DisplayName);
                }

                return num;
            });
            for (int i = 0; i < modules.Count; i++)
            {
                ProfilerModule profilerModule = modules[i];
                int orderIndex = profilerModule.orderIndex;
                if (orderIndex == int.MaxValue)
                {
                    profilerModule.orderIndex = i;
                }
            }
        }

        private void ConstructVisualTree()
        {
            VisualTreeAsset visualTreeAsset = EditorGUIUtility.Load("ProfilerWindow.uxml") as VisualTreeAsset;
            visualTreeAsset.CloneTree(base.rootVisualElement);
            string className = EditorGUIUtility.isProSkin ? "profiler-window--dark" : "profiler-window--light";
            base.rootVisualElement.AddToClassList(className);
            m_ToolbarAndChartsIMGUIContainer = base.rootVisualElement.Q<IMGUIContainer>("toolbar-and-charts__legacy-imgui-container");
            m_ToolbarAndChartsIMGUIContainer.onGUIHandler = DoLegacyGUI_ToolbarAndCharts;
            MainSplitView = base.rootVisualElement.Q<TwoPaneSplitView>("main-split-view");
            float @float = EditorPrefs.GetFloat("ProfilerWindow.MainSplitView.FixedPaneSize", k_MinimumWindowSize.y * 0.5f);
            MainSplitView.fixedPaneInitialDimension = @float;
            m_DetailsViewContainer = base.rootVisualElement.Q<VisualElement>("module-details-view__container");
        }

        private void SubscribeToGlobalEvents()
        {
            EditorApplication.playModeStateChanged -= OnPlaymodeStateChanged;
            EditorApplication.playModeStateChanged += OnPlaymodeStateChanged;
            EditorApplication.pauseStateChanged += OnPauseStateChanged;
            UserAccessiblitySettings.colorBlindConditionChanged = (Action)Delegate.Combine(UserAccessiblitySettings.colorBlindConditionChanged, new Action(OnSettingsChanged));
            ProfilerUserSettings.settingsChanged = (Action)Delegate.Combine(ProfilerUserSettings.settingsChanged, new Action(OnSettingsChanged));
            ProfilerDriver.profileLoaded += OnProfileLoaded;
            ProfilerDriver.profileCleared += OnProfileCleared;
            ProfilerDriver.profilerCaptureSaved += ProfilerWindowAnalytics.SendSaveLoadEvent;
            ProfilerDriver.profilerCaptureLoaded += ProfilerWindowAnalytics.SendSaveLoadEvent;
            ProfilerDriver.profilerConnected += ProfilerWindowAnalytics.SendConnectionEvent;
            ProfilerDriver.profilingStateChange += ProfilerWindowAnalytics.ProfilingStateChange;
        }

        private void UnsubscribeFromGlobalEvents()
        {
            EditorApplication.playModeStateChanged -= OnPlaymodeStateChanged;
            EditorApplication.pauseStateChanged -= OnPauseStateChanged;
            UserAccessiblitySettings.colorBlindConditionChanged = (Action)Delegate.Remove(UserAccessiblitySettings.colorBlindConditionChanged, new Action(OnSettingsChanged));
            ProfilerUserSettings.settingsChanged = (Action)Delegate.Remove(ProfilerUserSettings.settingsChanged, new Action(OnSettingsChanged));
            ProfilerDriver.profileLoaded -= OnProfileLoaded;
            ProfilerDriver.profileCleared -= OnProfileCleared;
            ProfilerDriver.profilerCaptureSaved -= ProfilerWindowAnalytics.SendSaveLoadEvent;
            ProfilerDriver.profilerCaptureLoaded -= ProfilerWindowAnalytics.SendSaveLoadEvent;
            ProfilerDriver.profilerConnected -= ProfilerWindowAnalytics.SendConnectionEvent;
            ProfilerDriver.profilingStateChange -= ProfilerWindowAnalytics.ProfilingStateChange;
        }

        private void OnSettingsChanged()
        {
            SaveViewSettings();
            foreach (ProfilerModule allModule in m_AllModules)
            {
                allModule.Rebuild();
            }

            Repaint();
        }

        private void Clear()
        {
            ProfilerDriver.ClearAllFrames();
        }

        private void OnProfileCleared()
        {
            ResetForClearedOrLoaded(cleared: true);
        }

        private void OnProfileLoaded()
        {
            ResetForClearedOrLoaded(cleared: false);
        }

        private void ResetForClearedOrLoaded(bool cleared)
        {
            m_LastFrameFromTick = -1;
            m_FrameCountLabelMinWidth = 0f;
            foreach (ProfilerModule allModule in m_AllModules)
            {
                allModule.Clear();
            }

            if (m_FrameDataView != null)
            {
                DisposeFrameDataView();
            }

            m_FrameDataView = null;
            if (cleared)
            {
                SetCurrentFrameDontPause(-1);
                m_CurrentFrameEnabled = true;
                NetworkDetailStats.m_NetworkOperations.Clear();
            }

            foreach (ProfilerModule allModule2 in m_AllModules)
            {
                allModule2.Clear();
                allModule2.Update();
            }

            RepaintImmediately();
        }

        internal ProfilerModule[] GetProfilerModules()
        {
            ProfilerModule[] array = new ProfilerModule[m_AllModules.Count];
            m_AllModules.CopyTo(array);
            return array;
        }

        internal void GetProfilerModules(ref List<ProfilerModule> outModules)
        {
            if (outModules == null)
            {
                outModules = new List<ProfilerModule>(m_AllModules);
                return;
            }

            outModules.Clear();
            outModules.AddRange(m_AllModules);
        }

        internal void CloseModule(ProfilerModule module)
        {
            if (module == selectedModule)
            {
                SelectFirstActiveModule();
            }
        }

        private void CheckForPlatformModuleChange()
        {
            string activeNativePlatformSupportModuleName = EditorUtility.GetActiveNativePlatformSupportModuleName();
            if (m_ActiveNativePlatformSupportModuleName != activeNativePlatformSupportModuleName)
            {
                OnActiveNativePlatformSupportModuleChanged(activeNativePlatformSupportModuleName);
            }
        }

        private void OnActiveNativePlatformSupportModuleChanged(string activeNativePlatformSupportModuleName)
        {
            ProfilerDriver.ClearAllFrames();
            m_ActiveNativePlatformSupportModuleName = activeNativePlatformSupportModuleName;
            foreach (ProfilerModule allModule in m_AllModules)
            {
                allModule.OnNativePlatformSupportModuleChanged();
            }

            Repaint();
        }

        private void SaveViewSettings()
        {
            foreach (ProfilerModule allModule in m_AllModules)
            {
                allModule.SaveViewSettings();
            }

            EditorPrefs.SetFloat("ProfilerWindow.MainSplitView.FixedPaneSize", MainSplitView.fixedPane.resolvedStyle.height);
            SessionState.SetInt("ProfilerWindow.SelectedModuleIndex", m_SelectedModuleIndex);
        }

        private void Awake()
        {
            if (Profiler.supported)
            {
                if (ProfilerUserSettings.rememberLastRecordState)
                {
                    m_Recording = EditorPrefs.GetBool("ProfilerEnabled", ProfilerUserSettings.defaultRecordState);
                }
                else
                {
                    m_Recording = SessionState.GetBool("ProfilerEnabled", ProfilerUserSettings.defaultRecordState);
                }

                ProfilerDriver.enabled = m_Recording;
                ProfilerDriver.profileEditor = SessionState.GetBool("ProfilerTargetMode", ProfilerUserSettings.defaultTargetMode == ProfilerEditorTargetMode.Editmode || ProfilerDriver.profileEditor);
                m_CurrentCallstackRecordMode = ProfilerDriver.memoryRecordMode;
            }
        }

        private void OnPlaymodeStateChanged(PlayModeStateChange stateChange)
        {
            m_CurrentFrameEnabled = false;
            if (stateChange == PlayModeStateChange.EnteredPlayMode)
            {
                ClearFramesOnPlayOrPlayerConnectionChange();
            }
        }

        private void OnPauseStateChanged(PauseState stateChange)
        {
            m_CurrentFrameEnabled = false;
        }

        internal void ClearFramesOnPlayOrPlayerConnectionChange()
        {
            if (m_ClearOnPlay)
            {
                Clear();
            }
        }

        private void OnDestroy()
        {
            if (!(WindowLayout.GetMaximizedWindow() != null) && Profiler.supported)
            {
                ProfilerDriver.enabled = false;
            }
        }

        private void OnFocus()
        {
            if (Profiler.supported)
            {
                ProfilerDriver.enabled = m_Recording;
            }

            ProfilerWindowAnalytics.OnProfilerWindowFocused();
        }

        private void OnLostFocus()
        {
            if (GUIUtility.hotControl != 0)
            {
                for (int i = 0; i < m_AllModules.Count; i++)
                {
                    ProfilerModule profilerModule = m_AllModules[i];
                    profilerModule.OnLostFocus();
                }
            }

            ProfilerWindowAnalytics.OnProfilerWindowLostFocus();
        }

        void IHasCustomMenu.AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(Styles.accessibilityModeLabel, UserAccessiblitySettings.colorBlindCondition != ColorBlindCondition.Default, OnToggleColorBlindMode);
        }

        private void OnToggleColorBlindMode()
        {
            UserAccessiblitySettings.colorBlindCondition = ((UserAccessiblitySettings.colorBlindCondition == ColorBlindCondition.Default) ? ColorBlindCondition.Deuteranopia : ColorBlindCondition.Default);
        }

        private void OnToggleShowStatsLabelsOnCurrentFrame()
        {
            ProfilerUserSettings.showStatsLabelsOnCurrentFrame = !ProfilerUserSettings.showStatsLabelsOnCurrentFrame;
        }

        [MenuItem("Window/Analysis/Profiler %7", false, 0)]
        internal static ProfilerWindow ShowProfilerWindow()
        {
            return EditorWindow.GetWindow<ProfilerWindow>(utility: false);
        }

        internal ProfilerWindow()
        {
        }

        [MenuItem("Window/Analysis/Profiler (Standalone Process)", false, 1)]
        private static void ShowProfilerOOP()
        {
            if (EditorUtility.DisplayDialog("Profiler (Standalone Process)", "The Standalone Profiler launches the Profiler window in a separate process from the Editor. This means that the performance of the Editor does not affect profiling data, and the Profiler does not affect the performance of the Editor. It takes around 3-4 seconds to launch.", "OK", DialogOptOutDecisionType.ForThisMachine, "UseOutOfProcessProfiler"))
            {
                ProfilerRoleProvider.LaunchProfilerProcess();
            }
        }

        private static string GetRecordingStateName(string defaultName)
        {
            if (!string.IsNullOrEmpty(defaultName))
            {
                return "of " + defaultName;
            }

            if (ProfilerDriver.profileEditor)
            {
                return "editmode";
            }

            return "playmode";
        }

        [Shortcut("Profiling/Profiler/RecordToggle", KeyCode.F9, ShortcutModifiers.None)]
        private static void RecordToggle()
        {
            bool flag = false;
            if (CommandService.Exists("ProfilerRecordToggle"))
            {
                object value = CommandService.Execute("ProfilerRecordToggle", CommandHint.Shortcut);
                flag = Convert.ToBoolean(value);
            }

            if (flag)
            {
                return;
            }

            if (EditorWindow.HasOpenInstances<ProfilerWindow>())
            {
                ProfilerWindow window = EditorWindow.GetWindow<ProfilerWindow>();
                window.SetRecordingEnabled(!window.IsSetToRecord());
            }
            else
            {
                ProfilerDriver.enabled = !ProfilerDriver.enabled;
            }

            using (IConnectionState connectionState = PlayerConnectionGUIUtility.GetConnectionState(null))
            {
                string defaultName = "";
                if (connectionState.connectedToTarget != ConnectionTarget.Editor)
                {
                    defaultName = connectionState.connectionName;
                }

                EditorGUI.hyperLinkClicked -= EditorGUI_HyperLinkClicked;
                EditorGUI.hyperLinkClicked += EditorGUI_HyperLinkClicked;
                if (ProfilerDriver.enabled)
                {
                    Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "Recording " + GetRecordingStateName(defaultName) + " has started...");
                }
                else
                {
                    Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "Recording has ended.\r\nClick <a openprofiler=\"true\">here</a> to open the profiler window.");
                }
            }
        }

        private static void EditorGUI_HyperLinkClicked(EditorWindow window, HyperLinkClickedEventArgs args)
        {
            if (args.hyperLinkData.ContainsKey("openprofiler"))
            {
                ShowProfilerWindow();
            }
        }

        private static void RepaintAllProfilerWindows()
        {
            foreach (ProfilerWindow s_ProfilerWindow in s_ProfilerWindows)
            {
                if (!s_ProfilerWindow.IgnoreRepaintAllProfilerWindowsTick && ProfilerDriver.lastFrameIndex != s_ProfilerWindow.m_LastFrameFromTick)
                {
                    s_ProfilerWindow.m_LastFrameFromTick = ProfilerDriver.lastFrameIndex;
                    s_ProfilerWindow.m_ToolbarAndChartsIMGUIContainer.MarkDirtyRepaint();
                    s_ProfilerWindow.InvokeSelectedFrameIndexChangedEventIfNecessary(s_ProfilerWindow.m_LastFrameFromTick);
                }
            }
        }

        private void SetProfileDeepScripts(bool deep)
        {
            bool deepProfiling = ProfilerDriver.deepProfiling;
            if (deepProfiling != deep)
            {
                if (ProfilerDriver.IsConnectionEditor())
                {
                    SetEditorDeepProfiling(deep);
                }
                else
                {
                    ProfilerDriver.deepProfiling = deep;
                }

                this.deepProfileChanged?.Invoke(deep);
            }
        }

        private string PickFrameLabel()
        {
            int num = ProfilerDriver.lastFrameIndex + 1;
            return ((m_CurrentFrame == -1) ? num : (m_CurrentFrame + 1)) + " / " + num;
        }

        private void PrevFrame()
        {
            int previousFrameIndex = ProfilerDriver.GetPreviousFrameIndex(m_CurrentFrame);
            if (previousFrameIndex != -1)
            {
                SetCurrentFrame(previousFrameIndex);
            }
        }

        private void NextFrame()
        {
            int nextFrameIndex = ProfilerDriver.GetNextFrameIndex(m_CurrentFrame);
            if (nextFrameIndex != -1)
            {
                SetCurrentFrame(nextFrameIndex);
            }
        }

        private static bool CheckFrameData(ProfilerProperty property)
        {
            return property?.frameDataReady ?? false;
        }

        internal HierarchyFrameDataView GetFrameDataView(string groupName, string threadName, ulong threadId, HierarchyFrameDataView.ViewModes viewMode, int profilerSortColumn, bool sortAscending)
        {
            int activeVisibleFrameIndex = GetActiveVisibleFrameIndex();
            int num = -1;
            using (ProfilerFrameDataIterator profilerFrameDataIterator = new ProfilerFrameDataIterator())
            {
                int threadCount = profilerFrameDataIterator.GetThreadCount(activeVisibleFrameIndex);
                for (int i = 0; i < threadCount; i++)
                {
                    profilerFrameDataIterator.SetRoot(activeVisibleFrameIndex, i);
                    string groupName2 = profilerFrameDataIterator.GetGroupName();
                    if ((!string.IsNullOrEmpty(groupName2) || !string.IsNullOrEmpty(groupName)) && groupName2 != groupName)
                    {
                        continue;
                    }

                    string threadName2 = profilerFrameDataIterator.GetThreadName();
                    if (!(threadName == threadName2))
                    {
                        continue;
                    }

                    using (RawFrameDataView rawFrameDataView = new RawFrameDataView(activeVisibleFrameIndex, i))
                    {
                        if (threadId == 0L || threadId == rawFrameDataView.threadId)
                        {
                            num = i;
                            break;
                        }

                        if (num < 0)
                        {
                            num = i;
                        }

                        continue;
                    }
                }
            }

            return GetFrameDataView(num, viewMode, profilerSortColumn, sortAscending);
        }

        private void DisposeFrameDataView()
        {
            this.frameDataViewAboutToBeDisposed();
            m_FrameDataView.Dispose();
        }

        internal HierarchyFrameDataView GetFrameDataView(int threadIndex, HierarchyFrameDataView.ViewModes viewMode, int profilerSortColumn, bool sortAscending)
        {
            int activeVisibleFrameIndex = GetActiveVisibleFrameIndex();
            if (activeVisibleFrameIndex < firstAvailableFrameIndex || activeVisibleFrameIndex > lastAvailableFrameIndex)
            {
                if (m_FrameDataView != null && m_FrameDataView.valid)
                {
                    DisposeFrameDataView();
                }
            }
            else if (activeVisibleFrameIndex != -1)
            {
                if (threadIndex < 0)
                {
                    threadIndex = 0;
                }
                else
                {
                    using (ProfilerFrameDataIterator profilerFrameDataIterator = new ProfilerFrameDataIterator())
                    {
                        profilerFrameDataIterator.SetRoot(activeVisibleFrameIndex, 0);
                        if (threadIndex >= profilerFrameDataIterator.GetThreadCount(activeVisibleFrameIndex))
                        {
                            threadIndex = 0;
                        }
                    }
                }
            }

            if (m_FrameDataView != null && m_FrameDataView.valid && m_FrameDataView.frameIndex == activeVisibleFrameIndex && m_FrameDataView.threadIndex == threadIndex && m_FrameDataView.viewMode == viewMode)
            {
                return m_FrameDataView;
            }

            if (m_FrameDataView != null)
            {
                DisposeFrameDataView();
            }

            m_FrameDataView = new HierarchyFrameDataView(activeVisibleFrameIndex, threadIndex, viewMode, profilerSortColumn, sortAscending);
            return m_FrameDataView;
        }

        private void UpdateModules()
        {
            foreach (ProfilerModule allModule in m_AllModules)
            {
                if (allModule.active)
                {
                    allModule.Update();
                }
            }
        }

        private void SetCallstackRecordMode(ProfilerMemoryRecordMode memRecordMode)
        {
            if (memRecordMode != m_CurrentCallstackRecordMode)
            {
                m_CurrentCallstackRecordMode = memRecordMode;
                ProfilerDriver.memoryRecordMode = memRecordMode;
                this.memoryRecordingModeChanged?.Invoke(memRecordMode);
            }
        }

        private void ToggleCallstackRecordModeFlag(object userData, string[] options, int selected)
        {
            m_CallstackRecordMode ^= Styles.recordCallstacksEnumValues[selected];
            if (m_CurrentCallstackRecordMode != 0)
            {
                SetCallstackRecordMode(m_CallstackRecordMode);
            }
        }

        internal void SaveProfilingData()
        {
            string @string = EditorPrefs.GetString("ProfilerRecentSaveLoadProfilePath");
            string directory = string.IsNullOrEmpty(@string) ? "" : Path.GetDirectoryName(@string);
            string defaultName = string.IsNullOrEmpty(@string) ? "" : Path.GetFileName(@string);
            string text = EditorUtility.SaveFilePanel(Styles.saveWindowTitle.text, directory, defaultName, "data");
            if (text.Length != 0)
            {
                EditorPrefs.SetString("ProfilerRecentSaveLoadProfilePath", text);
                ProfilerDriver.SaveProfile(text);
            }
        }

        internal void LoadProfilingData(bool keepExistingData)
        {
            string @string = EditorPrefs.GetString("ProfilerRecentSaveLoadProfilePath");
            string text = EditorUtility.OpenFilePanelWithFilters(Styles.loadWindowTitle.text, @string, Styles.loadProfilingDataFileFilters);
            if (text.Length == 0)
            {
                return;
            }

            EditorPrefs.SetString("ProfilerRecentSaveLoadProfilePath", text);
            if (ProfilerDriver.LoadProfile(text, keepExistingData))
            {
                ProfilerDriver.enabled = (m_Recording = false);
                SessionState.SetBool("ProfilerEnabled", m_Recording);
                if (ProfilerUserSettings.rememberLastRecordState)
                {
                    EditorPrefs.SetBool("ProfilerEnabled", m_Recording);
                }

                NetworkDetailStats.m_NetworkOperations.Clear();
            }
        }

        internal void SetRecordingEnabled(bool profilerEnabled)
        {
            ProfilerDriver.enabled = profilerEnabled;
            m_Recording = profilerEnabled;
            SessionState.SetBool("ProfilerEnabled", profilerEnabled);
            if (ProfilerUserSettings.rememberLastRecordState)
            {
                EditorPrefs.SetBool("ProfilerEnabled", profilerEnabled);
            }

            this.recordingStateChanged?.Invoke(m_Recording);
            Repaint();
        }

        private float DrawMainToolbar()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            PlayerConnectionGUILayout.ConnectionTargetSelectionDropdown(m_AttachProfilerState, EditorStyles.toolbarDropDown);
            bool flag = GUILayout.Toggle(m_Recording, m_Recording ? Styles.profilerRecordOn : Styles.profilerRecordOff, EditorStyles.toolbarButton);
            if (flag != m_Recording)
            {
                SetRecordingEnabled(flag);
            }

            FrameNavigationControls();
            using (new EditorGUI.DisabledScope(ProfilerDriver.lastFrameIndex == -1))
            {
                if (GUILayout.Button(Styles.clearData, EditorStyles.toolbarButton))
                {
                    Clear();
                    GUIUtility.ExitGUI();
                }
            }

            GUILayout.FlexibleSpace();
            SetClearOnPlay(GUILayout.Toggle(GetClearOnPlay(), Styles.clearOnPlay, EditorStyles.toolbarButton));
            bool deepProfilingSupported = m_AttachProfilerState.deepProfilingSupported;
            using (new EditorGUI.DisabledScope(!deepProfilingSupported))
            {
                SetProfileDeepScripts(GUILayout.Toggle(ProfilerDriver.deepProfiling, deepProfilingSupported ? Styles.deepProfile : Styles.deepProfileNotSupported, EditorStyles.toolbarButton));
            }

            GUILayout.FlexibleSpace();
            GUILayout.FlexibleSpace();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Styles.loadProfilingData, EditorStyles.toolbarButton, GUILayout.MaxWidth(25f)))
            {
                LoadProfilingData(Event.current.shift);
                GUIUtility.ExitGUI();
            }

            using (new EditorGUI.DisabledScope(ProfilerDriver.lastFrameIndex == -1))
            {
                if (GUILayout.Button(Styles.saveProfilingData, EditorStyles.toolbarButton))
                {
                    SaveProfilingData();
                    GUIUtility.ExitGUI();
                }
            }

            if (GUILayout.Button(Styles.helpButtonContent, EditorStyles.toolbarButton))
            {
                string url = Help.FindHelpNamed("ProfilerWindow");
                Help.BrowseURL(url);
            }

            Rect rect = GUILayoutUtility.GetRect(Styles.optionsButtonContent, EditorStyles.toolbarButtonRight);
            if (GUI.Button(rect, Styles.optionsButtonContent, EditorStyles.toolbarButtonRight))
            {
                GenericMenu genericMenu = new GenericMenu();
                genericMenu.AddItem(Styles.accessibilityModeLabel, UserAccessiblitySettings.colorBlindCondition != ColorBlindCondition.Default, OnToggleColorBlindMode);
                genericMenu.AddItem(Styles.showStatsLabelsOnCurrentFrameLabel, ProfilerUserSettings.showStatsLabelsOnCurrentFrame, OnToggleShowStatsLabelsOnCurrentFrame);
                genericMenu.AddSeparator("");
                genericMenu.AddItem(Styles.preferencesButtonContent, on: false, OpenProfilerPreferences);
                genericMenu.DropDown(rect);
            }

            GUILayout.EndHorizontal();
            return EditorStyles.toolbar.fixedHeight;
        }

        private void OpenProfilerPreferences()
        {
            SettingsWindow x = SettingsWindow.Show(SettingsScope.User, "Preferences/Analysis/Profiler");
            if (x == null)
            {
                Debug.LogError("Could not find Preferences for 'Analysis/Profiler'");
            }
        }

        private void FrameNavigationControls()
        {
            if (m_CurrentFrame > ProfilerDriver.lastFrameIndex)
            {
                SetCurrentFrameDontPause(ProfilerDriver.lastFrameIndex);
            }

            using (new EditorGUI.DisabledScope(ProfilerDriver.GetPreviousFrameIndex(m_CurrentFrame) == -1))
            {
                if (GUILayout.Button(Styles.prevFrame, EditorStyles.toolbarButton))
                {
                    if (m_CurrentFrame == -1)
                    {
                        PrevFrame();
                    }

                    PrevFrame();
                }
            }

            using (new EditorGUI.DisabledScope(ProfilerDriver.GetNextFrameIndex(m_CurrentFrame) == -1))
            {
                if (GUILayout.Button(Styles.nextFrame, EditorStyles.toolbarButton))
                {
                    NextFrame();
                }
            }

            using (new EditorGUI.DisabledScope(ProfilerDriver.lastFrameIndex < 0))
            {
                if (GUILayout.Toggle(ProfilerDriver.lastFrameIndex >= 0 && m_CurrentFrame == -1, Styles.currentFrame, EditorStyles.toolbarButton))
                {
                    if (!m_CurrentFrameEnabled)
                    {
                        SelectAndStayOnLatestFrame();
                    }
                }
                else if (m_CurrentFrame == -1)
                {
                    m_CurrentFrameEnabled = false;
                    PrevFrame();
                }
                else if (m_CurrentFrameEnabled && m_CurrentFrame >= 0)
                {
                    m_CurrentFrameEnabled = false;
                }
            }

            GUIContent content = new GUIContent(Styles.frame.text + PickFrameLabel());
            EditorStyles.toolbarLabel.CalcMinMaxWidth(content, out float minWidth, out float _);
            if (minWidth > m_FrameCountLabelMinWidth)
            {
                m_FrameCountLabelMinWidth = minWidth + 10f;
            }

            GUILayout.Label(content, EditorStyles.toolbarLabel, GUILayout.MinWidth(m_FrameCountLabelMinWidth));
        }

        //
        // 요약:
        //     Selects the newest frame that was profiled and if newer frames are profiled or
        //     loaded into the profiler window, the Profiler Window will keep showing the newest
        //     frame of these.
        public void SelectAndStayOnLatestFrame()
        {
            SetCurrentFrame(-1);
            m_LastFrameFromTick = ProfilerDriver.lastFrameIndex;
            m_CurrentFrameEnabled = true;
        }

        private void SetCurrentFrameDontPause(int frame)
        {
            m_CurrentFrame = frame;
            InvokeSelectedFrameIndexChangedEventIfNecessary(frame);
        }

        private void SetCurrentFrame(int frame)
        {
            bool flag = frame != -1 && ProfilerDriver.enabled && !ProfilerDriver.profileEditor && m_CurrentFrame != frame;
            if (flag && EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPaused = true;
            }

            this.currentFrameChanged?.Invoke(frame, flag);
            SetCurrentFrameDontPause(frame);
        }

        internal void SetActiveVisibleFrameIndex(int frame)
        {
            if (frame != -1 && (frame < ProfilerDriver.firstFrameIndex || frame > ProfilerDriver.lastFrameIndex))
            {
                throw new ArgumentOutOfRangeException("frame");
            }

            this.currentFrameChanged?.Invoke(frame, arg2: false);
            SetCurrentFrameDontPause(frame);
            Repaint();
        }

        private void DoLegacyGUI_ToolbarAndCharts()
        {
            if (Event.current.isMouse)
            {
                ProfilerWindowAnalytics.RecordProfilerSessionMouseEvent();
            }

            if (Event.current.isKey)
            {
                ProfilerWindowAnalytics.RecordProfilerSessionKeyboardEvent();
            }

            CheckForPlatformModuleChange();
            float num = DrawMainToolbar();
            m_GraphPos = EditorGUILayout.BeginScrollView(m_GraphPos, Styles.profilerGraphBackground);
            Rect rect = (MainSplitView.fixedPane != null) ? MainSplitView.fixedPane.layout : Rect.zero;
            GUIStyle verticalScrollbar = GUI.skin.verticalScrollbar;
            float x = rect.width - verticalScrollbar.fixedWidth - (float)verticalScrollbar.padding.horizontal;
            float y = rect.height - num;
            int num2 = DrawModuleChartViews(new Vector2(x, y));
            if (num2 != m_CurrentFrame)
            {
                SetCurrentFrame(num2);
                Repaint();
                if (Event.current.type != EventType.Repaint)
                {
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private int DrawModuleChartViews(Vector2 containerSize)
        {
            float num = 0f;
            int num2 = 0;
            int num3 = -1;
            for (int i = 0; i < m_AllModules.Count; i++)
            {
                ProfilerModule profilerModule = m_AllModules[i];
                if (profilerModule.active)
                {
                    num += profilerModule.GetMinimumChartHeight();
                    num2++;
                    num3 = i;
                }
            }

            int num4 = m_CurrentFrame;
            if (num2 > 0)
            {
                float num5 = 0f;
                bool flag = num < containerSize.y;
                if (flag)
                {
                    float num6 = containerSize.y - num;
                    num5 = GUIUtility.RoundToPixelGrid(num6 / (float)num2);
                }

                float num7 = 0f;
                for (int j = 0; j < m_AllModules.Count; j++)
                {
                    ProfilerModule profilerModule2 = m_AllModules[j];
                    if (!profilerModule2.active)
                    {
                        continue;
                    }

                    float num8 = profilerModule2.GetMinimumChartHeight();
                    if (flag)
                    {
                        if (j == num3)
                        {
                            float num9 = containerSize.y - num7;
                            num8 = num9;
                        }
                        else
                        {
                            num8 += num5;
                            num7 += num8;
                        }
                    }

                    Rect rect = GUILayoutUtility.GetRect(containerSize.x, num8);
                    if (Event.current.type != EventType.Layout && GUIClip.visibleRect.Overlaps(rect))
                    {
                        bool isSelected = m_SelectedModuleIndex == j;
                        int lastFrameIndex = ProfilerDriver.lastFrameIndex;
                        num4 = profilerModule2.DrawChartView(rect, num4, isSelected, lastFrameIndex);
                    }
                }
            }
            else
            {
                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label(Styles.noActiveModules);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.FlexibleSpace();
            }

            return num4;
        }

        void ProfilerModulesDropdownWindow.IResponder.OnModuleActiveStateChanged()
        {
            m_ToolbarAndChartsIMGUIContainer.MarkDirtyRepaint();
            if (m_SelectedModuleIndex == -1)
            {
                SelectFirstActiveModule();
            }
        }

        void ProfilerModulesDropdownWindow.IResponder.OnConfigureModules()
        {
            if (ModuleEditorWindow.TryGetOpenInstance(out ModuleEditorWindow moduleEditorWindow))
            {
                moduleEditorWindow.Focus();
                return;
            }

            moduleEditorWindow = ModuleEditorWindow.Present(m_AllModules, ConnectedToEditor);
            moduleEditorWindow.onChangesConfirmed += OnModuleEditorChangesConfirmed;
        }

        void ProfilerModulesDropdownWindow.IResponder.OnRestoreDefaultModules()
        {
            DeselectSelectedModuleIfNecessary();
            for (int num = m_AllModules.Count - 1; num >= 0; num--)
            {
                ProfilerModule profilerModule = m_AllModules[num];
                if (profilerModule is DynamicProfilerModule)
                {
                    DeleteProfilerModuleAtIndex(num);
                }

                profilerModule.ResetOrderIndexToDefault();
            }

            SortModuleCollectionInPlace(ref m_AllModules);
            UpdateModules();
            Repaint();
            if (ModuleEditorWindow.TryGetOpenInstance(out ModuleEditorWindow moduleEditorWindow))
            {
                moduleEditorWindow.Close();
                moduleEditorWindow = ModuleEditorWindow.Present(m_AllModules, ConnectedToEditor);
                moduleEditorWindow.onChangesConfirmed += OnModuleEditorChangesConfirmed;
            }
        }

        private void OnModuleEditorChangesConfirmed(ReadOnlyCollection<ModuleData> modules, ReadOnlyCollection<ModuleData> deletedModules)
        {
            int selectedModuleIndex = m_SelectedModuleIndex;
            int num = 0;
            foreach (ModuleData module in modules)
            {
                switch (module.editedState)
                {
                    case ModuleData.EditedState.Created:
                        //CreateNewProfilerModule(module, num);
                        break;
                    case ModuleData.EditedState.Updated:
                        UpdateProfilerModule(module, num, selectedModuleIndex);
                        break;
                }

                num++;
            }

            foreach (ModuleData deletedModule in deletedModules)
            {
                DeleteProfilerModule(deletedModule);
            }

            if (deletedModules.Count > 0)
            {
                for (int i = 0; i < m_AllModules.Count; i++)
                {
                    ProfilerModule profilerModule = m_AllModules[i];
                    profilerModule.orderIndex = i;
                }
            }

            SortModuleCollectionInPlace(ref m_AllModules);
            UpdateModules();
            Repaint();
        }

        private void UpdateProfilerModule(ModuleData moduleData, int orderIndex, int selectedModuleIndexCached)
        {
            string currentProfilerModuleIdentifier = moduleData.currentProfilerModuleIdentifier;
            int num = IndexOfModuleWithIdentifier(currentProfilerModuleIdentifier);
            if (num < 0)
            {
                throw new IndexOutOfRangeException($"Unable to update module '{moduleData.name}' at index '{num}'.");
            }

            ProfilerModule profilerModule = m_AllModules[num];
            bool flag = profilerModule.orderIndex == selectedModuleIndexCached;
            List<ProfilerCounterData> chartCounters = new List<ProfilerCounterData>(moduleData.chartCounters);
            List<ProfilerCounterData> detailCounters = new List<ProfilerCounterData>(moduleData.detailCounters);
            //ProfilerModuleBase profilerModuleBase = profilerModule as ProfilerModuleBase;
            //if (profilerModuleBase != null)
            //{
            //    profilerModuleBase.SetNameAndUpdateAllPreferences(moduleData.name);
            //    profilerModuleBase.SetCounters(chartCounters, detailCounters);
            //}

            profilerModule.orderIndex = orderIndex;
            if (flag)
            {
                m_SelectedModuleIndex = orderIndex;
            }
        }

        private void DeleteProfilerModule(ModuleData moduleData)
        {
            string currentProfilerModuleIdentifier = moduleData.currentProfilerModuleIdentifier;
            int index = IndexOfModuleWithIdentifier(currentProfilerModuleIdentifier);
            DeleteProfilerModuleAtIndex(index);
        }

        private void DeleteProfilerModuleAtIndex(int index)
        {
            if (index < 0 || index >= m_AllModules.Count)
            {
                throw new IndexOutOfRangeException($"Unable to delete module at index '{index}'.");
            }

            ProfilerModule profilerModule = m_AllModules[index];
            profilerModule.active = false;
            profilerModule.OnDisable();
            profilerModule.DeleteAllPreferences();
            m_AllModules.RemoveAt(index);
        }

        private int IndexOfModuleWithIdentifier(string moduleIdentifier)
        {
            int result = -1;
            for (int i = 0; i < m_AllModules.Count; i++)
            {
                ProfilerModule profilerModule = m_AllModules[i];
                if (profilerModule.Identifier.Equals(moduleIdentifier))
                {
                    result = i;
                    break;
                }
            }

            return result;
        }

        internal void SetClearOnPlay(bool enabled)
        {
            m_ClearOnPlay = enabled;
        }

        internal bool GetClearOnPlay()
        {
            return m_ClearOnPlay;
        }

        private void OnTargetedEditorConnectionChanged(EditorConnectionTarget change)
        {
            switch (change)
            {
                case EditorConnectionTarget.None:
                case EditorConnectionTarget.MainEditorProcessPlaymode:
                    ProfilerDriver.profileEditor = false;
                    this.recordingStateChanged?.Invoke(m_Recording);
                    break;
                case EditorConnectionTarget.MainEditorProcessEditmode:
                    ProfilerDriver.profileEditor = true;
                    this.recordingStateChanged?.Invoke(m_Recording);
                    break;
                default:
                    ProfilerDriver.profileEditor = false;
                    if (Unsupported.IsDeveloperMode())
                    {
                        Debug.LogError($"{change} is not implemented!");
                    }

                    break;
            }

            SessionState.SetBool("ProfilerTargetMode", ProfilerDriver.profileEditor);
        }

        private bool IsEditorConnectionTargeted(EditorConnectionTarget connection)
        {
            if ((uint)connection <= 2u)
            {
                return ProfilerDriver.profileEditor;
            }

            if (Unsupported.IsDeveloperMode())
            {
                Debug.LogError($"{connection} is not implemented!");
            }

            return !ProfilerDriver.profileEditor;
        }

        private void OnConnectedToPlayer(string player, EditorConnectionTarget? editorConnectionTarget)
        {
            if (!editorConnectionTarget.HasValue || editorConnectionTarget.Value == EditorConnectionTarget.None)
            {
                ClearFramesOnPlayOrPlayerConnectionChange();
            }
        }

        internal static bool SetEditorDeepProfiling(bool deep)
        {
            bool flag = true;
            if (ProcessService.level == ProcessLevel.UMP_MASTER && EditorApplication.isPlaying)
            {
                flag = ((!deep) ? EditorUtility.DisplayDialog(Styles.disableDeepProfilingWarningDialogTitle, Styles.disableDeepProfilingWarningDialogContent, Styles.domainReloadWarningDialogButton, Styles.cancelDialogButton, DialogOptOutDecisionType.ForThisSession, "ProfilerDeepProfilingWarning") : EditorUtility.DisplayDialog(Styles.enableDeepProfilingWarningDialogTitle, Styles.enableDeepProfilingWarningDialogContent, Styles.domainReloadWarningDialogButton, Styles.cancelDialogButton, DialogOptOutDecisionType.ForThisSession, "ProfilerDeepProfilingWarning"));
            }

            if (flag)
            {
                ProfilerDriver.deepProfiling = deep;
                if (ProcessService.level == ProcessLevel.UMP_MASTER)
                {
                    EditorUtility.RequestScriptReload();
                }
            }

            return flag;
        }

        internal void SetCategoriesInUse(IEnumerable<string> categoryNames, bool inUse)
        {
            if (inUse)
            {
                foreach (string categoryName in categoryNames)
                {
                    m_CategoryActivator.RetainCategory(categoryName);
                }

                return;
            }

            foreach (string categoryName2 in categoryNames)
            {
                m_CategoryActivator.ReleaseCategory(categoryName2);
            }
        }

        private void SelectModuleAtIndex(int index)
        {
            ProfilerModule moduleToSelect = ModuleAtIndex(index);
            SelectModuleWithIndexAndDeselectSelectedModuleIfNecessary(moduleToSelect, index);
        }

        private void SelectModule(ProfilerModule module)
        {
            int moduleIndexToSelect = IndexOfModule(module);
            SelectModuleWithIndexAndDeselectSelectedModuleIfNecessary(module, moduleIndexToSelect);
        }

        private void SelectFirstActiveModule()
        {
            int index = -1;
            for (int i = 0; i < m_AllModules.Count; i++)
            {
                ProfilerModule profilerModule = m_AllModules[i];
                if (profilerModule.active)
                {
                    index = i;
                    break;
                }
            }

            SelectModuleAtIndex(index);
        }

        private void SelectModuleWithIndexAndDeselectSelectedModuleIfNecessary(ProfilerModule moduleToSelect, int moduleIndexToSelect)
        {
            DeselectSelectedModuleIfNecessary();
            if (moduleToSelect == null)
            {
                return;
            }

            if (!moduleToSelect.active)
            {
                moduleToSelect.active = true;
            }

            try
            {
                VisualElement visualElement = moduleToSelect.CreateDetailsView();
                if (visualElement == null)
                {
                    throw new InvalidOperationException(moduleToSelect.DisplayName + " did not provide a details view.");
                }

                m_DetailsViewContainer.Add(visualElement);
            }
            catch (Exception ex)
            {
                Debug.LogError("Unable to create a details view for the module '" + moduleToSelect.DisplayName + "'. " + ex.Message);
            }

            m_SelectedModuleIndex = moduleIndexToSelect;
        }

        private void DeselectSelectedModuleIfNecessary()
        {
            DeselectModuleAtIndexIfNecessary(m_SelectedModuleIndex);
        }

        private void DeselectModuleAtIndexIfNecessary(int index)
        {
            ProfilerModule profilerModule = ModuleAtIndex(index);
            if (profilerModule != null)
            {
                profilerModule.CloseDetailsView();
                m_SelectedModuleIndex = -1;
            }
        }

        private int IndexOfModule(ProfilerModule module)
        {
            int result = -1;
            for (int i = 0; i < m_AllModules.Count; i++)
            {
                ProfilerModule profilerModule = m_AllModules[i];
                if (profilerModule.Equals(module))
                {
                    result = i;
                    break;
                }
            }

            return result;
        }

        private ProfilerModule ModuleAtIndex(int index)
        {
            if (index != -1 && index >= 0 && index < m_AllModules.Count)
            {
                return m_AllModules[index];
            }

            return null;
        }

        private void InvokeSelectedFrameIndexChangedEventIfNecessary(int newFrame)
        {
            if (newFrame != m_LastReportedSelectedFrameIndex)
            {
                this.SelectedFrameIndexChanged?.Invoke(selectedFrameIndex);
                m_LastReportedSelectedFrameIndex = newFrame;
            }
        }

        ProfilerModule IProfilerWindowController.GetProfilerModuleByType(Type T)
        {
            return GetProfilerModuleByType(T);
        }

        void IProfilerWindowController.Repaint()
        {
            Repaint();
        }

        void IProfilerWindowController.SetClearOnPlay(bool enabled)
        {
            SetClearOnPlay(enabled);
        }

        bool IProfilerWindowController.GetClearOnPlay()
        {
            return GetClearOnPlay();
        }

        HierarchyFrameDataView IProfilerWindowController.GetFrameDataView(string groupName, string threadName, ulong threadId, HierarchyFrameDataView.ViewModes viewMode, int profilerSortColumn, bool sortAscending)
        {
            return GetFrameDataView(groupName, threadName, threadId, viewMode, profilerSortColumn, sortAscending);
        }

        HierarchyFrameDataView IProfilerWindowController.GetFrameDataView(int threadIndex, HierarchyFrameDataView.ViewModes viewMode, int profilerSortColumn, bool sortAscending)
        {
            return GetFrameDataView(threadIndex, viewMode, profilerSortColumn, sortAscending);
        }

        bool IProfilerWindowController.IsRecording()
        {
            return IsRecording();
        }

        bool IProfilerWindowController.ProfilerWindowOverheadIsAffectingProfilingRecordingData()
        {
            return ProfilerWindowOverheadIsAffectingProfilingRecordingData();
        }

        ProfilerProperty IProfilerWindowController.CreateProperty()
        {
            return CreateProperty();
        }

        ProfilerProperty IProfilerWindowController.CreateProperty(int sortType)
        {
            return CreateProperty(sortType);
        }

        void IProfilerWindowController.CloseModule(ProfilerModule module)
        {
            CloseModule(module);
        }
    }
}
