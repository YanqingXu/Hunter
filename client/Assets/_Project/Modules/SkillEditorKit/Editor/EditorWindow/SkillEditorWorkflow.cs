// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public partial class SkillEditorWindow
{
    private VisualElement welcomeView, welcomeRecent, workArea, transportBar, inspectorBody, validationBody;
    private Label documentTitle, saveStatus, workflowHint, durationSummary, selectionTitle, checkSummary;
    private Foldout previewSettings, targetSettings, sceneSettings, checkPanel;
    private DropdownField previewSource;
    private Button saveDocumentButton, checkDocumentButton, previewResetButton, combatValidationButton;
    private GameObject workflowObservedActor, workflowObservedPrefab;
    private TwoPaneSplitView timelineSplit, workspaceSplit;
    [SerializeField] private float trackPanelWidth = 240, inspectorPanelWidth = 310;
    [SerializeField] private bool showProperties = true;
    private bool workflowReady, syncingWorkflow, showBasicProperties, workflowShowingWelcome;
    private int displayedRevision = -1;
    private double nextValidationTime;
    private SkillClip validatedDocument;
    private List<SkillClipValidator.ValidationIssue> currentIssues;

    public void OpenDocument(SkillClip clip)
    {
        if (SkillConfigObjectField == null) CreateGUI();
        if (skillConfig == clip) { RefreshWorkflow(); return; }
        SkillConfigObjectField.value = clip;
        if (clip != null) { SkillEditorDocuments.Remember(clip); showBasicProperties = false; }
        if (previewSettings != null) previewSettings.value = clip != null && currentPreviewCharacterObj == null;
        RefreshWorkflow(); RefreshInlineInspector();
    }

    private static Button Command(VisualElement parent, string name, string text, Action action, string tooltip = null)
    {
        var button = new Button(action) { name = name, text = text, tooltip = tooltip ?? text };
        button.AddToClassList("skill-command"); parent.Add(button); return button;
    }
    private static VisualElement Row(string name)
    {
        var row = new VisualElement { name = name }; row.AddToClassList("skill-row"); return row;
    }

    private void BuildWorkflowHeader()
    {
        workflowReady = false;
        workflowShowingWelcome = false;
        workflowObservedActor = workflowObservedPrefab = null;
        root.AddToClassList("skill-editor");
        var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/_Project/Modules/SkillEditorKit/Editor/EditorWindow/SkillEditorWorkflow.uss");
        if (sheet != null) root.styleSheets.Add(sheet);
        var top = root.Q("TopMenu");
        var oldButtons = top.Children().OfType<Button>().ToArray();
        top.Clear(); top.AddToClassList("skill-header");

        var commands = Row("DocumentCommands"); top.Add(commands);
        Command(commands, "NewDocumentButton", "＋ 新建", () => SkillNewDocumentWindow.Open(this));
        Command(commands, "OpenDocumentButton", "打开", () => SkillEditorDocuments.ShowOpen(this));
        Command(commands, "RecentDocumentButton", "最近使用", () => SkillEditorDocuments.ShowRecent(this));
        documentTitle = new Label("技能工作台") { name = "DocumentTitle" }; documentTitle.AddToClassList("skill-document-title"); commands.Add(documentTitle);
        saveDocumentButton = Command(commands, "SaveDocumentButton", "保存", SaveDocument, "立即保存技能配置（Ctrl+S）；默认自动保存");
        checkDocumentButton = Command(commands, "CheckDocumentButton", "检查配置", () => RunDocumentCheck(true));
        combatValidationButton = Command(commands, "CombatValidationButton", "命中测试", () =>
        {
            var prefab = currentPreviewCharacterPrefab;
            if (prefab == null && currentPreviewCharacterObj != null) prefab = PrefabUtility.GetCorrespondingObjectFromSource(currentPreviewCharacterObj);
            SkillCombatValidationWindow.Open(this, skillConfig, prefab);
        }, "在独立测试场景验证命中、阵营、穿透和墙壁阻挡；不修改正式场景");
        Command(commands, "ShowPropertiesButton", "属性面板", () => { showProperties = !showProperties; UpdatePropertyVisibility(); });
        Command(commands, "OpenPreviewViewButton", "预览视图", OpenPreviewView, "打开 Scene 预览视图并定位角色；不切换场景");

        var document = Row("DocumentIdentity"); top.Add(document);
        SkillConfigObjectField.label = "当前技能"; SkillConfigObjectField.allowSceneObjects = false;
        SkillConfigObjectField.style.marginLeft = 0; SkillConfigObjectField.style.fontSize = 12;
        SkillConfigObjectField.labelElement.style.minWidth = 74; SkillConfigObjectField.labelElement.style.width = 74;
        SkillConfigObjectField.AddToClassList("skill-asset-field"); document.Add(SkillConfigObjectField);
        saveStatus = new Label { name = "DocumentSaveStatus" }; saveStatus.AddToClassList("skill-save-status"); document.Add(saveStatus);
        SkillBasicButton.text = "技能信息"; document.Add(SkillBasicButton);

        var progress = Row("WorkflowProgress"); top.Add(progress);
        workflowHint = new Label { name = "WorkflowHint" }; workflowHint.AddToClassList("skill-workflow-hint"); progress.Add(workflowHint);

        previewSettings = new Foldout { name = "PreviewSettings", text = "预览设置", value = false }; top.Add(previewSettings);
        var bindings = new VisualElement { name = "PreviewBindings" }; previewSettings.Add(bindings);
        previewSource = new DropdownField("角色来源", new List<string> { "预制体副本（推荐）", "现有场景角色" }, 0) { name = "PreviewSourceMode" }; bindings.Add(previewSource);
        previewSource.RegisterValueChangedCallback(e =>
        {
            if (syncingWorkflow) return;
            bool scene = e.newValue == "现有场景角色";
            // A source mode is an actual binding choice, not a filter hiding the active actor.
            ClearPreviewCharacter(true);
            currentPreviewCharacterPrefab = null;
            PreviewCharacterPrefabObjectField.SetValueWithoutNotify(null);
            workflowObservedActor = workflowObservedPrefab = null;
            PreviewCharacterPrefabObjectField.style.display = scene ? DisplayStyle.None : DisplayStyle.Flex;
            PreviewCharacterObjectField.style.display = scene ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshWorkflow(); RefreshPreviewStatus();
        });
        foreach (var field in new[] { PreviewCharacterPrefabObjectField, PreviewCharacterObjectField })
        {
            bindings.Add(field); field.style.minWidth = 260; field.style.flexGrow = 1; field.style.fontSize = 12;
            field.labelElement.style.minWidth = 90;
        }
        PreviewCharacterPrefabObjectField.label = "角色预制体";
        PreviewCharacterPrefabObjectField.tooltip = "创建隔离副本用于预览，不运行游戏脚本，不写入场景；脚本重载后自动重建。";
        PreviewCharacterObjectField.label = "场景角色";
        PreviewCharacterObjectField.tooltip = "使用现有场景实例；预览停止后还原其状态。与预制体副本二选一。";
        var previewCommands = Row("PreviewCommands"); bindings.Add(previewCommands);
        Command(previewCommands, "UseSampleActorButton", "使用示例角色", () =>
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Modules/SkillEditorKit/Samples/PreviewActor.prefab");
            if (prefab != null) PreviewCharacterPrefabObjectField.value = prefab;
        }, "使用工程自带角色预制体的安全副本，不加载或保存场景");
        var restore = oldButtons.FirstOrDefault(button => button != LoadEditorSceneButton && button != LoadOldSceneButton && button != SkillBasicButton && button != addTrackButton);
        if (restore != null) { restore.name = "RestorePreviewButton"; restore.text = "停止并还原"; previewCommands.Add(restore); }
        previewResetButton = Command(previewCommands, "ResetPreviewButton", "重新生成副本", ResetManagedPreview, "仅重建托管预览副本；不重置技能配置或场景角色");
        Command(previewCommands, "FrameCharacterButton", "定位角色", () => FramePreview(false));
        Command(previewCommands, "FrameRangeButton", "定位范围", () => FramePreview(true));
        var ranges = new Toggle("显示范围") { name = "ShowAttackRangesToggle", value = showAttackRanges }; ranges.AddToClassList("skill-compact-toggle"); previewCommands.Add(ranges);
        ranges.RegisterValueChangedCallback(e => { showAttackRanges = e.newValue; RefreshPreviewStatus(); SceneView.RepaintAll(); });

        targetSettings = new Foldout { name = "PreviewReferences", text = "目标与落点（按技能需要设置）", value = false }; previewSettings.Add(targetSettings);
        AddReference(targetSettings, "预览目标", "PreviewTargetField", previewTarget, value => previewTarget = value);
        AddReference(targetSettings, "指定落点", "PreviewAimPointField", previewAimPoint, value => previewAimPoint = value);
        sceneSettings = new Foldout { name = "SceneTools", text = "场景工具（可选）", value = false }; previewSettings.Add(sceneSettings);
        var scenes = Row("SceneCommands"); sceneSettings.Add(scenes); scenes.Add(LoadEditorSceneButton); scenes.Add(LoadOldSceneButton);
        sceneSettings.Add(new Label("普通表现预览不要求切换场景。切换场景前会询问是否保存。") { style = { whiteSpace = WhiteSpace.Normal } });
        previewStatus = new Label { name = "PreviewStatusLabel" }; previewStatus.AddToClassList("skill-preview-status"); top.Add(previewStatus);
    }

    private void InitWorkflowLayout()
    {
        var ui = root.Q("TopMenu").parent;
        ui.style.flexGrow = 1; ui.style.minHeight = 0;
        workArea = new VisualElement { name = "SkillWorkArea" }; workArea.style.flexGrow = 1; workArea.style.minHeight = 0;
        var content = root.Q("Content");
        transportBar = root.Q("Controller"); transportBar.AddToClassList("skill-transport"); transportBar.RemoveFromHierarchy();
        workArea.Add(transportBar);
        var frameControls = transportBar.Q("FramerController");
        frameControls.Clear(); frameControls.Add(new Label("当前帧")); frameControls.Add(CurrentFrameField);
        frameControls.Add(new Label("结束帧")); frameControls.Add(FrameCountField);
        CurrentFrameField.tooltip = "当前播放头帧编号，从 0 开始"; FrameCountField.tooltip = "最后一个有效帧编号；0 到 N 共 N+1 帧";
        durationSummary = new Label { name = "SkillDurationSummary" }; durationSummary.AddToClassList("skill-duration"); transportBar.Add(durationSummary);
        Command(transportBar, "FitTimelineButton", "全览", FitTimeline, "缩放时间轴以显示完整技能与飞行尾段");

        var left = content.Q("Left"); var right = content.Q("Right");
        left.RemoveFromHierarchy(); right.RemoveFromHierarchy(); content.RemoveFromHierarchy();
        content.Clear(); content.style.flexGrow = 1; content.style.minWidth = 280; content.style.minHeight = 0;
        left.style.width = StyleKeyword.Auto; left.style.minWidth = 170;
        right.style.minWidth = 160; right.style.minHeight = 0;
        var header = Row("TrackHeader"); header.style.height = 30; header.Add(new Label("轨道")); header.Add(addTrackButton); left.Insert(0, header);
        timelineSplit = new TwoPaneSplitView(0, Mathf.Clamp(trackPanelWidth, 170, 400), TwoPaneSplitViewOrientation.Horizontal) { name = "TimelineSplit" };
        timelineSplit.style.flexGrow = 1; timelineSplit.Add(left); timelineSplit.Add(right); content.Add(timelineSplit);
        left.RegisterCallback<GeometryChangedEvent>(_ => { if (left.resolvedStyle.width >= 170) trackPanelWidth = left.resolvedStyle.width; });
        var inspector = new VisualElement { name = "InlineInspector" }; inspector.AddToClassList("skill-properties"); inspector.style.minWidth = 245;
        selectionTitle = new Label("属性") { name = "InlineInspectorTitle" }; selectionTitle.AddToClassList("skill-section-title"); inspector.Add(selectionTitle);
        var propertyScroll = new ScrollView { name = "InlineInspectorScroll" }; propertyScroll.style.flexGrow = 1; inspector.Add(propertyScroll);
        inspectorBody = new VisualElement { name = "InlineInspectorBody" }; propertyScroll.Add(inspectorBody);
        workspaceSplit = new TwoPaneSplitView(1, Mathf.Clamp(inspectorPanelWidth, 245, 480), TwoPaneSplitViewOrientation.Horizontal) { name = "WorkspaceSplit" };
        workspaceSplit.style.flexGrow = 1; workspaceSplit.style.minHeight = 0; workspaceSplit.Add(content); workspaceSplit.Add(inspector); workArea.Add(workspaceSplit);
        inspector.RegisterCallback<GeometryChangedEvent>(_ => { if (inspector.resolvedStyle.width >= 245) inspectorPanelWidth = inspector.resolvedStyle.width; });
        foreach (string name in new[] { "TrackMenuScrollView", "MainContentView" })
        {
            var scroll = root.Q<ScrollView>(name) ?? workArea.Q<ScrollView>(name); scroll.style.flexGrow = 1; scroll.style.minHeight = 0;
            if (name == "MainContentView") { scroll.horizontalScrollerVisibility = ScrollerVisibility.Auto; scroll.verticalScrollerVisibility = ScrollerVisibility.Auto; }
        }
        trackMenuParent.style.minHeight = 0; contentListView.style.minHeight = 0;
        contentViewPort.RegisterCallback<GeometryChangedEvent>(_ => UpdateTrackViewportHeight());

        welcomeView = new VisualElement { name = "WelcomeView" }; welcomeView.AddToClassList("skill-welcome");
        welcomeView.Add(new Label("从一个技能开始") { name = "WelcomeTitle", style = { fontSize = 24, unityFontStyleAndWeight = FontStyle.Bold } });
        welcomeView.Add(new Label("新建或打开技能，再选择预览角色。\n无需先切换场景，也不会自动修改原角色。") { style = { whiteSpace = WhiteSpace.Normal, marginTop = 10, marginBottom = 14 } });
        var welcomeCommands = Row("WelcomeCommands"); welcomeView.Add(welcomeCommands);
        Command(welcomeCommands, "WelcomeNewButton", "＋ 新建技能", () => SkillNewDocumentWindow.Open(this));
        Command(welcomeCommands, "WelcomeOpenButton", "打开已有技能", () => SkillEditorDocuments.ShowOpen(this));
        welcomeRecent = new VisualElement { name = "WelcomeRecent" }; welcomeView.Add(welcomeRecent);
        ui.Add(welcomeView); ui.Add(workArea);
        checkPanel = new Foldout { name = "ValidationPanel", text = "配置检查", value = false }; checkPanel.AddToClassList("skill-check-panel");
        checkSummary = new Label { name = "ValidationSummary" }; checkPanel.Add(checkSummary);
        var checkScroll = new ScrollView { name = "ValidationScroll" }; checkScroll.style.maxHeight = 140; checkPanel.Add(checkScroll);
        validationBody = new VisualElement { name = "ValidationIssues" }; checkScroll.Add(validationBody); ui.Add(checkPanel);
        workflowReady = true; UpdatePropertyVisibility(); RefreshWorkflow(); RefreshInlineInspector();
    }

    private void SaveDocument() { if (skillConfig == null) return; SkillEditorChangeUtility.SaveNow(skillConfig); RefreshWorkflow(); }
    private void OpenPreviewView()
    {
        var view = EditorWindow.GetWindow<SceneView>(); view.Show();
        FramePreview(false);
    }
    private void UpdatePropertyVisibility()
    {
        if (workspaceSplit == null) return;
        if (showProperties) workspaceSplit.UnCollapse(); else workspaceSplit.CollapseChild(1);
    }
    private void FitTimeline()
    {
        if (skillConfig == null || timerShaft == null) return;
        skillEditorConfig.frameUnitWidth = Mathf.Clamp((timerShaft.contentRect.width - 12) / (PreviewLastFrame + 1),
            .0001f, SkillEditorConfig.maxFrameWidthLV * SkillEditorConfig.standFrameUnitWidth);
        timelineScrollView.scrollOffset = new Vector2(0, timelineScrollView.scrollOffset.y);
        UpdateContentSize(); ResetTrack(); UpdateTimerShaftView();
    }
    private void UpdateTrackViewportHeight()
    {
        if (contentViewPort == null || contentListView == null || trackMenuParent == null) return;
        float minimum = Mathf.Max(0, contentViewPort.contentRect.height - 2);
        contentListView.style.minHeight = minimum; trackMenuParent.style.minHeight = minimum;
    }

    private void RefreshWorkflow()
    {
        if (!workflowReady) return;
        bool loaded = skillConfig != null;
        if (!loaded && !workflowShowingWelcome)
        {
            welcomeRecent.Clear();
            foreach (var clip in SkillEditorDocuments.Recent.Take(5))
            {
                var selected = clip;
                Command(welcomeRecent, "Recent_" + clip.GetInstanceID(),
                    (string.IsNullOrEmpty(clip.SkillName) ? clip.name : clip.SkillName) + "  ·  " + clip.name, () => OpenDocument(selected));
            }
        }
        workflowShowingWelcome = !loaded;
        welcomeView.style.display = loaded ? DisplayStyle.None : DisplayStyle.Flex;
        workArea.style.display = loaded ? DisplayStyle.Flex : DisplayStyle.None;
        previewSettings.style.display = loaded ? DisplayStyle.Flex : DisplayStyle.None;
        previewStatus.style.display = loaded ? DisplayStyle.Flex : DisplayStyle.None;
        checkPanel.style.display = loaded ? DisplayStyle.Flex : DisplayStyle.None;
        documentTitle.text = loaded ? (string.IsNullOrEmpty(skillConfig.SkillName) ? skillConfig.name : skillConfig.SkillName) : "技能工作台";
        documentTitle.tooltip = documentTitle.text;
        bool editable = SkillTimelineData.IsEditable(skillConfig, out string reason);
        saveDocumentButton.SetEnabled(loaded); checkDocumentButton.SetEnabled(loaded); SkillBasicButton.SetEnabled(loaded);
        combatValidationButton.SetEnabled(loaded && !EditorApplication.isPlayingOrWillChangePlaymode);
        addTrackButton.SetEnabled(editable); FrameCountField.SetEnabled(editable);
        PlayButton.SetEnabled(loaded && currentPreviewCharacterObj != null && !EditorApplication.isPlayingOrWillChangePlaymode);
        previewResetButton.SetEnabled(previewCharacterCreatedByEditor);
        if (!loaded) { saveStatus.text = "未打开技能"; workflowHint.text = "1 选择技能   →   2 设置预览角色   →   3 编辑、预览与检查"; return; }
        string saveError = SkillEditorChangeUtility.SaveError(skillConfig);
        saveStatus.text = !string.IsNullOrEmpty(saveError) ? "保存失败" : !EditorUtility.IsPersistent(skillConfig) ? "临时配置（未保存为资源）" :
            EditorUtility.IsDirty(skillConfig) ? "修改待保存…" : "已保存 · 自动保存开启";
        saveStatus.tooltip = saveError ?? AssetDatabase.GetAssetPath(skillConfig);
        saveStatus.style.color = !string.IsNullOrEmpty(saveError) ? new Color(1, .55f, .4f) :
            EditorUtility.IsDirty(skillConfig) ? new Color(.95f, .8f, .45f) : new Color(.6f, .79f, .68f);
        bool empty = !SkillTrackModel.Read(skillConfig).Any();
        workflowHint.text = !editable ? "当前配置只读 · " + reason : currentPreviewCharacterObj == null ?
            "下一步：展开“预览设置”选择角色；也可以先编辑轨道" : empty ? "下一步：添加轨道或把动画、特效等资源拖入对应轨道" :
            "编辑就绪 · 选择片段调整属性，拖动播放头预览；完成后检查配置";
        durationSummary.text = Math.Max(1, skillConfig.FrameRote) + " 帧/秒 · " + ((long)skillConfig.FrameCount + 1) + " 帧 · 结束于 " +
            ((double)skillConfig.FrameCount / Math.Max(1, skillConfig.FrameRote)).ToString("0.###") + " 秒";
        syncingWorkflow = true;
        bool useScene = currentPreviewCharacterObj != null && !previewCharacterCreatedByEditor;
        if (workflowObservedActor != currentPreviewCharacterObj || workflowObservedPrefab != currentPreviewCharacterPrefab)
        {
            previewSource.SetValueWithoutNotify(useScene ? "现有场景角色" : "预制体副本（推荐）");
            workflowObservedActor = currentPreviewCharacterObj; workflowObservedPrefab = currentPreviewCharacterPrefab;
        }
        useScene = previewSource.value == "现有场景角色";
        PreviewCharacterPrefabObjectField.style.display = useScene ? DisplayStyle.None : DisplayStyle.Flex;
        PreviewCharacterObjectField.style.display = useScene ? DisplayStyle.Flex : DisplayStyle.None;
        syncingWorkflow = false;
        int revision = SkillEditorChangeUtility.Revision(skillConfig);
        if (validatedDocument != skillConfig || displayedRevision != revision)
        {
            validatedDocument = skillConfig; displayedRevision = revision; nextValidationTime = EditorApplication.timeSinceStartup + .6;
            checkPanel.text = "配置检查 · 待更新";
        }
        if (nextValidationTime > 0 && EditorApplication.timeSinceStartup >= nextValidationTime && !IsPlaying && !TimelineSelection.IsDragging)
            RunDocumentCheck(false);
    }

    private void RunDocumentCheck(bool expand)
    {
        if (!workflowReady || skillConfig == null) return;
        nextValidationTime = 0;
        currentIssues = SkillClipValidator.ReadIssues(skillConfig);
        int errors = currentIssues.Count(issue => issue.IsError), warnings = currentIssues.Count - errors;
        checkPanel.text = "配置检查 · " + errors + " 错误 / " + warnings + " 提醒";
        checkSummary.text = currentIssues.Count == 0 ? "配置检查通过。表现预览不等于实际命中验证。" : "点击带定位标记的问题，跳转到对应片段。";
        validationBody.Clear();
        foreach (var issue in currentIssues)
        {
            var current = issue;
            var button = new Button(() => FocusIssue(current)) { text = (issue.IsError ? "错误 · " : "提醒 · ") + issue.Message,
                tooltip = issue.Data == null ? "查看技能基本信息" : "定位到对应片段" };
            button.style.whiteSpace = WhiteSpace.Normal; button.style.unityTextAlign = TextAnchor.MiddleLeft;
            if (issue.IsError) button.style.color = new Color(1, .65f, .5f);
            validationBody.Add(button);
        }
        if (expand) checkPanel.value = true;
    }
    private void FocusIssue(SkillClipValidator.ValidationIssue issue)
    {
        if (issue.Data == null) { ShowInlineBasicProperties(); return; }
        var entry = SkillTimelineData.Read(skillConfig).FirstOrDefault(item => ReferenceEquals(item.Data, issue.Data));
        if (entry == null || entry.Data == null) { RunDocumentCheck(true); return; }
        CurrentSelectFrameIndex = entry.Frame; TimelineSelection.SelectData(new[] { entry.Data });
        timelineScrollView.scrollOffset = new Vector2(Mathf.Max(0, entry.Frame * FrameUnitWidth - 40), timelineScrollView.scrollOffset.y);
        var item = TrackItems.FirstOrDefault(trackItem => ReferenceEquals(trackItem.Data, entry.Data));
        if (item != null) timelineScrollView.ScrollTo(item.Element);
    }

    internal void ShowInlineBasicProperties()
    {
        showBasicProperties = true; showProperties = true; UpdatePropertyVisibility(); RefreshInlineInspector();
    }
    internal void RefreshInlineInspector()
    {
        if (!workflowReady || inspectorBody == null) return;
        inspectorBody.Clear();
        if (skillConfig == null) return;
        if (showBasicProperties || TimelineSelection.Entries.Count == 0)
        {
            selectionTitle.text = "技能信息";
            var nameField = new TextField("技能名称") { value = skillConfig.SkillName ?? "", isDelayed = true }; inspectorBody.Add(nameField);
            nameField.RegisterValueChangedCallback(e => { SkillEditorChangeUtility.Apply("修改技能名称", () => skillConfig.SkillName = e.newValue); RefreshWorkflow(); });
            var spaceField = new DropdownField("游戏空间", new List<string> { "3D（向 +Z）", "2D 横版（XY 平面）" }, (int)skillConfig.Space);
            inspectorBody.Add(spaceField);
            spaceField.RegisterValueChangedCallback(e =>
            {
                SkillEditorChangeUtility.Apply("切换游戏空间", () => { skillConfig.Space = (SkillSpace)spaceField.index; skillConfig.DataVersion = SkillClip.CurrentDataVersion; });
                RefreshAfterDataChange(CurrentSelectFrameIndex);
            });
            if (skillConfig.Space == SkillSpace.TwoD)
            {
                inspectorBody.Add(new HelpBox("X 为左右、Y 为上下。动画使用 SpriteRenderer；判定使用 Collider2D。转身读取 flipX、缩放或 SkillFacing2D。", HelpBoxMessageType.Info));
                inspectorBody.Add(new Button(() => { var view=SceneView.lastActiveSceneView; if(view!=null) { view.in2DMode=true; if(PreviewCharacterObj!=null) view.Frame(new Bounds(PreviewCharacterObj.transform.position+Vector3.up,Vector3.one*8),false); } }) { text="Scene 切换 2D 视角" });
            }
            var rateField = new IntegerField("帧率（帧/秒）") { value = skillConfig.FrameRote, isDelayed = true }; inspectorBody.Add(rateField);
            rateField.RegisterValueChangedCallback(e =>
            {
                int value = Mathf.Clamp(e.newValue, 1, 240);
                if (SkillEditorChangeUtility.Apply("修改技能帧率", () => skillConfig.FrameRote = value)) RefreshAfterDataChange(CurrentSelectFrameIndex);
                else rateField.SetValueWithoutNotify(skillConfig.FrameRote);
            });
            inspectorBody.Add(new Label("选择时间轴片段，在这里编辑其参数。\n修改会自动保存；Ctrl+Z 撤销。") { style = { whiteSpace = WhiteSpace.Normal, marginTop = 10 } });
            inspectorBody.SetEnabled(SkillTimelineData.IsEditable(skillConfig, out _)); return;
        }
        selectionTitle.text = TimelineSelection.Entries.Count > 1 ? "已选 " + TimelineSelection.Entries.Count + " 个片段" : "片段属性";
        SkillEditorInspector.DrawSelection(inspectorBody, this);
    }
    internal void OnWorkflowSelectionChanged()
    {
        showBasicProperties = false;
        var attack = SelectedAttack(); if (attack != null) attack.TryPreview(true);
        var projectile = SelectedProjectile(); if (projectile != null) projectile.SamplePreview();
        RefreshInlineInspector(); RefreshPreviewStatus();
    }
}

}
