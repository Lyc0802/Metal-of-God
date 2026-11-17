using UnityEngine;

/// <summary>Owns player camera framing, orbit, collision and speed field of view.</summary>
[DisallowMultipleComponent]
public sealed class CameraController : MonoBehaviour
{
    public Camera ViewCamera;
    public float LookSensitivity = .12f;
    [Min(1)]
    public float CameraDistance = 8;
    public float CameraPivotHeight = 1;
    [Tooltip("Free camera offset relative to the view: left/right, height, forward/back.")]
    public Vector3 ShoulderCameraOffset = new Vector3(-.65f, 1.5f, -5f);
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
    private float yaw, pitch;
    private bool initialized;

    public void Initialize()
    {
        if (initialized) return;
        initialized = true;
        cameraBody = GetComponent<Rigidbody>();
        if (!ViewCamera) ViewCamera = Camera.main;
        CaptureView();
        CacheFovCamera();
        cameraFocusOffset = Vector3.up * ShoulderCameraOffset.y
            + Quaternion.Euler(0, yaw, 0) * Vector3.right * ShoulderCameraOffset.x;
        cameraOrbitDistance = Mathf.Max(0, -ShoulderCameraOffset.z);
        ResetFollowState();
    }

    public void ResetFollowState()
    {
        cameraUpdateFrame = -1;
        cameraCollisionInitialized = false;
        ResetCameraFollow();
    }

    public void CaptureView()
    {
        if (!ViewCamera) return;
        var angles = ViewCamera.transform.eulerAngles;
        yaw = angles.y;
        pitch = Mathf.DeltaAngle(0, angles.x);
        lockYawVelocity = lockPitchVelocity = 0;
    }

    public void ReadLookInput(Vector2 delta)
    {
        yaw += delta.x * LookSensitivity;
        pitch = Mathf.Clamp(pitch - delta.y * LookSensitivity, -75, 75);
    }

    public void RestoreFieldOfView()
    {
        if (fovCamera) fovCamera.fieldOfView = baseFieldOfView;
        fovVelocity = 0;
    }

    private void Awake() => Initialize();
    private void OnEnable() => ResetFollowState();
    private void OnDisable() => RestoreFieldOfView();

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

    public void UpdateCamera(bool tracking, Vector3 targetPoint)
    {
        if (!isActiveAndEnabled || !ViewCamera) return;
        // Called before selection and again after physics to follow the player.
        // Advance smoothing only once per frame, not once per call.
        bool advanceCamera = cameraUpdateFrame != Time.frameCount;
        if (advanceCamera)
        {
            cameraUpdateFrame = Time.frameCount;
            UpdateSpeedFov();
            AdvanceCameraFollow();
            var focus = tracking
                ? Vector3.up * CameraPivotHeight
                : Vector3.up * ShoulderCameraOffset.y
                    + Quaternion.Euler(0, yaw, 0) * Vector3.right * ShoulderCameraOffset.x;
            float requestedDistance = tracking ? CameraDistance : Mathf.Max(0, -ShoulderCameraOffset.z);
            if (tracking)
            {
                var delta = targetPoint - (transform.position + focus);
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
        var followPosition = tracking ? cameraFollowPosition : transform.position;
        var desiredPosition = followPosition + cameraFocusOffset + rotation * Vector3.back * cameraOrbitDistance;
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

}
