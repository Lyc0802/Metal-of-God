using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class AssassinCapeSetup
{
    private const string Folder="Assets/assassin-character-model/Rigging";
    private static T Save<T>(T asset,string name) where T:UnityEngine.Object
    {
        var path=Folder+"/"+name+".asset"; var old=AssetDatabase.LoadAssetAtPath<T>(path);
        if(old) { EditorUtility.CopySerialized(asset,old); UnityEngine.Object.DestroyImmediate(asset); EditorUtility.SetDirty(old); return old; }
        AssetDatabase.CreateAsset(asset,path); return asset;
    }
    private static Mesh Subset(Mesh source,HashSet<int> keep,string name)
    {
        int[] old=keep.OrderBy(i=>i).ToArray();
        var map=new Dictionary<int,int>(); for(int i=0;i<old.Length;i++) map[old[i]]=i;
        var mesh=new Mesh {name=name,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
        var vertices=source.vertices; var normals=source.normals; var uv=source.uv;
        var tangents=source.tangents; var weights=source.boneWeights;
        mesh.vertices=old.Select(i=>vertices[i]).ToArray();
        mesh.normals=old.Select(i=>normals[i]).ToArray();
        mesh.uv=old.Select(i=>uv[i]).ToArray();
        mesh.tangents=old.Select(i=>tangents[i]).ToArray();
        mesh.boneWeights=old.Select(i=>weights[i]).ToArray();
        mesh.bindposes=source.bindposes; mesh.subMeshCount=source.subMeshCount;
        for(int s=0;s<source.subMeshCount;s++)
        {
            var src=source.GetTriangles(s); var tris=new List<int>();
            for(int i=0;i<src.Length;i+=3)
                if(map.ContainsKey(src[i])&&map.ContainsKey(src[i+1])&&map.ContainsKey(src[i+2]))
                { tris.Add(map[src[i]]); tris.Add(map[src[i+1]]); tris.Add(map[src[i+2]]); }
            mesh.SetTriangles(tris,s);
        }
        mesh.RecalculateBounds(); return mesh;
    }
    public static void Configure(GameObject visual)
    {
        var body=visual.transform.Find("Assassin Body").GetComponent<SkinnedMeshRenderer>();
        var original=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/AssassinSkinnedMesh.asset");
        var bodyMesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/ActionBody.asset");
        var clothMesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/ActionCape.asset");
        if(!bodyMesh || !clothMesh)
        {
            var source=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Source/Assassin Unrigged.prefab"));
            var clothIndices=new HashSet<int>(); int offset=0;
            foreach(var mf in source.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr=mf.GetComponent<MeshRenderer>();
                if(!mr||!mr.enabled||!mf.gameObject.activeInHierarchy) continue;
                if(new[]{"cloth2","cloth3","cloth5","cloth9","cloth10","cloth12"}.Contains(mf.name))
                    for(int i=0;i<mf.sharedMesh.vertexCount;i++) clothIndices.Add(offset+i);
                offset+=mf.sharedMesh.vertexCount;
            }
            UnityEngine.Object.DestroyImmediate(source);
            if(offset!=original.vertexCount) throw new InvalidOperationException("Source topology changed; refuse an unsafe cape split.");
            bodyMesh=Save(Subset(original,new HashSet<int>(Enumerable.Range(0,offset).Where(i=>!clothIndices.Contains(i))),"Action Body"),"ActionBody");
            clothMesh=Subset(original,clothIndices,"Action Cape");
            // Skirt constraints follow pelvis, hood follows torso. No canned cloth bone oscillations.
            var weights=clothMesh.boneWeights;
            int hips=Array.FindIndex(body.bones,b=>b.name=="Hips");
            var positions=clothMesh.vertices;
            for(int i=0;i<weights.Length;i++) if(positions[i].y<.4f) weights[i]=new BoneWeight{boneIndex0=hips,weight0=1};
            clothMesh.boneWeights=weights;
            clothMesh=Save(clothMesh,"ActionCape");
        }
        body.sharedMesh=bodyMesh;
        var cape=visual.transform.Find("Simulated Cape");
        if(!cape) {cape=new GameObject("Simulated Cape").transform;cape.SetParent(visual.transform,false);}
        var skin=cape.GetComponent<SkinnedMeshRenderer>(); if(!skin) skin=cape.gameObject.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh=clothMesh; skin.bones=body.bones; skin.rootBone=body.rootBone; skin.sharedMaterials=body.sharedMaterials;
        skin.localBounds=new Bounds(new Vector3(0,.35f,0),new Vector3(1.2f,1.4f,1.4f)); skin.updateWhenOffscreen=true;
        var cloth=cape.GetComponent<Cloth>(); if(!cloth) cloth=cape.gameObject.AddComponent<Cloth>();
        var map=visual.GetComponentsInChildren<Transform>().Where(t=>body.bones.Contains(t)).ToDictionary(t=>t.name);
        var capsules=new List<CapsuleCollider>();
        Action<string,string,float> capsule=(a,b,radius)=>
        {
            var parent=map[a]; var child=parent.Find("Cape Collider");
            if(!child) {child=new GameObject("Cape Collider").transform;child.SetParent(parent,false);}
            child.gameObject.layer=2; // ignored by ray selection; triggers cannot push the Rigidbody
            Vector3 delta=parent.InverseTransformPoint(map[b].position);
            child.localPosition=delta*.5f; child.localRotation=Quaternion.FromToRotation(Vector3.up,delta.normalized);
            var c=child.GetComponent<CapsuleCollider>(); if(!c)c=child.gameObject.AddComponent<CapsuleCollider>();
            c.isTrigger=true; c.direction=1; c.radius=radius*1.12f; c.height=delta.magnitude+2*c.radius;
            capsules.Add(c);
        };
        capsule("Hips","Chest",.052f); capsule("Chest","Neck",.06f);
        capsule("Neck","HeadEnd",.045f);
        foreach(string side in new[]{"Left","Right"})
        {
            capsule(side+"Shoulder",side+"UpperArm",.035f);
            capsule(side+"UpperArm",side+"LowerArm",.028f);
            capsule(side+"LowerArm",side+"Hand",.024f);
            capsule(side+"UpperLeg",side+"LowerLeg",.035f);
            capsule(side+"LowerLeg",side+"Foot",.028f);
        }
        cloth.capsuleColliders=capsules.ToArray();
        // Cloth welds exact duplicate positions (UV seams). Author constraints in
        // bind space, not the scaled/animated coordinates returned by Cloth.vertices.
        var v=clothMesh.vertices.Distinct().ToArray();
        if(v.Length!=cloth.coefficients.Length) throw new InvalidOperationException("Unexpected cloth weld mapping.");
        var coeff=new ClothSkinningCoefficient[v.Length];
        for(int i=0;i<v.Length;i++)
        {
            float y=v[i].y;
            float free=y>.4f ? Mathf.Clamp01((.545f-y)/.12f) : Mathf.Clamp01((.35f-y)/.25f);
            coeff[i].maxDistance=free*(y>.4f?.09f:.25f);
            coeff[i].collisionSphereDistance=0;
        }
        cloth.coefficients=coeff; cloth.useGravity=true;
        cloth.stretchingStiffness=1; cloth.bendingStiffness=.65f; cloth.damping=.65f;
        cloth.friction=.35f; cloth.useTethers=true; cloth.enableContinuousCollision=true;
        cloth.clothSolverFrequency=180; cloth.worldVelocityScale=.12f; cloth.worldAccelerationScale=.1f;
        var lookup=new Dictionary<Vector3,uint>();
        for(int i=0;i<v.Length;i++)lookup[v[i]]=(uint)i;
        var positionsForCollision=clothMesh.vertices;var virtualIndices=new List<uint>();
        foreach(var triangle in clothMesh.triangles) virtualIndices.Add(lookup[positionsForCollision[triangle]]);
        var packed=new List<uint>();
        for(int i=0;i<virtualIndices.Count;i+=3)
        {packed.Add(virtualIndices[i]);packed.Add(virtualIndices[i+1]);packed.Add(virtualIndices[i+2]);packed.Add(0);}
        cloth.SetVirtualParticleWeights(new List<Vector3>{Vector3.one/3});
        cloth.SetVirtualParticleIndices(packed);
        cloth.randomAcceleration=Vector3.zero; cloth.collisionMassScale=.3f;
        var aero=visual.GetComponent<CapeAerodynamics>(); if(!aero)aero=visual.AddComponent<CapeAerodynamics>();
        aero.Panels=new[]{cloth}; aero.PlayerBody=visual.GetComponentInParent<Rigidbody>();
        EditorUtility.SetDirty(body); EditorUtility.SetDirty(skin); EditorUtility.SetDirty(cloth); EditorUtility.SetDirty(aero);
    }
}
