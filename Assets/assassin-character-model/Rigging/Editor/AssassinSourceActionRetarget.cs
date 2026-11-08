using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static partial class AssassinMotionAuthoring
{
    private static void SourceActionPose(Rig r,ReferenceMotion source,bool run,Vector2 direction,float phase)
    {
        Quaternion[] armRest=null;
        if(direction!=Vector2.up)
        {
            r.Reset(); ReferencePose(r,source,run,0);
            armRest=r.Bones.Select(b=>b.localRotation).ToArray();
            r.Reset(); ReferencePose(r,source,run,source.Clip.length*.5f);
            for(int i=0;i<r.Bones.Length;i++)armRest[i]=Quaternion.Slerp(armRest[i],r.Bones[i].localRotation,.5f);
            r.Reset();
        }
        ReferencePose(r,source,run,phase*source.Clip.length);
        if(direction!=Vector2.up)
        {
            // Keep the captured upper body coordination, but author real lateral
            // and backward foot contacts: the sources contain forward motion only.
            var upper=r.Bones.Where(b=>b.name.Contains("Arm")||b.name.Contains("Hand")||
                b.name.Contains("Shoulder")||new[]{"Spine","Chest","UpperChest","Neck","Head"}.Contains(b.name)).ToArray();
            var rotations=upper.Select(b=>b.localRotation).ToArray();
            r.Reset(); DirectionalGait(r,phase,direction,run);
            for(int i=0;i<upper.Length;i++)upper[i].localRotation=rotations[i];
            float armAmplitude=direction.y<0 ? .28f : Mathf.Lerp(1,.35f,Mathf.Abs(direction.x));
            foreach(var bone in upper.Where(b=>b.name.Contains("Arm")||b.name.Contains("Hand")||b.name.Contains("Shoulder")))
                bone.localRotation=Quaternion.Slerp(armRest[Array.IndexOf(r.Bones,bone)],bone.localRotation,armAmplitude);
            r.B("Spine").localRotation*=Quaternion.Euler(0,0,-3*direction.x);
        }
        foreach(var b in r.Bones.Where(b=>b.name.Contains("Cloth")))b.localRotation=Quaternion.identity;
    }
    [MenuItem("Tools/Assassin/Retarget Supplied Movement To Action Controller")]
    public static void RetargetSuppliedActionMovement()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Exit Play Mode first.");
        var model=PrefabUtility.LoadPrefabContents("Assets/assassin-character-model/Assassin Model.prefab");
        try
        {
            var r=new Rig(model.transform);
            using(var walk=new ReferenceMotion(WalkSource))
            using(var run=new ReferenceMotion(RunSource))
            {
                for(int i=0;i<8;i++)
                {
                    var direction=Directions[i];
                    Bake(r,"Walk"+DirectionNames[i],.9f,true,(rig,t)=>SourceActionPose(rig,walk,false,direction,t/.9f));
                    Bake(r,"Run"+DirectionNames[i],.65f,true,(rig,t)=>SourceActionPose(rig,run,true,direction,t/.65f));
                }
            }
            RetargetNewReferences(r);
            AssetDatabase.SaveAssets();
            System.IO.File.WriteAllText("Documentation/ActionAnimation/SourceRetarget.txt",
                "Retargeted forward/backward walk/run, Standing Walk Right into mirrored lateral walk/run clips, Jumping phases, and first strike from One Hand Sword Combo (source 0.30-1.40 seconds played in 0.38 seconds). Lateral arm amplitude is 35%. Diagonals retain independent directional contacts. Attack input movement is disabled; displacement is baked from source hips and collision-limited by Rigidbody.");
        }
        finally{PrefabUtility.UnloadPrefabContents(model);}
    }
}

