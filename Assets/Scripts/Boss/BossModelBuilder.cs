#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 以程式生成鋼鐵審判者、巨斧、石頭與鋼塊的低多邊形模型，存成 Assets/Boss/Models 下的 Mesh 資產。
/// 全部使用平面著色（每個三角形各自的頂點）以呈現稜角分明的風格。
/// </summary>
public static class BossModelBuilder
{
    private const string ModelFolder = "Assets/Boss/Models";
    private const string MaterialFolder = "Assets/Boss/Materials";

    // ------------------------------------------------------------------ mesh builder

    private class MB
    {
        private readonly List<Vector3> v = new List<Vector3>();
        private readonly List<int> t = new List<int>();

        /// <summary>加入三角形，並翻轉繞序使法線朝向 outward。</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0)
            {
                var swap = b;
                b = c;
                c = swap;
            }

            int i = v.Count;
            v.Add(a);
            v.Add(b);
            v.Add(c);
            t.Add(i);
            t.Add(i + 1);
            t.Add(i + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            Tri(a, b, c, outward);
            Tri(a, c, d, outward);
        }

        public Mesh ToMesh(string name)
        {
            var uv = new Vector2[v.Count];
            for (int i = 0; i < v.Count; i++)
            {
                uv[i] = new Vector2(v[i].x + v[i].z, v[i].y);
            }

            var m = new Mesh { name = name };
            m.indexFormat = v.Count > 60000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }
    }

    private static void Basis(Vector3 axis, out Vector3 u, out Vector3 w)
    {
        u = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > .95f ? Vector3.right : Vector3.Cross(axis, Vector3.up).normalized;
        w = Vector3.Cross(axis, u).normalized;
    }

    /// <summary>圓管（可錐形），沿 p0→p1。</summary>
    private static void Tube(MB mb, Vector3 p0, Vector3 p1, float r0, float r1, int seg, bool caps = true)
    {
        Vector3 axis = (p1 - p0).normalized;
        Basis(axis, out var u, out var w);
        for (int i = 0; i < seg; i++)
        {
            float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
            Vector3 d0 = u * Mathf.Cos(a0) + w * Mathf.Sin(a0), d1 = u * Mathf.Cos(a1) + w * Mathf.Sin(a1);
            Vector3 mid = (d0 + d1) * .5f;
            mb.Quad(p0 + d0 * r0, p0 + d1 * r0, p1 + d1 * r1, p1 + d0 * r1, mid);
            if (caps)
            {
                mb.Tri(p0, p0 + d0 * r0, p0 + d1 * r0, -axis);
                mb.Tri(p1, p1 + d0 * r1, p1 + d1 * r1, axis);
            }
        }
    }

    private static void Cone(MB mb, Vector3 baseCenter, Vector3 dir, float radius, float length, int seg)
    {
        Vector3 axis = dir.normalized;
        Basis(axis, out var u, out var w);
        Vector3 tip = baseCenter + axis * length;
        for (int i = 0; i < seg; i++)
        {
            float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
            Vector3 d0 = u * Mathf.Cos(a0) + w * Mathf.Sin(a0), d1 = u * Mathf.Cos(a1) + w * Mathf.Sin(a1);
            mb.Tri(baseCenter + d0 * radius, baseCenter + d1 * radius, tip, (d0 + d1) * .5f + axis * .3f);
            mb.Tri(baseCenter, baseCenter + d0 * radius, baseCenter + d1 * radius, -axis);
        }
    }

    /// <summary>橢球（UV 球），可加粗糙度。</summary>
    private static void Blob(MB mb, Vector3 center, Vector3 scale, int rings, int seg, float noise = 0, int seed = 0)
    {
        var grid = new Vector3[rings + 1, seg];
        var rnd = new System.Random(seed);
        var jitter = new float[rings + 1, seg];
        for (int r = 0; r <= rings; r++)
        {
            for (int s = 0; s < seg; s++)
            {
                jitter[r, s] = 1 + noise * (float)(rnd.NextDouble() * 2 - 1);
            }
        }

        for (int s = 0; s < seg; s++)
        {
            jitter[0, s] = jitter[0, 0];
            jitter[rings, s] = jitter[rings, 0];
        }

        for (int r = 0; r <= rings; r++)
        {
            float phi = Mathf.PI * r / rings;
            for (int s = 0; s < seg; s++)
            {
                float th = Mathf.PI * 2 * s / seg;
                var p = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                grid[r, s] = center + Vector3.Scale(p * jitter[r, s], scale);
            }
        }

        for (int r = 0; r < rings; r++)
        {
            for (int s = 0; s < seg; s++)
            {
                int s2 = (s + 1) % seg;
                Vector3 a = grid[r, s], b = grid[r, s2], c = grid[r + 1, s2], d = grid[r + 1, s];
                Vector3 outward = (a + b + c + d) * .25f - center;
                if (r == 0)
                {
                    mb.Tri(a, c, d, outward);
                }
                else if (r == rings - 1)
                {
                    mb.Tri(a, b, c, outward);
                }
                else
                {
                    mb.Quad(a, b, c, d, outward);
                }
            }
        }
    }

    /// <summary>沿 Y 軸的橢圓放樣（長袍）。</summary>
    private static void Robe(MB mb, float[] y, float[] rx, float[] rz, float[] cz, int seg)
    {
        int n = y.Length;
        var pts = new Vector3[n, seg];
        for (int i = 0; i < n; i++)
        {
            for (int s = 0; s < seg; s++)
            {
                float th = Mathf.PI * 2 * s / seg;
                float fold = 1 + .045f * Mathf.Sin(s * 5f + i * 1.7f) + .03f * Mathf.Sin(s * 11f - i);
                if (i == 0)
                {
                    fold += .09f * Mathf.Sin(s * 7f) + .05f * Mathf.Cos(s * 13f);
                }

                pts[i, s] = new Vector3(Mathf.Cos(th) * rx[i] * fold, y[i], cz[i] + Mathf.Sin(th) * rz[i] * fold);
            }
        }

        for (int i = 0; i < n - 1; i++)
        {
            for (int s = 0; s < seg; s++)
            {
                int s2 = (s + 1) % seg;
                Vector3 a = pts[i, s], b = pts[i, s2], c = pts[i + 1, s2], d = pts[i + 1, s];
                Vector3 mid = (a + b + c + d) * .25f;
                mb.Quad(a, b, c, d, new Vector3(mid.x, 0, mid.z - cz[i]));
            }
        }

        var top = new Vector3(0, y[n - 1], cz[n - 1]);
        for (int s = 0; s < seg; s++)
        {
            mb.Tri(top, pts[n - 1, s], pts[n - 1, (s + 1) % seg], Vector3.up);
        }
    }

    /// <summary>二十面體石塊：細分後對各頂點做半徑擾動，形成稜角岩石。</summary>
    private static Mesh Ico(string name, int subdivisions, float radius, Vector3 scale, float noise, int seed)
    {
        float tt = (1 + Mathf.Sqrt(5)) / 2;
        var verts = new List<Vector3>
        {
            new Vector3(-1, tt, 0), new Vector3(1, tt, 0), new Vector3(-1, -tt, 0), new Vector3(1, -tt, 0),
            new Vector3(0, -1, tt), new Vector3(0, 1, tt), new Vector3(0, -1, -tt), new Vector3(0, 1, -tt),
            new Vector3(tt, 0, -1), new Vector3(tt, 0, 1), new Vector3(-tt, 0, -1), new Vector3(-tt, 0, 1)
        };
        for (int i = 0; i < verts.Count; i++)
        {
            verts[i] = verts[i].normalized;
        }

        var faces = new List<int[]>
        {
            new[] { 0, 11, 5 }, new[] { 0, 5, 1 }, new[] { 0, 1, 7 }, new[] { 0, 7, 10 }, new[] { 0, 10, 11 },
            new[] { 1, 5, 9 }, new[] { 5, 11, 4 }, new[] { 11, 10, 2 }, new[] { 10, 7, 6 }, new[] { 7, 1, 8 },
            new[] { 3, 9, 4 }, new[] { 3, 4, 2 }, new[] { 3, 2, 6 }, new[] { 3, 6, 8 }, new[] { 3, 8, 9 },
            new[] { 4, 9, 5 }, new[] { 2, 4, 11 }, new[] { 6, 2, 10 }, new[] { 8, 6, 7 }, new[] { 9, 8, 1 }
        };
        for (int s = 0; s < subdivisions; s++)
        {
            var next = new List<int[]>();
            var cache = new Dictionary<long, int>();
            System.Func<int, int, int> mid = (a, b) =>
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (!cache.TryGetValue(key, out int idx))
                {
                    verts.Add(((verts[a] + verts[b]) * .5f).normalized);
                    idx = verts.Count - 1;
                    cache[key] = idx;
                }

                return idx;
            };
            foreach (var f in faces)
            {
                int ab = mid(f[0], f[1]), bc = mid(f[1], f[2]), ca = mid(f[2], f[0]);
                next.Add(new[] { f[0], ab, ca });
                next.Add(new[] { f[1], bc, ab });
                next.Add(new[] { f[2], ca, bc });
                next.Add(new[] { ab, bc, ca });
            }

            faces = next;
        }

        var rnd = new System.Random(seed);
        var pos = new Vector3[verts.Count];
        for (int i = 0; i < verts.Count; i++)
        {
            float r = radius * (1 + noise * (float)(rnd.NextDouble() * 2 - 1));
            pos[i] = Vector3.Scale(verts[i] * r, scale);
        }

        var mb = new MB();
        foreach (var f in faces)
        {
            Vector3 a = pos[f[0]], b = pos[f[1]], c = pos[f[2]];
            mb.Tri(a, b, c, (a + b + c) / 3f);
        }

        return mb.ToMesh(name);
    }

    // ------------------------------------------------------------------ boss parts

    private static Mesh BuildCloth()
    {
        var mb = new MB();
        // 長袍：下擺寬、腰收、肩寬。
        Robe(mb,
            new[] { 0f, .5f, 1.5f, 2.6f, 3.5f, 4.3f, 4.75f },
            new[] { 1.6f, 1.4f, 1.1f, .95f, 1.2f, 1.45f, .75f },
            new[] { 1.25f, 1.1f, .9f, .8f, .88f, .85f, .62f },
            new[] { 0f, 0f, -.03f, -.05f, -.02f, 0f, .05f },
            30);
        // 披在肩上的高領與兜帽後垂。
        Robe(mb,
            new[] { 4.5f, 4.95f, 5.4f },
            new[] { .95f, .82f, .5f },
            new[] { .8f, .72f, .52f },
            new[] { -.2f, -.3f, -.3f },
            20);
        // 袖子（上臂）。
        // 左右手臂都是獨立部件（可舉起 / 伸直，見 BuildLeftArm* / BuildRightArm*）。
        // 後方破爛披風下擺。
        for (int i = -3; i <= 3; i++)
        {
            float x = i * .42f;
            float len = 1.6f + .35f * Mathf.Sin(i * 2.1f);
            Vector3 a = new Vector3(x - .2f, 3.9f, -.78f), b = new Vector3(x + .2f, 3.9f, -.78f);
            Vector3 c = new Vector3(x + .1f, 3.9f - len, -.95f), d = new Vector3(x - .1f, 3.9f - len - .15f, -.95f);
            mb.Quad(a, b, c, d, Vector3.back);
            mb.Quad(a, b, c, d, Vector3.forward);
        }

        return mb.ToMesh("InquisitorCloth");
    }

    private static Mesh BuildSteel()
    {
        var mb = new MB();
        // 胸甲、肩甲、肩刺。
        Blob(mb, new Vector3(0, 3.75f, .38f), new Vector3(.95f, .85f, .42f), 8, 14, .04f, 1);
        Blob(mb, new Vector3(0, 4.45f, .1f), new Vector3(.8f, .25f, .6f), 5, 14, .03f, 2);
        foreach (float sx in new[] { -1f, 1f })
        {
            Blob(mb, new Vector3(sx * 1.55f, 4.3f, 0), new Vector3(.8f, .48f, .72f), 7, 14, .05f, sx > 0 ? 3 : 4);
            Cone(mb, new Vector3(sx * 1.55f, 4.65f, 0), new Vector3(sx * .45f, 1, 0), .16f, .95f, 7);
            Cone(mb, new Vector3(sx * 1.95f, 4.5f, .2f), new Vector3(sx * 1, .55f, .5f), .12f, .7f, 7);
            Cone(mb, new Vector3(sx * 1.95f, 4.5f, -.2f), new Vector3(sx * 1, .55f, -.5f), .12f, .7f, 7);
        }

        // 腰帶與扣環。
        Tube(mb, new Vector3(0, 2.5f, -.03f), new Vector3(0, 2.8f, -.03f), 1.02f, 1.0f, 24, false);
        Blob(mb, new Vector3(0, 2.66f, .8f), new Vector3(.28f, .24f, .12f), 5, 10, .02f, 5);
        // 靴。
        Blob(mb, new Vector3(-.6f, .22f, .55f), new Vector3(.42f, .24f, .72f), 5, 10, .03f, 8);
        Blob(mb, new Vector3(.6f, .22f, .55f), new Vector3(.42f, .24f, .72f), 5, 10, .03f, 9);
        // 穿過雙眼的鋼刺（審判者標誌）。
        foreach (float sx in new[] { -1f, 1f })
        {
            Tube(mb, new Vector3(sx * .17f, 5.52f, .1f), new Vector3(sx * .17f, 5.52f, 1.05f), .05f, .03f, 8);
            Cone(mb, new Vector3(sx * .17f, 5.52f, 1.05f), Vector3.forward, .03f, .45f, 8);
            Cone(mb, new Vector3(sx * .17f, 5.52f, .1f), Vector3.back, .05f, .35f, 8);
        }

        // 頭冠環刺。
        for (int i = 0; i < 9; i++)
        {
            float a = i * Mathf.PI * 2 / 9;
            Vector3 dir = new Vector3(Mathf.Cos(a), .55f, Mathf.Sin(a)).normalized;
            Cone(mb, new Vector3(Mathf.Cos(a) * .42f, 5.78f, Mathf.Sin(a) * .42f - .05f), dir, .055f, .55f, 6);
        }

        return mb.ToMesh("InquisitorSteel");
    }

    private static Mesh BuildSkin()
    {
        var mb = new MB();
        Blob(mb, new Vector3(0, 5.4f, .05f), new Vector3(.42f, .56f, .47f), 9, 14, .02f, 10);
        Tube(mb, new Vector3(0, 4.7f, 0), new Vector3(0, 5.0f, 0), .22f, .2f, 10, false);
        return mb.ToMesh("InquisitorSkin");
    }

    private static Mesh BuildGlow()
    {
        var mb = new MB();
        foreach (float sx in new[] { -1f, 1f })
        {
            Blob(mb, new Vector3(sx * .17f, 5.52f, .45f), new Vector3(.075f, .075f, .075f), 4, 8);
        }

        // 胸前標記。
        Blob(mb, new Vector3(0, 3.8f, .78f), new Vector3(.2f, .2f, .06f), 5, 10);
        return mb.ToMesh("InquisitorGlow");
    }

    // 左臂（獨立部件，原點在左肩）。肩 S=(-1.45,4.25,0)，肘 E=(-1.8,3.0,.45)，腕 W=(-1.55,2.35,1.1)。
    private static readonly Vector3 LeftShoulder = new Vector3(-1.45f, 4.25f, 0);
    private static Vector3 L(float x, float y, float z) => new Vector3(x, y, z) - LeftShoulder;

    // 右臂（持斧手，獨立部件，原點在右肩）。肩 (1.45,4.25,0)，肘 (1.75,3.05,.5)，握把 (1.9,3.6,.9)。
    private static readonly Vector3 RightShoulder = new Vector3(1.45f, 4.25f, 0);
    private static Vector3 R(float x, float y, float z) => new Vector3(x, y, z) - RightShoulder;

    private static Mesh BuildRightArmCloth()
    {
        var mb = new MB();
        Tube(mb, R(1.45f, 4.25f, 0), R(1.75f, 3.05f, .5f), .36f, .3f, 12);
        return mb.ToMesh("RightArmCloth");
    }

    private static Mesh BuildRightArmSteel()
    {
        var mb = new MB();
        Tube(mb, R(1.75f, 3.05f, .5f), R(1.9f, 3.6f, .9f), .3f, .27f, 10);
        Blob(mb, R(1.9f, 3.6f, .9f), new Vector3(.32f, .32f, .34f), 6, 10, .04f, 7);
        return mb.ToMesh("RightArmSteel");
    }

    private static Mesh BuildLeftArmCloth()
    {
        var mb = new MB();
        Tube(mb, L(-1.45f, 4.25f, 0), L(-1.8f, 3.0f, .45f), .36f, .3f, 12);
        return mb.ToMesh("LeftArmCloth");
    }

    private static Mesh BuildLeftArmSteel()
    {
        var mb = new MB();
        Tube(mb, L(-1.8f, 3.0f, .45f), L(-1.55f, 2.35f, 1.1f), .3f, .25f, 10);
        return mb.ToMesh("LeftArmSteel");
    }

    private static Mesh BuildLeftFist()
    {
        var mb = new MB();
        Blob(mb, L(-1.5f, 2.2f, 1.2f), new Vector3(.3f, .3f, .32f), 6, 10, .04f, 6);
        return mb.ToMesh("LeftFist");
    }

    private static Mesh BuildLeftHandOpen()
    {
        var mb = new MB();
        Vector3 palm = L(-1.5f, 2.2f, 1.2f);
        Blob(mb, palm, new Vector3(.27f, .3f, .1f), 6, 10, .03f, 12);
        // 手指沿前臂方向伸展並微微張開。
        Vector3 d = new Vector3(.25f, -.65f, .65f).normalized;
        for (int i = 0; i < 4; i++)
        {
            float off = (i - 1.5f) * .13f;
            Vector3 p0 = palm + new Vector3(off, 0, 0) + d * .2f;
            Tube(mb, p0, p0 + d * .6f + new Vector3(off * .8f, 0, 0), .05f, .035f, 6);
        }

        Tube(mb, palm + new Vector3(.22f, 0, 0), palm + new Vector3(.5f, -.1f, .2f), .05f, .035f, 6);
        return mb.ToMesh("LeftHandOpen");
    }

    private static Mesh BuildAxeShaft()
    {
        var mb = new MB();
        Tube(mb, new Vector3(0, 0, -1.2f), new Vector3(0, 0, 4.7f), .11f, .11f, 10);
        // 握把纏繞環與尾端配重。
        for (int i = 0; i < 5; i++)
        {
            float z = -.5f + i * .35f;
            Tube(mb, new Vector3(0, 0, z), new Vector3(0, 0, z + .12f), .15f, .15f, 10);
        }

        Blob(mb, new Vector3(0, 0, -1.3f), new Vector3(.2f, .2f, .22f), 5, 8, .03f, 11);
        return mb.ToMesh("AxeShaft");
    }

    private static Mesh BuildAxeHead()
    {
        var mb = new MB();
        const int n = 14;
        const float half = .12f;
        var innerPlus = new Vector3[n + 1];
        var innerMinus = new Vector3[n + 1];
        var edge = new Vector3[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n;
            float z = Mathf.Lerp(4.0f, 5.5f, t);
            float depth = .85f + 1.35f * Mathf.Pow(Mathf.Max(0, Mathf.Sin(Mathf.PI * t)), .75f);
            float ez = Mathf.Lerp(3.5f, 6.0f, t);
            innerPlus[i] = new Vector3(half, -.15f, z);
            innerMinus[i] = new Vector3(-half, -.15f, z);
            edge[i] = new Vector3(0, -depth, ez);
        }

        for (int i = 0; i < n; i++)
        {
            mb.Quad(innerPlus[i], innerPlus[i + 1], edge[i + 1], edge[i], Vector3.right);
            mb.Quad(innerMinus[i], innerMinus[i + 1], edge[i + 1], edge[i], Vector3.left);
            mb.Quad(innerPlus[i], innerPlus[i + 1], innerMinus[i + 1], innerMinus[i], Vector3.up);
        }

        mb.Tri(innerPlus[0], innerMinus[0], edge[0], Vector3.back);
        mb.Tri(innerPlus[n], innerMinus[n], edge[n], Vector3.forward);
        // 軸套、背刺與頂端矛尖。
        Tube(mb, new Vector3(0, 0, 4.0f), new Vector3(0, 0, 5.5f), .22f, .22f, 10);
        Cone(mb, new Vector3(0, .1f, 4.75f), new Vector3(0, 1, .15f), .14f, 1.0f, 8);
        Cone(mb, new Vector3(0, 0, 5.5f), Vector3.forward, .16f, .75f, 8);
        return mb.ToMesh("AxeHead");
    }

    // ------------------------------------------------------------------ assets

    private static Material Mat(string name, Color color, float metallic, float smooth, Color? emission = null)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }

        m.SetColor("_BaseColor", color);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smooth);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetColor("_EmissionColor", emission.Value);
        }

        EditorUtility.SetDirty(m);
        return m;
    }

    private static Mesh Save(Mesh mesh)
    {
        string path = ModelFolder + "/" + mesh.name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing)
        {
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    private static void EnsureFolders()
    {
        foreach (var f in new[] { "Assets/Boss", ModelFolder, MaterialFolder, "Assets/Boss/Prefabs" })
        {
            if (!AssetDatabase.IsValidFolder(f))
            {
                int i = f.LastIndexOf('/');
                AssetDatabase.CreateFolder(f.Substring(0, i), f.Substring(i + 1));
            }
        }
    }

    private static GameObject Part(Transform parent, string name, Mesh mesh, Material mat)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        g.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return g;
    }

    /// <summary>產生所有 Mesh 與材質資產，回傳 Boss 身體與斧頭用的網格。</summary>
    [MenuItem("Boss/Build Models")]
    public static void BuildAssets()
    {
        EnsureFolders();
        var steel = Mat("BossSteel", new Color(.2f, .21f, .24f), .9f, .62f);
        Mat("BossCloth", new Color(.07f, .065f, .08f), 0f, .12f);
        Mat("BossSkin", new Color(.58f, .55f, .52f), 0f, .3f);
        Mat("BossGlow", new Color(.6f, .1f, .05f), 0f, .4f, new Color(4f, .5f, .15f));
        Mat("StoneProjectile", new Color(.42f, .38f, .33f), 0f, .08f);
        Mat("ProjectileSteel", new Color(.62f, .68f, .78f), 1f, .78f);
        Save(BuildCloth());
        Save(BuildSteel());
        Save(BuildSkin());
        Save(BuildGlow());
        Save(BuildRightArmCloth());
        Save(BuildRightArmSteel());
        Save(BuildLeftArmCloth());
        Save(BuildLeftArmSteel());
        Save(BuildLeftFist());
        Save(BuildLeftHandOpen());
        Save(BuildAxeShaft());
        Save(BuildAxeHead());
        Save(Ico("RockProjectile", 1, .5f, new Vector3(1f, .82f, 1.12f), .3f, 7));
        Save(Ico("SteelShardProjectile", 0, .5f, new Vector3(.85f, .85f, 1.25f), .12f, 3));
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        if (!steel)
        {
            Debug.LogWarning("BossSteel material missing.");
        }
    }

    private static Mesh Load(string name) => AssetDatabase.LoadAssetAtPath<Mesh>(ModelFolder + "/" + name + ".asset");

    private static Material LoadMat(string name) => AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/" + name + ".mat");

    /// <summary>清掉 Body 底下舊的方塊身體，換成新模型。</summary>
    public static void PopulateBody(Transform body)
    {
        for (int i = body.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(body.GetChild(i).gameObject);
        }

        Part(body, "Inquisitor Cloth", Load("InquisitorCloth"), LoadMat("BossCloth"));
        Part(body, "Inquisitor Steel", Load("InquisitorSteel"), LoadMat("BossSteel"));
        Part(body, "Inquisitor Skin", Load("InquisitorSkin"), LoadMat("BossSkin"));
        Part(body, "Inquisitor Glow", Load("InquisitorGlow"), LoadMat("BossGlow"));

        // 可舉起的左臂：原點在左肩。手有「張開」與「握拳」兩個版本，由 Boss 腳本切換。
        var arm = new GameObject("Left Arm Pivot").transform;
        arm.SetParent(body, false);
        arm.localPosition = LeftShoulder;
        Part(arm, "Left Arm Cloth", Load("LeftArmCloth"), LoadMat("BossCloth"));
        Part(arm, "Left Arm Steel", Load("LeftArmSteel"), LoadMat("BossSteel"));
        Part(arm, "Left Hand Open", Load("LeftHandOpen"), LoadMat("BossSteel"));
        Part(arm, "Left Fist", Load("LeftFist"), LoadMat("BossSteel")).SetActive(false);
        var tip = new GameObject("Left Hand Tip").transform;
        tip.SetParent(arm, false);
        tip.localPosition = L(-1.5f, 2.2f, 1.2f);

        // 持斧的右臂：平常握著斧頭；拋斧後可伸直（投飛用）。
        var right = new GameObject("Right Arm Pivot").transform;
        right.SetParent(body, false);
        right.localPosition = RightShoulder;
        Part(right, "Right Arm Cloth", Load("RightArmCloth"), LoadMat("BossCloth"));
        Part(right, "Right Arm Steel", Load("RightArmSteel"), LoadMat("BossSteel"));
        var rightTip = new GameObject("Right Hand Tip").transform;
        rightTip.SetParent(right, false);
        rightTip.localPosition = R(1.9f, 3.6f, .9f);
    }

    /// <summary>把斧頭的兩個可推拉部件（已掛 MetalDisplayer）換成新模型並更新碰撞器。</summary>
    public static void SkinAxe(Transform shaft, Transform head)
    {
        Skin(shaft, Load("AxeShaft"), LoadMat("BossSteel"));
        Skin(head, Load("AxeHead"), LoadMat("BossSteel"));
    }

    private static void Skin(Transform t, Mesh mesh, Material mat)
    {
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
        t.GetComponent<MeshFilter>().sharedMesh = mesh;
        t.GetComponent<Renderer>().sharedMaterial = mat;
        var box = t.GetComponent<BoxCollider>();
        if (box)
        {
            box.center = mesh.bounds.center;
            box.size = mesh.bounds.size;
        }
    }

    /// <summary>更新投射物預製物的外觀。</summary>
    public static void SkinProjectilePrefabs()
    {
        SkinPrefab("Assets/Boss/Prefabs/Boss Projectile Stone.prefab", Load("RockProjectile"), LoadMat("StoneProjectile"), 1.1f);
        SkinPrefab("Assets/Boss/Prefabs/Boss Projectile Metal.prefab", Load("SteelShardProjectile"), LoadMat("ProjectileSteel"), .85f);
    }

    private static void SkinPrefab(string path, Mesh mesh, Material mat, float scale)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        root.GetComponent<MeshFilter>().sharedMesh = mesh;
        root.GetComponent<Renderer>().sharedMaterial = mat;
        root.transform.localScale = Vector3.one * scale;
        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
    }
}
#endif
