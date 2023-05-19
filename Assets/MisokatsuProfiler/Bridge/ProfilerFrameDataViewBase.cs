using UnityEngine;
using System;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditor.MPE;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace UnityEditorInternal.Profiling
{
    [Serializable]
    internal class ProfilerFrameDataViewBase
    {
        public event Action<ProfilerViewType> OnChangeViewType = viewType => { };
        public event Action<bool> OnToggleLive = on => { };

        protected static class BaseStyles
        {
            public static readonly GUIContent noData = EditorGUIUtility.TrTextContent("No frame data available. Select a frame from the charts above to see its details here.");
            public static GUIContent disabledSearchText = EditorGUIUtility.TrTextContent("Showing search results are disabled while recording with deep profiling.\nStop recording to view search results.");
            public static GUIContent cpuGPUTime = EditorGUIUtility.TrTextContent("CPU:{0}ms   GPU:{1}ms");

            public static readonly GUIStyle header = "OL title";
            public static readonly GUIStyle label = "OL label";
            public static readonly GUIStyle toolbar = EditorStyles.toolbar;
            public static readonly GUIStyle selectionExtraInfoArea = EditorStyles.helpBox;
            public static readonly GUIContent warningTriangle = EditorGUIUtility.IconContent("console.infoicon.inactive.sml");
            public static readonly GUIStyle tooltip = new GUIStyle("AnimationEventTooltip");
            public static readonly GUIStyle tooltipText = new GUIStyle("AnimationEventTooltip");
            public static readonly GUIStyle tooltipArrow = "AnimationEventTooltipArrow";
            public static readonly GUIStyle tooltipButton = EditorStyles.miniButton;
            public static readonly GUIStyle tooltipDropdown = new GUIStyle("MiniPopup");
            public static readonly int tooltipButtonAreaControlId = "ProfilerTimelineTooltipButton".GetHashCode();
            public static readonly int timelineTimeAreaControlId = "ProfilerTimelineTimeArea".GetHashCode();

            public static readonly GUIContent tooltipCopyTooltip = EditorGUIUtility.TrTextContent("Copy", "Copy to Clipboard");

            public static readonly GUIContent showDetailsDropdownContent = EditorGUIUtility.TrTextContent("Show");
            public static readonly GUIContent showFullDetailsForCallStacks = EditorGUIUtility.TrTextContent("Full details for Call Stacks");
            public static readonly GUIContent showSelectedSampleStacks = EditorGUIUtility.TrTextContent("Selected Sample Stack ...");
            public static readonly GUIStyle viewTypeToolbarDropDown = new GUIStyle(EditorStyles.toolbarDropDownLeft);
            public static readonly GUIStyle threadSelectionToolbarDropDown = new GUIStyle(EditorStyles.toolbarDropDown);
            public static readonly GUIStyle detailedViewTypeToolbarDropDown = new GUIStyle(EditorStyles.toolbarDropDown);
            public static readonly GUIContent updateLive = EditorGUIUtility.TrTextContent("Live", "Display the current or selected frame while recording Playmode or Editor. This increases the overhead in the EditorLoop when the Profiler Window is repainted.");
            public static readonly GUIContent liveUpdateMessage = EditorGUIUtility.TrTextContent("Displaying of frame data disabled while recording Playmode or Editor. To see the data, pause recording, or toggle \"Live\" display mode on. " +
                "\n \"Live\" display mode increases the overhead in the EditorLoop when the Profiler Window is repainted.");

            public static readonly string selectionExtraInfoHierarhcyView = L10n.Tr("Selection Info: ");
            public static readonly string proxySampleMessage = L10n.Tr("Sample \"{0}\" {1} {2} deeper not found in this frame within the selected Sample Stack.");
            public static readonly string proxySampleMessageScopeSingular = L10n.Tr("scope");
            public static readonly string proxySampleMessageScopePlural = L10n.Tr("scopes");
            public static readonly string proxySampleMessageTooltip = L10n.Tr("Selected Sample Stack: {0}");
            public static readonly string proxySampleMessagePart2TimelineView = L10n.Tr("\nClosest match:\n");

            public static readonly string callstackText = LocalizationDatabase.GetLocalizedString("Call Stack:");

            // 6 seems like a good default value for margins used in quite some places. Do note though, that this is little more than a semi-randomly chosen magic number.
            public const int magicMarginValue = 6;
            const float k_DetailedViewTypeToolbarDropDownWidth = 150f;

            public static readonly Rect tooltipArrowRect = new Rect(-32, 0, 64, 6);

            static BaseStyles()
            {
                viewTypeToolbarDropDown.fixedWidth = Chart.kSideWidth;
                viewTypeToolbarDropDown.stretchWidth = false;

                detailedViewTypeToolbarDropDown.fixedWidth = k_DetailedViewTypeToolbarDropDownWidth;
                tooltip.contentOffset = new Vector2(0, 0);
                tooltip.overflow = new RectOffset(0, 0, 0, 0);
                tooltipText = new GUIStyle(tooltip);
                tooltipText.onNormal.background = null;
                tooltipDropdown.margin.right += magicMarginValue;
            }
        }

        protected const string k_ShowFullDetailsForCallStacksPrefKey = "Profiler.ShowFullDetailsForCallStacks";

        internal static bool showFullDetailsForCallStacks
        {
            get
            {
                return EditorPrefs.GetBool(k_ShowFullDetailsForCallStacksPrefKey, false);
            }
            set
            {
                if (value != showFullDetailsForCallStacks)
                {
                    EditorPrefs.SetBool(k_ShowFullDetailsForCallStacksPrefKey, value);
                    callStackNeedsRegeneration = true;
                }
            }
        }

        [NonSerialized]
        internal static bool callStackNeedsRegeneration = false;

        [NonSerialized]
        public string dataAvailabilityMessage = null;

        static readonly GUIContent[] kCPUProfilerViewTypeNames = new GUIContent[]
        {
            EditorGUIUtility.TrTextContent("Hierarchy"),
            EditorGUIUtility.TrTextContent("Raw Hierarchy")
        };

        static GUIContent GetCPUProfilerViewTypeName(ProfilerViewType viewType)
        {
            switch (viewType)
            {
                case ProfilerViewType.Hierarchy:
                    return kCPUProfilerViewTypeNames[1];
                case ProfilerViewType.Timeline:
                    return kCPUProfilerViewTypeNames[0];
                case ProfilerViewType.RawHierarchy:
                    return kCPUProfilerViewTypeNames[2];
                default:
                    throw new NotImplementedException($"Lookup Not Implemented for {viewType}");
            }
        }

        static readonly int[] kCPUProfilerViewTypes = new int[]
        {
            (int)ProfilerViewType.Hierarchy,
            (int)ProfilerViewType.RawHierarchy
        };

        protected IProfilerWindowController m_ProfilerWindow;

        public CPUOrGPUProfilerModule cpuModule { get; private set; }

        

        public virtual void OnEnable(CPUOrGPUProfilerModule cpuOrGpuModule, IProfilerWindowController profilerWindow)
        {
            m_ProfilerWindow = profilerWindow;
            cpuModule = cpuOrGpuModule;
        }

        public virtual void OnDisable()
        {
        }

        protected void DrawViewTypePopup(ProfilerViewType viewType)
        {
            var newViewType = (ProfilerViewType)EditorGUILayout.IntPopup((int)viewType, kCPUProfilerViewTypeNames, kCPUProfilerViewTypes, BaseStyles.viewTypeToolbarDropDown, GUILayout.Width(BaseStyles.viewTypeToolbarDropDown.fixedWidth));

            if (newViewType != viewType)
            {
                OnChangeViewType.Invoke(newViewType);
                GUIUtility.ExitGUI();
            }
        }

        protected void DrawLiveUpdateToggle(bool updateViewLive)
        {
            using (new EditorGUI.DisabledScope(ProcessService.level != ProcessLevel.Main))
            {
                // This button is only needed in the Master Process
                var newUpdateViewLive = GUILayout.Toggle(updateViewLive, BaseStyles.updateLive, EditorStyles.toolbarButton);

                if (newUpdateViewLive != updateViewLive)
                {
                    OnToggleLive.Invoke(updateViewLive);
                }
            }
        }

        internal class SelectedSampleStackWindow : EditorWindow
        {
            public static class Content
            {
                public static readonly GUIContent title = EditorGUIUtility.TrTextContent("Sample Stack");
                public static readonly GUIContent selectedSampleStack = EditorGUIUtility.TrTextContent("Selected Sample Stack");
                public static readonly GUIContent actualSampleStack = EditorGUIUtility.TrTextContent("Actual Sample Stack");
            }

            [NonSerialized]
            static GCHandle m_PinnedInstance;

            [SerializeField]
            GUIContent m_SelectedSampleStackText = null;
            [SerializeField]
            GUIContent m_ActualSampleStackText = null;

            static readonly Vector2 k_DefaultInitalSize = new Vector2(100, 200);

            [NonSerialized]
            bool initialized = false;

            public static Vector2 CalculateSize(GUIContent selectedSampleStackText, GUIContent actualSampleStackText)
            {
                var copyButtonSize = GUI.skin.button.CalcSize(BaseStyles.tooltipCopyTooltip);
                if (copyButtonSize.x < 2)
                    return Vector2.zero; // Likely in a bad UI state, abort.

                var neededSelectedSampleStackSize = CalcSampleStackSize(Content.selectedSampleStack, selectedSampleStackText, copyButtonSize);
                var neededActualSampleStackSize = CalcSampleStackSize(Content.actualSampleStack, actualSampleStackText, copyButtonSize);

                var neededSize = new Vector2(neededSelectedSampleStackSize.x + neededActualSampleStackSize.x, Mathf.Max(neededSelectedSampleStackSize.y, neededActualSampleStackSize.y));

                neededSize.y += copyButtonSize.y + BaseStyles.magicMarginValue;

                // keep at least a minimum size according to the initial size
                neededSize.x = Mathf.Max(neededSize.x, k_DefaultInitalSize.x);
                return neededSize;
            }

            // use SelectedSampleStackWindow.CalculateSize from within OnGui to get precalculatedSize and avoid glitches on the first frame
            public static void ShowSampleStackWindow(Vector2 position, Vector2 precalculatedSize, GUIContent selectedSampleStackText, GUIContent actualSampleStackText)
            {
                var window = ShowSampleStackWindowInternal(position, precalculatedSize, selectedSampleStackText, actualSampleStackText);
                window.titleContent = Content.title;
                window.initialized = precalculatedSize != Vector2.zero;
            }

            static SelectedSampleStackWindow ShowSampleStackWindowInternal(Vector2 position, Vector2 size, GUIContent selectedSampleStackText, GUIContent actualSampleStackText)
            {
                if (m_PinnedInstance.IsAllocated && m_PinnedInstance.Target != null)
                {
                    var target = m_PinnedInstance.Target as SelectedSampleStackWindow;
                    target.Close();
                    DestroyImmediate(target);
                }
                // can't calculate the size in here if the call does not come from an OnGui context, so going with a sane default and resizing on initialization
                var rect = new Rect(position, size);
                var window = GetWindowWithRect<SelectedSampleStackWindow>(rect, true);
                window.m_SelectedSampleStackText = selectedSampleStackText;
                window.m_ActualSampleStackText = actualSampleStackText;
                window.m_Parent.window.m_DontSaveToLayout = true;
                return window;
            }

            void OnEnable()
            {
                m_PinnedInstance = GCHandle.Alloc(this);
            }

            void OnDisable()
            {
                m_PinnedInstance.Free();
            }

            void OnGUI()
            {
                if (m_SelectedSampleStackText == null && m_ActualSampleStackText == null)
                    Close();
                if (!initialized)
                {
                    // Ugly fallback in case the size was not pre calculated... this will cause a one frame glitch
                    var neededSize = CalculateSize(m_SelectedSampleStackText, m_ActualSampleStackText);
                    if (neededSize != Vector2.zero)
                    {
                        var rect = new Rect(position.position, neededSize);
                        position = rect;
                        minSize = rect.size;
                        maxSize = rect.size;
                        initialized = true;
                        Repaint();
                    }
                }

                GUILayout.BeginHorizontal();
                ShowSampleStack(Content.selectedSampleStack, m_SelectedSampleStackText);
                ShowSampleStack(Content.actualSampleStack, m_ActualSampleStackText);
                GUILayout.EndHorizontal();
            }

            static Vector2 CalcSampleStackSize(GUIContent label, GUIContent sampleStack, Vector2 copyButtonSize)
            {
                if (sampleStack != null)
                {
                    var neededSelectedSampleStackSize = GUI.skin.textArea.CalcSize(sampleStack);
                    var labelSize = GUI.skin.label.CalcSize(Content.selectedSampleStack);
                    neededSelectedSampleStackSize.y += copyButtonSize.y + BaseStyles.magicMarginValue;
                    neededSelectedSampleStackSize.x = Mathf.Max(neededSelectedSampleStackSize.x + BaseStyles.magicMarginValue, labelSize.x + BaseStyles.magicMarginValue);
                    neededSelectedSampleStackSize.x = Mathf.Max(neededSelectedSampleStackSize.x + BaseStyles.magicMarginValue, copyButtonSize.x + BaseStyles.magicMarginValue);
                    return neededSelectedSampleStackSize;
                }
                return Vector2.zero;
            }

            void ShowSampleStack(GUIContent label, GUIContent sampleStack)
            {
                if (sampleStack != null)
                {
                    GUILayout.BeginVertical();
                    GUILayout.Label(label);
                    GUILayout.TextArea(sampleStack.text, GUILayout.ExpandHeight(true));
                    if (GUILayout.Button(BaseStyles.tooltipCopyTooltip))
                    {
                        Clipboard.stringValue = sampleStack.text;
                    }
                    GUILayout.EndVertical();
                }
            }
        }

        public virtual void Clear()
        {
        }
    }
}
