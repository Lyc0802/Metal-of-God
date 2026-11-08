using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;

// Original offline choreography, baked to ordinary independent Generic clips.
public static partial class AssassinMotionAuthoring
{
    public static readonly string[] DirectionNames={"Forward","ForwardRight","Right","BackwardRight","Backward","BackwardLeft","Left","ForwardLeft"};
    public static readonly Vector2[] Directions=Enumerable.Range(0,8).Select(i=>new Vector2(Mathf.Sin(i*Mathf.PI/4),Mathf.Cos(i*Mathf.PI/4))).ToArray();
    private static void GuardArms(Rig r,float phase,bool run)
    {
        // Stable anatomical elbow hinges; the weapon arm carries a low guard
        // while the free arm counter-swings, instead of chasing world hand targets.
        WalkArms(r,phase);
        foreach(string side in new[]{"Left","Right"})
        {
            bool weapon=side=="Right";
            var upper=r.B(side+"UpperArm"); var lower=r.B(side+"LowerArm"); var hand=r.B(side+"Hand");
            var u=r.Positions[Array.IndexOf(r.Bones,lower)];
            var l=r.Positions[Array.IndexOf(r.Bones,hand)];
            var hinge=Vector3.Cross(u,l).normalized;
            float flex=weapon ? (run?48:32) : (run?65:25);
            flex+=Mathf.Sin(phase*2*Mathf.PI-.2f)*(weapon?3:8);
            lower.localRotation=Quaternion.AngleAxis(flex-Vector3.Angle(u,l),hinge);
            if(weapon) upper.localRotation=Quaternion.Slerp(upper.localRotation,
                Quaternion.Euler(-12,0,0)*Quaternion.LookRotation(V(-.12f,-1,.1f),Vector3.left)*
                Quaternion.Inverse(Quaternion.LookRotation(u,hinge)),.75f);
            hand.localRotation=Quaternion.identity;
        }
    }
    private static void DirectionalGait(Rig r,float phase,Vector2 direction,bool run)
    {
        float wave=Mathf.Cos(phase*2*Mathf.PI), side=direction.x, forward=direction.y;
        float stance=run ? .36f : .62f;
        float travel=(run ? .42f : .37f)*Mathf.Lerp(1,.58f,Mathf.Abs(side));
        // Side steps keep their own lateral lanes; backward steps keep the knees forward.
        float bob=run ? -.037f+.012f*Mathf.Cos(phase*4*Mathf.PI) : -.022f+.0035f*Mathf.Cos(phase*4*Mathf.PI);
        r.B("Hips").localPosition+=V(.008f*wave,bob,0);
        r.Turn("Hips",V((run?7:2)*forward,-(run?5:3)*wave*forward,-(run?4:2)*wave-3*side));
        r.Turn("Spine",V((run?5:1)*forward,(run?4:2)*wave*forward,-3*side));
        r.Turn("Chest",V(1,(run?7:3)*wave*forward+side*4,1.5f*wave));
        r.Turn("Neck",V(-3*forward,-side*3,-1*wave));
        GuardArms(r,phase,run);
        foreach(string s in new[]{"Left","Right"})
        {
            float sign=s=="Left"?1:-1, p=Mathf.Repeat(phase+(sign<0?.5f:0),1);
            float along,lift,pitch;
            if(p<stance)
            {
                along=Mathf.Lerp(travel*.5f,-travel*.5f,p/stance);
                lift=0;
                pitch=(-8*(1-Ease(p/.09f))+16*Ease((p-stance+.1f)/.1f))*forward;
            }
            else
            {
                float u=(p-stance)/(1-stance);
                // Match the stance velocity at both ends of a Hermite swing.
                float m=-travel/stance*(1-stance);
                along=(2*u*u*u-3*u*u+1)*(-travel*.5f)+(u*u*u-2*u*u+u)*m
                    +(-2*u*u*u+3*u*u)*travel*.5f+(u*u*u-u*u)*m;
                lift=(run?.085f:.035f)*Mathf.Pow(Mathf.Sin(u*Mathf.PI),1.4f);
                pitch=Mathf.Lerp(16,-8,Ease(u))*forward;
            }
            float x=sign*.075f+side*along;
            // Opening/closing side step instead of crossing through the other shin.
            x=sign*Mathf.Max(.022f,sign*x);
            float rollLift=Mathf.Max(0,.062f*(Mathf.Cos(pitch*Mathf.Deg2Rad)-1)
                +(pitch>0?.118f:-.04f)*Mathf.Sin(pitch*Mathf.Deg2Rad));
            Leg(r,s,V(x,.062f+lift+rollLift,-.008f+forward*along),pitch,side*12+sign*3);
        }
    }
    private static void AirPose(Rig r,float t,bool falling)
    {
        Idle(r,0);
        float settle=Ease(t/.22f);
        r.B("Hips").localPosition+=V(0,-.012f,0);
        r.Turn("Spine",V(falling?-5:7,0,0)); r.Turn("Chest",V(falling?-3:4,0,0));
        foreach(string s in new[]{"Left","Right"})
        {
            float sign=s=="Left"?1:-1;
            Leg(r,s,V(sign*.073f,.062f+(falling?.025f:.075f)*settle,-.035f+sign*.02f),falling?10:18,sign*5);
        }
        GuardArms(r,.15f,true);
        r.B("LeftUpperArm").localRotation=Quaternion.Euler(falling?-18:-28,0,-8)*r.B("LeftUpperArm").localRotation;
        if(!falling)
        {
            float tuck=.35f+.65f*settle;
            r.Turn("Hips",V(6*tuck,0,0));
            r.Turn("Spine",V(16*tuck,0,0));
            r.Turn("Chest",V(11*tuck,0,0));
            r.Turn("UpperChest",V(5*tuck,0,0));
            r.Turn("Neck",V(5*tuck,0,0));
            r.Turn("Head",V(4*tuck,0,0));
            foreach(string side in new[]{"Left","Right"})
            {
                float sign=side=="Left"?1:-1;
                r.B(side+"Shoulder").localRotation=Quaternion.Euler(0,-sign*9*tuck,0);
                var lower=r.B(side+"LowerArm");var hand=r.B(side+"Hand");
                var u=r.Positions[Array.IndexOf(r.Bones,lower)];
                var l=r.Positions[Array.IndexOf(r.Bones,hand)];
                var hinge=Vector3.Cross(u,l).normalized;
                lower.localRotation=Quaternion.AngleAxis(Mathf.Lerp(48,side=="Left"?85:70,tuck)-Vector3.Angle(u,l),hinge);
                r.B(side+"UpperArm").localRotation=Quaternion.Euler(-12*tuck,0,sign*5*tuck)*r.B(side+"UpperArm").localRotation;
            }
        }
    }
    private static void Crouch(Rig r,float amount)
    {
        Idle(r,0);
        r.B("Hips").localPosition+=V(0,-.065f*amount,-.014f*amount);
        r.Turn("Spine",V(13*amount,0,0)); r.Turn("Chest",V(6*amount,0,0));
        foreach(string s in new[]{"Left","Right"}) Leg(r,s,V(s=="Left"?.075f:-.075f,.062f,-.008f),0);
        RelaxedArms(r,-.022f*amount,0,false);
    }
    private static void WeightedAttack(Rig r,float t)
    {
        Attack(r,t);
        float p=t/PlayerCharacterAnimation.AuthoredAttackDuration;
        float weight=Ease(p/.28f)*(1-Ease((p-.66f)/.34f));
        float drive=Ease((p-.28f)/.2f),recover=Ease((p-.72f)/.28f);
        float turn=(-13*weight+29*drive*(1-recover));
        r.B("Hips").localRotation*=Quaternion.Euler(0,turn,0);
        r.B("Chest").localRotation*=Quaternion.Euler(4*weight,turn*.35f,-3*weight);
        r.B("Hips").localPosition+=V(.015f*weight,-.03f*weight,.025f*drive*(1-recover));
        float step=Ease((p-.18f)/.22f)*(1-recover);
        float lift=p>.18f&&p<.4f ? .025f*Mathf.Sin((p-.18f)/.22f*Mathf.PI) : 0;
        Leg(r,"Left",V(.075f,.062f+lift,.015f+.045f*step),0,5+turn*.25f);
        Leg(r,"Right",V(-.075f,.062f,-.035f),5*drive*(1-recover),-8+turn*.45f);
        foreach(var b in r.Bones.Where(b=>b.name.Contains("Cloth"))) b.localRotation=Quaternion.identity;
    }
    [MenuItem("Tools/Assassin/Build Action Locomotion")]
    public static void BuildActionLocomotion()
    {
        if(Application.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        const string prefabPath="Assets/assassin-character-model/Assassin Model.prefab";
        var prefab=PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var rig=new Rig(prefab.transform);
            RelaxedArms(rig,0,0,false);
            SwordMount=Quaternion.Inverse(rig.B("RightHand").rotation)*rig.Root.rotation*Quaternion.LookRotation(RestBlade,Vector3.up);
            rig.Reset();
            var idle=Bake(rig,"ActionIdle",4,true,(r,t)=>{Idle(r,t); foreach(var b in r.Bones.Where(b=>b.name.Contains("Cloth"))) b.localRotation=Quaternion.identity;});
            var walks=new AnimationClip[8]; var runs=new AnimationClip[8];
            for(int i=0;i<8;i++)
            {
                Vector2 d=Directions[i];
                walks[i]=Bake(rig,"Walk"+DirectionNames[i],.9f,true,(r,t)=>DirectionalGait(r,t/.9f,d,false));
                runs[i]=Bake(rig,"Run"+DirectionNames[i],.65f,true,(r,t)=>DirectionalGait(r,t/.65f,d,true));
            }
            var start=Bake(rig,"JumpAnticipation",.09f,false,(r,t)=>Crouch(r,Ease(t/.09f)));
            var jump=Bake(rig,"JumpAscending",.55f,false,(r,t)=>AirPose(r,t,false));
            var fall=Bake(rig,"FallDescending",.8f,true,(r,t)=>AirPose(r,.5f,true));
            var land=Bake(rig,"Landing",.3f,false,(r,t)=>Crouch(r,t<.065f?Ease(t/.065f):1-Ease((t-.065f)/.235f)));
            var attack=Bake(rig,"SwordAttack",.64f,false,WeightedAttack);
            string path=Folder+"/ActionLocomotion.controller";
            var c=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if(!c) c=AnimatorController.CreateAnimatorControllerAtPath(path);
            var machine=c.layers[0].stateMachine;
            foreach(var st in machine.states) machine.RemoveState(st.state);
            foreach(var tree in AssetDatabase.LoadAllAssetsAtPath(path).OfType<BlendTree>())
                UnityEngine.Object.DestroyImmediate(tree,true);
            c.parameters=new AnimatorControllerParameter[0];
            foreach(string p in new[]{"MoveX","MoveY","Speed","Moving","RunBlend","WalkRate","VerticalVelocity","AttackProgress"})
                c.AddParameter(p,AnimatorControllerParameterType.Float);
            foreach(string p in new[]{"IsGrounded","IsAttacking"}) c.AddParameter(p,AnimatorControllerParameterType.Bool);
            var parameters=c.parameters; parameters.First(p=>p.name=="WalkRate").defaultFloat=1; c.parameters=parameters;
            Func<string,AnimationClip[],BlendTree> directionTree=(name,clips)=>
            {
                var tree=new BlendTree {name=name,blendType=BlendTreeType.FreeformDirectional2D,blendParameter="MoveX",blendParameterY="MoveY",useAutomaticThresholds=false};
                AssetDatabase.AddObjectToAsset(tree,c);
                for(int i=0;i<8;i++) tree.AddChild(clips[i],Directions[i]);
                return tree;
            };
            var pace=new BlendTree {name="Walk Run Blend",blendType=BlendTreeType.Simple1D,blendParameter="RunBlend",useAutomaticThresholds=false};
            AssetDatabase.AddObjectToAsset(pace,c);
            pace.AddChild(directionTree("Walk 8 Directions",walks),0); pace.AddChild(directionTree("Run 8 Directions",runs),1);
            var locomotion=new BlendTree {name="Idle Moving Blend",blendType=BlendTreeType.Simple1D,blendParameter="Moving",useAutomaticThresholds=false};
            AssetDatabase.AddObjectToAsset(locomotion,c); locomotion.AddChild(idle,0); locomotion.AddChild(pace,1);
            var move=machine.AddState("Locomotion"); move.motion=locomotion; move.speedParameter="WalkRate"; move.speedParameterActive=true; machine.defaultState=move;
            string[] names={"Jump Anticipation","Jump Ascending","Fall Descending","Landing","Sword Attack"};
            AnimationClip[] motions={start,jump,fall,land,attack};
            for(int i=0;i<names.Length;i++)
            {
                var state=machine.AddState(names[i]); state.motion=motions[i]; state.writeDefaultValues=true;
                if(i==4) {state.timeParameter="AttackProgress";state.timeParameterActive=true;}
            }
            var animator=prefab.GetComponent<Animator>(); animator.runtimeAnimatorController=c; animator.applyRootMotion=false;
            AssassinCapeSetup.Configure(prefab);
            PrefabUtility.SaveAsPrefabAsset(prefab,prefabPath);
            foreach(var bridge in UnityEngine.Object.FindObjectsByType<PlayerCharacterAnimation>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                if(!bridge.CharacterAnimator) continue;
                bridge.CharacterAnimator.runtimeAnimatorController=c;
                bridge.WalkReferenceSpeed=1.9f; bridge.RunReferenceSpeed=5.1f; bridge.MaximumWalkRate=bridge.MaximumRunRate=2.5f;
                var movement=bridge.GetComponent<PlayerMovement>();
                movement.MovementSpeed=2.1f; movement.SprintSpeed=5.2f;
                movement.GroundAcceleration=22; movement.AttackMovementMultiplier=.25f; movement.JumpAnticipation=.09f;
                var combat=bridge.GetComponent<PlayerCombat>(); combat.AnimateSwordPivot=false;
                combat.HitStart=.34f; combat.HitEnd=.58f; combat.SwingDuration=.64f;
                AlignGameplaySword(combat);
                AssassinCapeSetup.Configure(bridge.CharacterAnimator.gameObject);
                var feet=bridge.GetComponent<ActionFootPlacement>();
                if(!feet) feet=bridge.gameObject.AddComponent<ActionFootPlacement>();
                feet.Animator=bridge.CharacterAnimator; EditorUtility.SetDirty(feet);
                EditorUtility.SetDirty(bridge); EditorUtility.SetDirty(movement); EditorUtility.SetDirty(combat);
                EditorSceneManager.MarkSceneDirty(bridge.gameObject.scene);
            }
            EditorUtility.SetDirty(c); AssetDatabase.SaveAssets(); EditorSceneManager.SaveOpenScenes();
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
    }
}
