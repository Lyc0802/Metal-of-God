using UnityEngine;

public sealed class ObstacleSpawner : MonoBehaviour
{
    public ObstacleLifecycle Prefab;
    public Transform Player;
    public int ObstacleCount = 110;
    public Vector2 ArenaHalfSize = new Vector2(23, 23);
    private int pending;
    private float retryAt;

    private void Start()
    {
        pending = Mathf.Max(0, ObstacleCount - GetComponentsInChildren<ObstacleLifecycle>().Length);
    }

    private void Update()
    {
        if (pending == 0 || Time.time < retryAt) return;
        // Bounded work; keep the replacement debt if the arena is temporarily full.
        for (int i = 0; i < 4 && pending > 0; i++)
        {
            if (!TrySpawn()) break;
            pending--;
        }
        retryAt = Time.time + .1f;
    }

    public void Replace(ObstacleLifecycle obstacle)
    {
        if (!obstacle || !obstacle.gameObject.activeSelf) return;
        obstacle.gameObject.SetActive(false);
        Destroy(obstacle.gameObject);
        pending++;
    }

    private bool TrySpawn()
    {
        Physics.SyncTransforms();
        for (int attempt = 0; attempt < 64; attempt++)
        {
            var position = transform.position + new Vector3(
                Random.Range(-ArenaHalfSize.x, ArenaHalfSize.x),
                Prefab.transform.localScale.y * .5f + .06f,
                Random.Range(-ArenaHalfSize.y, ArenaHalfSize.y));
            if (Player && Vector3.ProjectOnPlane(position - Player.position, Vector3.up).sqrMagnitude < 9f)
                continue;
            var rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            // Include walls, player and all other colliders, not just other obstacles.
            var half = Prefab.transform.localScale * .5f + Vector3.one * .025f;
            if (Physics.CheckBox(position, half, rotation, ~0, QueryTriggerInteraction.Ignore)) continue;
            Create(position, rotation);
            Physics.SyncTransforms();
            return true;
        }
        return false;
    }

    private ObstacleLifecycle Create(Vector3 position, Quaternion rotation)
    {
        var instance = Instantiate(Prefab, transform);
        instance.transform.SetPositionAndRotation(position, rotation);
        instance.Owner = this;
        return instance;
    }

}
