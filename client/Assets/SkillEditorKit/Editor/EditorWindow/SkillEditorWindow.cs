// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public partial class SkillEditorWindow : EditorWindow
{
    public static SkillEditorWindow Instance;
    [MenuItem("技能编辑器（独立版）/打开工作台", false, 0)]
    [MenuItem("SkillEditorKit/Open Workbench")]
    public static void ShowExample()
    {
        SkillEditorWindow wnd = GetWindow<SkillEditorWindow>();
        wnd.titleContent = new GUIContent("技能编辑器·独立版");
    }
    private VisualElement root;
    private volatile bool validateRefreshRequested;
    public SkillPreviewSession PreviewSession { get; private set; }
    private string lastPreviewError;
    private SkillTimelineSelection timelineSelection;
    public SkillTimelineSelection TimelineSelection => timelineSelection ?? (timelineSelection = new SkillTimelineSelection(this));
    public IEnumerable<TrackItemBase> TrackItems => trackList.SelectMany(track => track.Items);
    public string ActiveTrackId { get; set; }
    public IEnumerable<SkillTrackBase> TrackViews => trackList;
    private Button addTrackButton;
    public void SetActiveTrack(string id)
    {
        ActiveTrackId = id;
        foreach (var track in trackList) track.Lane?.RefreshState();
    }
    public float FrameUnitWidth => skillEditorConfig.frameUnitWidth;
    private ScrollView timelineScrollView;
    public float TimelineScrollX => timelineScrollView == null ? 0 : timelineScrollView.scrollOffset.x;
    public void FocusTimeline()
    {
        Selection.activeObject = this;
        root?.Focus();
    }

    public void ScrollTimelineEdge(Vector2 mouse)
    {
        if (timelineScrollView == null) return;
        var bounds = timelineScrollView.contentViewport.worldBound;
        float amount = mouse.x < bounds.xMin + 24 ? -12 : mouse.x > bounds.xMax - 24 ? 12 : 0;
        if (amount == 0) return;
        var offset = timelineScrollView.scrollOffset;
        offset.x = Mathf.Max(0, offset.x + amount);
        timelineScrollView.scrollOffset = offset;
    }
    public void CreateGUI()
    {
        Instance = this;
        EndPreview();
        EndPlayheadDrag(false);
        TimelineSelection.Cancel();
        DestoryTracks();
        SkillClip.SetValidateAction(() => validateRefreshRequested = true);
        root = rootVisualElement;
        root.Clear();
        workflowReady = false;
        root.focusable = true;
        root.UnregisterCallback<KeyDownEvent>(OnEditorKeyDown, TrickleDown.TrickleDown);
        root.RegisterCallback<KeyDownEvent>(OnEditorKeyDown, TrickleDown.TrickleDown);
        var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/SkillEditorKit/Editor/EditorWindow/SkillEditorWindow.uxml");
        if (visualTree == null)
        {
            root.Add(new HelpBox("技能编辑器界面资源丢失。", HelpBoxMessageType.Error));
            return;
        }
        VisualElement labelFromUXML = visualTree.Instantiate();
        root.Add(labelFromUXML);
        InitTopMenu();
        InitTimerShaft();
        InitConsole();
        InitContent();
        InitWorkflowLayout();
        if (skillConfig != null)
        {
            SkillConfigObjectField.SetValueWithoutNotify(skillConfig);
            CurrentFrameCount = skillConfig.FrameCount;
        }
        else
        {
            CurrentFrameCount = 100;
        }
        RestorePreviewBinding();
        CurrentSelectFrameIndex = Mathf.Max(0, currentSelectFrameIndex);
        TickSkill();
        RefreshWorkflow();
    }

    private void ResetView()
    {
        if (skillConfig == null || contentListView == null) return;
        CurrentFrameCount = skillConfig.FrameCount;
        ResetTrack();
        TickSkill();
    }

    public void RefreshAfterUndo()
    {
        IsPlaying = false;
        TimelineSelection.Cancel();
        ResetView();
        SkillEditorInspector.Instance?.Show();
        RefreshInlineInspector();
    }

    public void RefreshAfterDataChange(int focusFrame, object dataToSelect = null)
    {
        IsPlaying = false;
        TimelineSelection.Cancel();
        CurrentFrameCount = skillConfig == null ? 0 : skillConfig.FrameCount;
        ResetTrack();
        CurrentSelectFrameIndex = Mathf.Clamp(focusFrame, 0, PreviewLastFrame);
        if (dataToSelect != null)
        {
            if (dataToSelect is IEnumerable<SkillFrameEventBase> group) TimelineSelection.SelectData(group);
            else if (dataToSelect is SkillFrameEventBase data) TimelineSelection.SelectData(new[] { data });
        }
        TickSkill();
        RefreshInlineInspector();
    }

    // 窗口销毁会调用，但是直接关闭Unity不会调用
    // private void OnDestroy()

    private void OnEnable()
    {
        Instance = this;
        SceneView.beforeSceneGui += OnSceneGUI;
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorSceneManager.sceneSaving += OnSceneSaving;
        AssemblyReloadEvents.beforeAssemblyReload += EndPreview;
        EditorApplication.quitting += EndPreview;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }
    private void OnDisable()
    {
        EndPreview();
        TimelineSelection.Cancel();
        IsPlaying = false;
        if (skillConfig != null)
        {
            SkillEditorChangeUtility.SaveNow(skillConfig);
        }
        SceneView.beforeSceneGui -= OnSceneGUI;
        EditorSceneManager.sceneOpened -= OnSceneOpened;
        EditorSceneManager.sceneSaving -= OnSceneSaving;
        AssemblyReloadEvents.beforeAssemblyReload -= EndPreview;
        EditorApplication.quitting -= EndPreview;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        DestoryTracks();
        ReleaseOwnedPreview();
        getPostionForRootMotion = null;
        if (Instance == this)
        {
            SkillClip.SetValidateAction(null);
            Instance = null;
        }
    }

    private void OnLostFocus() => EndPlayheadDrag(false);

    #region TopMenu
    private const string skillEditorScenePath = "Assets/SkillEditorKit/Samples/SkillEditorScene.unity";
    private string oldScenePath;

    private Button LoadEditorSceneButton;
    private Button LoadOldSceneButton;
    private Button SkillBasicButton;

    private ObjectField PreviewCharacterPrefabObjectField;
    private ObjectField PreviewCharacterObjectField;
    private ObjectField SkillConfigObjectField;
    [SerializeField] private GameObject currentPreviewCharacterPrefab;
    [SerializeField] private GameObject currentPreviewCharacterObj;
    [SerializeField] private bool previewCharacterCreatedByEditor;
    public GameObject PreviewCharacterObj { get => currentPreviewCharacterObj; }

    private void InitTopMenu()
    {
        LoadEditorSceneButton = root.Q<Button>(nameof(LoadEditorSceneButton));
        LoadEditorSceneButton.clicked += LoadEditorSceneButtonClick;

        LoadOldSceneButton = root.Q<Button>(nameof(LoadOldSceneButton));
        LoadOldSceneButton.clicked += LoadOldSceneButtonClick;

        SkillBasicButton = root.Q<Button>(nameof(SkillBasicButton));
        SkillBasicButton.clicked += SkillBasicButtonClick;
        var restoreButton = new Button(EndPreview) { text = "恢复预览", tooltip = "停止预览并恢复角色、武器及动画属性" };
        SkillBasicButton.parent.Add(restoreButton);
        addTrackButton = new Button(SkillTrackActions.ShowAddMenu) { name = "AddTrackButton", text = "＋ 添加轨道" };
        SkillBasicButton.parent.Add(addTrackButton);

        PreviewCharacterPrefabObjectField = root.Q<ObjectField>(nameof(PreviewCharacterPrefabObjectField));
        PreviewCharacterPrefabObjectField.objectType = typeof(GameObject);
        PreviewCharacterPrefabObjectField.allowSceneObjects = false;
        PreviewCharacterPrefabObjectField.RegisterValueChangedCallback(PreviewCharacterPrefabObjectFieldValueChanged);

        PreviewCharacterObjectField = root.Q<ObjectField>(nameof(PreviewCharacterObjectField));
        PreviewCharacterObjectField.objectType = typeof(GameObject);
        PreviewCharacterObjectField.allowSceneObjects = true;
        PreviewCharacterObjectField.RegisterValueChangedCallback(PreviewCharacterObjectFieldValueChanged);

        SkillConfigObjectField = root.Q<ObjectField>(nameof(SkillConfigObjectField));
        SkillConfigObjectField.objectType = typeof(SkillClip);
        SkillConfigObjectField.RegisterValueChangedCallback(SkillConfigObjectFieldValueChanged);
        InitPreviewTools();
    }

    public bool OnEditorScene
    {
        get
        {
            string currentScenePath = EditorSceneManager.GetActiveScene().path;
            return currentScenePath == skillEditorScenePath;
        }
    }

    // 加载编辑器场景
    private void LoadEditorSceneButtonClick()
    {
        string currentScenePath = EditorSceneManager.GetActiveScene().path;
        // 当前是编辑器场景，但是玩家依然点击了加载编辑器场景，没有意义
        if (currentScenePath == skillEditorScenePath) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(skillEditorScenePath) == null)
        {
            Debug.LogError($"技能编辑器场景不存在：{skillEditorScenePath}");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        oldScenePath = currentScenePath;
        IsPlaying = false;
        EditorSceneManager.OpenScene(skillEditorScenePath);
    }
    // 回归旧场景
    private void LoadOldSceneButtonClick()
    {
        if (!string.IsNullOrEmpty(oldScenePath))
        {
            string currentScenePath = EditorSceneManager.GetActiveScene().path;
            // 当前场景和旧场景是同一个场景，没有切换意义
            if (currentScenePath == oldScenePath) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(oldScenePath) == null)
            {
                Debug.LogWarning($"原场景不存在：{oldScenePath}");
                return;
            }
            IsPlaying = false;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            ClearPreviewCharacter(true);
            EditorSceneManager.OpenScene(oldScenePath);
        }
        else Debug.LogWarning("场景不存在！");
    }
    // 查看技能基本信息
    private void SkillBasicButtonClick()
    {
        if (skillConfig != null)
        {
            ShowInlineBasicProperties();
        }
    }

    // 角色预制体修改
    private void PreviewCharacterPrefabObjectFieldValueChanged(ChangeEvent<UnityEngine.Object> evt)
    {
        if (evt.newValue == currentPreviewCharacterPrefab) return;
        ClearPreviewCharacter(true);
        currentPreviewCharacterPrefab = evt.newValue as GameObject;
        RestorePreviewBinding();
        TickSkill();
    }

    // 角色预览对象修改
    private void PreviewCharacterObjectFieldValueChanged(ChangeEvent<UnityEngine.Object> evt)
    {
        if (evt.newValue == currentPreviewCharacterObj) return;
        var next = evt.newValue as GameObject;
        if (next != null && (EditorUtility.IsPersistent(next) || !next.scene.IsValid()))
        {
            PreviewCharacterObjectField.SetValueWithoutNotify(currentPreviewCharacterObj);
            lastPreviewError = "场景角色需要场景实例；预制体请放入左侧托管角色栏。";
            RefreshPreviewStatus(); return;
        }
        EndPreview();
        if (previewCharacterCreatedByEditor && currentPreviewCharacterObj != evt.newValue)
        {
            ClearPreviewCharacter(true);
        }
        currentPreviewCharacterObj = evt.newValue as GameObject;
        currentPreviewCharacterPrefab = null;
        PreviewCharacterPrefabObjectField?.SetValueWithoutNotify(null);
        previewCharacterCreatedByEditor = false;
        TickSkill();
    }

    private void ClearPreviewCharacter(bool destroyEditorCreatedObject)
    {
        EndPreview();
        if (destroyEditorCreatedObject && previewCharacterCreatedByEditor && currentPreviewCharacterObj != null)
        {
            DestroyImmediate(currentPreviewCharacterObj);
        }
        currentPreviewCharacterObj = null;
        previewCharacterCreatedByEditor = false;
        PreviewCharacterObjectField?.SetValueWithoutNotify(null);
    }

    private void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (scene != EditorSceneManager.GetActiveScene()) return;

        EndPreview();
        IsPlaying = false;
        ClearPreviewCharacter(true);
        RestorePreviewBinding();
        DestoryTracks();
        getPostionForRootMotion = null;
        if (skillConfig != null && contentListView != null && trackMenuParent != null)
        {
            InitTrack();
            TickSkill();
        }
    }

    // 技能配置修改
    private void SkillConfigObjectFieldValueChanged(ChangeEvent<UnityEngine.Object> evt)
    {
        EndPreview();
        IsPlaying = false;
        SkillEditorChangeUtility.SaveNow(skillConfig);
        TimelineSelection.Clear();
        skillConfig = evt.newValue as SkillClip;
        if (skillConfig != null) SkillEditorDocuments.Remember(skillConfig);
        CurrentSelectFrameIndex = 0;
        if (skillConfig == null)
        {
            CurrentFrameCount = 100;
        }
        else
        {
            CurrentFrameCount = skillConfig.FrameCount;
        }
        // 刷新轨道
        ResetTrack();
        if (previewSettings != null) previewSettings.value = skillConfig != null && currentPreviewCharacterObj == null;
        RefreshWorkflow(); RefreshInlineInspector();
    }

    #endregion

    #region TimerShaft
    private IMGUIContainer timerShaft;
    private IMGUIContainer selectLine;
    private IMGUIContainer playheadHandle;
    private VisualElement playheadDragSource;
    private const float PlayheadHitWidth = 26;
    private int playheadStartFrame;
    private float playheadGrabOffset;
    private VisualElement contentContainer;
    private VisualElement contentViewPort;

    [SerializeField] private int currentSelectFrameIndex = -1;
    public int CurrentSelectFrameIndex
    {
        get => currentSelectFrameIndex;
        private set
        {
            int old = currentSelectFrameIndex;
            currentSelectFrameIndex = Mathf.Clamp(value, 0, PreviewLastFrame);
            CurrentFrameField?.SetValueWithoutNotify(currentSelectFrameIndex);

            if (old != currentSelectFrameIndex)
            {
                UpdateTimerShaftView();
                TickSkill();
            }
        }
    }

    private int currentFrameCount;
    public int CurrentFrameCount
    {
        get => currentFrameCount;
        set
        {
            // FrameCount is the last valid frame index. A value of 0 therefore
            // represents a valid one-frame skill containing frame 0.
            currentFrameCount = Mathf.Clamp(value, 0, SkillTimelineData.MaxFrame);
            if (FrameCountField != null) FrameCountField.SetValueWithoutNotify(currentFrameCount);
            // View refresh must never mutate the asset, including malformed/future data.
            // Content区域的尺寸变化
            UpdateContentSize();
        }
    }

    // 当前内容区域的偏移坐标
    private float contentOffsetPos => timelineScrollView != null ? Mathf.Max(0, TimelineScrollX) :
        contentContainer == null ? 0 : Mathf.Abs(contentContainer.transform.position.x);
    private float currentSlectFramePos { get => currentSelectFrameIndex * skillEditorConfig.frameUnitWidth; }
    private void InitTimerShaft()
    {
        ScrollView MainContentView = root.Q<ScrollView>("MainContentView");
        contentContainer = MainContentView.Q<VisualElement>("unity-content-container");
        contentViewPort = MainContentView.Q<VisualElement>("unity-content-viewport");

        timerShaft = root.Q<IMGUIContainer>("TimerShaft");
        timerShaft.onGUIHandler = DrawTimerShaft;
        timerShaft.RegisterCallback<WheelEvent>(TimerShaftWheel);
        BindPlayheadDrag(timerShaft, false);

        playheadHandle = new IMGUIContainer(DrawPlayheadHandle)
        {
            name = "PlayheadHandle", tooltip = "拖动播放头预览；Esc 返回拖动前的帧", pickingMode = PickingMode.Position
        };
        playheadHandle.style.position = Position.Absolute;
        playheadHandle.style.width = PlayheadHitWidth;
        playheadHandle.style.height = 30;
        playheadHandle.style.top = 0;
        timerShaft.Add(playheadHandle);
        BindPlayheadDrag(playheadHandle, true);
        timerShaft.RegisterCallback<GeometryChangedEvent>(_ => UpdatePlayheadHandle());

        selectLine = root.Q<IMGUIContainer>("SelectLine");
        selectLine.onGUIHandler = DrawSelectLine;
    }

    private void DrawTimerShaft()
    {
        Handles.BeginGUI();
        Handles.color = Color.white;
        Rect rect = timerShaft.contentRect;
        // 起始索引
        int tickStep = 1;
        while (tickStep * FrameUnitWidth < 48 && tickStep < SkillTimelineData.MaxFrame) tickStep *= 10;
        if (tickStep > 1 && tickStep * FrameUnitWidth >= 240) tickStep /= 5;
        else if (tickStep > 1 && tickStep * FrameUnitWidth >= 96) tickStep /= 2;
        int minorStep = Mathf.Max(1, tickStep / 5);
        int index = Mathf.CeilToInt(contentOffsetPos / FrameUnitWidth / minorStep) * minorStep;
        float startOffset = index * FrameUnitWidth - contentOffsetPos;
        for (float i = startOffset; i < rect.width; i += FrameUnitWidth * minorStep)
        {
            // 绘制长线、文本
            if (index % tickStep == 0)
            {
                Handles.DrawLine(new Vector3(i, rect.height - 10), new Vector3(i, rect.height));
                string indexStr = index.ToString();
                float labelWidth = Mathf.Max(36, indexStr.Length * 8);
                GUI.Label(new Rect(i - labelWidth * .5f, 0, labelWidth, 20), indexStr);
            }
            else Handles.DrawLine(new Vector3(i, rect.height - 5), new Vector3(i, rect.height));
            index += minorStep;
        }
        if (skillConfig != null)
        {
            float end = skillConfig.FrameCount * FrameUnitWidth - contentOffsetPos;
            if (end >= 0 && end <= rect.width) GUI.Label(new Rect(Mathf.Max(0, end - 62), 0, 65, 20), "技能结束");
        }
        Handles.EndGUI();
    }

    private void DrawPlayheadHandle()
    {
        float xPosition = currentSlectFramePos - contentOffsetPos;
        if (xPosition < 0 || xPosition > timerShaft.contentRect.width) return;
        EditorGUIUtility.AddCursorRect(playheadHandle.contentRect, MouseCursor.SlideArrow);
        Handles.BeginGUI();
        Color previous = Handles.color;
        Handles.color = playheadDragSource != null ? new Color(.36f, .76f, 1) : new Color(.2f, .61f, .94f);
        Handles.DrawAAConvexPolygon(new Vector3(6, 3), new Vector3(20, 3), new Vector3(20, 18),
            new Vector3(13, 28), new Vector3(6, 18));
        Handles.color = previous;
        Handles.EndGUI();
        // Three small grooves make the head read as a draggable handle.
        for (int x = 9; x <= 15; x += 3) EditorGUI.DrawRect(new Rect(x, 7, 1, 8), new Color(1, 1, 1, .85f));
    }

    private void UpdatePlayheadHandle()
    {
        if (playheadHandle == null || timerShaft == null) return;
        float x = currentSlectFramePos - contentOffsetPos;
        bool visible = currentSelectFrameIndex >= 0 && x >= 0 && x <= timerShaft.contentRect.width;
        // Keep the captured element alive while dragging outside the visible ruler.
        playheadHandle.style.visibility = visible || playheadDragSource == playheadHandle ? Visibility.Visible : Visibility.Hidden;
        playheadHandle.style.left = x - PlayheadHitWidth * .5f;
        playheadHandle.MarkDirtyRepaint();
    }

    private void TimerShaftWheel(WheelEvent evt)
    {
        EndPlayheadDrag(false);
        skillEditorConfig.frameUnitWidth = Mathf.Clamp(FrameUnitWidth * Mathf.Pow(1.15f, -evt.delta.y), .0001f,
            SkillEditorConfig.maxFrameWidthLV * SkillEditorConfig.standFrameUnitWidth);
        UpdateTimerShaftView();
        UpdateContentSize();
        ResetTrack();
    }

    private void BindPlayheadDrag(VisualElement area, bool handle)
    {
        area.RegisterCallback<MouseDownEvent>(evt =>
        {
            if (evt.button != 0) return;
            EndPlayheadDrag(false);
            TimelineSelection.Cancel();
            IsPlaying = false;
            FocusTimeline();
            playheadStartFrame = CurrentSelectFrameIndex;
            float localX = timerShaft.WorldToLocal(evt.mousePosition).x;
            // Clicking near the edge of the wide handle must not move the playhead.
            playheadGrabOffset = handle ? localX - (currentSlectFramePos - contentOffsetPos) : 0;
            playheadDragSource = area;
            area.CaptureMouse();
            if (!handle) CurrentSelectFrameIndex = GetFrameIndexByMousePos(localX);
            UpdatePlayheadHandle();
            evt.PreventDefault(); evt.StopPropagation();
        });
        area.RegisterCallback<MouseMoveEvent>(evt =>
        {
            if (playheadDragSource != area) return;
            ScrollTimelineEdge(evt.mousePosition);
            ScrubPlayhead(evt.mousePosition);
            evt.PreventDefault(); evt.StopPropagation();
        });
        area.RegisterCallback<MouseUpEvent>(evt =>
        {
            if (evt.button != 0 || playheadDragSource != area) return;
            ScrubPlayhead(evt.mousePosition);
            EndPlayheadDrag(false);
            evt.PreventDefault(); evt.StopPropagation();
        });
        area.RegisterCallback<MouseCaptureOutEvent>(_ =>
        {
            if (playheadDragSource == area) EndPlayheadDrag(false);
        });
    }

    private void ScrubPlayhead(Vector2 mouse) => CurrentSelectFrameIndex =
        GetFrameIndexByMousePos(timerShaft.WorldToLocal(mouse).x - playheadGrabOffset);

    private void EndPlayheadDrag(bool restore)
    {
        var source = playheadDragSource;
        if (source == null) return;
        playheadDragSource = null;
        if (source.HasMouseCapture()) source.ReleaseMouse();
        if (restore) CurrentSelectFrameIndex = playheadStartFrame;
        UpdatePlayheadHandle();
    }

    /// <summary>
    /// 根据鼠标坐标获取帧索引
    /// </summary>
    public int GetFrameIndexByMousePos(float x)
    {
        return GetFrameIndexByPos(x + contentOffsetPos);
    }

    public int GetFrameIndexByPos(float x)
    {
        return Mathf.RoundToInt(x / skillEditorConfig.frameUnitWidth);
    }

    private void DrawSelectLine()
    {
        if (skillConfig != null)
        {
            float end = skillConfig.FrameCount * FrameUnitWidth - contentOffsetPos;
            if (end >= 0 && end <= contentViewPort.contentRect.width)
            {
                Handles.BeginGUI(); Handles.color = new Color(.95f, .65f, .28f, .8f);
                Handles.DrawDottedLine(new Vector3(end, 28), new Vector3(end, contentViewPort.contentRect.height + timerShaft.contentRect.height), 4);
                Handles.EndGUI();
            }
        }
        // 判断当前选中帧是否在视图范围内
        if (currentSlectFramePos >= contentOffsetPos)
        {
            Handles.BeginGUI();
            Handles.color = Color.white;
            float x = currentSlectFramePos - contentOffsetPos;
            Handles.DrawLine(new Vector3(x, 28), new Vector3(x, contentViewPort.contentRect.height + timerShaft.contentRect.height));
            Handles.EndGUI();
        }
    }

    private void UpdateTimerShaftView()
    {
        UpdatePlayheadHandle();
        timerShaft?.MarkDirtyLayout(); // 标志为需要重新绘制的
        selectLine?.MarkDirtyLayout(); // 标志为需要重新绘制的
        timerShaft?.MarkDirtyRepaint();
        selectLine?.MarkDirtyRepaint();
    }

    #endregion

    #region Console
    private Button PreviouFrameButton;
    private Button PlayButton;
    private Button NextFrameButton;
    private Toggle LoopPlaybackToggle;
    private DropdownField PlaybackSpeedField;
    private static readonly float[] PlaybackSpeeds = { .25f, .5f, .75f, 1, 1.25f, 1.5f, 2 };
    private static readonly List<string> PlaybackSpeedLabels = new List<string> { "0.25×", "0.5×", "0.75×", "1×", "1.25×", "1.5×", "2×" };
    [SerializeField] private float playbackSpeed = 1;
    public float PlaybackSpeed
    {
        get => NormalizePlaybackSpeed(playbackSpeed);
        set => SetPlaybackSpeedAt(value, EditorApplication.timeSinceStartup);
    }
    private static float NormalizePlaybackSpeed(float value) => Array.IndexOf(PlaybackSpeeds, value) >= 0 ? value : 1;

    // Re-anchor with the old speed before changing it. Keep sub-frame progress so
    // repeatedly switching rates neither jumps the playhead nor loses elapsed time.
    internal void SetPlaybackSpeedAt(float value, double now)
    {
        float speed = NormalizePlaybackSpeed(value);
        if (PlaybackSpeed != speed && IsPlaying)
        {
            AdvancePlayback(now);
            if (IsPlaying)
            {
                startFrameIndex = PlaybackFrameAt(now);
                startTime = now;
            }
        }
        playbackSpeed = speed;
        PlaybackSpeedField?.SetValueWithoutNotify(PlaybackSpeedLabels[Array.IndexOf(PlaybackSpeeds, speed)]);
        if (IsPlaying) EditorAudioUnility.SetPlaybackSpeed(speed);
    }

    [SerializeField] private bool loopPlayback;
    public bool LoopPlayback
    {
        get => loopPlayback;
        set
        {
            loopPlayback = value;
            LoopPlaybackToggle?.SetValueWithoutNotify(value);
        }
    }
    private IntegerField CurrentFrameField;
    private IntegerField FrameCountField;
    private void InitConsole()
    {
        PreviouFrameButton = root.Q<Button>(nameof(PreviouFrameButton));
        PreviouFrameButton.clicked += PreviouFrameButtonClick;

        PlayButton = root.Q<Button>(nameof(PlayButton));
        PlayButton.clicked += PlayButtonClick;
        RefreshPlayButton();

        LoopPlaybackToggle = root.Q<Toggle>(nameof(LoopPlaybackToggle));
        LoopPlaybackToggle.SetValueWithoutNotify(loopPlayback);
        LoopPlaybackToggle.RegisterValueChangedCallback(evt => LoopPlayback = evt.newValue);

        PlaybackSpeedField = root.Q<DropdownField>(nameof(PlaybackSpeedField));
        PlaybackSpeedField.choices = new List<string>(PlaybackSpeedLabels);
        playbackSpeed = PlaybackSpeed;
        PlaybackSpeedField.SetValueWithoutNotify(PlaybackSpeedLabels[Array.IndexOf(PlaybackSpeeds, playbackSpeed)]);
        PlaybackSpeedField.RegisterValueChangedCallback(evt =>
        {
            int index = PlaybackSpeedLabels.IndexOf(evt.newValue);
            if (index >= 0) PlaybackSpeed = PlaybackSpeeds[index];
        });

        NextFrameButton = root.Q<Button>(nameof(NextFrameButton));
        NextFrameButton.clicked += NextFrameButtonClick;

        CurrentFrameField = root.Q<IntegerField>(nameof(CurrentFrameField));
        CurrentFrameField.RegisterValueChangedCallback(CurrentFrameFieldValueChanged);

        FrameCountField = root.Q<IntegerField>(nameof(FrameCountField));
        FrameCountField.RegisterValueChangedCallback(FrameCountValueChanged);
    }

    private void PreviouFrameButtonClick()
    {
        IsPlaying = false;
        CurrentSelectFrameIndex -= 1;
    }
    private void PlayButtonClick()
    {
        IsPlaying = !IsPlaying;
    }
    private void NextFrameButtonClick()
    {
        IsPlaying = false;
        CurrentSelectFrameIndex += 1;
    }
    private void CurrentFrameFieldValueChanged(ChangeEvent<int> evt)
    {
        if (CurrentSelectFrameIndex != evt.newValue) CurrentSelectFrameIndex = evt.newValue;
    }
    private void FrameCountValueChanged(ChangeEvent<int> evt)
    {
        if (CurrentFrameCount == evt.newValue) return;
        int frames = Mathf.Clamp(evt.newValue, 0, SkillTimelineData.MaxFrame);
        if (SkillEditorChangeUtility.Apply("修改技能总帧数", () => skillConfig.FrameCount = frames))
        {
            CurrentFrameCount = frames;
            CurrentSelectFrameIndex = Mathf.Min(CurrentSelectFrameIndex, frames);
        }
        else FrameCountField.SetValueWithoutNotify(CurrentFrameCount);
    }


    #endregion

    #region Config
    [SerializeField] private SkillClip skillConfig;
    public SkillClip SkillConfig { get => skillConfig; }
    private SkillEditorConfig skillEditorConfig = new SkillEditorConfig();

    public void SaveConfig()
    {
        if (skillConfig != null)
        {
            SkillEditorChangeUtility.MarkChanged();
            ResetTrackData();
        }
    }

    private void ResetTrackData()
    {
        // 重新引用一下数据
        for (int i = 0; i < trackList.Count; i++)
        {
            trackList[i].OnConfigChanged();
        }
    }

    #endregion

    #region Track
    private VisualElement trackMenuParent;
    private VisualElement contentListView;
    private List<SkillTrackBase> trackList = new List<SkillTrackBase>();
    private void InitContent()
    {
        contentListView = root.Q<VisualElement>("ContentListView");
        trackMenuParent = root.Q<VisualElement>("TrackMenuList");

        ScrollView trackMneuScrollView = root.Q<ScrollView>("TrackMenuScrollView");
        ScrollView mainContentView = root.Q<ScrollView>("MainContentView");
        timelineScrollView = mainContentView;
        TimelineSelection.BindMarquee(contentListView);
        selectLine.pickingMode = PickingMode.Ignore;

        trackMneuScrollView.verticalScroller.valueChanged += (value) =>
        {
            mainContentView.verticalScroller.value = value;
        };
        mainContentView.verticalScroller.valueChanged += (value) =>
        {
            trackMneuScrollView.verticalScroller.value = value;
        };
        mainContentView.horizontalScroller.valueChanged += _ => UpdateTimerShaftView();
        // mainContentView.verticalScroller.valueChanged += ContentVerticalScorllerValueChanged;
        UpdateContentSize();
        InitTrack();
    }


    private void InitTrack()
    {
        // 如果没有配置，也不需要初始化轨道
        if (skillConfig == null) return;
        if (addTrackButton != null)
        {
            bool legacy = !skillConfig.UseTrackModel && SkillTimelineData.Read(skillConfig).Count > 0;
            addTrackButton.text = legacy ? "升级轨道（备份）" : "＋ 添加轨道";
            addTrackButton.tooltip = legacy ? "预览并备份迁移当前技能，再按需添加轨道" : "添加事件、主动画、音效、特效、攻击或投射物轨道";
        }
        if (skillConfig.UseTrackModel)
        {
            if (!SkillTrackModel.ValidateStructure(skillConfig, out string reason)) { Debug.LogWarning(reason); return; }
            foreach (var model in skillConfig.Tracks)
            {
                SkillTrackBase view;
                switch (model.Kind)
                {
                    case SkillEventKind.Custom: view = new EventTrack(); break;
                    case SkillEventKind.Animation: view = new AnimationTrack(); break;
                    case SkillEventKind.Audio: view = new AudioTrack(); break;
                    case SkillEventKind.Effect: view = new EffectTrack(); break;
                    case SkillEventKind.Attack: view = new AttackDetectionTrack(); break;
                    case SkillEventKind.Projectile: view = new ProjectileTrack(); break;
                    default: continue;
                }
                view.Model = model;
                view.Init(trackMenuParent, contentListView, skillEditorConfig.frameUnitWidth);
                trackList.Add(view);
            }
            getPostionForRootMotion = (frame, recover) => PreviewSession == null ? Vector3.zero : PreviewSession.GetRootPosition(skillConfig, frame);
            if (!skillConfig.Tracks.Any(t => t.TrackId == ActiveTrackId)) ActiveTrackId = skillConfig.Tracks.FirstOrDefault()?.TrackId;
            SetActiveTrack(ActiveTrackId);
            return;
        }
        if (SkillTimelineData.Read(skillConfig).Count == 0) return;
        InitEventTrack();
        InitAnimationTrack();
        InitAudioTrack();
        InitEffectTrack();
        InitAttackDetectionTrack();
        // ....
    }


    private void InitEventTrack()
    {
        EventTrack eventTrack = new EventTrack();
        eventTrack.Init(trackMenuParent, contentListView, skillEditorConfig.frameUnitWidth);
        trackList.Add(eventTrack);
    }

    private void InitAnimationTrack()
    {
        AnimationTrack animationTrack = new AnimationTrack();
        animationTrack.Init(trackMenuParent, contentListView, skillEditorConfig.frameUnitWidth);
        trackList.Add(animationTrack);
        getPostionForRootMotion = animationTrack.GetPostionForRootMotion;
    }

    private void InitAudioTrack()
    {
        AudioTrack audioTrack = new AudioTrack();
        audioTrack.Init(trackMenuParent, contentListView, skillEditorConfig.frameUnitWidth);
        trackList.Add(audioTrack);
    }

    private void InitEffectTrack()
    {
        EffectTrack effectTrack = new EffectTrack();
        effectTrack.Init(trackMenuParent, contentListView, skillEditorConfig.frameUnitWidth);
        trackList.Add(effectTrack);
    }

    private void InitAttackDetectionTrack()
    {
        AttackDetectionTrack attackDetectionTrack = new AttackDetectionTrack();
        attackDetectionTrack.Init(trackMenuParent, contentListView, skillEditorConfig.frameUnitWidth);
        trackList.Add(attackDetectionTrack);
    }

    private void ResetTrack()
    {
        TimelineSelection.Cancel();
        // Odin Undo restores new graph objects. Rebind track ownership, not just item visuals.
        bool rebuild = skillConfig == null || skillConfig.UseTrackModel || trackList.Any(t => t.Model != null);
        if (rebuild) DestoryTracks();
        if (skillConfig != null)
        {
            if (trackList.Count == 0) InitTrack();
            else foreach (var track in trackList) track.ResetView(skillEditorConfig.frameUnitWidth);
        }
        TimelineSelection.Rebind();
    }

    private void DestoryTracks()
    {
        for (int i = 0; i < trackList.Count; i++)
        {
            trackList[i].Destory();
        }
        trackList.Clear();
    }

    private void UpdateContentSize()
    {
        if (contentListView == null) return;
        contentListView.style.width = skillEditorConfig.frameUnitWidth * (PreviewLastFrame + 1);
    }



    public void ShowTrackItemOnInspecotr(TrackItemBase trackItem, SkillTrackBase track)
    {
        TimelineSelection.SelectOnly(trackItem);
    }

    private void OnEditorKeyDown(KeyDownEvent evt)
    {
        if (playheadDragSource != null)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                EndPlayheadDrag(true);
                evt.PreventDefault(); evt.StopPropagation();
            }
            return;
        }
        if (skillConfig == null || IsEditingField(evt.target)) return;

        bool handled = false;
        if (evt.keyCode == KeyCode.Escape)
        {
            if (TimelineSelection.IsDragging) { TimelineSelection.Cancel(); TimelineSelection.Rebind(); }
            else TimelineSelection.Clear();
            handled = true;
        }
        else if (TimelineSelection.IsDragging) return;
        else if (evt.ctrlKey && evt.keyCode == KeyCode.Z)
        {
            if (evt.shiftKey) Undo.PerformRedo(); else Undo.PerformUndo();
            handled = true;
        }
        else if (evt.ctrlKey && evt.keyCode == KeyCode.Y)
        {
            Undo.PerformRedo(); handled = true;
        }
        else if (evt.ctrlKey && evt.keyCode == KeyCode.A)
        {
            TimelineSelection.SelectAll(); handled = true;
        }
        else if (evt.ctrlKey && evt.keyCode == KeyCode.S)
        {
            SkillEditorChangeUtility.SaveNow(skillConfig); handled = true;
        }
        else if (evt.ctrlKey && evt.keyCode == KeyCode.C)
        {
            handled = SkillEditorClipboard.CopySelected();
        }
        else if (evt.ctrlKey && evt.keyCode == KeyCode.V)
        {
            handled = SkillEditorClipboard.PasteAt(CurrentSelectFrameIndex);
        }
        else if (evt.ctrlKey && evt.keyCode == KeyCode.D)
        {
            handled = SkillEditorClipboard.DuplicateSelected();
        }
        else if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace)
        {
            handled = SkillEditorClipboard.DeleteSelected();
        }
        else if (evt.keyCode == KeyCode.LeftArrow || evt.keyCode == KeyCode.RightArrow)
        {
            int direction = evt.keyCode == KeyCode.LeftArrow ? -1 : 1;
            handled = SkillEditorClipboard.MoveSelected(direction * (evt.shiftKey ? 10 : 1));
        }
        else if (evt.keyCode == KeyCode.Space)
        {
            IsPlaying = !IsPlaying;
            handled = true;
        }
        else if (evt.keyCode == KeyCode.Home)
        {
            IsPlaying = false;
            CurrentSelectFrameIndex = 0;
            handled = true;
        }
        else if (evt.keyCode == KeyCode.End)
        {
            IsPlaying = false;
            CurrentSelectFrameIndex = PreviewLastFrame;
            handled = true;
        }

        if (!handled) return;
        evt.PreventDefault();
        evt.StopPropagation();
    }

    private static bool IsEditingField(IEventHandler eventTarget)
    {
        VisualElement element = eventTarget as VisualElement;
        while (element != null)
        {
            if (element is TextField || element is IntegerField || element is FloatField ||
                element is Vector3Field || element is ObjectField || element is DropdownField || element is Toggle)
            {
                return true;
            }
            element = element.parent;
        }
        return false;
    }
    #endregion

    #region Preview
    public void EndPreview()
    {
        EndPlayheadDrag(false);
        IsPlaying = false;
        foreach (var track in trackList)
        {
            track.OnStop();
            if (track is EffectTrack)
                foreach (var item in track.Items.OfType<EffectTrackItem>()) item.CleanEffectPreviewObj();
            if (track is ProjectileTrack projectile) projectile.Cleanup();
        }
        var session = PreviewSession;
        PreviewSession = null;
        session?.Dispose();
        lastPreviewError = null;
        SceneView.RepaintAll();
    }

    private void OnSceneSaving(Scene scene, string path) => EndPreview();
    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode)
        { EndPreview(); ReleaseOwnedPreview(); }
        if (state == PlayModeStateChange.EnteredEditMode) { RestorePreviewBinding(); TickSkill(); }
    }

    private bool isPlaying;
    public bool IsPlaying
    {
        get => isPlaying;
        set
        {
            bool canPlay = value && skillConfig != null;
            if (isPlaying == canPlay) return;
            if (canPlay)
            {
                StartPlaybackAt(currentSelectFrameIndex >= PreviewLastFrame ? 0 : currentSelectFrameIndex,
                    EditorApplication.timeSinceStartup);
            }
            else
            {
                isPlaying = false;
                RefreshPlayButton();
                // OnStop
                for (int i = 0; i < trackList.Count; i++)
                {
                    trackList[i].OnStop();
                }
            }
        }
    }

    private void RefreshPlayButton()
    {
        if (PlayButton == null) return;
        PlayButton.text = isPlaying ? "❚❚" : "▶";
        PlayButton.tooltip = isPlaying ? "暂停预览（Space）" : "播放预览（Space）";
    }

    private void StartPlaybackAt(int frame, double clockTime)
    {
        // Sample while stopped so audio tracks are started exactly once by OnPlay.
        // Explicitly resample an unchanged frame for one-frame loops and restored previews.
        int previousFrame = currentSelectFrameIndex;
        CurrentSelectFrameIndex = frame;
        if (previousFrame == currentSelectFrameIndex) TickSkill();
        if (lastPreviewError != null) return;

        // Monotonic editor time also allows loops to preserve their fractional frame.
        startTime = clockTime;
        startFrameIndex = currentSelectFrameIndex;
        isPlaying = true;
        RefreshPlayButton();
        EditorAudioUnility.StopAllAudios();
        foreach (var track in trackList)
            if (track.Model == null || track.Model.Enabled) track.OnPlay(currentSelectFrameIndex);
    }

    private double startTime;
    private double startFrameIndex;
    private double PlaybackFrameAt(double now) => startFrameIndex + Math.Max(0, now - startTime) *
        Math.Max(1, skillConfig.FrameRote) * PlaybackSpeed;

    private void Update()
    {
        if (validateRefreshRequested && !TimelineSelection.IsDragging)
        {
            validateRefreshRequested = false;
            ResetView();
        }
        AdvancePlayback(EditorApplication.timeSinceStartup);
        RefreshPreviewStatus();
        RefreshWorkflow();
    }

    // Separate the clock from the editor update so boundary behaviour is deterministic.
    internal void AdvancePlayback(double now)
    {
        if (!IsPlaying) return;
        if (skillConfig == null)
        {
            EndPreview();
            return;
        }
        double frameRate = Math.Max(1, skillConfig.FrameRote);
        double elapsedFrame = PlaybackFrameAt(now);
        double targetFrame = Math.Floor(elapsedFrame);
        // FrameCount is inclusive: frame 0 through frame N occupy N + 1 frame intervals.
        int loopFrameCount = PreviewLastFrame + 1;
        if (loopPlayback && targetFrame >= loopFrameCount)
        {
            int wrappedFrame = (int)(targetFrame % loopFrameCount);
            // Restore the original pose/root and release owned audio/effects before
            // creating the next preview session. Never accumulate motion across loops.
            EndPreview();
            StartPlaybackAt(wrappedFrame, now - (elapsedFrame - targetFrame) / (frameRate * PlaybackSpeed));
        }
        else if (!loopPlayback && targetFrame >= PreviewLastFrame)
        {
            CurrentSelectFrameIndex = PreviewLastFrame;
            EndPreview();
        }
        else CurrentSelectFrameIndex = (int)targetFrame;
    }
    public void TickSkill()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        // 驱动技能表现
        if (skillConfig != null && currentPreviewCharacterObj != null)
        {
            try
            {
                if (PreviewSession == null) PreviewSession = new SkillPreviewSession(currentPreviewCharacterObj);
                if (skillConfig.UseTrackModel) PreviewSession.Evaluate(skillConfig, Math.Min(currentSelectFrameIndex, skillConfig.FrameCount));
                for (int i = 0; i < trackList.Count; i++)
                {
                    var track = trackList[i];
                    if (track is ProjectileTrack) { if (track.Model.Enabled) track.TickView(currentSelectFrameIndex); continue; }
                    if (currentSelectFrameIndex > skillConfig.FrameCount)
                    {
                        track.OnStop();
                        if (track is EffectTrack) foreach (var item in track.Items.OfType<EffectTrackItem>()) item.CleanEffectPreviewObj();
                    }
                    else if (track.Model == null || track.Model.Enabled) track.TickView(currentSelectFrameIndex);
                }
                lastPreviewError = null;
            }
            catch (Exception ex)
            {
                string previous = lastPreviewError;
                EndPreview();
                lastPreviewError = ex.Message;
                if (previous != ex.Message) Debug.LogWarning("技能预览已恢复并停止：" + ex.Message);
            }
        }
        RefreshPreviewStatus();
        SceneView.RepaintAll();
        RefreshWorkflow();
    }

    private Func<int, bool, Vector3> getPostionForRootMotion;
    public Vector3 GetPostionForRootMotion(int frameIndex, bool recove = false)
    {
        Vector3 result = getPostionForRootMotion != null ? getPostionForRootMotion(frameIndex, recove) : Vector3.zero;
        if (recove) TickSkill();
        return result;
    }
    #endregion

    #region Gizmo和SceneGUI
    [DrawGizmo(GizmoType.NotInSelectionHierarchy | GizmoType.Selected)]
    private static void DrawGizmos(SkillPlayer skill_Player, GizmoType gizmoType)
    {
        if (Instance == null || Instance.currentPreviewCharacterObj == null || Instance.currentPreviewCharacterObj.GetComponent<SkillPlayer>() != skill_Player) return;
        for (int i = 0; i < Instance.trackList.Count; i++)
        {
            if (!(Instance.trackList[i] is AttackDetectionTrack) &&
                (Instance.trackList[i].Model == null || Instance.trackList[i].Model.Enabled)) Instance.trackList[i].DrawGizmos();
        }
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        if (currentPreviewCharacterObj == null) return;
        for (int i = 0; i < trackList.Count; i++)
        {
            if (trackList[i] is AttackDetectionTrack || trackList[i] is ProjectileTrack)
            { if (ShowAttackRanges) trackList[i].OnSceneGUI(); }
            else if (trackList[i].Model == null || (trackList[i].Model.Enabled && !trackList[i].Model.Locked)) trackList[i].OnSceneGUI();
        }
    }

    #endregion

}

public class SkillEditorConfig
{
    public const int standFrameUnitWidth = 10;  // 标准帧单位宽度
    public const int maxFrameWidthLV = 10;      //  当前帧单位宽度
    public float frameUnitWidth = 10;           //  当前帧单位宽度，允许缩小至完整技能
    public float defaultFrameRote = 10;         //  默认帧率
}

}
