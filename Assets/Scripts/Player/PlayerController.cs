using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// 協調玩家輸入、目標選取與鏡頭；實際移動及攻擊由各自元件執行。
/// </summary>
[RequireComponent(typeof(PlayerMovement), typeof(PlayerMetalInventory), typeof(PlayerMetalSelector))]
[RequireComponent(typeof(PlayerMetalManipulator), typeof(MetalTargetIndicator))]
[RequireComponent(typeof(PlayerHealth), typeof(PlayerTargetLockOn), typeof(PlayerCombat))]
[RequireComponent(typeof(PlayerEnemyManipulator))]
public class PlayerController : MonoBehaviour
{
    public Camera ViewCamera;
    public InputActionAsset InputActions;
    public float LookSensitivity = .12f;
    [Min(1)]
    public float CameraDistance = 8;
    public float CameraPivotHeight = 1;
    [Header("Speed Field of View")]
    public bool EnableSpeedFov = true;
    [Min(0)]
    public float FovStartSpeed = 6;
    [Min(.1f)]
    public float FovFullSpeed = 14;
    [Range(0, 30)]
    public float MaximumSpeedFovBoost = 18;
    [Min(.01f)]
    public float FovBlendTime = .3f;
    private Rigidbody cameraBody;
    private Camera fovCamera;
    private float baseFieldOfView, fovVelocity;
    public float TargetFieldOfView
    {
        get
        {
            float baseline = fovCamera ? baseFieldOfView : ViewCamera ? ViewCamera.fieldOfView : 60;
            float speed = cameraBody ? cameraBody.linearVelocity.magnitude : 0;
            float amount = 0;
            if (EnableSpeedFov)
            {
                float fullSpeed = Mathf.Max(FovStartSpeed + .01f, FovFullSpeed);
                float speedFraction = Mathf.InverseLerp(FovStartSpeed, fullSpeed, speed);
                amount = Mathf.SmoothStep(0, 1, speedFraction);
            }

            return Mathf.Clamp(baseline + MaximumSpeedFovBoost * amount, 1, 179);
        }
    }

    [Header("Camera Follow Dead Zone")]
    [UnityEngine.Serialization.FormerlySerializedAs("CameraMaxFollowLag")]
    [Min(0)]
    public float CameraFollowRadius = 1.25f;
    [Min(1)]
    public float CameraTeleportDistance = 8;
    private Vector3 cameraFollowPosition, lastCameraPlayerPosition;
    private bool cameraFollowInitialized;
    [Min(.01f)]
    public float LockCameraSmoothTime = .18f;
    public float LockCameraPitch = 15;
    private float lockYawVelocity, lockPitchVelocity;
    [Min(.01f)]
    public float CameraModeBlendTime = .2f;
    private Vector3 cameraFocusOffset, cameraFocusVelocity;
    private float cameraOrbitDistance, cameraDistanceVelocity;
    private int cameraUpdateFrame = -1;
    [Header("Camera Collision")]
    [Min(.01f)]
    public float CameraCollisionRadius = .25f;
    [Min(0)]
    public float CameraCollisionPadding = .08f;
    [Min(.1f)]
    public float CameraCollisionReturnSpeed = 6;
    public LayerMask CameraCollisionMask = ~0;
    private float cameraCollisionDistance;
    private bool cameraCollisionInitialized;
    [Range(0, 1)]
    public float AimDarkness = .18f;
    [Range(.05f, .45f)]
    public float AimClearRadius = .18f;
    [Range(.01f, .3f)]
    public float AimFeather = .1f;
    [Min(1)]
    public float AimObjectPadding = 1.15f;
    [Min(1)]
    public float AimMinimumRadius = 35;
    private Texture2D aimMask;
    private float maskRadius = -1, maskFeather = -1;
    private PlayerMovement movement;
    private PlayerMetalSelector selector;
    private PlayerMetalManipulator manipulator;
    private PlayerMetalInventory inventory;
    private MetalTargetIndicator indicator;
    private InputActionAsset actions;
    private InputAction move, jump, display, ability, look;
    private InputAction scroll;
    private PlayerTargetLockOn enemyLock;
    private PlayerCombat combat;
    private PlayerHealth health;
    private InputAction lockAction, attackAction, sprintAction;
    private PlayerEnemyManipulator enemyManipulator;
    private float yaw, pitch;
    private bool rightHeldLastFrame, abilityHeld;
    private EventSystem pointerEventSystem;
    private PointerEventData pointerEvent;
    private readonly List<RaycastResult> pointerHits = new List<RaycastResult>();
    private bool lockCameraSuppressed;
    public bool IsSelectingMetal { get; private set; }
    public bool IsSelectingTarget => IsSelectingMetal;

    private void Awake()
    {
        cameraBody = GetComponent<Rigidbody>();
        movement = GetComponent<PlayerMovement>();
        selector = GetComponent<PlayerMetalSelector>();
        manipulator = GetComponent<PlayerMetalManipulator>();
        inventory = GetComponent<PlayerMetalInventory>();
        indicator = GetComponent<MetalTargetIndicator>();
        enemyManipulator = GetComponent<PlayerEnemyManipulator>();
        enemyLock = GetComponent<PlayerTargetLockOn>();
        combat = GetComponent<PlayerCombat>();
        health = GetComponent<PlayerHealth>();
        if (!ViewCamera)
        {
            ViewCamera = Camera.main;
        }

        if (ViewCamera)
        {
            yaw = ViewCamera.transform.eulerAngles.y;
            pitch = ViewCamera.transform.eulerAngles.x;
        }

        CacheFovCamera();
        cameraFocusOffset = Vector3.up * CameraPivotHeight;
        cameraOrbitDistance = CameraDistance;
        if (!InputActions)
        {
            Debug.LogError("Assign the project InputSystem_Actions asset to PlayerController.", this);
            enabled = false;
            return;
        }

        actions = Instantiate(InputActions);
        move = actions.FindAction("Player/Move", true);
        jump = actions.FindAction("Player/Jump", true);
        display = actions.FindAction("Player/Select", true);
        ability = actions.FindAction("Player/Ability", true);
        look = actions.FindAction("Player/Look", true);
        lockAction = actions.FindAction("Player/LockOn", true);
        attackAction = actions.FindAction("Player/Attack", true);
        sprintAction = actions.FindAction("Player/Sprint", true);
        scroll = new InputAction("ManipulationScroll", InputActionType.Value, "<Mouse>/scroll/y");
    }

    private void SetCursorCaptured(bool captured)
    {
        Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !captured;
    }

    private void OnEnable()
    {
        cameraUpdateFrame = -1;
        cameraCollisionInitialized = false;
        ResetCameraFollow();
        actions?.Enable();
        scroll?.Enable();
        SetCursorCaptured(true);
    }

    public bool IsPointerOverUI(Vector2 screenPosition)
    {
        var current = EventSystem.current;
        if (!current)
        {
            return false;
        }

        if (pointerEventSystem != current || pointerEvent == null)
        {
            pointerEventSystem = current;
            pointerEvent = new PointerEventData(current);
        }

        pointerEvent.position = screenPosition;
        pointerHits.Clear();
        current.RaycastAll(pointerEvent, pointerHits);
        foreach (var hit in pointerHits)
        {
            if (hit.module is GraphicRaycaster)
            {
                return true;
            }
        }

        return false;
    }

    private void Update()
    {
        bool gameplayMouse = UpdateCursorCapture();
        if (!ViewCamera || (health && health.IsDead))
        {
            return;
        }

        if (sprintAction.WasPressedThisFrame())
        {
            movement.ToggleSprint();
        }

        if (!enemyLock.IsLocked)
        {
            lockCameraSuppressed = false;
        }

        if (lockAction.WasPressedThisFrame())
        {
            enemyLock.ToggleLock();
            lockCameraSuppressed = false;
        }

        bool held = abilityHeld = gameplayMouse && ability.IsPressed();
        if (held && !rightHeldLastFrame)
        {
            BeginAbilityAim();
        }

        UpdateLookInput();
        if (held && combat.IsAttacking)
        {
            combat.CancelAttack();
        }

        if (held && !rightHeldLastFrame)
        {
            IsSelectingMetal = true;
        }

        if (!held)
        {
            IsSelectingMetal = false;
        }

        rightHeldLastFrame = held;
        // Selection must use this frame's camera, before dispatching ability input.
        UpdateCamera();
        if (IsSelectingMetal && !manipulator.IsLocked)
        {
            selector.Select(ViewCamera);
        }
        else
        {
            selector.ClearSelection();
        }

        movement.ReadInput(move.ReadValue<Vector2>(), jump.WasPressedThisFrame(), ViewCamera);
        float wheel = IsSelectingMetal ? scroll.ReadValue<float>() : 0;
        enemyManipulator.HandleInput(held, wheel, selector.CurrentEnemy, selector.EnemyHit.point);
        manipulator.HandleInput(held, wheel, selector.Current);
        // Successful use consumes this button hold, even if the ability later breaks range.
        if (manipulator.IsLocked || enemyManipulator.HasActivatedThisHold)
        {
            IsSelectingMetal = false;
            selector.ClearSelection();
        }

        if (gameplayMouse && !held && attackAction.WasPressedThisFrame())
        {
            combat.TryAttack();
        }

        UpdateMetalOutlines();
    }

    private bool UpdateCursorCapture()
    {
        // Query the current pointer position directly; EventSystem hover state can be a frame old.
        bool pointerOverUI = Cursor.lockState != CursorLockMode.Locked && Mouse.current != null
            && IsPointerOverUI(Mouse.current.position.ReadValue());
        bool recaptured = false;
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            SetCursorCaptured(false);
        }
        else if (!pointerOverUI && Mouse.current != null
            && (Mouse.current.leftButton.wasPressedThisFrame
            || Mouse.current.rightButton.wasPressedThisFrame))
        {
            recaptured = Cursor.lockState != CursorLockMode.Locked;
            SetCursorCaptured(true);
        }

        return Cursor.lockState == CursorLockMode.Locked && !pointerOverUI && !recaptured;
    }

    private void BeginAbilityAim()
    {
        // RMB enters ability aiming with free camera and ordinary player steering.
        enemyLock.Unlock();
        lockCameraSuppressed = true;
        // Continue from the rendered view, rather than switching camera anchors abruptly.
        var angles = ViewCamera.transform.eulerAngles;
        yaw = angles.y;
        pitch = Mathf.DeltaAngle(0, angles.x);
        lockYawVelocity = lockPitchVelocity = 0;
    }

    private void UpdateLookInput()
    {
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            var delta = look.ReadValue<Vector2>();
            if (enemyLock.IsLocked && !lockCameraSuppressed && !abilityHeld && !manipulator.IsLocked)
            {
                enemyLock.HandleSwitchInput(delta, look.activeControl != null && look.activeControl.device is Gamepad);
            }
            else
            {
                yaw += delta.x * LookSensitivity;
                pitch = Mathf.Clamp(pitch - delta.y * LookSensitivity, -75, 75);
            }
        }
    }

    private void UpdateMetalOutlines()
    {
        bool show = (display.IsPressed() || IsSelectingMetal)
            && (inventory.CanUse(MetalType.Iron)
            || inventory.CanUse(MetalType.Steel));
        foreach (var metal in MetalDisplayer.ActiveMetals)
        {
            metal.Unhighlight();
            if (show)
            {
                metal.Display();
            }
            else
            {
                metal.Undisplay();
            }
        }

        if (show && IsSelectingMetal && selector.Current.Valid)
        {
            selector.Current.Metal.Highlight();
        }
    }

    private void LateUpdate()
    {
        if (ViewCamera)
        {
            UpdateCamera();
        }

        indicator.Show(IsSelectingMetal ? selector.Current : default);
    }

    private void ResetCameraFollow()
    {
        cameraFollowPosition = lastCameraPlayerPosition = transform.position;
        cameraFollowInitialized = true;
    }

    private void AdvanceCameraFollow()
    {
        var playerPosition = transform.position;
        if (!cameraFollowInitialized
            || (playerPosition - lastCameraPlayerPosition).sqrMagnitude > CameraTeleportDistance * CameraTeleportDistance)
        {
            ResetCameraFollow();
            cameraCollisionInitialized = false;
            return;
        }

        lastCameraPlayerPosition = playerPosition;
        // Keep this world-space anchor still inside the horizontal dead zone.
        // Move it only enough to keep the player inside the radius; never recenter while idle.
        var offset = cameraFollowPosition - playerPosition;
        offset.y = 0;
        cameraFollowPosition = playerPosition + Vector3.ClampMagnitude(offset, Mathf.Max(0, CameraFollowRadius));
    }

    private void UpdateCamera()
    {
        // Called before selection and again after physics to follow the player.
        // Advance smoothing only once per frame, not once per call.
        bool advanceCamera = cameraUpdateFrame != Time.frameCount;
        if (advanceCamera)
        {
            cameraUpdateFrame = Time.frameCount;
            UpdateSpeedFov();
            AdvanceCameraFollow();
            var focus = Vector3.up * CameraPivotHeight;
            float requestedDistance = CameraDistance;
            bool tracking = enemyLock && enemyLock.IsLocked && !lockCameraSuppressed && !abilityHeld
                && !manipulator.IsLocked;
            if (tracking)
            {
                var delta = enemyLock.Target.TargetPoint - (transform.position + focus);
                if (delta.sqrMagnitude > .01f)
                {
                    float targetYaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                    float elevation = Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg;
                    yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref lockYawVelocity, LockCameraSmoothTime);
                    pitch = Mathf.SmoothDampAngle(
                        pitch,
                        Mathf.Clamp(LockCameraPitch - elevation, -35, 60),
                        ref lockPitchVelocity,
                        LockCameraSmoothTime);
                    focus += delta * .3f;
                    requestedDistance = Mathf.Max(CameraDistance, delta.magnitude * .7f);
                }
            }
            else
            {
                lockYawVelocity = lockPitchVelocity = 0;
            }

            cameraFocusOffset = Vector3.SmoothDamp(
                cameraFocusOffset,
                focus,
                ref cameraFocusVelocity,
                CameraModeBlendTime);
            cameraOrbitDistance = Mathf.SmoothDamp(
                cameraOrbitDistance,
                requestedDistance,
                ref cameraDistanceVelocity,
                CameraModeBlendTime);
        }

        var rotation = Quaternion.Euler(pitch, yaw, 0);
        var desiredPosition = cameraFollowPosition + cameraFocusOffset + rotation * Vector3.back * cameraOrbitDistance;
        // Framing may look between the two characters, but collision must start at the player.
        // A focus point inside a pillar must never become a physical camera origin.
        var anchor = transform.position + Vector3.up * CameraPivotHeight;
        var boom = desiredPosition - anchor;
        float length = boom.magnitude;
        if (length < .001f)
        {
            ViewCamera.transform.SetPositionAndRotation(anchor, rotation);
            return;
        }

        var direction = boom / length;
        float safeDistance = length;
        foreach (var hit in Physics.SphereCastAll(
            anchor,
            CameraCollisionRadius,
            direction,
            length,
            CameraCollisionMask,
            QueryTriggerInteraction.Ignore))
        {
            if (IgnoreCameraCollider(hit.collider) || hit.distance <= .0001f)
            {
                continue;
            }

            safeDistance = Mathf.Min(safeDistance, Mathf.Max(0, hit.distance - CameraCollisionPadding));
        }

        // Sphere casts can report an ambiguous zero hit when their initial sphere touches geometry.
        // Ignore that false collapse, but retain a center-ray check for real walls along the boom.
        foreach (var hit in Physics.RaycastAll(
            anchor,
            direction,
            length,
            CameraCollisionMask,
            QueryTriggerInteraction.Ignore))
        {
            if (IgnoreCameraCollider(hit.collider) || hit.distance <= .0001f)
            {
                continue;
            }

            safeDistance = Mathf.Min(safeDistance, Mathf.Max(0, hit.distance - CameraCollisionPadding));
        }

        if (!cameraCollisionInitialized || safeDistance < cameraCollisionDistance)
        {
            cameraCollisionDistance = safeDistance;
            cameraCollisionInitialized = true;
        }
        else if (advanceCamera)
        {
            cameraCollisionDistance = Mathf.MoveTowards(
                cameraCollisionDistance,
                safeDistance,
                CameraCollisionReturnSpeed * Time.deltaTime);
        }

        // Retract immediately to avoid clipping; recover outward once per frame without popping.
        cameraCollisionDistance = Mathf.Min(cameraCollisionDistance, safeDistance);
        ViewCamera.transform.SetPositionAndRotation(anchor + direction * cameraCollisionDistance, rotation);
    }

    private bool IgnoreCameraCollider(Collider collider)
    {
        return !collider || collider.transform.IsChildOf(transform) || collider.GetComponentInParent<Enemy>();
    }

    private void CacheFovCamera()
    {
        if (fovCamera == ViewCamera)
        {
            return;
        }

        if (fovCamera)
        {
            fovCamera.fieldOfView = baseFieldOfView;
        }

        fovCamera = ViewCamera;
        fovVelocity = 0;
        if (fovCamera)
        {
            baseFieldOfView = fovCamera.fieldOfView;
        }
    }

    private void UpdateSpeedFov()
    {
        CacheFovCamera();
        if (!ViewCamera || ViewCamera.orthographic)
        {
            return;
        }

        ViewCamera.fieldOfView = Mathf.SmoothDamp(
            ViewCamera.fieldOfView,
            TargetFieldOfView,
            ref fovVelocity,
            Mathf.Max(.01f, FovBlendTime),
            Mathf.Infinity,
            Time.deltaTime);
    }

    private void OnDisable()
    {
        if (fovCamera)
        {
            fovCamera.fieldOfView = baseFieldOfView;
        }

        fovVelocity = 0;
        abilityHeld = false;
        IsSelectingMetal = false;
        rightHeldLastFrame = true;
        SetCursorCaptured(false);
        actions?.Disable();
        scroll?.Disable();
        if (manipulator)
        {
            manipulator.Release();
        }

        if (enemyManipulator)
        {
            enemyManipulator.Release();
        }

        foreach (var metal in MetalDisplayer.ActiveMetals)
        {
            metal.Undisplay();
        }
    }

    private void OnApplicationFocus(bool focus)
    {
        SetCursorCaptured(focus && isActiveAndEnabled);
        if (!focus)
        {
            abilityHeld = false;
            IsSelectingMetal = false;
            rightHeldLastFrame = true;
            if (manipulator)
            {
                manipulator.Release();
            }

            if (enemyManipulator)
            {
                enemyManipulator.Release();
            }
        }
    }

    private void OnGUI()
    {
        if (!IsSelectingMetal || Event.current.type != EventType.Repaint)
        {
            return;
        }

        if (!aimMask || maskRadius != AimClearRadius || maskFeather != AimFeather)
        {
            if (aimMask)
            {
                Destroy(aimMask);
            }

            aimMask = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            aimMask.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[256 * 256];
            for (int y = 0; y < 256; y++)
            {
                for (int x = 0; x < 256; x++)
                {
                    float r = Vector2.Distance(new Vector2((x + .5f) / 256, (y + .5f) / 256), new Vector2(.5f, .5f));
                    pixels[y * 256 + x] = new Color(
                        0,
                        0,
                        0,
                        Mathf.SmoothStep(0, 1, Mathf.InverseLerp(AimClearRadius, AimClearRadius + AimFeather, r)));
                }
            }

            aimMask.SetPixels(pixels);
            aimMask.Apply();
            maskRadius = AimClearRadius;
            maskFeather = AimFeather;
        }

        var previous = GUI.color;
        GUI.color = new Color(1, 1, 1, AimDarkness);
        bool visible = false;
        Vector2 center = Vector2.zero;
        float radius = AimMinimumRadius;
        if (ViewCamera && selector.TryGetFocusBounds(out var bounds))
        {
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = new Vector2(
                float.NegativeInfinity,
                float.NegativeInfinity);
            visible = true;
            for (int i = 0; i < 8; i++)
            {
                var corner = bounds.center + Vector3.Scale(
                    bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var screen = ViewCamera.WorldToScreenPoint(corner);
                if (screen.z <= ViewCamera.nearClipPlane)
                {
                    visible = false;
                    break;
                }

                min = Vector2.Min(min, new Vector2(screen.x, screen.y));
                max = Vector2.Max(max, new Vector2(screen.x, screen.y));
            }

            center = (min + max) * .5f;
            radius = Mathf.Max(AimMinimumRadius, (max - min).magnitude * .5f * AimObjectPadding);
        }

        if (visible)
        {
            // The same pixel scale in X and Y makes a true circle on any screen aspect ratio.
            float span = radius / Mathf.Max(.01f, AimClearRadius);
            GUI.DrawTextureWithTexCoords(
                new Rect(0, 0, Screen.width, Screen.height),
                aimMask,
                new Rect(.5f - center.x / span, .5f - center.y / span, Screen.width / span, Screen.height / span));
        }
        else
        {
            GUI.color = new Color(0, 0, 0, AimDarkness);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        }

        GUI.color = previous;
    }

    private void OnDestroy()
    {
        if (aimMask)
        {
            Destroy(aimMask);
        }

        scroll?.Dispose();
        if (actions)
        {
            Destroy(actions);
        }
    }
}
