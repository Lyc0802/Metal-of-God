using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 依照 Tin 開關調整夜視參數與音效感知，並在停用時還原設定。
/// </summary>
[RequireComponent(typeof(PlayerMetalInventory), typeof(PlayerHealth))]
public class PlayerTinAbility : MonoBehaviour
{
    public Camera ViewCamera;
    [Header("Night Vision - material relighting, no exposure boost")]
    [Range(0, 1)]
    public float NightVisionStrength = 1;
    [Tooltip("Upper reference for the added diffuse lighting, in directional-light intensity units.")]
    [Range(0, 2)]
    public float ReferenceLightIntensity = 2;
    [Tooltip("Brightness of surfaces facing away from the reference light. Keeps dark rooms readable.")]
    [Range(.05f, 1)]
    public float MinimumSurfaceLighting = .35f;
    [Tooltip("Retain some original shadow contrast below the lighting reference.")]
    [Range(0, .8f)]
    public float ShadowRetention = .2f;
    [Tooltip("Optional direction reference. Its intensity and shadows are never modified.")]
    public Light ReferenceLight;
    public static PlayerTinAbility ActiveInstance { get; private set; }
    public Light DirectionReference => ReferenceLight ? ReferenceLight : RenderSettings.sun;
    public Vector3 ReferenceLightDirection => DirectionReference
        ? -DirectionReference.transform.forward
        : new Vector3(
        .35f,
        .8f,
        .45f).normalized;

    public Color ReferenceLightColor
    {
        get
        {
            var light = DirectionReference;
            if (!light)
            {
                return Color.white;
            }

            return light.color.linear * (light.useColorTemperature
                ? Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature)
                : Color.white);
        }
    }

    [Header("Directional Sound HUD")]
    [Min(1)]
    public float SoundDetectionDistance = 30;
    [Min(.1f)]
    public float SoundPersistence = .65f;
    public Color SoundColor = new Color(1, .78f, .25f, 1);
    [Min(24)]
    public float MinimumCueWidth = 140, MaximumCueWidth = 340;
    [Min(8)]
    public float MinimumCueDepth = 36, MaximumCueDepth = 96;
    [Min(.01f)]
    public float CueGrowthDuration = .18f;
    [Min(0)]
    public float ScreenEdgeMargin = 4;
    [Range(1, 32)]
    public int SoundPoolSize = 16;
    public bool IsActive => isActiveAndEnabled && inventory && health && !health.IsDead
        && inventory.CanUse(MetalType.Tin);

    public int ActiveCueCount
    {
        get
        {
            int n = 0;
            if (cues != null)
            {
                foreach (var cue in cues)
                {
                    if (cue.Remaining > 0)
                    {
                        n++;
                    }
                }
            }

            return n;
        }
    }

    private PlayerMetalInventory inventory;
    private PlayerHealth health;
    private GameObject hudObject;
    private Texture2D cueTexture;
    sealed class SoundCue
    {
        public SoundEmitter Source;
        public RawImage Image;
        public RectTransform Rect;
        public Vector3 Position;
        public float Remaining, Strength, Age;
    }

    private SoundCue[] cues;
    private int nextCue;
    private void CreateEffects()
    {
        inventory = GetComponent<PlayerMetalInventory>();
        health = GetComponent<PlayerHealth>();
        if (!ViewCamera)
        {
            ViewCamera = Camera.main;
        }

        CreateSoundHUD();
    }

    private void CreateSoundHUD()
    {
        // One shared icon and fixed UI pool; no world-space ripples or per-sound allocations.
        const int width = 256, height = 96;
        cueTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        cueTexture.hideFlags = HideFlags.DontSave;
        cueTexture.name = "Tin Edge Inward Waves";
        cueTexture.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Texture top touches the screen border. Each successive inward band is wider.
                float across = x - (width - 1) * .5f, inward = height - 1 - y, alpha = 0;
                for (int wave = 0; wave < 3; wave++)
                {
                    float halfWidth = 30 + wave * 44, fraction = across / halfWidth;
                    if (Mathf.Abs(fraction) > 1)
                    {
                        continue;
                    }

                    float depth = 10 + wave * 30 + 8 * fraction * fraction;
                    float ends = Mathf.Clamp01((halfWidth - Mathf.Abs(across)) / 3);
                    alpha = Mathf.Max(alpha, Mathf.Clamp01(2.5f - Mathf.Abs(inward - depth)) * ends);
                }

                pixels[y * width + x] = new Color(1, 1, 1, alpha);
            }
        }

        cueTexture.SetPixels(pixels);
        cueTexture.Apply();
        hudObject = new GameObject("Tin Directional Sound HUD", typeof(RectTransform), typeof(Canvas));
        hudObject.transform.SetParent(transform, false);
        var canvas = hudObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;
        cues = new SoundCue[Mathf.Clamp(SoundPoolSize, 1, 32)];
        for (int i = 0; i < cues.Length; i++)
        {
            var go = new GameObject(
                "Sound Direction " + i,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RawImage));
            go.transform.SetParent(hudObject.transform, false);
            var image = go.GetComponent<RawImage>();
            image.texture = cueTexture;
            image.raycastTarget = false;
            image.enabled = false;
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, 1);
            cues[i] = new SoundCue
            {
                Image = image,
                Rect = rect
            };
        }
    }

    private void OnEnable()
    {
        CreateEffects();
        RefreshState();
        SoundEmitter.Emitted += OnSound;
    }

    private void OnDisable()
    {
        SoundEmitter.Emitted -= OnSound;
        DisposeEffects();
    }

    private void OnSound(SoundEmitter source, Vector3 position, float strength)
    {
        if (cues == null || !IsActive || !ViewCamera || !TryGetSoundCue(position, strength, out _, out _))
        {
            return;
        }

        SoundCue cue = null;
        foreach (var existing in cues)
        {
            if (existing.Remaining > 0 && existing.Source == source)
            {
                cue = existing;
                break;
            }
        }

        if (cue == null)
        {
            cue = cues[nextCue];
            nextCue = (nextCue + 1) % cues.Length;
            cue.Age = 0;
        }

        cue.Source = source;
        cue.Position = position;
        cue.Strength = strength;
        cue.Remaining = Mathf.Max(.1f, SoundPersistence);
        UpdateCue(cue); // Place on the border before enabling; never flash at the default center.
    }

    // Horizontal bearing is relative to camera yaw: front/top, rear/bottom, left/left, right/right.
    public bool TryGetSoundCue(Vector3 position, float strength, out Vector2 direction, out float intensity)
    {
        direction = Vector2.up;
        intensity = 0;
        if (!ViewCamera || strength <= 0)
        {
            return false;
        }

        var offset = position - transform.position;
        float distance = offset.magnitude, range = Mathf.Max(1, SoundDetectionDistance);
        if (distance >= range)
        {
            return false;
        }

        var forward = Vector3.ProjectOnPlane(ViewCamera.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < .001f)
        {
            forward = Vector3.ProjectOnPlane(ViewCamera.transform.up, Vector3.up);
        }

        forward.Normalize();
        var right = Vector3.Cross(Vector3.up, forward);
        direction = new Vector2(Vector3.Dot(offset, right), Vector3.Dot(offset, forward));
        direction = direction.sqrMagnitude > .001f ? direction.normalized : Vector2.up;
        intensity = (1 - distance / range) * Mathf.Clamp01(strength);
        return intensity > .001f;
    }

    public void RefreshState()
    {
        if (IsActive)
        {
            ActiveInstance = this;
        }
        else
        {
            if (ActiveInstance == this)
            {
                ActiveInstance = null;
            }

            HideEffects();
        }
    }

    // Returns the OUTER pivot of a border-only cue, with a top pivot facing outward.
    // Intersect a ray with a rectangle, not an ellipse: diagonals must also touch an edge.
    public void GetSoundCueLayout(
        Vector2 direction,
        float intensity,
        out Vector2 position,
        out Vector2 size,
        out float rotation)
    {
        float scale = Mathf.Clamp(Screen.height / 1080f, .5f, 2);
        float proximity = Mathf.Sqrt(Mathf.Clamp01(intensity));
        float width = Mathf.Lerp(MinimumCueWidth, MaximumCueWidth, proximity) * scale;
        float depth = Mathf.Lerp(MinimumCueDepth, MaximumCueDepth, proximity) * scale;
        float shortSide = Mathf.Min(Screen.width, Screen.height);
        width = Mathf.Clamp(width, 16, shortSide * .65f);
        depth = Mathf.Clamp(depth, 8, shortSide * .16f);
        var half = new Vector2(Screen.width, Screen.height) * .5f;
        half -= Vector2.one * Mathf.Clamp(ScreenEdgeMargin * scale, 0, shortSide * .02f);
        if (direction.sqrMagnitude < .001f)
        {
            direction = Vector2.up;
        }

        float tx = Mathf.Abs(direction.x) > .0001f ? half.x / Mathf.Abs(direction.x) : float.PositiveInfinity;
        float ty = Mathf.Abs(direction.y) > .0001f ? half.y / Mathf.Abs(direction.y) : float.PositiveInfinity;
        position = direction * Mathf.Min(tx, ty);
        if (tx < ty)
        {
            position.x = Mathf.Sign(direction.x) * half.x;
            position.y = Mathf.Clamp(position.y, -half.y + width * .5f, half.y - width * .5f);
            rotation = direction.x > 0 ? -90 : 90;
        }
        else
        {
            position.y = Mathf.Sign(direction.y) * half.y;
            position.x = Mathf.Clamp(position.x, -half.x + width * .5f, half.x - width * .5f);
            rotation = direction.y > 0 ? 0 : 180;
        }

        size = new Vector2(width, depth);
    }

    private void UpdateCue(SoundCue cue)
    {
        if (cue.Source)
        {
            cue.Position = cue.Source.WorldPosition;
        }

        if (cue.Remaining <= 0 || !TryGetSoundCue(cue.Position, cue.Strength, out var direction, out var intensity))
        {
            cue.Image.enabled = false;
            cue.Remaining = 0;
            return;
        }

        GetSoundCueLayout(direction, intensity, out var position, out var size, out var rotation);
        float growth = Mathf.SmoothStep(0, 1, Mathf.Clamp01(cue.Age / Mathf.Max(.01f, CueGrowthDuration)));
        cue.Rect.anchoredPosition = position;
        cue.Rect.sizeDelta = Vector2.Scale(size, new Vector2(Mathf.Lerp(.6f, 1, growth), Mathf.Lerp(.55f, 1, growth)));
        cue.Rect.localRotation = Quaternion.Euler(0, 0, rotation);
        var color = SoundColor;
        color.a *= Mathf.Lerp(.12f, 1, intensity) * Mathf.Clamp01(cue.Remaining / Mathf.Max(.1f, SoundPersistence));
        cue.Image.color = color;
        cue.Image.enabled = true;
    }

    private void Update()
    {
        RefreshState();
        if (!IsActive || !ViewCamera || cues == null)
        {
            return;
        }

        foreach (var cue in cues)
        {
            if (cue.Remaining <= 0)
            {
                continue;
            }

            cue.Remaining = Mathf.Max(0, cue.Remaining - Time.deltaTime);
            cue.Age += Time.deltaTime;
            UpdateCue(cue);
        }
    }

    private void HideEffects()
    {
        if (ActiveInstance == this)
        {
            ActiveInstance = null;
        }

        if (cues != null)
        {
            foreach (var cue in cues)
            {
                cue.Remaining = 0;
                cue.Source = null;
                if (cue.Image)
                {
                    cue.Image.enabled = false;
                }
            }
        }
    }

    private void OnDestroy()
    {
        DisposeEffects();
    }

    private void DisposeEffects()
    {
        HideEffects();
        if (hudObject)
        {
            Destroy(hudObject);
        }

        if (cueTexture)
        {
            Destroy(cueTexture);
        }

        hudObject = null;
        cueTexture = null;
        cues = null;
    }
}
