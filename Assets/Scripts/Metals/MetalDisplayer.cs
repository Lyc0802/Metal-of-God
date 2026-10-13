using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Renderer))]
public class MetalDisplayer : MonoBehaviour
{
    private Renderer _renderer;
    private Rigidbody _rigidbody;
    private MaterialPropertyBlock _propertyBlock;

    private bool _isRegistered = false;
    private bool _isDisplayed = false;
    private bool _isHighlighted = false;

    private static readonly int OutlineEnabledID =
        Shader.PropertyToID("_OutlineEnabled");

    private static readonly int OutlineObjectRadiusID =
        Shader.PropertyToID("_OutlineObjectRadius");

    private static readonly int OutlineColorID =
        Shader.PropertyToID("_OutlineColor");


    [Header("Reference")]
    [SerializeField] private Transform player;


    [Header("Metal Setting")]
    public float weight = 1f;
    private float radius;


    [Header("Detection")]
    [SerializeField]
    private float registerDistance = 15f;

    [SerializeField]
    private float unregisterDistance = 17f;
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
        _renderer = GetComponent<Renderer>();
        _rigidbody = GetComponent<Rigidbody>();

        _propertyBlock = new MaterialPropertyBlock();

        SetupOutlineRadius();
        SetOutlineEnabled(false);
        SetOutlineColor(new Color(0f, 2f, 4f, 1f));

        _registrationTimer = Random.Range(0f, registrationCheckInterval);
    }


    private void Start()
    {
        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");

            if (playerObject != null) player = playerObject.transform;
        }

        UpdateRegistration();

    }


    private void Update()
    {
        if (player == null) return;
        _registrationTimer -= Time.deltaTime;

        if (_registrationTimer <= 0f)
        {
            _registrationTimer += registrationCheckInterval;

            UpdateRegistration();
        }
    }


    // =====================================================
    // Registration
    // =====================================================

    private void UpdateRegistration()
    {
        float sqrDistance = (player.position - transform.position).sqrMagnitude - radius;

        if (!_isRegistered)
        {
            float registerSqr = registerDistance * registerDistance;

            if (sqrDistance <= registerSqr) Register();
        }
        else
        {
            float unregisterSqr = unregisterDistance * unregisterDistance;

            if (sqrDistance > unregisterSqr) Unregister();
        }
    }


    private void Register()
    {
        _isRegistered = true;

        // player.GetComponent<PlayerController>().RegisterMetal(this);
    }


    private void Unregister()
    {
        _isRegistered = false;

        // 離開範圍一定清掉狀態
        Undisplay();
        Unhighlight();

        // player.GetComponent<PlayerController>().UnregisterMetal(this);
    }


    // =====================================================
    // Outline
    // =====================================================

    public void Display()
    {
        if (_isDisplayed) return;

        _isDisplayed = true;

        SetOutlineEnabled(true);
    }


    public void Undisplay()
    {
        if (!_isDisplayed) return;

        _isDisplayed = false;
        _isHighlighted = false;

        SetOutlineEnabled(false);

        // 順便恢復預設顏色
        SetOutlineColor(new Color(0f, 2f, 4f, 1f));
    }


    public bool CanSelect()
    {
        return _isRegistered;
    }


    public void Highlight()
    {
        if (!_isRegistered) return;

        if (_isHighlighted) return;

        _isHighlighted = true;

        SetOutlineColor( new Color(2f, 0.5f, 0f, 1f));
    }


    public void Unhighlight()
    {
        if (!_isHighlighted) return;

        _isHighlighted = false;

        SetOutlineColor(new Color(0f, 2f, 4f, 1f));
    }


    // =====================================================
    // Push / Pull
    // =====================================================

    public void Push()
    {
        if (!_isRegistered) return;
    }


    public void Pull()
    {
        if (!_isRegistered) return;
    }


    // =====================================================
    // Material Property
    // =====================================================

    private void SetupOutlineRadius()
    {
        var meshFilter = GetComponent<MeshFilter>();

        if (meshFilter == null ||
            meshFilter.sharedMesh == null)
        {
            return;
        }

        radius = meshFilter.sharedMesh.bounds.extents.magnitude;

        _renderer.GetPropertyBlock(_propertyBlock);

        _propertyBlock.SetFloat(
            OutlineObjectRadiusID,
            radius
        );

        _renderer.SetPropertyBlock(_propertyBlock);
    }


    private void SetOutlineEnabled(bool enabled)
    {
        _renderer.GetPropertyBlock(_propertyBlock);

        _propertyBlock.SetFloat(
            OutlineEnabledID,
            enabled ? 1f : 0f
        );

        _renderer.SetPropertyBlock(_propertyBlock);
    }


    private void SetOutlineColor(Color color)
    {
        _renderer.GetPropertyBlock(_propertyBlock);

        _propertyBlock.SetColor(
            OutlineColorID,
            color
        );

        _renderer.SetPropertyBlock(_propertyBlock);
    }


#if UNITY_EDITOR
    private void OnValidate()
    {
        // 保證取消範圍一定比註冊範圍大
        unregisterDistance =
            Mathf.Max(
                unregisterDistance,
                registerDistance
            );
    }
#endif
}