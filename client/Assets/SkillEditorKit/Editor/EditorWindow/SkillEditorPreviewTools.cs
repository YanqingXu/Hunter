// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public partial class SkillEditorWindow
{
    [SerializeField] private bool showAttackRanges = true;
    [SerializeField] private Transform previewTarget;
    [SerializeField] private Transform previewAimPoint;
    private Label previewStatus;
    public bool ShowAttackRanges => showAttackRanges;
    public Transform PreviewTarget => previewTarget;
    public Transform PreviewAimPoint => previewAimPoint;
    // This is a view extent, not the skill's authored end frame.
    internal int PreviewLastFrame
    {
        get
        {
            long last = CurrentFrameCount;
            if (skillConfig != null)
                foreach (var entry in SkillTrackModel.Read(skillConfig))
                    if (entry.Data is SkillProjectileEvent projectile && SkillProjectileEvent.Validate(projectile, skillConfig.FrameRote, out _))
                        last = Math.Max(last, (long)entry.Frame + projectile.PreviewFrames(skillConfig.FrameRote));
            return (int)Math.Min(SkillTimelineData.MaxFrame, last);
        }
    }

    private void InitPreviewTools() => BuildWorkflowHeader();

    private void AddReference(VisualElement parent, string label, string name, Transform value, Action<Transform> set)
    {
        var field = new ObjectField(label) { name = name, objectType = typeof(Transform), allowSceneObjects = true, value = value };
        // References now live in a vertical foldout: a 280px basis would become
        // a 280px field height instead of the old toolbar's intended field width.
        field.style.minWidth = 260; field.style.flexBasis = StyleKeyword.Auto; field.style.flexGrow = 0; field.style.flexShrink = 0;
        field.labelElement.style.width = 84; field.labelElement.style.minWidth = 84;
        field.RegisterValueChangedCallback(e =>
        {
            var next = e.newValue as Transform;
            if (next != null && EditorUtility.IsPersistent(next)) { field.SetValueWithoutNotify(value); return; }
            value = next; set(next); TickSkill();
        });
        parent.Add(field);
    }

    private void ReleaseOwnedPreview()
    {
        if (!previewCharacterCreatedByEditor) return;
        if (currentPreviewCharacterObj != null) DestroyImmediate(currentPreviewCharacterObj);
        currentPreviewCharacterObj = null; previewCharacterCreatedByEditor = false;
        PreviewCharacterObjectField?.SetValueWithoutNotify(null);
    }

    internal void RestorePreviewBinding()
    {
        PreviewCharacterPrefabObjectField?.SetValueWithoutNotify(currentPreviewCharacterPrefab);
        if (currentPreviewCharacterObj == null && currentPreviewCharacterPrefab != null && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            try
            {
                currentPreviewCharacterObj = SkillPreviewObjects.CreateCharacter(currentPreviewCharacterPrefab);
                previewCharacterCreatedByEditor = true;
                currentPreviewCharacterObj.name = "[技能预览] " + currentPreviewCharacterPrefab.name;
                currentPreviewCharacterObj.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                currentPreviewCharacterObj.SetActive(true);
                lastPreviewError = null;
            }
            catch (Exception ex) { lastPreviewError = "无法重建预览角色：" + ex.Message; }
        }
        PreviewCharacterObjectField?.SetValueWithoutNotify(currentPreviewCharacterObj);
        RefreshPreviewStatus();
    }

    private void ResetManagedPreview()
    {
        EndPreview();
        ReleaseOwnedPreview();
        RestorePreviewBinding();
        CurrentSelectFrameIndex = 0;
        TickSkill();
    }

    private void FramePreview(bool range)
    {
        var scene = SceneView.lastActiveSceneView;
        if (scene == null || currentPreviewCharacterObj == null) return;
        var item = SelectedAttack();
        if (range && item != null && item.TryPreview(true))
        {
            scene.Frame(new Bounds(item.PreviewPose.Position, Vector3.one * item.PreviewSize), false); return;
        }
        var projectile = SelectedProjectile();
        if (range && projectile != null && projectile.SamplePreview() && projectile.PreviewPoses.Count > 0)
        { scene.Frame(new Bounds(projectile.PreviewPoses[0].Position, Vector3.one * Mathf.Max(2, projectile.Event.Radius * 4)), false); return; }
        var bounds = new Bounds(currentPreviewCharacterObj.transform.position, Vector3.one * 2);
        foreach (var renderer in currentPreviewCharacterObj.GetComponentsInChildren<Renderer>(true)) bounds.Encapsulate(renderer.bounds);
        scene.Frame(bounds, false);
    }

    private AttackDetectionTrackItem SelectedAttack() => TrackItems.OfType<AttackDetectionTrackItem>().FirstOrDefault(i => i.IsSelected);
    private ProjectileTrackItem SelectedProjectile() => TrackItems.OfType<ProjectileTrackItem>().FirstOrDefault(i => i.IsSelected);

    internal string PreviewStatusText
    {
        get
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "运行模式中｜编辑预览已暂停";
            if (!string.IsNullOrEmpty(lastPreviewError)) return lastPreviewError;
            if (skillConfig == null) return "未选择技能配置";
            if (currentPreviewCharacterObj == null) return "未选择预览角色｜展开预览设置，选择角色来源";
            if (!showAttackRanges) return "范围显示已关闭｜勾选“显示范围”即可恢复";
            var selected = SelectedAttack();
            if (selected != null)
            {
                if (!string.IsNullOrEmpty(selected.PreviewError)) return selected.PreviewError;
                return "表现预览｜第 " + currentSelectFrameIndex + " 帧｜" + selected.PreviewState;
            }
            var projectile = SelectedProjectile();
            if (projectile != null)
            {
                if (!string.IsNullOrEmpty(projectile.PreviewError)) return projectile.PreviewError;
                return "表现预览｜第 " + currentSelectFrameIndex + " 帧｜" + projectile.PreviewState +
                    (projectile.Event.Prefab == null ? "｜未设置子弹外观" : "");
            }
            return "表现预览｜第 " + currentSelectFrameIndex + " 帧｜选中攻击片段查看范围；不执行实际伤害或阻挡判定";
        }
    }
    private void RefreshPreviewStatus()
    {
        if (previewStatus != null) previewStatus.text = PreviewStatusText;
    }
}

}
