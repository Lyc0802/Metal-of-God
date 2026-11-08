using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 依照有效揮劍窗口更新劍刃拖尾，只負責視覺呈現。
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SwordSwingTrail : MonoBehaviour
{
    public PlayerCombat Combat;
    public Transform BladeBase, BladeTip;
    [Range(.02f, .2f)]
    public float Lifetime = .085f;
    [Range(4, 48)]
    public int MaximumSamples = 24;
    [Min(0)]
    public float BasePadding = .08f, TipPadding = .14f;
    [Min(.01f)]
    public float BladeHalfWidth = .12f;
    public Color TrailColor = new Color(.2f, .65f, 1, .65f);
    struct Sample
    {
        public Vector3 Base, Tip, Side;
        public float Time;
    }

    private readonly List<Sample> samples = new List<Sample>(48);
    private readonly List<Vector3> vertices = new List<Vector3>(96);
    private readonly List<Vector2> uv = new List<Vector2>(96);
    private readonly List<Color> colors = new List<Color>(96);
    private readonly List<int> triangles = new List<int>(282);
    private Mesh mesh;
    private bool wasSwinging;
    private void OnEnable()
    {
        if (!mesh)
        {
            mesh = new Mesh
            {
                name = "Actual Sword Blade Sweep"
            };
            mesh.MarkDynamic();
        }

        GetComponent<MeshFilter>().sharedMesh = mesh;
        ClearTrail();
    }

    public void ClearTrail()
    {
        samples.Clear();
        wasSwinging = false;
        if (mesh)
        {
            mesh.Clear();
        }
    }

    private void LateUpdate()
    {
        bool swinging = Combat && Combat.IsBladeSwinging && BladeBase && BladeTip;
        if (swinging && !wasSwinging)
        {
            samples.Clear();
        }

        wasSwinging = swinging;
        float cutoff = Time.time - Mathf.Max(.02f, Lifetime);
        while (samples.Count > 0 && samples[0].Time < cutoff)
        {
            samples.RemoveAt(0);
        }

        if (swinging)
        {
            var bladeDirection = (BladeTip.position - BladeBase.position).normalized;
            var next = new Sample
            {
                Base = BladeBase.position,
                Tip = BladeTip.position,
                Side = Vector3.Cross(BladeBase.up, bladeDirection).normalized,
                Time = Time.time
            };
            if (samples.Count == 0
                || (next.Tip - samples[samples.Count - 1].Tip).sqrMagnitude + (next.Base - samples[samples.Count - 1].Base).sqrMagnitude > .000001f)
            {
                while (samples.Count >= Mathf.Clamp(MaximumSamples, 4, 48))
                {
                    samples.RemoveAt(0);
                }

                samples.Add(next);
            }
        }

        BuildMesh();
    }

    private void BuildMesh()
    {
        mesh.Clear();
        if (samples.Count == 0)
        {
            return;
        }

        vertices.Clear();
        uv.Clear();
        colors.Clear();
        triangles.Clear();
        for (int i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            // Extend beyond both actual blade endpoints so the entire blade stays inside the glow.
            var axis = (sample.Tip - sample.Base).normalized;
            vertices.Add(transform.InverseTransformPoint(sample.Base - axis * Mathf.Max(0, BasePadding)));
            vertices.Add(transform.InverseTransformPoint(sample.Tip + axis * Mathf.Max(0, TipPadding)));
            uv.Add(new Vector2(.5f, .25f));
            uv.Add(new Vector2(.5f, .75f));
            var color = TrailColor;
            color.a *= Mathf.Clamp01(1 - (Time.time - sample.Time) / Mathf.Max(.02f, Lifetime));
            colors.Add(color);
            colors.Add(color);
            if (i == 0)
            {
                continue;
            }

            int a = (i - 1) * 2, b = i * 2;
            triangles.Add(a);
            triangles.Add(a + 1);
            triangles.Add(b);
            triangles.Add(b);
            triangles.Add(a + 1);
            triangles.Add(b + 1);
        }

        // A soft cap spans both sides of the current blade, not only the area behind its motion.
        var latest = samples[samples.Count - 1];
        var bladeAxis = (latest.Tip - latest.Base).normalized;
        var bottom = latest.Base - bladeAxis * Mathf.Max(0, BasePadding);
        var top = latest.Tip + bladeAxis * Mathf.Max(0, TipPadding);
        var side = latest.Side * Mathf.Max(.01f, BladeHalfWidth);
        int cap = vertices.Count;
        vertices.Add(transform.InverseTransformPoint(bottom - side));
        vertices.Add(transform.InverseTransformPoint(top - side));
        vertices.Add(transform.InverseTransformPoint(bottom + side));
        vertices.Add(transform.InverseTransformPoint(top + side));
        uv.Add(new Vector2(.42f, .25f));
        uv.Add(new Vector2(.42f, .75f));
        uv.Add(new Vector2(.58f, .25f));
        uv.Add(new Vector2(.58f, .75f));
        var capColor = TrailColor;
        capColor.a *= Mathf.Clamp01(1 - (Time.time - latest.Time) / Mathf.Max(.02f, Lifetime));
        for (int i = 0; i < 4; i++)
        {
            colors.Add(capColor);
        }

        triangles.Add(cap);
        triangles.Add(cap + 1);
        triangles.Add(cap + 2);
        triangles.Add(cap + 2);
        triangles.Add(cap + 1);
        triangles.Add(cap + 3);
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    private void OnDisable()
    {
        ClearTrail();
    }

    private void OnDestroy()
    {
        if (mesh)
        {
            Destroy(mesh);
        }
    }
}
