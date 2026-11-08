using UnityEngine;
using SimpleOutline;

/// <summary>
/// 管理實體金屬的範圍註冊、顯示與選取 Outline；與 Bronze 偵測獨立。
/// </summary>
[RequireComponent(typeof(Renderer))]
public class MetalDisplayer : OutlineTarget
{
    public static readonly System.Collections.Generic.HashSet<MetalDisplayer> ActiveMetals = new System.Collections.Generic.HashSet<MetalDisplayer>();
    private void OnEnable()
    {
        ActiveMetals.Add(this);
    }

    private Rigidbody _rigidbody;
    private bool _isRegistered = false;
    private bool _isDisplayed = false;
    private bool _isHighlighted = false;
    [Header("Reference")]
    [SerializeField]
    private Transform player;
    [Header("Metal Setting")]
    public float weight = 1f;
    private float radius;
    [Header("Weight Outline Colors")]
    public bool UseWeightOutlineColors = true;
    public Color LighterOutlineColor = new Color(.1f, .5f, 1f, 1f);
    public Color HeavierOutlineColor = new Color(1f, .45f, .08f, 1f);
    public Color EqualOutlineColor = new Color(.6f, .6f, .6f, 1f);
    [Range(1f, 3f)]
    public float SelectedOutlineBrightness = 1.6f;
    private PlayerMetalManipulator playerMetal;
    public override Color GetOutlineColor(Color defaultColor)
    {
        if (!playerMetal && player)
        {
            playerMetal = player.GetComponent<PlayerMetalManipulator>();
        }

        Color color = base.GetOutlineColor(defaultColor);
        if (UseWeightOutlineColors && playerMetal)
        {
            color = Mathf.Approximately(weight, playerMetal.Weight)
                ? EqualOutlineColor
                : weight < playerMetal.Weight
                ? LighterOutlineColor
                : HeavierOutlineColor;
        }

        if (_isHighlighted)
        {
            float brightness = Mathf.Max(1f, SelectedOutlineBrightness);
            color = new Color(color.r * brightness, color.g * brightness, color.b * brightness, color.a);
        }

        return color;
    }

    [Header("Detection")]
    [SerializeField]
    private float registerDistance = 30f;
    [SerializeField]
    private float unregisterDistance = 32f;
    [SerializeField]
    private float registrationCheckInterval = 0.1f;
    private float _registrationTimer;
    [Header("Force")]
    [SerializeField]
    private float pushForce = 5f;
    [SerializeField]
    private float pullForce = 5f;
    private void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>();
        _properties = new MaterialPropertyBlock();
        _rigidbody = GetComponent<Rigidbody>();
        SetupRadius();
        _registrationTimer = Random.Range(0f, registrationCheckInterval);
    }

    private void Start()
    {
        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
            {
                player = playerObject.transform;
            }
        }

        if (player != null)
        {
            UpdateRegistration();
        }
    }

    private void Update()
    {
        if (player == null)
        {
            return;
        }

        _registrationTimer -= Time.deltaTime;
        if (_registrationTimer <= 0f)
        {
            _registrationTimer = registrationCheckInterval;
            UpdateRegistration();
        }
    }

    private void OnDisable()
    {
        Undisplay();
        ActiveMetals.Remove(this);
        _isRegistered = false;
        _isDisplayed = false;
        _isHighlighted = false;
    }

    // =====================================================
    // Registration
    // =====================================================
    private void UpdateRegistration()
    {
        float centerDistance = Vector3.Distance(player.position, transform.position);
        float maxScale = Mathf.Max(
            Mathf.Abs(transform.lossyScale.x),
            Mathf.Abs(transform.lossyScale.y),
            Mathf.Abs(transform.lossyScale.z));
        float worldRadius = radius * maxScale;
        float surfaceDistance = Mathf.Max(0f, centerDistance - worldRadius);
        if (!_isRegistered)
        {
            if (surfaceDistance <= registerDistance)
            {
                Register();
            }
        }
        else
        {
            if (surfaceDistance > unregisterDistance)
            {
                Unregister();
            }
        }
    }

    private void Register()
    {
        if (_isRegistered)
        {
            return;
        }

        _isRegistered = true;
    // player.GetComponent<PlayerController>()
    //     .RegisterMetal(this);
    }

    private void Unregister()
    {
        if (!_isRegistered)
        {
            return;
        }

        _isRegistered = false;
        Undisplay();
        Unhighlight();
    // player.GetComponent<PlayerController>()
    //     .UnregisterMetal(this);
    }

    // =====================================================
    // Outline
    // =====================================================
    public void Display()
    {
        if (!_isRegistered)
        {
            return;
        }

        if (_isDisplayed)
        {
            return;
        }

        _isDisplayed = true;
        SetOutlined(true);
    }

    public void Undisplay()
    {
        if (!_isDisplayed)
        {
            return;
        }

        _isDisplayed = false;
        _isHighlighted = false;
        SetOutlined(false);
    }

    public bool CanSelect()
    {
        return _isRegistered;
    }

    public void Highlight()
    {
        if (!_isRegistered || !_isDisplayed)
        {
            return;
        }

        _isHighlighted = true;
    }

    public void Unhighlight()
    {
        if (!_isHighlighted)
        {
            return;
        }

        _isHighlighted = false;
    }

    // =====================================================
    // Push / Pull
    // =====================================================
    public void Push()
    {
        if (!_isRegistered)
        {
            return;
        }
    // TODO: Push
    }

    public void Pull()
    {
        if (!_isRegistered)
        {
            return;
        }
    // TODO: Pull
    }

    // =====================================================
    // Radius
    // =====================================================
    private void SetupRadius()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            radius = 0.5f;
            return;
        }

        radius = meshFilter.sharedMesh.bounds.extents.magnitude;
    }
#if UNITY_EDITOR
    private void OnValidate()
    {
        registerDistance =
            Mathf.Max(0f, registerDistance);

        unregisterDistance =
            Mathf.Max(unregisterDistance, registerDistance);

        registrationCheckInterval =
            Mathf.Max(0.01f, registrationCheckInterval);
    }
#endif
}
