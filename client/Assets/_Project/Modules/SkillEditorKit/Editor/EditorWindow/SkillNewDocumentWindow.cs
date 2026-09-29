// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class SkillNewDocumentWindow : EditorWindow
{
    private SkillEditorWindow owner;
    private TextField skillName;
    private IntegerField rate, endFrame;
    private DropdownField starter, space;
    private Label summary, error;
    public static void Open(SkillEditorWindow owner)
    {
        var window = CreateInstance<SkillNewDocumentWindow>(); window.owner = owner;
        window.titleContent = new GUIContent("新建技能"); window.minSize = new Vector2(390, 340); window.maxSize = new Vector2(540, 440); window.ShowUtility();
    }
    public void CreateGUI()
    {
        var root = rootVisualElement; root.Clear(); root.style.paddingLeft = root.style.paddingRight = 16; root.style.paddingTop = root.style.paddingBottom = 16;
        root.Add(new Label("创建技能资源") { style = { fontSize = 18, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 12 } });
        skillName = new TextField("技能名称") { value = "新技能" }; root.Add(skillName);
        space = new DropdownField("游戏空间",new List<string> { "3D（向 +Z）","2D 横版（XY 平面）" },owner?.SkillConfig?.Space == SkillSpace.TwoD ? 1 : 0); root.Add(space);
        starter = new DropdownField("起始轨道", new List<string> { "近战", "投射物", "空白" }, 0); root.Add(starter);
        rate = new IntegerField("帧率（帧/秒）") { value = 30, isDelayed = true }; root.Add(rate);
        endFrame = new IntegerField("结束帧（从 0 开始）") { value = 29, isDelayed = true }; root.Add(endFrame);
        summary = new Label(); root.Add(summary);
        rate.RegisterValueChangedCallback(_ => RefreshSummary()); endFrame.RegisterValueChangedCallback(_ => RefreshSummary()); RefreshSummary();
        root.Add(new HelpBox("起始轨道只创建空轨道，不会自动填写动画、伤害或特效。原有技能不会被修改。", HelpBoxMessageType.Info));
        error = new Label { style = { color = new Color(1, .55f, .4f), whiteSpace = WhiteSpace.Normal } }; root.Add(error);
        root.Add(new Button(CreateDocument) { text = "选择保存位置并创建", style = { height = 30, marginTop = 10 } });
    }
    private void RefreshSummary() => summary.text = rate.value > 0 && endFrame.value >= 0 ?
        "共 " + ((long)endFrame.value + 1) + " 个采样帧 · 结束于 " + ((double)endFrame.value / rate.value).ToString("0.###") + " 秒" : "请填写有效帧率和结束帧";
    private void CreateDocument()
    {
        if (string.IsNullOrWhiteSpace(skillName.value)) { error.text = "请先填写技能名称。"; return; }
        string fileName = string.Join("_", skillName.value.Split(System.IO.Path.GetInvalidFileNameChars()));
        string path = EditorUtility.SaveFilePanelInProject("保存新技能", fileName, "asset", "请选择新文件名。");
        if (string.IsNullOrEmpty(path)) return;
        try { var clip = SkillEditorDocuments.Create(path, skillName.value, rate.value, endFrame.value, starter.value);
            clip.Space=(SkillSpace)space.index; SkillEditorChangeUtility.MarkChanged(clip); SkillEditorChangeUtility.SaveNow(clip);
            owner?.OpenDocument(clip); Close(); }
        catch (Exception ex) { error.text = ex.Message; }
    }
}

}
