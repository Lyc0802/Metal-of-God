using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// 協調玩家輸入與目標選取；鏡頭、移動及攻擊由各自元件執行。
/// </summary>
[RequireComponent(typeof(PlayerMovement), typeof(PlayerMetalInventory), typeof(PlayerMetalSelector))]
[RequireComponent(typeof(PlayerMetalManipulator), typeof(MetalTargetIndicator))]
[RequireComponent(typeof(PlayerHealth), typeof(PlayerTargetLockOn), typeof(PlayerCombat))]
[RequireComponent(typeof(PlayerEnemyManipulator), typeof(CameraController))]
public class PlayerController : MonoBehaviour
{
    public InputActionAsset InputActions;
    private CameraController cameraController;
    // Existing targeting systems can keep reading the player's view camera.
    public Camera ViewCamera
    {
        get
        {
            if (!cameraController) cameraController = GetComponent<CameraController>();
            return cameraController ? cameraController.ViewCamera : null;
        }
        set
        {
            if (!cameraController) cameraController = GetComponent<CameraController>();
            if (cameraController) cameraController.ViewCamera = value;
        }
    }
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
    private PlayerCharacterAnimation characterAnimation;
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
    private bool rightHeldLastFrame, abilityHeld;
    private const float AbilityTimeScale = .25f;
    private bool abilitySlowMotionActive;
    private float timeScaleBeforeAbility, fixedDeltaBeforeAbility;
    private EventSystem pointerEventSystem;
    private PointerEventData pointerEvent;
    private readonly List<RaycastResult> pointerHits = new List<RaycastResult>();
    private bool lockCameraSuppressed, focusInterrupted;
    public bool IsSelectingMetal { get; private set; }
    public bool IsSelectingTarget => IsSelectingMetal;
    public bool IsUsingAbility => isActiveAndEnabled && abilityHeld;

    // Metal selection wheel
    private MetalBurningWheel metalBurningWheel;

    private void Awake()
    {
        cameraController = GetComponent<CameraController>();
        cameraController.Initialize();
        movement = GetComponent<PlayerMovement>();
        characterAnimation = GetComponent<PlayerCharacterAnimation>();
        selector = GetComponent<PlayerMetalSelector>();
        manipulator = GetComponent<PlayerMetalManipulator>();
        inventory = GetComponent<PlayerMetalInventory>();
        indicator = GetComponent<MetalTargetIndicator>();
        enemyManipulator = GetComponent<PlayerEnemyManipulator>();
        enemyLock = GetComponent<PlayerTargetLockOn>();
        combat = GetComponent<PlayerCombat>();
        health = GetComponent<PlayerHealth>();
        health.Died += HandleDeath;
        if (health.IsDead)
        {
            HandleDeath();
            return;
        }
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

    // Health owns the death decision; the controller coordinates stopping player actions.
    // Subscribe until destruction so death also cancels actions while input is disabled.
    private void HandleDeath()
    {
        if (combat)
        {
            combat.CancelAttack();
            combat.enabled = false;
        }
        if (characterAnimation) characterAnimation.StopMetalAbility();
        if (manipulator) manipulator.Release();
        if (enemyManipulator) enemyManipulator.Release();
        if (enemyLock)
        {
            enemyLock.Unlock();
            enemyLock.enabled = false;
        }
        if (movement) movement.enabled = false;
        SetAbilitySlowMotion(false);
        abilityHeld = false;
        IsSelectingMetal = false;
        enabled = false;
    }

    private void SetCursorCaptured(bool captured)
    {
        Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !captured;
    }

    private void OnEnable()
    {
        if (health && health.IsDead)
        {
            HandleDeath();
            return;
        }
        cameraController.ResetFollowState();
        actions?.Enable();
        scroll?.Enable();
        SetCursorCaptured(true);
        if (health)
        {
            health.Damaged += OnDamaged;
        }
    }

    /// <summary>被擊中時中斷專注：結束金屬瞄準 / 推拉，必須放開右鍵再重新按下才能再次瞄準。</summary>
    private void OnDamaged(float damage)
    {
        if (abilityHeld || IsSelectingMetal || (manipulator && manipulator.IsLocked))
        {
            focusInterrupted = true;
        }

        IsSelectingMetal = false;
        if (selector)
        {
            selector.ClearSelection();
        }

        if (manipulator)
        {
            manipulator.Release();
        }
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

        // LMB and RMB don't aim and attack while the selection wheel is open
        if (metalBurningWheel == null) metalBurningWheel = FindAnyObjectByType<MetalBurningWheel>();
        if (metalBurningWheel != null && metalBurningWheel.IsOpen) gameplayMouse = false;

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
        if (focusInterrupted)
        {
            // 專注被打斷：在放開右鍵之前，一律視為沒有按住。
            if (!held)
            {
                focusInterrupted = false;
            }

            held = abilityHeld = false;
        }
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
        // 攜帶中（按住右鍵、拉來的輕金屬已貼在身上）時，滾輪向前（推）會把它射出去。
        manipulator.AimCamera = ViewCamera;
        float wheel = IsSelectingMetal || manipulator.IsCarrying ? scroll.ReadValue<float>() : 0;
        if (enemyManipulator.HandleInput(held, wheel, selector.CurrentEnemy, selector.EnemyHit.point)
            && characterAnimation)
            characterAnimation.PlayMetalAbility(wheel > 0 ? MetalType.Zinc : MetalType.Brass);
        bool wasManipulating = manipulator.IsLocked;
        int launchesBefore = manipulator.LaunchCount;
        manipulator.HandleInput(held, wheel, selector.Current);
        if (manipulator.LaunchCount != launchesBefore && characterAnimation)
            characterAnimation.PlayMetalAbility(MetalType.Steel);
        if (!wasManipulating && manipulator.IsLocked && characterAnimation)
            characterAnimation.PlayMetalAbility(manipulator.IsPull ? MetalType.Iron : MetalType.Steel);
        // Successful use consumes this button hold, even if the ability later breaks range.
        if (manipulator.IsLocked || enemyManipulator.HasActivatedThisHold)
        {
            IsSelectingMetal = false;
            selector.ClearSelection();
        }

        if (gameplayMouse && attackAction.WasPressedThisFrame())
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
        cameraController.CaptureView();
    }

    private void UpdateLookInput()
    {
        // Metal selection wheel check, the camera musn't rotate
        if (metalBurningWheel != null && metalBurningWheel.IsOpen) return;

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            var delta = look.ReadValue<Vector2>();
            if (enemyLock.IsLocked && !lockCameraSuppressed && !abilityHeld && !manipulator.IsLocked)
            {
                enemyLock.HandleSwitchInput(delta, look.activeControl != null && look.activeControl.device is Gamepad);
            }
            else
            {
                cameraController.ReadLookInput(delta);
            }
        }
    }

    private void UpdateMetalOutlines()
    {
        bool show = (display.IsPressed() || IsSelectingMetal)
            && (inventory.CanUse(MetalType.Iron)
            || inventory.CanUse(MetalType.Steel));
        // 攜帶中：不再顯示所有金屬的外框（藍線也隱藏），只標示出攜帶的那個物品。
        MetalDisplayer carried = manipulator.IsCarrying ? manipulator.Target.Metal : null;
        foreach (var metal in MetalDisplayer.ActiveMetals)
        {
            metal.Unhighlight();
            if (carried)
            {
                if (metal == carried)
                {
                    metal.Display();
                    metal.Highlight();
                }
                else
                {
                    metal.Undisplay();
                }
            }
            else if (show)
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
        // Slow motion only while choosing a target; it ends once an ability is activated this hold.
        // Apply after input updates so closing the wheel cannot overwrite ability slow motion.
        // 已經鎖定目標（推 / 拉進行中）或已發動過敵人金屬能力，就算右鍵還按著也不再慢動作。
        bool aimingOnly = IsSelectingMetal && !manipulator.IsLocked && !enemyManipulator.HasActivatedThisHold;
        SetAbilitySlowMotion(aimingOnly && ViewCamera && !(health && health.IsDead)
            && !(metalBurningWheel && metalBurningWheel.IsOpen));
        if (ViewCamera)
        {
            UpdateCamera();
        }

        indicator.Show(IsSelectingMetal ? selector.Current : default);
    }

    private void SetAbilitySlowMotion(bool active)
    {
        if (active)
        {
            if (!abilitySlowMotionActive)
            {
                timeScaleBeforeAbility = Time.timeScale;
                fixedDeltaBeforeAbility = Time.fixedDeltaTime;
                abilitySlowMotionActive = true;
            }

            Time.timeScale = AbilityTimeScale;
            Time.fixedDeltaTime = fixedDeltaBeforeAbility * AbilityTimeScale
                / Mathf.Max(.0001f, timeScaleBeforeAbility);
        }
        else if (abilitySlowMotionActive)
        {
            // An open wheel owns its own time settings; leave those intact.
            if (!(metalBurningWheel && metalBurningWheel.IsOpen))
            {
                Time.timeScale = timeScaleBeforeAbility;
                Time.fixedDeltaTime = fixedDeltaBeforeAbility;
            }
            abilitySlowMotionActive = false;
        }
    }

    private void UpdateCamera()
    {
        bool tracking = enemyLock && enemyLock.IsLocked && !lockCameraSuppressed && !abilityHeld
            && !manipulator.IsLocked;
        cameraController.UpdateCamera(tracking, tracking ? enemyLock.Target.TargetPoint : Vector3.zero);
    }

    private void OnDisable()
    {
        if (health)
        {
            health.Damaged -= OnDamaged;
        }

        focusInterrupted = false;
        SetAbilitySlowMotion(false);
        if (cameraController) cameraController.RestoreFieldOfView();
        if (characterAnimation) characterAnimation.StopMetalAbility();
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
            SetAbilitySlowMotion(false);
            if (characterAnimation) characterAnimation.StopMetalAbility();
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
        if (health) health.Died -= HandleDeath;
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
