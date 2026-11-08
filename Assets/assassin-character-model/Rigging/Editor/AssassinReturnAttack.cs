using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;

public static partial class AssassinMotionAuthoring
{
    [MenuItem("Tools/Assassin/Build Return Sword Attack")]
    public static void BuildReturnSwordAttack()
    {
        if(Application.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        var model=PrefabUtility.LoadPrefabContents("Assets/assassin-character-model/Assassin Model.prefab");
        try { BakeReturnSwordAttack(new Rig(model.transform)); }
        finally { PrefabUtility.UnloadPrefabContents(model); }
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveOpenScenes();
    }

    private static void BakeReturnSwordAttack(Rig r)
    {
        using(var source=new ReferenceMotion("Assets/assassin-character-model/One Hand Sword Combo.fbx"))
        {
            const float start=1.4f,end=2.3f,duration=.38f;
            // Use the source combo's next forward cut, which starts exactly where
            // the return cut ends, rather than forcing a reset to the original ready pose.
            const float forwardStart=2.3f,forwardEnd=3.5f;
            r.Reset();RawReference(r,source,start);
            var readyRot=r.Bones.Select(b=>b.localRotation).ToArray();
            var readyPos=r.Bones.Select(b=>b.localPosition).ToArray();
            var forwardClip=Bake(r,"SwordAttack",duration,false,(rig,t)=>
            {
                float phase=t/duration;
                RawReference(rig,source,Mathf.Lerp(forwardStart,forwardEnd,phase));
                // Blend only the forward cut's follow-through into the return start.
                // These are neighbouring sword-side poses, never an idle stance.
                float settle=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.85f,1,phase));
                for(int i=0;i<rig.Bones.Length;i++)
                {
                    rig.Bones[i].localRotation=Quaternion.Slerp(rig.Bones[i].localRotation,readyRot[i],settle);
                    rig.Bones[i].localPosition=Vector3.Lerp(rig.Bones[i].localPosition,readyPos[i],settle);
                }
                var hip=rig.B("Hips").localPosition;hip.x=0;hip.z=0;rig.B("Hips").localPosition=hip;
            });
            var clip=Bake(r,"SwordAttackReturn",duration,false,(rig,t)=>
            {
                RawReference(rig,source,Mathf.Lerp(start,end,t/duration));
                var hip=rig.B("Hips").localPosition;hip.x=0;hip.z=0;rig.B("Hips").localPosition=hip;
            });
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(Folder+"/ActionLocomotion.controller");
            var machine=controller.layers[0].stateMachine;
            var forwardState=machine.states.Select(s=>s.state).First(s=>s.name=="Sword Attack");
            forwardState.motion=forwardClip;forwardState.timeParameter="AttackProgress";forwardState.timeParameterActive=true;
            EditorUtility.SetDirty(forwardState);
            var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="Sword Attack Return");
            if(!state)state=machine.AddState("Sword Attack Return");
            if(!controller.parameters.Any(p=>p.name=="ReturnAttackProgress"))
                controller.AddParameter("ReturnAttackProgress",AnimatorControllerParameterType.Float);
            state.motion=clip;state.timeParameter="ReturnAttackProgress";state.timeParameterActive=true;
            state.writeDefaultValues=true;
            EditorUtility.SetDirty(controller);
            source.Clip.SampleAnimation(source.Model,start);
            var first=source.Bones["Hips"].position;
            float scale=(r.B("LeftLowerLeg").localPosition.magnitude+r.B("LeftFoot").localPosition.magnitude)/source.LegLength;
            var x=new AnimationCurve();var z=new AnimationCurve();
            for(int i=0;i<=60;i++)
            {
                source.Clip.SampleAnimation(source.Model,Mathf.Lerp(start,end,i/60f));
                var offset=Mirror(source.Bones["Hips"].position-first)*scale;
                x.AddKey(i/60f,offset.x);z.AddKey(i/60f,offset.z);
            }
            source.Clip.SampleAnimation(source.Model,forwardStart);
            var forwardOrigin=source.Bones["Hips"].position;
            var forwardX=new AnimationCurve();var forwardZ=new AnimationCurve();
            for(int i=0;i<=60;i++)
            {
                source.Clip.SampleAnimation(source.Model,Mathf.Lerp(forwardStart,forwardEnd,i/60f));
                var offset=Mirror(source.Bones["Hips"].position-forwardOrigin)*scale;
                forwardX.AddKey(i/60f,offset.x);forwardZ.AddKey(i/60f,offset.z);
            }
            foreach(var bridge in UnityEngine.Object.FindObjectsByType<PlayerCharacterAnimation>(FindObjectsSortMode.None))
            {
                var combat=bridge.GetComponent<PlayerCombat>();
                combat.AlternateAttacks=true;combat.SwingDuration=duration;combat.AttackCooldown=duration;
                combat.ReturnHitStart=.5f;combat.ReturnHitEnd=.83f;
                combat.HitStart=.5f;combat.HitEnd=.76f;
                var motion=bridge.GetComponent<AttackAnimationMotion>();
                if(!motion)throw new InvalidOperationException("Missing source animation motion component.");
                motion.ReturnLocalX=x;motion.ReturnLocalZ=z;
                motion.LocalX=forwardX;motion.LocalZ=forwardZ;
                EditorUtility.SetDirty(combat);EditorUtility.SetDirty(motion);
                EditorSceneManager.MarkSceneDirty(bridge.gameObject.scene);
            }
        }
    }
}
