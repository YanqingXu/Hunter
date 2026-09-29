namespace SkillEditorKit.Editor
{
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>Integration regressions using real Unity physics, runtime casts and sprite animation.</summary>
public static class Skill2DVerification
{
    private static readonly List<string> results=new List<string>();
    private static int failures;
    private static void Check(bool condition,string reason) { if(!condition) throw new Exception(reason); }
    private static void Near(float actual,float expected,string reason) => Check(Mathf.Abs(actual-expected)<.002f,reason+": "+actual+" vs "+expected);
    private static void Test(string name,Action body)
    { try { body(); results.Add("PASS  "+name); } catch(Exception ex) { failures++; results.Add("FAIL  "+name+" — "+ex); } }

    [MenuItem("技能编辑器（独立版）/2D/运行适配回归检查")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || AnimationMode.InAnimationMode())
            throw new InvalidOperationException("请先停止运行与动画预览。");
        results.Clear(); failures=0; Skill2DSamples.Create();
        Test("Private 2D physics scene is isolated",()=> { using(var f=new Fixture()) Check(!f.Scene.GetPhysicsScene2D().Equals(Physics2D.defaultPhysicsScene),"shared scene"); });
        Test("Right-facing launch uses +X at world-up height",()=>MotionCase(false,false));
        Test("Sprite flipX fires left without inverting height",()=>MotionCase(true,false));
        Test("Negative transform scale fires left",()=>MotionCase(false,true));
        Test("Double visual mirror fires right",()=>
        {
            using(var f=new Fixture()) { f.Actor.transform.localScale=new Vector3(-1,1,1); f.Actor.AddComponent<SpriteRenderer>().flipX=true;
                var q=SkillFacing2D.Rotation(f.Actor.transform); Check((q*Vector3.right).x>.99f,"double mirror"); }
        });
        Test("Manual direction override",()=>
        { using(var f=new Fixture()) { f.Actor.AddComponent<SkillFacing2D>().SetFacing(-1); Check((SkillFacing2D.Rotation(f.Actor.transform)*Vector3.right).x<-.99f,"manual left"); } });
        Test("Spread rotates around Z",()=>
        {
            using(var f=new Fixture()) { var p=new SkillProjectileEvent { Count=3,SpreadAngle=60 };
                SkillProjectileMotion.TryLaunch(p,f.Actor.transform,null,null,0,out var a,out _,SkillSpace.TwoD);
                SkillProjectileMotion.TryLaunch(p,f.Actor.transform,null,null,2,out var b,out _,SkillSpace.TwoD);
                Near((a.Rotation*Vector3.right).y,-.5f,"lower spread"); Near((b.Rotation*Vector3.right).y,.5f,"upper spread"); }
        });
        Test("Socket launch uses its local X axis",()=>
        {
            using(var f=new Fixture()) { var socket=new GameObject("Muzzle"); socket.transform.SetParent(f.Actor.transform,false); socket.transform.localPosition=new Vector3(.5f,1,0); socket.transform.localRotation=Quaternion.Euler(0,0,90);
                var p=new SkillProjectileEvent { Anchor=SkillRangeAnchor.Socket,Facing=SkillRangeFacing.SocketForward,SocketPath="Muzzle",LaunchHeight=0,LaunchForward=1 };
                Check(SkillProjectileMotion.TryLaunch(p,f.Actor.transform,null,null,0,out var pose,out var error,SkillSpace.TwoD),error);
                Near(pose.Position.y,2,"muzzle point"); Near((pose.Rotation*Vector3.right).y,1,"upward gun"); }
        });
        Test("Arc stays in XY and rises when facing left",()=>
        {
            var p=new SkillProjectileEvent { Flight=SkillProjectileFlight.Arc,Distance=6,FlightFrames=30,ArcHeight=2 };
            var m=new SkillProjectileMotion(p,30,new SkillAttackShape.Pose { Space=SkillSpace.TwoD,Position=new Vector3(0,1,7),Rotation=Quaternion.Euler(0,0,180) });
            m.Advance(.5,null); Near(m.Pose.Position.x,-3,"arc X"); Near(m.Pose.Position.y,3,"arc Y"); Near(m.Pose.Position.z,7,"plane");
            m.Advance(.5,null); Near(m.Pose.Position.x,-6,"endpoint X"); Near(m.Pose.Position.y,1,"endpoint Y");
        });
        Test("Homing ignores target Z",()=>
        {
            var p=new SkillProjectileEvent { Flight=SkillProjectileFlight.Homing,Distance=6,FlightFrames=30,TurnDegreesPerSecond=360 };
            var m=new SkillProjectileMotion(p,30,new SkillAttackShape.Pose { Space=SkillSpace.TwoD,Position=new Vector3(0,0,4),Rotation=Quaternion.identity });
            m.Advance(.5,new Vector3(2,4,100)); Near(m.Pose.Position.z,4,"homing plane"); Check(m.Pose.Position.y>1,"must turn up");
        });
        Test("2D box hits Collider2D and ignores Collider",()=>
        {
            using(var f=new Fixture()) { var hit=f.Target(new Vector3(1,1,20)); var miss=f.Target(new Vector3(1,1,0),false);
                f.CastAttack(new AttackBoxDetectionData { UseFootOrigin=true,Scale=new Vector3(2,2,0) }); Check(f.Hits(hit)==1 && f.Hits(miss)==0,"wrong physics world"); }
        });
        Test("Homing reversal stays in XY for a directly-behind target",()=>
        {
            var p=new SkillProjectileEvent { Flight=SkillProjectileFlight.Homing,Distance=6,FlightFrames=30,TurnDegreesPerSecond=360 };
            var m=new SkillProjectileMotion(p,30,new SkillAttackShape.Pose { Space=SkillSpace.TwoD,Position=new Vector3(0,0,4),Rotation=Quaternion.identity });
            for(int i=0;i<40;i++) { m.Advance(.01,new Vector3(-10,0,4)); Near(m.Pose.Position.z,4,"reversal plane"); }
            Check((m.Pose.Rotation*Vector3.right).x<0,"did not turn left");
        });
        Test("Left-facing melee hits only the left target",()=>
        {
            using(var f=new Fixture()) { f.Actor.AddComponent<SpriteRenderer>().flipX=true;
                var left=f.Target(new Vector3(-1,1,0)); var right=f.Target(new Vector3(1,1,0));
                f.CastAttack(new AttackBoxDetectionData { UseFootOrigin=true,Scale=new Vector3(2,2,1) }); Check(f.Hits(left)==1 && f.Hits(right)==0,"left melee"); }
        });
        Test("2D circle hit",()=>
        { using(var f=new Fixture()) { var t=f.Target(new Vector3(1,1,0)); f.CastAttack(new AttackSphereDetectionData { UseFootOrigin=true,Radius=1 }); Check(f.Hits(t)==1,"circle"); } });
        Test("2D forward sector excludes behind and inner hole",()=>
        {
            using(var f=new Fixture()) { var front=f.Target(new Vector3(1.5f,0,0)); var back=f.Target(new Vector3(-1.5f,0,0)); var hole=f.Target(Vector3.zero);
                f.CastAttack(new AttackFanDetectionData { UseFootOrigin=true,Radius=3,InsideRadius=.7f,Angle=180,Height=0 });
                Check(f.Hits(front)==1 && f.Hits(back)==0 && f.Hits(hole)==0,"sector selection"); }
        });
        Test("2D swept box cannot tunnel through a thin target",()=>Sweep(false));
        Test("2D swept circle cannot tunnel through a thin target",()=>Sweep(true));
        Test("2D projectile high-speed sweep and duplicate colliders",()=>
        {
            using(var f=new Fixture()) { var t=f.Target(new Vector3(5,1,0)); t.gameObject.AddComponent<CircleCollider2D>().radius=.2f;
                f.CastProjectile(new SkillProjectileEvent { Distance=20,FlightFrames=1,Radius=.03f,PierceCount=3 }); f.Fly(.04);
                Check(f.Hits(t)==1,"must hit exactly once"); }
        });
        Test("2D projectile pierces two enemies",()=>
        {
            using(var f=new Fixture()) { var a=f.Target(new Vector3(2,1,0)); var b=f.Target(new Vector3(4,1,0)); var c=f.Target(new Vector3(6,1,0));
                f.CastProjectile(new SkillProjectileEvent { Distance=10,FlightFrames=1,PierceCount=1 }); f.Fly(.04);
                Check(f.Hits(a)==1 && f.Hits(b)==1 && f.Hits(c)==0,"pierce limit/order"); }
        });
        Test("2D wall stops projectile before enemy",()=>
        {
            using(var f=new Fixture()) { var t=f.Target(new Vector3(5,1,0)); f.Wall(new Vector3(3,1,0));
                f.CastProjectile(new SkillProjectileEvent { Distance=10,FlightFrames=1 }); f.Fly(.04);
                Check(f.Hits(t)==0 && !f.Projectiles().Any(),"wall was ignored"); }
        });
        Test("2D wall blocks melee line of sight",()=>
        {
            using(var f=new Fixture()) { var t=f.Target(new Vector3(3,1,0)); f.Wall(new Vector3(1.5f,1,0));
                f.CastAttack(new AttackBoxDetectionData { UseFootOrigin=true,Scale=new Vector3(4,2,1) },new SkillHitRules { Mode=SkillHitMode.OncePerAttack,BlockedByWalls=true,WallLayers=1 });
                Check(f.Hits(t)==0,"2D line of sight"); }
        });
        Test("2D teams and target layers",()=>
        {
            using(var f=new Fixture()) { var ally=f.Target(new Vector3(1,1,0)); ally.Team=1; var wrong=f.Target(new Vector3(2,1,0)); wrong.gameObject.layer=29; var enemy=f.Target(new Vector3(3,1,0));
                f.CastProjectile(new SkillProjectileEvent { Distance=10,FlightFrames=1 }); f.Fly(.04);
                Check(f.Hits(ally)==0 && f.Hits(wrong)==0 && f.Hits(enemy)==1,"team/layer filter"); }
        });
        Test("Space is captured for an in-flight skill",()=>
        {
            using(var f=new Fixture()) { var t=f.Target(new Vector3(5,1,0)); f.CastProjectile(new SkillProjectileEvent { Distance=10,FlightFrames=30 });
                f.Clip.Space=SkillSpace.ThreeD; f.Fly(.8); Check(f.Hits(t)==1,"mutable clip changed projectile mode"); }
        });
        Test("3D box default behavior is preserved",()=>
        {
            using(var f=new Fixture(SkillSpace.ThreeD)) { var t=f.Target(new Vector3(0,1,1),false);
                f.CastAttack(new AttackBoxDetectionData { UseFootOrigin=true,Scale=new Vector3(2,2,2) }); Check(f.Hits(t)==1,"3D box"); }
        });
        Test("3D projectile still travels +Z",()=>
        {
            using(var f=new Fixture(SkillSpace.ThreeD)) { var t=f.Target(new Vector3(0,1,5),false);
                f.CastProjectile(new SkillProjectileEvent { Distance=10,FlightFrames=1 }); f.Fly(.04); Check(f.Hits(t)==1,"3D projectile"); }
        });
        Test("2D sprite timeline preview and restoration",()=>
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Skill2DSamples.Folder+"/Player2D.prefab");
            var clip=AssetDatabase.LoadAssetAtPath<SkillClip>(Skill2DSamples.Folder+"/Shoot2D.asset");
            var actor=Object.Instantiate(prefab);
            try { var sr=actor.GetComponentInChildren<SpriteRenderer>(); var original=sr.sprite;
                using(var preview=new SkillPreviewSession(actor)) { preview.Evaluate(clip,4); Check(sr.sprite!=original,"sprite keyframe not sampled"); }
                Check(sr.sprite==original,"preview did not restore sprite"); }
            finally { Object.DestroyImmediate(actor); }
        });
        Test("Runtime sprite animation samples through Animator",()=>
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Skill2DSamples.Folder+"/Player2D.prefab"); var actor=Object.Instantiate(prefab);
            try { var sr=actor.GetComponentInChildren<SpriteRenderer>(); var original=sr.sprite; var driver=actor.GetComponent<SkillAnimationPlayer>(); driver.Init();
                var animation=AssetDatabase.LoadAssetAtPath<AnimationClip>(Skill2DSamples.Folder+"/ShootSprite.anim");
                var sample=driver.PlaySkillAnimation(new SkillAnimationEvent { AnimationClip=animation,DurationFrame=9 },30); sample(4d/30);
                Check(sr.sprite!=original,"runtime sprite not sampled"); driver.StopSkillAnimation(); }
            finally { Object.DestroyImmediate(actor); }
        });
        Test("Workbench independent 2D hit test uses runtime",()=>
        {
            var clip=AssetDatabase.LoadAssetAtPath<SkillClip>(Skill2DSamples.Folder+"/Shoot2D.asset");
            var settings=new SkillCombatValidation.Settings { Skill=clip,ActorPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Skill2DSamples.Folder+"/Player2D.prefab"),ExpectedHits=1 };
            settings.Targets[0].Position=new Vector3(3,1,0); var report=SkillCombatValidation.Run(settings);
            Check(report.Complete && report.Hits==1,report.Error ?? "expected one hit, got "+report.Hits);
        });
        Test("Both 2D skill assets pass configuration validation",()=>
        {
            foreach(string name in new[] { "Shoot2D","Melee2D" })
            { var clip=AssetDatabase.LoadAssetAtPath<SkillClip>(Skill2DSamples.Folder+"/"+name+".asset");
                var errors=SkillClipValidator.ReadIssues(clip).Where(i=>i.IsError).ToArray(); Check(errors.Length==0,string.Join(";",errors.Select(i=>i.Message))); }
        });
        results.Insert(0,"Unity "+Application.unityVersion+" | "+(results.Count-failures)+"/"+results.Count+" passed | "+DateTime.Now.ToString("s"));
        string path=Arg("-skill2DReport") ?? "Logs/Skill2DVerification.txt";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))); File.WriteAllLines(path,results);
        Debug.Log(string.Join("\n",results));
        if(failures>0) throw new Exception(failures+" 2D adaptation regressions failed. See "+path);
    }
    public static void RunBatch()
    {
        Run(); AssetDatabase.SaveAssets();
        string package=Arg("-skill2DPackage");
        if(!string.IsNullOrEmpty(package)) AssetDatabase.ExportPackage("Assets/SkillEditorKit",package,ExportPackageOptions.Recurse);
    }
    public static void ExportBatch()
    {
        string package=Arg("-skill2DPackage");
        if(string.IsNullOrEmpty(package)) throw new ArgumentException("Missing -skill2DPackage output path");
        AssetDatabase.SaveAssets(); AssetDatabase.ExportPackage("Assets/SkillEditorKit",package,ExportPackageOptions.Recurse);
    }
    private static string Arg(string key)
    { var args=Environment.GetCommandLineArgs(); int i=Array.IndexOf(args,key); return i>=0 && i+1<args.Length ? args[i+1] : null; }
    private static void MotionCase(bool flip,bool scale)
    {
        using(var f=new Fixture())
        {
            f.Actor.AddComponent<SpriteRenderer>().flipX=flip; if(scale) f.Actor.transform.localScale=new Vector3(-1,1,1);
            var p=new SkillProjectileEvent { LaunchHeight=1.25f,LaunchForward=.5f,Distance=6,FlightFrames=30 };
            Check(SkillProjectileMotion.TryLaunch(p,f.Actor.transform,null,null,0,out var pose,out var error,SkillSpace.TwoD),error);
            float sign=flip||scale ? -1 : 1; Near(pose.Position.x,.5f*sign,"launch X"); Near(pose.Position.y,1.25f,"launch height");
            var motion=new SkillProjectileMotion(p,30,pose); motion.Advance(.5,null); Near(motion.Pose.Position.x,3.5f*sign,"flight X"); Near(motion.Pose.Position.y,1.25f,"flight height");
        }
    }
    private static void Sweep(bool circle)
    {
        using(var f=new Fixture())
        {
            var t=f.Target(new Vector3(5,.2f,0));
            AttackShapeDetectionDataBase shape=circle ? (AttackShapeDetectionDataBase)new AttackSphereDetectionData { Radius=.2f } : new AttackBoxDetectionData { Scale=new Vector3(.2f,.4f,1) };
            shape.UseFootOrigin=true; shape.Motion=AttackRangeMotion.LinearFlight; shape.StartDistance=0; shape.EndDistance=10;
            f.CastAttack(shape); f.Player.AdvanceBy(1.001f/30); Check(f.Hits(t)==1,"sweep missed");
        }
    }
    private sealed class Fixture : IDisposable
    {
        public Scene Scene; public GameObject Actor; public SkillPlayer Player; public SkillClip Clip;
        private readonly Dictionary<SkillValidationTarget,int> hits=new Dictionary<SkillValidationTarget,int>();
        public Fixture(SkillSpace space=SkillSpace.TwoD)
        {
            Scene=EditorSceneManager.NewPreviewScene(); Actor=new GameObject("Test caster"); SceneManager.MoveGameObjectToScene(Actor,Scene);
            var owner=Actor.AddComponent<SkillValidationTarget>(); owner.Team=1;
            Player=Actor.AddComponent<SkillPlayer>(); Player.attackDetectionLayer=1<<30; Player.QueryScene=Scene.GetPhysicsScene(); Player.QueryScene2D=Scene.GetPhysicsScene2D();
            Player.Init(owner,null,Actor.transform); Player.StartPlaySkillBehaviour(new SkillBehaviourBase());
            Clip=ScriptableObject.CreateInstance<SkillClip>(); Clip.Space=space; Clip.FrameRote=30; Clip.FrameCount=30; Clip.DataVersion=SkillClip.CurrentDataVersion; Clip.UseTrackModel=true;
        }
        public SkillValidationTarget Target(Vector3 position,bool twoD=true)
        {
            var go=new GameObject("Target "+hits.Count); SceneManager.MoveGameObjectToScene(go,Scene); go.layer=30; go.transform.position=position;
            if(twoD) go.AddComponent<BoxCollider2D>().size=new Vector2(.2f,.4f); else go.AddComponent<BoxCollider>().size=new Vector3(.2f,.4f,.2f);
            var t=go.AddComponent<SkillValidationTarget>(); t.Team=2; hits[t]=0; t.Received=(target,data)=>hits[target]++; return t;
        }
        public void Wall(Vector3 position)
        { var go=new GameObject("Wall"); SceneManager.MoveGameObjectToScene(go,Scene); go.transform.position=position; go.AddComponent<BoxCollider2D>().size=new Vector2(.15f,3); }
        public int Hits(SkillValidationTarget target)=>hits[target];
        public void CastAttack(AttackShapeDetectionDataBase shape,SkillHitRules rules=null)
        {
            Skill2DSamples.Add(Clip,SkillEventKind.Attack,0,new SkillAttackDetectionEvent { DurationFrame=1,AttackDetectionData=shape,
                HitRules=rules ?? new SkillHitRules { Mode=SkillHitMode.OncePerAttack } }); Cast();
        }
        public void CastProjectile(SkillProjectileEvent p) { Skill2DSamples.Add(Clip,SkillEventKind.Projectile,0,p); Cast(); }
        private void Cast() { Physics.SyncTransforms(); Physics2D.SyncTransforms(); Player.PlaySkillClip(Clip); Check(Player.IsPlaying,"cast failed"); }
        public SkillProjectile[] Projectiles()=>SkillProjectile.Active.Where(p=>p!=null && p.gameObject.scene==Scene).ToArray();
        public void Fly(double seconds) { foreach(var p in Projectiles()) p.AdvanceBy(seconds); }
        public void Dispose()
        { Player.StopSkillClip(false); foreach(var p in Projectiles()) if(p!=null) p.Stop(); Object.DestroyImmediate(Clip); EditorSceneManager.ClosePreviewScene(Scene); }
    }
}
}
