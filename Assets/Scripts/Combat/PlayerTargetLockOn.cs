using UnityEngine;

/// <summary>
/// 管理敵人鎖定、目標切換及面向；鏡頭跟隨由 PlayerController 處理。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerTargetLockOn : MonoBehaviour
{
    public Camera ViewCamera;
    [Min(1)]
    public float LockOnRange = 25;
    [Range(1, 180)]
    public float LockOnAngle = 65;
    public float RotationSpeed = 540;
    public float SwitchCooldown = .3f, MouseSwitchThreshold = 35, StickSwitchThreshold = .65f;
    private bool switchArmed = true;
    private float nextSwitch;
    private bool CanLock(Enemy enemy, out Vector3 viewport)
    {
        viewport = Vector3.zero;
        if (!ViewCamera || !enemy || !enemy.isActiveAndEnabled || enemy.IsDead
            || Vector3.Distance(
            body.worldCenterOfMass,
            enemy.TargetPoint) > LockOnRange)
        {
            return false;
        }

        var delta = enemy.TargetPoint - ViewCamera.transform.position;
        if (Vector3.Angle(ViewCamera.transform.forward, delta) > LockOnAngle)
        {
            return false;
        }

        viewport = ViewCamera.WorldToViewportPoint(enemy.TargetPoint);
        return viewport.z > 0 && viewport.x >= 0 && viewport.x <= 1 && viewport.y >= 0 && viewport.y <= 1
            && CombatSight.Clear(
            ViewCamera.transform.position,
            enemy.TargetPoint,
            transform,
            enemy.transform);
    }

    private void SetTarget(Enemy enemy)
    {
        if (Target)
        {
            Target.Died -= OnTargetDeath;
        }

        Target = enemy;
        if (Target)
        {
            Target.Died += OnTargetDeath;
        }
    }

    public void HandleSwitchInput(Vector2 input, bool gamepad)
    {
        float threshold = gamepad ? StickSwitchThreshold : MouseSwitchThreshold;
        if (input.magnitude < threshold * .3f)
        {
            switchArmed = true;
        }

        if (!IsLocked || !switchArmed || Time.time < nextSwitch || input.magnitude < threshold)
        {
            return;
        }

        switchArmed = false;
        nextSwitch = Time.time + SwitchCooldown;
        SwitchTarget(input);
    }

    public void SwitchTarget(Vector2 input)
    {
        if (!IsLocked || input.sqrMagnitude < .001f)
        {
            return;
        }

        var current = ViewCamera.WorldToViewportPoint(Target.TargetPoint);
        Enemy best = null;
        float score = float.PositiveInfinity;
        foreach (var enemy in Enemy.ActiveEnemies)
        {
            if (enemy == Target || !CanLock(enemy, out var v))
            {
                continue;
            }

            var offset = new Vector2(v.x - current.x, v.y - current.y);
            if (offset.sqrMagnitude < .0001f)
            {
                continue;
            }

            float alignment = Vector2.Dot(input.normalized, offset.normalized);
            if (alignment < .3f)
            {
                continue;
            }

            float rank = offset.magnitude + (1 - alignment) * .4f;
            if (rank < score)
            {
                score = rank;
                best = enemy;
            }
        }

        if (best)
        {
            SetTarget(best);
        }
    }

    public Enemy Target { get; private set; }
    public bool IsLocked => Target && !Target.IsDead;

    private Rigidbody body;
    private PlayerHealth health;
    private PlayerMetalManipulator metal;
    private GameObject marker;
    private LineRenderer ring;
    private Material material;
    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        health = GetComponent<PlayerHealth>();
        metal = GetComponent<PlayerMetalManipulator>();
        if (!ViewCamera)
        {
            ViewCamera = Camera.main;
        }

        marker = new GameObject("Enemy Lock-On Indicator");
        ring = marker.AddComponent<LineRenderer>();
        ring.loop = true;
        ring.positionCount = 32;
        ring.useWorldSpace = false;
        ring.widthMultiplier = .035f;
        material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.SetColor("_BaseColor", Color.yellow);
        ring.sharedMaterial = material;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        for (int i = 0; i < 32; i++)
        {
            float a = i * Mathf.PI * 2 / 32;
            ring.SetPosition(i, new Vector3(Mathf.Cos(a) * .22f, Mathf.Sin(a) * .22f, 0));
        }

        marker.SetActive(false);
    }

    public void ToggleLock()
    {
        if (Target)
        {
            Unlock();
            return;
        }

        if (!ViewCamera || (health && health.IsDead))
        {
            return;
        }

        Enemy best = null;
        float score = float.PositiveInfinity;
        foreach (var enemy in Enemy.ActiveEnemies)
        {
            if (!CanLock(enemy, out var v))
            {
                continue;
            }

            var delta = enemy.TargetPoint - ViewCamera.transform.position;
            float rank = new Vector2(v.x - .5f, v.y - .5f).sqrMagnitude + delta.magnitude * .0001f;
            if (rank < score)
            {
                score = rank;
                best = enemy;
            }
        }

        if (best)
        {
            SetTarget(best);
            switchArmed = true;
        }
    }

    private void OnTargetDeath(Enemy enemy)
    {
        Unlock();
    }

    public void Unlock()
    {
        SetTarget(null);
        switchArmed = true;
        if (marker)
        {
            marker.SetActive(false);
        }
    }

    public void ValidateTarget()
    {
        if (Target
            && (Target.IsDead || !Target.isActiveAndEnabled
            || Vector3.Distance(
            body.worldCenterOfMass,
            Target.TargetPoint) > LockOnRange))
        {
            Unlock();
        }
    }

    private void Update()
    {
        ValidateTarget();
    }

    public void FaceTarget()
    {
        if (!IsLocked
            || (metal && metal.IsLocked && metal.Target.Valid
            && metal.Weight < metal.Target.Metal.weight))
        {
            return;
        }

        var direction = Target.TargetPoint - body.worldCenterOfMass;
        direction.y = 0;
        if (direction.sqrMagnitude > .001f)
        {
            body.MoveRotation(Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(direction), RotationSpeed * Time.fixedDeltaTime));
        }
    }

    private void FixedUpdate()
    {
        FaceTarget();
    }

    private void LateUpdate()
    {
        if (!marker)
        {
            return;
        }

        marker.SetActive(IsLocked);
        if (IsLocked && ViewCamera)
        {
            marker.transform.SetPositionAndRotation(
                Target.TargetPoint - ViewCamera.transform.forward * .55f,
                ViewCamera.transform.rotation);
        }
    }

    private void OnDisable()
    {
        Unlock();
    }

    private void OnDestroy()
    {
        if (marker)
        {
            Destroy(marker);
        }

        if (material)
        {
            Destroy(material);
        }
    }
}
