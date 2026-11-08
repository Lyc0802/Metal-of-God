using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class ActionAnimationVerification
{
    public const string Output="Documentation/ActionAnimation";
    private static IEnumerator routine;
    private static int frame=-1;
    private static readonly List<string> report=new List<string>();
    private static bool background;
    static ActionAnimationVerification(){EditorApplication.update+=Tick;}
    private static void Check(bool value,string message)
    {
        if(!value) throw new InvalidOperationException(message);
        report.Add("PASS "+message); File.WriteAllLines(Output+"/Tests.txt",report);
    }
    private static void Tick()
    {
        if(EditorApplication.isCompiling) return;
        if(routine!=null)
        {
            if(!Application.isPlaying || Time.frameCount==frame || EditorApplication.isPaused) return;
            frame=Time.frameCount;
            try {if(!routine.MoveNext())Finish("COMPLETE");} catch(Exception e){Finish("FAIL "+e);}
        }
        else if(SessionState.GetBool("ActionAnimationTests",false)&&Application.isPlaying)
        {
            SessionState.SetBool("ActionAnimationTests",false);
            if(File.Exists(Output+"/Tests.txt")) report.AddRange(File.ReadAllLines(Output+"/Tests.txt"));
            background=Application.runInBackground;Application.runInBackground=true;
            routine=Tests();
        }
    }
    private static void Finish(string text)
    {
        report.Add(text); File.WriteAllLines(Output+"/Tests.txt",report);
        routine=null;Application.runInBackground=background;EditorApplication.isPlaying=false;
    }
    [MenuItem("Tools/Assassin/Verify Action Animations")]
    public static void Run()
    {
        Directory.CreateDirectory(Output);report.Clear();
        VerifyAssets();
        SessionState.SetBool("ActionAnimationTests",true);EditorApplication.isPlaying=true;
    }
    private static AnimationClip Clip(string name)=>AssetDatabase.LoadAssetAtPath<AnimationClip>(AssassinMotionAuthoring.Folder+"/Animations/"+name+".anim");
    public static void VerifyAssets()
    {
        foreach(string gait in new[]{"Walk","Run"})
        {
            var signatures=new HashSet<string>();
            foreach(string direction in AssassinMotionAuthoring.DirectionNames)
            {
                var clip=Clip(gait+direction); Check(clip,clip?clip.name+" exists":"missing "+gait+direction);
                var bindings=AnimationUtility.GetCurveBindings(clip);
                Check(bindings.All(b=>b.path.StartsWith("Rig/")),clip.name+" only animates bones, no root movement");
                var values=new List<float>();
                foreach(var b in bindings)
                {
                    var curve=AnimationUtility.GetEditorCurve(clip,b);
                    CheckFinite(curve);
                    if(Mathf.Abs(curve.Evaluate(0)-curve.Evaluate(clip.length))>.0001f) throw new Exception("Loop seam "+clip.name);
                    if(b.path.Contains("Leg")) values.Add(curve.Evaluate(clip.length*.23f));
                }
                signatures.Add(string.Join(",",values.Select(v=>v.ToString("F5"))));
            }
            Check(signatures.Count==8,gait+" has eight genuinely different leg poses and seamless cycles");
        }
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(AssassinMotionAuthoring.Folder+"/ActionLocomotion.controller");
        var trees=AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller)).OfType<BlendTree>().Where(t=>t.blendType==BlendTreeType.FreeformDirectional2D).ToArray();
        Check(trees.Length==2&&trees.All(t=>t.children.Length==8&&t.children.Select(c=>c.motion).Distinct().Count()==8),"two 2D Freeform Directional trees each reference eight independent clips");
        foreach(string name in new[]{"ActionIdle","JumpAnticipation","JumpAscending","FallDescending","Landing","SwordAttack"}) Check(Clip(name),name+" clip exists");
    }
    private static void CheckFinite(AnimationCurve curve)
    {if(curve.keys.Any(k=>float.IsNaN(k.value)||float.IsInfinity(k.value)))throw new Exception("Invalid curve");}
    private static IEnumerator Tests()
    {
        yield return null;yield return null;
        var bridge=Object.FindFirstObjectByType<PlayerCharacterAnimation>();
        var player=bridge.gameObject;var movement=player.GetComponent<PlayerMovement>();var body=player.GetComponent<Rigidbody>();
        var combat=player.GetComponent<PlayerCombat>();var controller=player.GetComponent<PlayerController>();var view=controller.ViewCamera;
        controller.enabled=false;
        // Gameplay camera is parented to Player; its controller normally cancels
        // that rotation. Detach in this fixture while the input controller is off.
        view.transform.SetParent(null,true);
        foreach(var ai in Object.FindObjectsByType<TestEnemy>(FindObjectsSortMode.None)) ai.enabled=false;
        var inventory=player.GetComponent<PlayerMetalInventory>(); foreach(var reserve in inventory.Metals)reserve.IsEnabled=false;
        player.GetComponent<PlayerHealth>().CurrentHealth=100;
        var platform=GameObject.CreatePrimitive(PrimitiveType.Cube);platform.name="Temporary animation test floor";
        platform.transform.position=new Vector3(100,0,100);platform.transform.localScale=new Vector3(80,1,80);
        body.position=new Vector3(100,1.55f,100);body.rotation=Quaternion.identity;body.linearVelocity=Vector3.zero;
        view.transform.position=body.position+new Vector3(0,3,-7);view.transform.LookAt(body.position+Vector3.forward*4);
        var enemyGo=GameObject.CreatePrimitive(PrimitiveType.Capsule);enemyGo.transform.position=body.position+Vector3.forward*9;
        var enemy=enemyGo.AddComponent<Enemy>();enemy.MaxHealth=enemy.CurrentHealth=1000;
        Physics.SyncTransforms();
        var lockOn=player.GetComponent<PlayerTargetLockOn>();lockOn.Unlock();lockOn.ToggleLock();
        Check(lockOn.Target==enemy,"existing lock-on input route acquires visible enemy");
        for(float until=Time.time+.6f;Time.time<until;)yield return null;
        Check(movement.IsGrounded&&bridge.VisualState=="Locomotion","Idle settles to grounded Locomotion");
        var cape=bridge.CharacterAnimator.GetComponentInChildren<Cloth>();
        Check(cape&&cape.coefficients.Any(c=>c.maxDistance==0)&&cape.coefficients.Any(c=>c.maxDistance>0),"cape contains fixed anchors and free simulated vertices");
        Check(cape.capsuleColliders.Length>=11&&cape.capsuleColliders.All(c=>c.isTrigger),"body/shoulder/arm/leg cloth capsules cannot apply physical contact forces");
        Vector3[] capeStart=cape.vertices;
        for(int run=0;run<2;run++)
        {
            if((run==1)!=movement.IsSprinting)movement.ToggleSprint();
            for(int d=0;d<8;d++)
            {
                var input=AssassinMotionAuthoring.Directions[d];
                for(float until=Time.time+.62f;Time.time<until;){movement.ReadInput(input,false,view);yield return null;}
                var local=player.transform.InverseTransformDirection(body.linearVelocity);var expected=new Vector2(local.x,local.z).normalized;
                var actual=new Vector2(bridge.CharacterAnimator.GetFloat("MoveX"),bridge.CharacterAnimator.GetFloat("MoveY"));
                Check(Vector2.Dot(expected,actual.normalized)>.92f,(run==0?"Walk ":"Run ")+AssassinMotionAuthoring.DirectionNames[d]+" follows actual local velocity");
                var target=enemy.TargetPoint-body.worldCenterOfMass;target.y=0;
                Check(Vector3.Dot(player.transform.forward,target.normalized)>.95f,"Lock-on facing remains independent of strafe animation "+run+"/"+d);
                Check(cape.vertices.All(v=>!float.IsNaN(v.x)&&v.magnitude<4),"cape remains finite and bounded "+run+"/"+d);
            }
        }
        Check(cape.vertices.Zip(capeStart,(a,b)=>Vector3.Distance(a,b)).Max()>.005f,"cape actually responds to locomotion");
        movement.ReadInput(Vector2.zero,false,view);
        for(float until=Time.time+.5f;Time.time<until;)yield return null;
        movement.ReadInput(Vector2.zero,true,view);
        bool prep=false,rise=false,fall=false,land=false;
        for(float until=Time.time+3;Time.time<until;)
        {
            prep|=bridge.VisualState=="Jump Anticipation";rise|=bridge.VisualState=="Jump Ascending";
            fall|=bridge.VisualState=="Fall Descending";land|=bridge.VisualState=="Landing";yield return null;
        }
        Check(prep&&rise&&fall&&land,"real Rigidbody jump traverses anticipation, ascent, fall and landing");
        lockOn.Unlock();view.transform.rotation=Quaternion.Euler(0,90,0);
        for(float until=Time.time+.5f;Time.time<until;){movement.ReadInput(Vector2.up,false,view);yield return null;}
        Check(body.linearVelocity.x>1&&Vector3.Dot(player.transform.forward,Vector3.right)>.9f,"unlocked camera-relative movement and physical turning preserved; velocity="+body.linearVelocity+" facing="+player.transform.forward+" desired="+movement.DesiredMoveDirection);
        movement.ReadInput(Vector2.zero,false,view);
        for(float until=Time.time+.5f;Time.time<until;)yield return null;
        body.rotation=Quaternion.identity;view.transform.rotation=Quaternion.identity;
        enemyGo.transform.position=body.position+Vector3.forward*1.7f;
        Physics.SyncTransforms();float hp=enemy.CurrentHealth;
        movement.ReadInput(Vector2.zero,false,view);
        Check(combat.TryAttack(),"existing Sword Attack starts");
        bool attacking=false;float moved=0;var before=body.position;
        for(float until=Time.time+combat.SwingDuration+.15f;Time.time<until;){attacking|=bridge.VisualState=="Sword Attack";moved=Mathf.Max(moved,Vector3.Distance(before,body.position));yield return null;}
        Check(attacking&&enemy.CurrentHealth<hp,"attack clock, animated strike and existing damage remain integrated");
        Check(moved>.02f,"attack displacement comes from source animation without movement input");
        movement.ReadInput(Vector2.zero,false,view);
        inventory.Get(MetalType.Iron).IsEnabled=true;
        var anchor=GameObject.CreatePrimitive(PrimitiveType.Cube);anchor.transform.position=body.position+new Vector3(0,7,2);
        var metal=anchor.AddComponent<MetalDisplayer>();metal.weight=500;
        var surface=new MetalSurface{Metal=metal,Collider=anchor.GetComponent<Collider>(),LocalNormal=Vector3.down};
        var manip=player.GetComponent<PlayerMetalManipulator>();manip.HandleInput(true,-1,surface);
        for(float until=Time.time+.25f;Time.time<until;)yield return null;
        Check(manip.IsLocked&&body.linearVelocity.y>0&&bridge.VisualState=="Jump Ascending","Iron lift selects air pose from actual vertical velocity");
        manip.Release();
        Check(!bridge.CharacterAnimator.applyRootMotion,"Animator never applies root motion");
        Check(!EditorUtility.scriptCompilationFailed,"no compilation errors");
    }
    [MenuItem("Tools/Assassin/Render Action Animation Poses")]
    public static void RenderPoses()
    {
        Directory.CreateDirectory(Output+"/Poses");
        string[] names={"ActionIdle","WalkForward","WalkRight","WalkLeft","WalkBackward","RunForward","RunRight","RunLeft","RunBackward","JumpAscending","FallDescending","Landing","SwordAttack"};
        var visual=Object.FindFirstObjectByType<PlayerCharacterAnimation>().CharacterAnimator.gameObject;
        foreach(string name in names) for(int f=0;f<4;f++)
        {
            var utility=new PreviewRenderUtility();
            try
            {
                var clone=Object.Instantiate(visual);clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);clone.transform.localScale=Vector3.one*2.87f;
                foreach(var c in clone.GetComponentsInChildren<Cloth>())c.enabled=false;
                foreach(var b in clone.GetComponentsInChildren<MonoBehaviour>())b.enabled=false;
                clone.GetComponent<Animator>().enabled=false;utility.AddSingleGO(clone);
                Clip(name).SampleAnimation(clone,Clip(name).length*(.06f+f*.23f));
                utility.camera.transform.position=new Vector3(2.6f,1.7f,4.3f);utility.camera.transform.LookAt(new Vector3(0,1,0));
                utility.camera.orthographic=true;utility.camera.orthographicSize=1.25f;utility.camera.nearClipPlane=.1f;utility.camera.farClipPlane=20;
                utility.camera.clearFlags=CameraClearFlags.SolidColor;utility.camera.backgroundColor=new Color(.07f,.085f,.11f);
                utility.lights[0].intensity=1.5f;utility.lights[0].transform.rotation=Quaternion.Euler(35,30,0);
                utility.lights[1].intensity=.8f;utility.ambientColor=new Color(.35f,.35f,.35f);
                utility.BeginStaticPreview(new Rect(0,0,320,360));utility.Render(true,false);
                var image=utility.EndStaticPreview();File.WriteAllBytes(Output+"/Poses/"+name+"_"+f+".png",image.EncodeToPNG());Object.DestroyImmediate(image);
            }finally{utility.Cleanup();}
        }
    }
}

