using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.SceneManagement;

namespace SkillEditorKit.Editor
{
    /// <summary>Small Unity-native examples. No original game art or game framework is required.</summary>
    public static class SkillStandaloneSamples
    {
        public const string Folder = "Assets/SkillEditorKit/Samples";
        [MenuItem("技能编辑器（独立版）/创建基础示例（不会覆盖）")]
        public static void Create()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/SkillEditorKit", "Samples");
            string actorPath = Folder + "/PreviewActor.prefab";
            if (!File.Exists(actorPath))
            {
                var root = new GameObject("PreviewActor");
                root.SetActive(false);
                try
                {
                    root.AddComponent<Animator>();
                    var player = root.AddComponent<SkillPlayer>(); player.attackDetectionLayer = 1 << 30;
                    var body = GameObject.CreatePrimitive(PrimitiveType.Capsule); body.name = "Body";
                    body.transform.SetParent(root.transform, false); body.transform.localPosition = Vector3.up;
                    UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
                    var left = new GameObject("weaponShield_l"); left.transform.SetParent(root.transform, false); left.transform.localPosition = new Vector3(-.65f, 1, .3f);
                    var right = new GameObject("weaponShield_r"); right.transform.SetParent(root.transform, false); right.transform.localPosition = new Vector3(.65f, 1, .3f);
                    var weapon = GameObject.CreatePrimitive(PrimitiveType.Cube); weapon.name = "PreviewWeapon";
                    weapon.transform.SetParent(root.transform, false); weapon.transform.localScale = new Vector3(.12f, .12f, .8f);
                    var collider = weapon.GetComponent<BoxCollider>(); collider.isTrigger = true; collider.enabled = false;
                    var weaponController = weapon.AddComponent<SkillWeapon>();
                    var weaponSerialized = new SerializedObject(weaponController); weaponSerialized.FindProperty("detectionCollider").objectReferenceValue = collider; weaponSerialized.ApplyModifiedPropertiesWithoutUndo();
                    var constraint = weapon.AddComponent<ParentConstraint>();
                    constraint.AddSource(new ConstraintSource { sourceTransform = left.transform, weight = 0 });
                    constraint.AddSource(new ConstraintSource { sourceTransform = right.transform, weight = 1 });
                    constraint.locked = true; constraint.constraintActive = true;
                    var serialized = new SerializedObject(player); serialized.FindProperty("mainWeaponParentConstraint").objectReferenceValue = constraint; serialized.ApplyModifiedPropertiesWithoutUndo();
                    player.WeaponDic["MainWeapon"] = weaponController;
                    root.SetActive(true);
                    PrefabUtility.SaveAsPrefabAsset(root, actorPath);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            string animationPath = Folder + "/PreviewSwing.anim";
            var animation = AssetDatabase.LoadAssetAtPath<AnimationClip>(animationPath);
            if (animation == null)
            {
                animation = new AnimationClip { name = "PreviewSwing", frameRate = 30 };
                AnimationUtility.SetEditorCurve(animation, EditorCurveBinding.FloatCurve("weaponShield_r", typeof(Transform), "m_LocalPosition.z"),
                    AnimationCurve.EaseInOut(0, .3f, 1, 1));
                AnimationUtility.SetEditorCurve(animation, EditorCurveBinding.FloatCurve("weaponShield_l", typeof(Transform), "m_LocalPosition.z"),
                    AnimationCurve.EaseInOut(0, .3f, 1, 1));
                AssetDatabase.CreateAsset(animation, animationPath);
            }
            if (!File.Exists(Folder + "/Melee.asset"))
            {
                var clip = SkillEditorDocuments.Create(Folder + "/Melee.asset", "示例：近战范围", 30, 29, "空白");
                Add(clip, SkillEventKind.Animation, 0, new SkillAnimationEvent { AnimationClip = animation, DurationFrame = 30 });
                Add(clip, SkillEventKind.Attack, 8, new SkillAttackDetectionEvent { DurationFrame = 4,
                    AttackDetectionData = new AttackBoxDetectionData { UseFootOrigin = true, StartDistance = 2, HeightOffset = 1, Scale = new Vector3(2, 2, 2) },
                    HitRules = new SkillHitRules { Mode = SkillHitMode.OncePerAttack } });
                EditorUtility.SetDirty(clip); SkillEditorChangeUtility.MarkChanged(clip); SkillEditorChangeUtility.SaveNow(clip);
            }
            if (!File.Exists(Folder + "/Projectile.asset"))
            {
                var clip = SkillEditorDocuments.Create(Folder + "/Projectile.asset", "示例：独立投射物", 30, 14, "空白");
                Add(clip, SkillEventKind.Projectile, 3, new SkillProjectileEvent { FlightFrames = 30, Distance = 8, LaunchHeight = 1 });
                SkillEditorChangeUtility.MarkChanged(clip); SkillEditorChangeUtility.SaveNow(clip);
            }
            AssetDatabase.SaveAssets();
        }
        public static void CreateBatch()
        {
            if (!Application.isBatchMode || !File.Exists(".skill-editor-kit-validation"))
                throw new InvalidOperationException("Scene generation is restricted to the disposable validation project.");
            Create();
            if (!File.Exists(Folder + "/SkillEditorScene.unity"))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/PreviewActor.prefab"), scene);
                EditorSceneManager.SaveScene(scene, Folder + "/SkillEditorScene.unity");
            }
            AssetDatabase.SaveAssets();
        }
        private static void Add(SkillClip clip, SkillEventKind kind, int frame, SkillFrameEventBase data)
        {
            var track = new SkillTrackData { Kind = kind, Name = kind.ToString() };
            SkillTrackModel.SetFrame(data, frame); track.Clips.Add(new SkillTrackClip { Frame = frame, Data = data }); clip.Tracks.Add(track);
        }
    }
}
