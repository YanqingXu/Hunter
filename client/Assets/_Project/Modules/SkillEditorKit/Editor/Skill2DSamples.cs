namespace SkillEditorKit.Editor
{
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class Skill2DSamples
{
    public const string Folder="Assets/_Project/Modules/SkillEditorKit/Samples/TwoD";
    [MenuItem("技能编辑器（独立版）/2D/创建横版示例（不会覆盖）")]
    public static void Create()
    {
        if(!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Project/Modules/SkillEditorKit/Samples","TwoD");
        var idle=SpriteAsset("SoldierIdle",false,false);
        var firing=SpriteAsset("SoldierFire",true,false);
        var bullet=SpriteAsset("Bullet",false,true);
        var animation=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"/ShootSprite.anim");
        var spriteBinding=EditorCurveBinding.PPtrCurve("Visual",typeof(SpriteRenderer),"m_Sprite");
        if(animation!=null)
        {
            var keys=AnimationUtility.GetObjectReferenceCurve(animation,spriteBinding);
            if(keys!=null && Array.Exists(keys,key=>key.value==null))
                AnimationUtility.SetObjectReferenceCurve(animation,spriteBinding,new[] {
                    new ObjectReferenceKeyframe { time=0,value=idle },new ObjectReferenceKeyframe { time=.1f,value=firing },
                    new ObjectReferenceKeyframe { time=.2f,value=idle },new ObjectReferenceKeyframe { time=.3f,value=idle } });
        }
        if(animation==null)
        {
            animation=new AnimationClip { name="ShootSprite",frameRate=30 };
            AnimationUtility.SetObjectReferenceCurve(animation,EditorCurveBinding.PPtrCurve("Visual",typeof(SpriteRenderer),"m_Sprite"),
                new[] { new ObjectReferenceKeyframe { time=0,value=idle },new ObjectReferenceKeyframe { time=.1f,value=firing },
                    new ObjectReferenceKeyframe { time=.2f,value=idle }, new ObjectReferenceKeyframe { time=.3f,value=idle } });
            AssetDatabase.CreateAsset(animation,Folder+"/ShootSprite.anim");
        }
        var bulletPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Bullet2D.prefab");
        RepairMissingSprite(bulletPrefab,bullet,Folder+"/Bullet2D.prefab");
        if(bulletPrefab==null)
        {
            var obj=new GameObject("Bullet2D");
            try { var sr=obj.AddComponent<SpriteRenderer>(); sr.sprite=bullet; sr.sortingOrder=10;
                bulletPrefab=PrefabUtility.SaveAsPrefabAsset(obj,Folder+"/Bullet2D.prefab"); }
            finally { Object.DestroyImmediate(obj); }
        }
        var shoot=AssetDatabase.LoadAssetAtPath<SkillClip>(Folder+"/Shoot2D.asset");
        if(shoot==null)
        {
            shoot=SkillEditorDocuments.Create(Folder+"/Shoot2D.asset","2D：步枪射击",30,9,"空白"); shoot.Space=SkillSpace.TwoD;
            Add(shoot,SkillEventKind.Animation,0,new SkillAnimationEvent { AnimationClip=animation,DurationFrame=9 });
            Add(shoot,SkillEventKind.Projectile,3,new SkillProjectileEvent { Prefab=bulletPrefab,LaunchHeight=1.1f,LaunchForward=.65f,
                Distance=12,FlightFrames=18,Radius=.07f,WallLayers=1,Hit=new AttackHitConfig { AttackMultiply=1 } });
            Save(shoot);
        }
        var melee=AssetDatabase.LoadAssetAtPath<SkillClip>(Folder+"/Melee2D.asset");
        if(melee==null)
        {
            melee=SkillEditorDocuments.Create(Folder+"/Melee2D.asset","2D：近战挥击",30,14,"空白"); melee.Space=SkillSpace.TwoD;
            Add(melee,SkillEventKind.Animation,0,new SkillAnimationEvent { AnimationClip=animation,DurationFrame=14 });
            Add(melee,SkillEventKind.Attack,3,new SkillAttackDetectionEvent { DurationFrame=5,
                AttackDetectionData=new AttackBoxDetectionData { UseFootOrigin=true,StartDistance=.1f,HeightOffset=.2f,Scale=new Vector3(1.8f,1.4f,1) },
                HitRules=new SkillHitRules { Mode=SkillHitMode.OncePerAttack },AttackHitConfig=new AttackHitConfig { AttackMultiply=2 } });
            Save(melee);
        }
        var actor=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Player2D.prefab");
        RepairMissingSprite(actor,idle,Folder+"/Player2D.prefab");
        if(actor==null)
        {
            var obj=new GameObject("Player2D");
            try
            {
                obj.AddComponent<Animator>(); obj.AddComponent<SkillAnimationPlayer>();
                obj.AddComponent<SkillPlayer>().attackDetectionLayer=1<<30;
                var visual=new GameObject("Visual"); visual.transform.SetParent(obj.transform,false);
                var sr=visual.AddComponent<SpriteRenderer>(); sr.sprite=idle; sr.sortingOrder=2;
                obj.AddComponent<SkillFacing2D>().Sprite=sr;
                var demo=obj.AddComponent<SkillDemoActor2D>(); demo.Team=1; demo.PlayerControlled=true; demo.Shoot=shoot; demo.Melee=melee;
                actor=PrefabUtility.SaveAsPrefabAsset(obj,Folder+"/Player2D.prefab");
            }
            finally { Object.DestroyImmediate(obj); }
        }
        string scenePath=Folder+"/Skill2DDemo.unity";
        if(!File.Exists(scenePath))
        {
            var previous=SceneManager.GetActiveScene();
            if(!Application.isBatchMode)
                for(int i=0;i<SceneManager.sceneCount;i++)
                    if(string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                    { AssetDatabase.SaveAssets(); Debug.LogWarning("2D 技能与预制体已生成。请保存当前未命名场景，再次执行此菜单以生成示例场景。"); return; }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            try
            {
                PrefabUtility.InstantiatePrefab(actor,scene);
                var camera=new GameObject("Camera").AddComponent<Camera>(); camera.tag="MainCamera";
                SceneManager.MoveGameObjectToScene(camera.gameObject,scene);
                camera.orthographic=true; camera.orthographicSize=4; camera.transform.position=new Vector3(1.5f,2,-10);
                camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.055f,.075f,.12f); camera.gameObject.AddComponent<AudioListener>();
                foreach(float x in new[] { -5f,5f,8f })
                {
                    var obj=new GameObject("Target "+x); obj.layer=30; obj.transform.position=new Vector3(x,0,0);
                    SceneManager.MoveGameObjectToScene(obj,scene);
                    var sr=obj.AddComponent<SpriteRenderer>(); sr.sprite=idle; sr.color=new Color(1,.45f,.35f); sr.flipX=x>0;
                    var box=obj.AddComponent<BoxCollider2D>(); box.size=new Vector2(.65f,1.45f); box.offset=new Vector2(0,.75f);
                    obj.AddComponent<SkillDemoActor2D>().Team=2;
                }
                var ground=new GameObject("Ground"); ground.transform.position=new Vector3(0,-.12f,0);
                SceneManager.MoveGameObjectToScene(ground,scene);
                var groundSprite=ground.AddComponent<SpriteRenderer>(); groundSprite.sprite=bullet; groundSprite.color=new Color(.22f,.35f,.42f);
                ground.transform.localScale=new Vector3(120,3,1);
                var collider=ground.AddComponent<BoxCollider2D>(); collider.size=new Vector2(.3f,.1f);
                if(!EditorSceneManager.SaveScene(scene,scenePath)) throw new InvalidOperationException("无法保存 2D 示例场景："+scenePath);
            }
            finally
            {
                if(Application.isBatchMode) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                else { if(previous.IsValid()) SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene,true); }
            }
        }
        AssetDatabase.SaveAssets();
    }
    [MenuItem("技能编辑器（独立版）/2D/定位横版示例")]
    public static void Locate()
    { Create(); var asset=AssetDatabase.LoadAssetAtPath<SkillClip>(Folder+"/Shoot2D.asset"); Selection.activeObject=asset; EditorGUIUtility.PingObject(asset); }

    private static Sprite SpriteAsset(string name,bool flash,bool bullet)
    {
        string path=Folder+"/"+name+".png";
        var existing=AssetDatabase.LoadAssetAtPath<Sprite>(path); if(existing!=null) return existing;
        int width=bullet ? 6 : 24, height=bullet ? 2 : 24;
        var tex=new Texture2D(width,height,TextureFormat.RGBA32,false) { name=name,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp };
        tex.SetPixels(new Color[width*height]);
        Action<int,int,int,int,Color> rect=(x,y,w,h,c)=> { for(int i=x;i<x+w;i++) for(int j=y;j<y+h;j++) tex.SetPixel(i,j,c); };
        if(bullet) rect(0,0,6,2,new Color(1,.82f,.2f));
        else
        {
            var dark=new Color(.13f,.19f,.22f); var green=new Color(.32f,.6f,.28f); var skin=new Color(.94f,.7f,.46f);
            rect(7,0,4,7,dark); rect(13,0,4,7,dark); rect(7,7,10,9,green);
            rect(8,16,9,6,skin); rect(7,21,11,3,dark); rect(15,18,2,1,Color.black);
            rect(14,11,8,3,dark); rect(13,10,3,3,skin);
            if(flash) { rect(21,10,3,5,Color.yellow); rect(22,9,2,7,new Color(1,.5f,.05f)); }
        }
        tex.Apply(); File.WriteAllBytes(path,tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Single;
        importer.spritePixelsPerUnit=16; importer.filterMode=FilterMode.Point; importer.mipmapEnabled=false;
        importer.textureCompression=TextureImporterCompression.Uncompressed;
        var settings=new TextureImporterSettings(); importer.ReadTextureSettings(settings);
        settings.spriteAlignment=(int)SpriteAlignment.Custom; settings.spritePivot=bullet ? new Vector2(.5f,.5f) : new Vector2(.5f,0);
        importer.SetTextureSettings(settings); importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
    private static void RepairMissingSprite(GameObject prefab,Sprite sprite,string path)
    {
        if(prefab==null || prefab.GetComponentInChildren<SpriteRenderer>().sprite!=null) return;
        var contents=PrefabUtility.LoadPrefabContents(path);
        try { contents.GetComponentInChildren<SpriteRenderer>().sprite=sprite; PrefabUtility.SaveAsPrefabAsset(contents,path); }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
    }
    internal static void Add(SkillClip clip,SkillEventKind kind,int frame,SkillFrameEventBase data)
    {
        data.EventId=Guid.NewGuid().ToString("N"); SkillTrackModel.SetFrame(data,frame);
        var track=new SkillTrackData { Kind=kind,Name=kind.ToString() };
        track.Clips.Add(new SkillTrackClip { Frame=frame,Data=data }); clip.Tracks.Add(track);
    }
    private static void Save(SkillClip clip)
    { EditorUtility.SetDirty(clip); SkillEditorChangeUtility.MarkChanged(clip); SkillEditorChangeUtility.SaveNow(clip); }
}
}
