#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class AddMeshColliders
{
    // 選單：Tools > Add Mesh Colliders To Selection
    // 幫選取物件（含所有子物件）中，每個有網格但還沒有碰撞體的物件加上 Mesh Collider
    [MenuItem("Tools/Add Mesh Colliders To Selection")]
    static void Run()
    {
        int added = 0;
        foreach (var go in Selection.gameObjects)
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh || mf.GetComponent<Collider>()) continue;
                var mc = Undo.AddComponent<MeshCollider>(mf.gameObject);
                mc.sharedMesh = mf.sharedMesh;
                added++;
            }
        Debug.Log($"[AddMeshColliders] 加了 {added} 個 Mesh Collider");
    }
}
#endif