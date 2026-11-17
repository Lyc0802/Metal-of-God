using UnityEngine;

/// <summary>
/// 顯示目前金屬接觸面的選取提示，不負責選取或施力。
/// </summary>
public class MetalTargetIndicator : MonoBehaviour
{
    [Min(.01f)]
    public float Radius = .15f;
    public Color Color = Color.cyan;
    public float SurfaceOffset = .012f, SampleDepth = .08f, HeightTolerance = .03f, NormalTolerance = 15;
    private MeshRenderer ring;
    private Mesh disk;
    private Material material;
    private void Awake()
    {
        var go = new GameObject("Metal Target Indicator");
        ring = go.AddComponent<MeshRenderer>();
        disk = new Mesh
        {
            name = "Solid Metal Target Disk"
        };
        var vertices = new Vector3[49];
        var triangles = new int[48 * 3];
        for (int i = 0; i < 48; i++)
        {
            float a = i * Mathf.PI * 2 / 48;
            vertices[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = (i + 1) % 48 + 1;
        }

        disk.vertices = vertices;
        disk.triangles = triangles;
        disk.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = disk;
        material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.SetFloat("_Cull", 0);
        ring.sharedMaterial = material;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        ring.enabled = false;
    }

    public void Show(MetalSurface surface)
    {
        ring.enabled = false;
        if (!surface.Valid)
        {
            return;
        }

        Vector3 n = surface.Normal, center = surface.Point;
        Vector3 u = Vector3.Cross(n, Mathf.Abs(n.y) < .9f ? Vector3.up : Vector3.right).normalized, v = Vector3.Cross(
            n,
            u);
        for (int i = 0; i < 16; i++)
        {
            float a = i * Mathf.PI * 2 / 16;
            Vector3 sample = center + Radius * (u * Mathf.Cos(a) + v * Mathf.Sin(a));
            if (!Physics.Raycast(
                sample + n * SampleDepth,
                -n,
                out var h,
                SampleDepth * 2,
                ~0,
                QueryTriggerInteraction.Ignore)
                || h.collider != surface.Collider
                || Vector3.Angle(
                h.normal,
                n) > NormalTolerance
                || Mathf.Abs(Vector3.Dot(h.point - center, n)) > HeightTolerance)
            {
                return;
            }
        }

        material.SetColor("_BaseColor", Color);
        ring.transform.SetPositionAndRotation(center + n * SurfaceOffset, Quaternion.LookRotation(n));
        ring.transform.localScale = Vector3.one * Radius;
        ring.enabled = true;
    }

    private void OnDisable()
    {
        if (ring)
        {
            ring.enabled = false;
        }
    }

    private void OnDestroy()
    {
        if (ring)
        {
            Destroy(ring.gameObject);
        }

        if (material)
        {
            Destroy(material);
        }

        if (disk)
        {
            Destroy(disk);
        }
    }
}
