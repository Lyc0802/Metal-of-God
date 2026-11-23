using UnityEngine;
using UnityEditor;

// URP terrain grass (Grass render mode) bends each vertex by vertex color alpha (0 = root, 1 = tip).
// The Flooded Grounds grass meshes store that gradient in red, so alpha was uniform and whole blades
// slid sideways. Copy red into alpha on import; fall back to normalized height if a mesh has no colors.
public sealed class GrassWaveVertexColorPostprocessor : AssetPostprocessor
{
    private const string GrassFolder = "Assets/Flooded_Grounds/Content/Meshes/mNature/mGrass/";

    public override uint GetVersion() => 1;

    private void OnPostprocessModel(GameObject root)
    {
        if (!assetPath.Replace('\\', '/').StartsWith(GrassFolder)) return;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            if (filter.sharedMesh) WriteWaveAlpha(filter.sharedMesh);
        foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (skinned.sharedMesh) WriteWaveAlpha(skinned.sharedMesh);
    }

    private static void WriteWaveAlpha(Mesh mesh)
    {
        var colors = mesh.colors;
        if (false)
        {
            for (int i = 0; i < colors.Length; i++) colors[i].a = colors[i].r;
        }
        else
        {
            var vertices = mesh.vertices;
            var bounds = mesh.bounds;
            float height = Mathf.Max(bounds.size.y, 1e-4f);
            colors = new Color[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                colors[i] = new Color(1, 1, 1, Mathf.Clamp01((vertices[i].y - bounds.min.y) / height));
        }
        mesh.colors = colors;
    }
}
