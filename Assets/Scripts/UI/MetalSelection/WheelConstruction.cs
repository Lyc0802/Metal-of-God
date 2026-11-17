using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// 
/// 3D 版的金屬輪盤：用程式生成有厚度的立體物件，再套上材質（MetalWheelSkin 指定）。
///   最底下：一個大圓（鐵輪盤底座）
///   每一塊（8 塊）：外圈（例如皮革）+ 疊在前面的內圈（比較黑）+ 中間一張金屬的圖片
///     內圈的長度 = 存量；燃燒中內圈發出金屬色的光（呼吸閃爍）；滑鼠指到時整塊往外、往前浮起；用完時外圈變暗紅
///   正中央：一個圓（胃）
///
/// 怎麼畫在畫面上：
///   輪盤放在離場景很遠的地方（y = -20000），由一台專用的 Overlay 攝影機拍，疊在主攝影機的畫面上。
///   這台攝影機只看得到前後幾公尺，所以拍不到場景；輪盤有自己的燈，在暗處也一樣亮。
///   每幀把 3D 輪盤對齊到這個 UI 物件在螢幕上的位置與大小，所以 MetalBurningWheel 的指標、火花、文字都不用改。
///
/// 對外的介面跟原本的 MetalWheel 一樣：Create / hovered / Rect / Radius / SectorPoint。
/// 
public class WheelConstruction : MonoBehaviour
{
    public MetalReserves reserves;
    public int hovered = -1;

    float radius;                         // UI 單位（像素，參考解析度 1080 高）
    const float InnerRatio = 0.38f;       // 扇形內半徑 / 輪盤半徑（跟原本的輪盤一樣）
    const float CenterRatio = 0.30f;      // 中央圓的半徑
    static readonly Vector3 RigOrigin = new Vector3(0f, -20000f, 0f);
    const float CamDistance = 10f, CamFov = 20f;

    MetalWheelSkin skin;
    CanvasGroup group;
    readonly Text[] labels = new Text[MetalInfo.Count];
    Text centerText;

    // ---- 3D 物件 ----
    Transform rig, wheelRoot;
    Camera cam;
    Light keyLight, fillLight;
    Material dimMat;
    readonly Transform[] sectorRoots = new Transform[MetalInfo.Count];
    readonly MeshFilter[] innerMeshes = new MeshFilter[MetalInfo.Count];
    readonly Material[] outerMats = new Material[MetalInfo.Count];
    readonly Material[] floorMats = new Material[MetalInfo.Count];
    readonly Material[] innerMats = new Material[MetalInfo.Count];
    readonly Material[] iconMats = new Material[MetalInfo.Count];
    readonly float[] builtAmount = new float[MetalInfo.Count];
    readonly Color[] outerBase = new Color[MetalInfo.Count];

    public RectTransform Rect => (RectTransform)transform;
    public float Radius => radius;

    public static WheelConstruction Create(Transform parent, MetalReserves reserves, float radius)
    {
        var go = new GameObject("WheelConstruction", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var w = go.AddComponent<WheelConstruction>();
        w.reserves = reserves;
        w.radius = radius;
        w.skin = FindAnyObjectByType<MetalWheelSkin>();
        if (!w.skin) Debug.LogWarning("[WheelConstruction] 場景裡沒有 MetalWheelSkin，先用預設的純色材質");

        // 原本的半透明黑色背景是 UI，會蓋在 3D 輪盤上面把它變暗 → 關掉，改由 3D 那邊畫變暗
        var dim = parent.Find("Dim");
        if (dim) dim.gameObject.SetActive(false);

        w.BuildLabels();
        w.BuildRig();
        return w;
    }

    // ==================================================================
    // UI：金屬名稱與中央文字（照原本的輪盤）
    // ==================================================================

    void BuildLabels()
    {
        for (int i = 0; i < MetalInfo.Count; i++)
        {
            var t = NewText(transform, MetalInfo.Names[i], 26);
            t.rectTransform.anchoredPosition = MetalInfo.Direction(MetalInfo.MidAngle(i)) * (radius * (skin ? skin.backgroundRadius : 1.12f) + 36f);
            labels[i] = t;
        }
        centerText = NewText(transform, "", 22);
        centerText.rectTransform.sizeDelta = new Vector2(radius * 0.6f, 90f);
    }

    static Text NewText(Transform parent, string s, int size)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = s;
        t.fontSize = size;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.rectTransform.sizeDelta = new Vector2(160, 40);
        go.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.8f);
        return t;
    }

    // ==================================================================
    // 3D：攝影機、燈、底座、8 塊、中央
    // ==================================================================

    void BuildRig()
    {
        rig = new GameObject("WheelConstruction_Rig").transform;
        rig.position = RigOrigin;

        // 攝影機：在輪盤前方 CamDistance 公尺，往 +z 看；只看前後 3 公尺
        cam = new GameObject("WheelCamera").AddComponent<Camera>();
        cam.transform.SetParent(rig, false);
        cam.transform.localPosition = new Vector3(0, 0, -CamDistance);
        cam.fieldOfView = CamFov;
        cam.nearClipPlane = CamDistance - 3f;
        cam.farClipPlane = CamDistance + 3f;
        var data = cam.GetUniversalAdditionalCameraData();
        data.renderType = CameraRenderType.Overlay;
        data.renderPostProcessing = false;
        data.renderShadows = false;
        var main = Camera.main;
        if (main) main.GetUniversalAdditionalCameraData().cameraStack.Add(cam);
        else Debug.LogError("[WheelConstruction] 找不到 Main Camera，輪盤不會顯示");

        // 燈：主光從左上前方、補光從右下，範圍只到輪盤附近
        keyLight = NewLight("KeyLight", 3f);
        fillLight = NewLight("FillLight", 1.2f);

        wheelRoot = new GameObject("Wheel").transform;
        wheelRoot.SetParent(rig, false);

        // 變暗用的大片（在輪盤後面，蓋住整個畫面）：取代原本 UI 的半透明黑色背景
        dimMat = new Material(Shader.Find("Sprites/Default")) { color = new Color(0, 0, 0, 0) };
        var dim = GameObject.CreatePrimitive(PrimitiveType.Quad);
        DestroyImmediate(dim.GetComponent<Collider>());
        dim.name = "Dim";
        dim.transform.SetParent(cam.transform, false);
        dim.transform.localPosition = new Vector3(0, 0, CamDistance + 2f);
        dim.transform.localScale = new Vector3(100f, 100f, 1f);
        SetupRenderer(dim.GetComponent<MeshRenderer>(), dimMat);

        float bgR = skin ? skin.backgroundRadius : 1.12f;
        float tOuter = skin ? skin.outerThickness : 0.06f;
        float tInner = skin ? skin.innerThickness : 0.03f;

        // 最底下的大圓（在所有東西後面）
        var bg = NewPart("Background", wheelRoot, Mat(skin ? skin.backgroundMaterial : null, new Color(0.25f, 0.25f, 0.27f)));
        bg.GetComponent<MeshFilter>().sharedMesh = Plate(0f, bgR, 0f, 360f, 0.04f, Tiling);
        bg.transform.localPosition = new Vector3(0, 0, 0.06f + tOuter / 2f);

        // 8 塊
        float gap = skin ? skin.gapDegrees : 1.5f;
        for (int i = 0; i < MetalInfo.Count; i++)
        {
            var root = new GameObject(MetalInfo.Names[i]).transform;
            root.SetParent(wheelRoot, false);
            sectorRoots[i] = root;

            float start = MetalInfo.StartAngle(i) - gap;
            float end = MetalInfo.StartAngle(i) - MetalInfo.SectorAngle + gap;   // 順時針

            // 外圈：中間挖空的框，凹槽有側壁和底面（內圈嵌在這個凹槽裡）
            outerMats[i] = Mat(PickMaterial(skin ? skin.outerMaterials : null, i, skin ? skin.outerMaterial : null),
                               new Color(0.45f, 0.30f, 0.18f));
            outerBase[i] = GetColor(outerMats[i]);
            floorMats[i] = new Material(outerMats[i]);                         // 凹槽底面：同材質但暗一點
            var floorCol = outerBase[i] * 0.45f;
            floorCol.a = outerBase[i].a;
            SetColor(floorMats[i], floorCol);
            var outer = NewPart("Outer", root, outerMats[i]);
            outer.GetComponent<MeshRenderer>().sharedMaterials = new[] { outerMats[i], floorMats[i] };
            HoleShape(i, 0f, out float hIn, out float hOut, out float h0, out float h1);
            outer.GetComponent<MeshFilter>().sharedMesh = Frame(InnerRatio, 1f, end, start, hIn, hOut, h0, h1, tOuter, Tiling);

            // 內圈：嵌在凹槽裡、底部貼著凹槽底面，比外框表面低；長度 = 存量（每幀依存量重建）
            innerMats[i] = Mat(PickMaterial(skin ? skin.innerMaterials : null, i, skin ? skin.innerMaterial : null),
                               new Color(0.08f, 0.08f, 0.09f));
            var inner = NewPart("Inner", root, innerMats[i]);
            inner.transform.localPosition = new Vector3(0, 0, FloorZ - tInner / 2f);
            innerMeshes[i] = inner.GetComponent<MeshFilter>();
            builtAmount[i] = -1f;

            // 中間的圖片：放在扇形的中心，永遠正立
            var tex = skin && skin.icons != null && i < skin.icons.Length ? skin.icons[i] : null;
            if (tex)
            {
                iconMats[i] = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
                var icon = GameObject.CreatePrimitive(PrimitiveType.Quad);
                DestroyImmediate(icon.GetComponent<Collider>());
                icon.name = "Icon";
                icon.transform.SetParent(root, false);
                float midR = (InnerRatio + 1f) / 2f;
                var p = MetalInfo.Direction(MetalInfo.MidAngle(i)) * midR;
                icon.transform.localPosition = new Vector3(p.x, p.y, FloorZ - tInner - 0.005f);
                float size = (1f - InnerRatio) * (skin ? skin.iconSize : 0.32f) * 1.6f;
                float aspect = tex.height > 0 ? (float)tex.width / tex.height : 1f;
                icon.transform.localScale = new Vector3(size * aspect, size, 1f);
                SetupRenderer(icon.GetComponent<MeshRenderer>(), iconMats[i]);
            }
        }

        // 正中央的圓
        var center = NewPart("Center", wheelRoot, Mat(skin ? skin.centerMaterial : null, new Color(0.10f, 0.08f, 0.09f)));
        center.GetComponent<MeshFilter>().sharedMesh = Plate(0f, CenterRatio, 0f, 360f, tOuter * 1.4f, Tiling);
        center.transform.localPosition = new Vector3(0, 0, -tOuter * 0.2f);

        rig.gameObject.SetActive(false);
    }

    float Tiling => skin ? Mathf.Max(0.01f, skin.textureTiling) : 1f;
    float TOuter => skin ? skin.outerThickness : 0.06f;
    /// 凹槽底面的 z（外框背面再往前一點點）。z 越小越靠近攝影機。
    float FloorZ => TOuter / 2f - 0.002f;

    /// 第 i 塊的凹槽形狀：從外框的邊往內縮 inset（角度方向換算成在中間半徑處的同樣寬度）。
    /// shrink：再額外往內縮多少（內圈用，避免貼著凹槽側壁閃爍）。
    void HoleShape(int i, float shrink, out float rIn, out float rOut, out float a0, out float a1)
    {
        float inset = (skin ? skin.innerInset : 0.04f) + shrink;
        float gap = skin ? skin.gapDegrees : 1.5f;
        float midR = (InnerRatio + 1f) / 2f;
        float angInset = gap + Mathf.Rad2Deg * inset / midR;
        rIn = InnerRatio + inset;
        rOut = 1f - inset;
        a0 = MetalInfo.StartAngle(i) - MetalInfo.SectorAngle + angInset;
        a1 = MetalInfo.StartAngle(i) - angInset;
    }

    Light NewLight(string name, float intensity)
    {
        var l = new GameObject(name).AddComponent<Light>();
        l.transform.SetParent(rig, false);
        l.type = LightType.Point;
        l.intensity = intensity;
        l.shadows = LightShadows.None;
        return l;
    }

    GameObject NewPart(string name, Transform parent, Material mat)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        SetupRenderer(go.GetComponent<MeshRenderer>(), mat);
        return go;
    }

    static void SetupRenderer(MeshRenderer r, Material mat)
    {
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    /// 這一塊專屬的材質有填就用它，沒填就用共用的
    static Material PickMaterial(Material[] perSector, int i, Material shared) =>
        perSector != null && i < perSector.Length && perSector[i] != null ? perSector[i] : shared;

    /// 每塊用自己的材質複本（才能各自改顏色、發光），不會改到原本的材質檔
    static Material Mat(Material source, Color fallback)
    {
        if (source) return new Material(source);
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var m = new Material(shader ? shader : Shader.Find("Standard"));
        SetColor(m, fallback);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.6f);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.5f);
        return m;
    }

    static Color GetColor(Material m) =>
        m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;

    static void SetColor(Material m, Color c)
    {
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
    }

    static void SetEmission(Material m, Color c)
    {
        if (!m.HasProperty("_EmissionColor")) return;
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        m.SetColor("_EmissionColor", c);
    }

    // ==================================================================
    // 每幀：對齊 UI、狀態變化
    // ==================================================================

    void LateUpdate()
    {
        if (!rig) return;
        if (!group) group = GetComponentInParent<CanvasGroup>();
        float alpha = group ? group.alpha : 1f;
        bool visible = alpha > 0.01f;
        if (rig.gameObject.activeSelf != visible) rig.gameObject.SetActive(visible);
        if (!visible) return;

        AlignToScreen();
        dimMat.color = new Color(0, 0, 0, 0.45f * alpha);

        float t = Time.unscaledTime;
        float tInner = skin ? skin.innerThickness : 0.03f;
        float tOuter = TOuter;

        for (int i = 0; i < MetalInfo.Count; i++)
        {
            float amount = reserves ? reserves.amount[i] : 1f;
            bool burning = reserves && reserves.burning[i];
            bool hover = i == hovered;
            var col = MetalInfo.Colors[i];

            // 內圈長度 = 存量（變化夠大才重建網格）
            if (Mathf.Abs(amount - builtAmount[i]) > 0.003f)
            {
                builtAmount[i] = amount;
                HoleShape(i, 0.004f, out float rIn, out float rMax, out float a0, out float a1);
                float rOut = Mathf.Lerp(rIn, rMax, amount);
                innerMeshes[i].gameObject.SetActive(amount > 0.005f);
                if (amount > 0.005f)
                {
                    if (innerMeshes[i].sharedMesh) Destroy(innerMeshes[i].sharedMesh);
                    innerMeshes[i].sharedMesh = Plate(rIn, rOut, a0, a1, tInner, Tiling);
                }
            }

            // 燃燒中：內圈發出金屬色的光，呼吸閃爍（每種金屬相位錯開）
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 5f + i);
            SetEmission(innerMats[i], burning ? col * Mathf.Lerp(0.6f, 2.2f, pulse) : Color.black);
            if (iconMats[i]) iconMats[i].color = burning ? Color.Lerp(Color.white, col, 0.25f) : new Color(0.75f, 0.75f, 0.75f, hover ? 1f : 0.85f);

            // 用完：外圈偏暗紅；指到：外圈稍微變亮
            var baseCol = outerBase[i];
            var outerCol = amount <= 0f ? Color.Lerp(baseCol, new Color(0.45f, 0.06f, 0.06f), 0.6f)
                         : hover ? baseCol * 1.25f : baseCol;
            outerCol.a = baseCol.a;
            SetColor(outerMats[i], outerCol);

            // 指到：整塊往外推、往鏡頭浮起
            var dir = MetalInfo.Direction(MetalInfo.MidAngle(i));
            var want = hover ? new Vector3(dir.x * 0.07f, dir.y * 0.07f, -tOuter * 1.5f) : Vector3.zero;
            sectorRoots[i].localPosition = Vector3.Lerp(sectorRoots[i].localPosition, want, 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime));

            if (labels[i])
            {
                labels[i].fontSize = hover ? 32 : 26;
                labels[i].color = burning ? Color.Lerp(col, Color.white, 0.5f) : Color.white;
            }
        }

        if (centerText)
        {
            if (hovered < 0 || !reserves) centerText.text = "Drag to a metal";
            else
            {
                string action = reserves.amount[hovered] <= 0f ? "EMPTY"
                              : reserves.burning[hovered] ? "Extinguish" : "Burn";
                centerText.text = $"{MetalInfo.Names[hovered]}\n{action}  {reserves.amount[hovered] * 100f:0}%";
            }
        }
    }

    /// 讓 3D 輪盤在螢幕上的位置、大小，剛好等於這個 UI 物件（含 MetalBurningWheel 的放大動畫）
    void AlignToScreen()
    {
        Vector2 screenCenter = RectTransformUtility.WorldToScreenPoint(null, transform.position);   // Overlay Canvas → camera = null
        float radiusPx = radius * transform.lossyScale.x;

        float worldHeight = 2f * CamDistance * Mathf.Tan(CamFov * 0.5f * Mathf.Deg2Rad);
        float pixelsPerUnit = Screen.height / worldHeight;
        float worldRadius = radiusPx / pixelsPerUnit;

        wheelRoot.position = cam.ScreenToWorldPoint(new Vector3(screenCenter.x, screenCenter.y, CamDistance));
        wheelRoot.rotation = Quaternion.identity;
        wheelRoot.localScale = Vector3.one * worldRadius;

        // 燈跟著輪盤：主光左上前方、補光右下前方
        keyLight.transform.position = wheelRoot.position + new Vector3(-0.7f, 0.9f, -1.6f) * worldRadius;
        keyLight.range = worldRadius * 6f;
        fillLight.transform.position = wheelRoot.position + new Vector3(0.9f, -0.6f, -1.2f) * worldRadius;
        fillLight.range = worldRadius * 5f;
    }

    /// 扇形中線、靠外側的位置（給 UI 粒子用，Wheel 的區域座標）
    public Vector2 SectorPoint(int i) => MetalInfo.Direction(MetalInfo.MidAngle(i)) * radius * Mathf.Lerp(InnerRatio, 1f, 0.7f);

    void OnDestroy()
    {
        var main = Camera.main;
        if (main && cam) main.GetUniversalAdditionalCameraData().cameraStack.Remove(cam);
        if (rig) Destroy(rig.gameObject);
    }

    /// 中間挖空的扇形框：外輪廓 (rIn..rOut, a0..a1)，凹槽 (hIn..hOut, h0..h1)。
    /// submesh 0 = 框的正面與外側面、凹槽側壁；submesh 1 = 凹槽底面（在 FloorZ 附近，朝攝影機）。
    static Mesh Frame(float rIn, float rOut, float a0, float a1,
                      float hIn, float hOut, float h0, float h1, float thickness, float tiling)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs = new List<Vector2>();
        var frameTris = new List<int>();
        var floorTris = new List<int>();
        float zf = -thickness / 2f, zb = thickness / 2f, zFloor = thickness / 2f - 0.002f;
        int k = Mathf.Max(2, Mathf.CeilToInt((a1 - a0) / 3f));   // 弧的分段
        const int m = 2;                                           // 直邊的分段

        Vector2 PlanarUV(Vector2 p) => (p * 0.5f + new Vector2(0.5f, 0.5f)) * tiling;

        void Quad(List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n,
                  Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int s = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);
            uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) >= 0f)
            { tris.Add(s); tris.Add(s + 1); tris.Add(s + 2); tris.Add(s); tris.Add(s + 2); tris.Add(s + 3); }
            else
            { tris.Add(s); tris.Add(s + 2); tris.Add(s + 1); tris.Add(s); tris.Add(s + 3); tris.Add(s + 2); }
        }

        // 沿著輪廓走一圈：內弧 a0→a1、直邊 a1（內→外）、外弧 a1→a0、直邊 a0（外→內）
        // 外輪廓和凹槽用同樣的點數，第 j 個點互相對應，正面就是兩圈之間的帶子
        List<Vector2> Loop(float r0, float r1, float b0, float b1)
        {
            var pts = new List<Vector2>();
            for (int j = 0; j <= k; j++) pts.Add(MetalInfo.Direction(Mathf.Lerp(b0, b1, (float)j / k)) * r0);
            for (int j = 1; j <= m; j++) pts.Add(MetalInfo.Direction(b1) * Mathf.Lerp(r0, r1, (float)j / m));
            for (int j = 1; j <= k; j++) pts.Add(MetalInfo.Direction(Mathf.Lerp(b1, b0, (float)j / k)) * r1);
            for (int j = 1; j < m; j++) pts.Add(MetalInfo.Direction(b0) * Mathf.Lerp(r1, r0, (float)j / m));
            return pts;
        }

        var outer = Loop(rIn, rOut, a0, a1);
        var hole = Loop(hIn, hOut, h0, h1);
        int count = outer.Count;
        Vector2 outerC = MetalInfo.Direction((a0 + a1) / 2f) * ((rIn + rOut) / 2f);
        Vector2 holeC = MetalInfo.Direction((h0 + h1) / 2f) * ((hIn + hOut) / 2f);

        float perim = 0f, holePerim = 0f;
        for (int j = 0; j < count; j++)
        {
            int q = (j + 1) % count;
            Vector2 o0 = outer[j], o1 = outer[q], i0 = hole[j], i1 = hole[q];

            // 正面：外輪廓與凹槽之間的框
            Quad(frameTris, new Vector3(o0.x, o0.y, zf), new Vector3(o1.x, o1.y, zf),
                 new Vector3(i1.x, i1.y, zf), new Vector3(i0.x, i0.y, zf), Vector3.back,
                 PlanarUV(o0), PlanarUV(o1), PlanarUV(i1), PlanarUV(i0));

            // 外側面：法線朝框的外面
            Vector2 d = o1 - o0;
            Vector2 nrm = new Vector2(d.y, -d.x).normalized;
            if (Vector2.Dot(nrm, (o0 + o1) / 2f - outerC) < 0f) nrm = -nrm;
            float u0 = perim * tiling, u1 = (perim += d.magnitude) * tiling, vT = thickness * tiling;
            Quad(frameTris, new Vector3(o0.x, o0.y, zf), new Vector3(o0.x, o0.y, zb), new Vector3(o1.x, o1.y, zb),
                 new Vector3(o1.x, o1.y, zf), new Vector3(nrm.x, nrm.y, 0f),
                 new Vector2(u0, 0), new Vector2(u0, vT), new Vector2(u1, vT), new Vector2(u1, 0));

            // 凹槽側壁：法線朝凹槽中間
            Vector2 e = i1 - i0;
            Vector2 hn = new Vector2(e.y, -e.x).normalized;
            if (Vector2.Dot(hn, holeC - (i0 + i1) / 2f) < 0f) hn = -hn;
            float w0 = holePerim * tiling, w1 = (holePerim += e.magnitude) * tiling, vD = (zFloor - zf) * tiling;
            Quad(frameTris, new Vector3(i0.x, i0.y, zf), new Vector3(i1.x, i1.y, zf), new Vector3(i1.x, i1.y, zFloor),
                 new Vector3(i0.x, i0.y, zFloor), new Vector3(hn.x, hn.y, 0f),
                 new Vector2(w0, 0), new Vector2(w1, 0), new Vector2(w1, vD), new Vector2(w0, vD));
        }

        // 凹槽底面：內弧和外弧之間的帶子
        for (int j = 0; j < k; j++)
        {
            float b0 = Mathf.Lerp(h0, h1, (float)j / k), b1 = Mathf.Lerp(h0, h1, (float)(j + 1) / k);
            Vector2 p0 = MetalInfo.Direction(b0) * hIn, p1 = MetalInfo.Direction(b1) * hIn;
            Vector2 q0 = MetalInfo.Direction(b0) * hOut, q1 = MetalInfo.Direction(b1) * hOut;
            Quad(floorTris, new Vector3(p0.x, p0.y, zFloor), new Vector3(q0.x, q0.y, zFloor),
                 new Vector3(q1.x, q1.y, zFloor), new Vector3(p1.x, p1.y, zFloor), Vector3.back,
                 PlanarUV(p0), PlanarUV(q0), PlanarUV(q1), PlanarUV(p1));
        }

        var mesh = new Mesh { name = "WheelFrame" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(frameTris, 0);
        mesh.SetTriangles(floorTris, 1);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ==================================================================
    // 網格：有厚度的扇形板子（rIn = 0 時是實心圓）
    // ==================================================================

    /// 角度用數學慣例（0° = 右，90° = 上），a0 &lt; a1。板子的正面朝 -z（朝向攝影機）。
    /// 正面 / 背面的 UV 用平面投影（整個輪盤共用同一張貼圖），側面的 UV 沿著邊延伸。
    static Mesh Plate(float rIn, float rOut, float a0, float a1, float thickness, float tiling)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        float zf = -thickness / 2f, zb = thickness / 2f;
        int steps = Mathf.Max(2, Mathf.CeilToInt((a1 - a0) / 3f));

        Vector2 P(float r, float a) => MetalInfo.Direction(a) * r;
        Vector2 PlanarUV(Vector2 p) => (p * 0.5f + new Vector2(0.5f, 0.5f)) * tiling;

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int s = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);
            uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
            // 三角形的正面方向要跟 n 一致（Unity：cross(b-a, c-a) 指向正面）
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) >= 0f)
            { tris.Add(s); tris.Add(s + 1); tris.Add(s + 2); tris.Add(s); tris.Add(s + 2); tris.Add(s + 3); }
            else
            { tris.Add(s); tris.Add(s + 2); tris.Add(s + 1); tris.Add(s); tris.Add(s + 3); tris.Add(s + 2); }
        }

        float arcLenOut = rOut * (a1 - a0) * Mathf.Deg2Rad;
        float arcLenIn = rIn * (a1 - a0) * Mathf.Deg2Rad;
        for (int k = 0; k < steps; k++)
        {
            float aA = Mathf.Lerp(a0, a1, (float)k / steps);
            float aB = Mathf.Lerp(a0, a1, (float)(k + 1) / steps);
            Vector2 oA = P(rOut, aA), oB = P(rOut, aB), iA = P(rIn, aA), iB = P(rIn, aB);

            // 正面（朝攝影機）與背面
            Quad(new Vector3(iA.x, iA.y, zf), new Vector3(oA.x, oA.y, zf), new Vector3(oB.x, oB.y, zf), new Vector3(iB.x, iB.y, zf),
                 Vector3.back, PlanarUV(iA), PlanarUV(oA), PlanarUV(oB), PlanarUV(iB));
            Quad(new Vector3(iA.x, iA.y, zb), new Vector3(iB.x, iB.y, zb), new Vector3(oB.x, oB.y, zb), new Vector3(oA.x, oA.y, zb),
                 Vector3.forward, PlanarUV(iA), PlanarUV(iB), PlanarUV(oB), PlanarUV(oA));

            // 外側弧面
            float uA = arcLenOut * k / steps * tiling, uB = arcLenOut * (k + 1) / steps * tiling, vT = thickness * tiling;
            Vector2 nMid = MetalInfo.Direction((aA + aB) / 2f);
            Quad(new Vector3(oA.x, oA.y, zf), new Vector3(oA.x, oA.y, zb), new Vector3(oB.x, oB.y, zb), new Vector3(oB.x, oB.y, zf),
                 new Vector3(nMid.x, nMid.y, 0f), new Vector2(uA, 0), new Vector2(uA, vT), new Vector2(uB, vT), new Vector2(uB, 0));

            // 內側弧面（實心圓沒有）
            if (rIn > 0.001f)
            {
                float wA = arcLenIn * k / steps * tiling, wB = arcLenIn * (k + 1) / steps * tiling;
                Quad(new Vector3(iA.x, iA.y, zf), new Vector3(iB.x, iB.y, zf), new Vector3(iB.x, iB.y, zb), new Vector3(iA.x, iA.y, zb),
                     new Vector3(-nMid.x, -nMid.y, 0f), new Vector2(wA, 0), new Vector2(wB, 0), new Vector2(wB, vT), new Vector2(wA, vT));
            }
        }

        // 兩條直邊（整圈就不用）
        if (a1 - a0 < 359.9f)
        {
            foreach (var (a, sign) in new[] { (a0, -1f), (a1, 1f) })
            {
                Vector2 i0 = P(rIn, a), o0 = P(rOut, a);
                Vector2 tangent = MetalInfo.Direction(a + 90f) * sign;          // 往扇形外面的方向
                float len = (rOut - rIn) * tiling, vT = thickness * tiling;
                Quad(new Vector3(i0.x, i0.y, zf), new Vector3(o0.x, o0.y, zf), new Vector3(o0.x, o0.y, zb), new Vector3(i0.x, i0.y, zb),
                     new Vector3(tangent.x, tangent.y, 0f), new Vector2(0, 0), new Vector2(len, 0), new Vector2(len, vT), new Vector2(0, vT));
            }
        }

        var mesh = new Mesh { name = "WheelPlate" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateTangents();       // 法線貼圖需要切線
        mesh.RecalculateBounds();
        return mesh;
    }
}