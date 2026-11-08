using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static partial class AssassinMotionAuthoring
{
    private static void RawReference(Rig r,ReferenceMotion source,float sourceTime)
    {
        source.PhaseOffset=0;
        ReferencePose(r,source,false,Mathf.Min(sourceTime,source.Clip.length-.0001f));
        foreach(var b in r.Bones.Where(b=>b.name.Contains("Cloth")))b.localRotation=Quaternion.identity;
    }
    private static void RetargetNewReferences(Rig r)
    {
        const string dir="Assets/assassin-character-model/";
        using(var side=new ReferenceMotion(dir+"Standing Walk Right.fbx"))
        {
            // ReferencePose reflects source X to match this avatar's bone convention.
            // Its source-right step therefore becomes target-left; reflect again for right.
            float best=float.NegativeInfinity;
            for(int i=0;i<120;i++)
            {
                side.Clip.SampleAnimation(side.Model,side.Clip.length*i/120);
                var foot=side.Bones["LeftFoot"].position;
                float score=foot.x-side.Bones["Hips"].position.x;
                if(foot.y<side.Ground+.12f && score>best){best=score;side.PhaseOffset=i/120f;}
            }
            Bake(r,"WalkLeft",.9f,true,(rig,t)=>SideReference(rig,side,t/.9f,false));
            Bake(r,"WalkRight",.9f,true,(rig,t)=>SideReference(rig,side,t/.9f,true));
            Bake(r,"RunLeft",.65f,true,(rig,t)=>SideRunReference(rig,side,t/.65f,false));
            Bake(r,"RunRight",.65f,true,(rig,t)=>SideRunReference(rig,side,t/.65f,true));
        }
        using(var walk=new ReferenceMotion(dir+"Walking Backwards.fbx"))
        using(var run=new ReferenceMotion(dir+"Running Backward.fbx"))
        {
            // Align the backward cycle at left foot behind the hip, matching the
            // existing directional contact convention, rather than forward maximum.
            foreach(var source in new[]{walk,run})
            {
                float best=float.PositiveInfinity;
                for(int i=0;i<120;i++)
                {
                    source.Clip.SampleAnimation(source.Model,source.Clip.length*i/120);
                    float z=source.Bones["LeftFoot"].position.z-source.Bones["Hips"].position.z;
                    if(z<best){best=z;source.PhaseOffset=i/120f;}
                }
            }
            for(int i=3;i<=5;i++)
            {
                var direction=Directions[i];
                Bake(r,"Walk"+DirectionNames[i],.9f,true,(rig,t)=>
                {if(i==4)BackwardReference(rig,walk,false,t/.9f);else SourceActionPose(rig,walk,false,direction,t/.9f);
                 foreach(var b in rig.Bones.Where(b=>b.name.Contains("Cloth")))b.localRotation=Quaternion.identity;});
                Bake(r,"Run"+DirectionNames[i],.65f,true,(rig,t)=>
                {if(i==4)BackwardReference(rig,run,true,t/.65f);else SourceActionPose(rig,run,true,direction,t/.65f);
                 foreach(var b in rig.Bones.Where(b=>b.name.Contains("Cloth")))b.localRotation=Quaternion.identity;});
            }
        }
        using(var jump=new ReferenceMotion(dir+"Jumping.fbx"))
        {
            Bake(r,"JumpAnticipation",.18f,false,(rig,t)=>RawReference(rig,jump,Mathf.Lerp(.12f,.48f,t/.18f)));
            Bake(r,"JumpAscending",.55f,false,(rig,t)=>JumpReference(rig,jump,Mathf.Lerp(.48f,.8f,t/.55f)));
            Bake(r,"FallDescending",.8f,false,(rig,t)=>JumpReference(rig,jump,Mathf.Lerp(.8f,1.1f,t/.8f)));
            Bake(r,"Landing",.3f,false,(rig,t)=>RawReference(rig,jump,Mathf.Lerp(1.1f,1.72f,t/.3f)));
        }
        using(var attack=new ReferenceMotion(dir+"One Hand Sword Combo.fbx"))
        {
            const float start=.3f,end=1.4f,duration=.38f;
            // Single first strike, not the entire multi-hit combo.
            Bake(r,"SwordAttack",duration,false,(rig,t)=>
            {
                RawReference(rig,attack,Mathf.Lerp(start,end,t/duration));
                var hip=rig.B("Hips").localPosition;hip.x=0;hip.z=0;rig.B("Hips").localPosition=hip;
            });
            attack.Clip.SampleAnimation(attack.Model,start);
            var first=attack.Bones["Hips"].position;
            float scale=(r.B("LeftLowerLeg").localPosition.magnitude+r.B("LeftFoot").localPosition.magnitude)/attack.LegLength;
            var x=new AnimationCurve();var z=new AnimationCurve();
            for(int i=0;i<=66;i++)
            {
                attack.Clip.SampleAnimation(attack.Model,Mathf.Lerp(start,end,i/66f));
                var offset=Mirror(attack.Bones["Hips"].position-first)*scale;
                x.AddKey(i/66f,offset.x);z.AddKey(i/66f,offset.z);
            }
            foreach(var bridge in UnityEngine.Object.FindObjectsByType<PlayerCharacterAnimation>(FindObjectsSortMode.None))
            {
                var combat=bridge.GetComponent<PlayerCombat>();
                combat.SwingDuration=duration;combat.AttackCooldown=duration;combat.HitStart=.54f;combat.HitEnd=.82f;combat.StrikeEnd=.84f;
                var movement=bridge.GetComponent<PlayerMovement>();movement.AttackMovementMultiplier=0;movement.JumpAnticipation=.18f;
                var motion=bridge.GetComponent<AttackAnimationMotion>();if(!motion)motion=bridge.gameObject.AddComponent<AttackAnimationMotion>();
                motion.LocalX=x;motion.LocalZ=z;motion.Visual=bridge.CharacterAnimator.transform;
                EditorUtility.SetDirty(combat);EditorUtility.SetDirty(movement);EditorUtility.SetDirty(motion);
                EditorSceneManager.MarkSceneDirty(bridge.gameObject.scene);
            }
        }
        BakeReturnSwordAttack(r);
        EditorSceneManager.SaveOpenScenes();
    }
    private static void BackwardReference(Rig r,ReferenceMotion source,bool run,float phase)
    {
        ReferencePose(r,source,run,0);
        var arms=r.Bones.Where(b=>b.name.Contains("Arm")||b.name.Contains("Shoulder")||b.name.Contains("Hand")).ToArray();
        var neutral=arms.Select(b=>b.localRotation).ToArray();
        r.Reset();ReferencePose(r,source,run,source.Clip.length*.5f);
        for(int i=0;i<arms.Length;i++)neutral[i]=Quaternion.Slerp(neutral[i],arms[i].localRotation,.5f);
        r.Reset();ReferencePose(r,source,run,phase*source.Clip.length);
        for(int i=0;i<arms.Length;i++)arms[i].localRotation=Quaternion.Slerp(neutral[i],arms[i].localRotation,.35f);
    }
    private static void SideReference(Rig r,ReferenceMotion source,float phase,bool reflect)
    {
        r.Reset();
        var bind=r.Bones.Select(b=>b.rotation).ToArray();
        BackwardReference(r,source,false,phase+(reflect?.5f:0));
        // Strip lateral travel just as forward locomotion strips longitudinal travel.
        var hip=r.B("Hips").localPosition;hip.x=0;hip.z=0;r.B("Hips").localPosition=hip;
        if(reflect)
        {
            var pose=r.Bones.Select(b=>b.rotation).ToArray();
            for(int i=0;i<r.Bones.Length;i++)
            {
                string name=r.Bones[i].name;
                string other=name.StartsWith("Left")?"Right"+name.Substring(4):name.StartsWith("Right")?"Left"+name.Substring(5):name;
                int j=Array.IndexOf(r.Bones,r.B(other));
                r.Bones[i].rotation=Mirror(pose[j]*Quaternion.Inverse(bind[j]))*bind[i];
            }
        }
        foreach(var b in r.Bones.Where(b=>b.name.Contains("Cloth")))b.localRotation=Quaternion.identity;
    }
    private static void SideRunReference(Rig r,ReferenceMotion source,float phase,bool reflect)
    {
        SideReference(r,source,phase,reflect);
        // Adapt the supplied side-step into a lower, wider running gait, preserving
        // its knee bend planes, foot orientation and left/right contact ordering.
        var sides=new[]{"Left","Right"};
        var feet=sides.Select(s=>r.Root.InverseTransformPoint(r.B(s+"Foot").position)).ToArray();
        var knees=sides.Select(s=>r.Root.InverseTransformPoint(r.B(s+"LowerLeg").position)).ToArray();
        var rotations=sides.Select(s=>Quaternion.Inverse(r.Root.rotation)*r.B(s+"Foot").rotation).ToArray();
        r.B("Hips").localPosition+=Vector3.down*.012f;
        for(int i=0;i<2;i++)
        {
            feet[i].x*=1.15f;
            Limb(r,sides[i]+"UpperLeg",sides[i]+"LowerLeg",sides[i]+"Foot",feet[i],knees[i],rotations[i]);
        }
        r.B("Spine").localRotation*=Quaternion.Euler(4,0,0);
    }
    private static void JumpReference(Rig r,ReferenceMotion source,float time)
    {
        RawReference(r,source,time);
        float tuck=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.48f,.72f,time))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.85f,1.1f,time)));
        r.B("Spine").localRotation*=Quaternion.Euler(8*tuck,0,0);
        r.B("Chest").localRotation*=Quaternion.Euler(6*tuck,0,0);
        var arms=r.Bones.Where(b=>b.name.Contains("Arm")||b.name.Contains("Hand")).ToArray();
        var original=arms.Select(b=>b.localRotation).ToArray();
        GuardArms(r,.15f,true);
        for(int i=0;i<arms.Length;i++)arms[i].localRotation=Quaternion.Slerp(original[i],arms[i].localRotation,.55f*tuck);
    }
}
