using UnityEngine;

/// <summary>
/// 埋伏型敵人：平時蹲伏在暗處完全不動（關閉 TestEnemy AI），玩家靠近或先被打到時，
/// 燃燒 Pewter 爆發——移動與傷害大幅提升，並且立刻鎖定玩家追擊。
/// </summary>
[RequireComponent(typeof(Enemy), typeof(TestEnemy))]
public class EnemyAmbush : MonoBehaviour
{
    public PlayerHealth Player;
    [Tooltip("玩家進到這個水平距離就發動伏擊。")]
    [Min(1)] public float TriggerRange = 7;
    [Tooltip("發動後的移動速度倍率（Pewter 加速）。")]
    [Min(1)] public float WakeSpeedMultiplier = 1.8f;
    [Tooltip("發動後的傷害倍率。")]
    [Min(1)] public float WakeDamageMultiplier = 1.4f;
    [Min(1)] public float WakeDetectionRange = 40;
    [Tooltip("蹲伏時身體的縮放（只縮放高度），發動時恢復。")]
    [Range(.4f, 1)] public float LurkHeight = .72f;
    public Transform Model;
    [Tooltip("蹲伏期間暫停這個 Animator（定格在待機姿勢），發動時才開始動。")]
    public Animator ModelAnimator;
    [Tooltip("眼睛等發光部位。蹲伏時微弱、發動時明亮。")]
    public Renderer[] GlowRenderers;
    public Color LurkGlow = new Color(.25f, .35f, .5f);
    public Color AwakeGlow = new Color(2.2f, 2.6f, 3.4f);

    public bool IsAwake { get; private set; }

    private TestEnemy ai;
    private Enemy enemy;
    private float lastHealth;
    private Vector3 modelScale;
    private MaterialPropertyBlock block;

    private void Awake()
    {
        ai = GetComponent<TestEnemy>();
        enemy = GetComponent<Enemy>();
        lastHealth = enemy.CurrentHealth;
        block = new MaterialPropertyBlock();
        if (Model)
        {
            modelScale = Model.localScale;
            Model.localScale = new Vector3(modelScale.x, modelScale.y * LurkHeight, modelScale.z);
        }

        if (ModelAnimator)
        {
            ModelAnimator.Update(0f); // 先把待機姿勢算出來，再定格。
            ModelAnimator.speed = 0;
        }

        ai.enabled = false;
        SetGlow(LurkGlow);
    }

    private void Update()
    {
        if (IsAwake)
        {
            return;
        }

        if (!Player)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p)
            {
                Player = p.GetComponent<PlayerHealth>();
            }
        }

        bool hurt = enemy.CurrentHealth < lastHealth - .01f;
        lastHealth = enemy.CurrentHealth;
        bool near = false;
        if (Player && !Player.IsDead)
        {
            Vector3 d = Player.transform.position - transform.position;
            d.y = 0;
            near = d.magnitude <= TriggerRange;
        }

        if (!enemy.IsDead && (near || hurt))
        {
            Wake();
        }
    }

    public void Wake()
    {
        if (IsAwake)
        {
            return;
        }

        IsAwake = true;
        ai.ChaseSpeed *= WakeSpeedMultiplier;
        // Pewter 加速後是快速移動的近戰：邊追邊揮擊，玩家衝刺也甩不掉。
        ai.AttackWhileMoving = true;
        ai.AttackDamage *= WakeDamageMultiplier;
        ai.DetectionRange = Mathf.Max(ai.DetectionRange, WakeDetectionRange);
        if (Model)
        {
            Model.localScale = modelScale;
        }

        if (ModelAnimator)
        {
            ModelAnimator.speed = 1;
        }

        SetGlow(AwakeGlow);
        ai.enabled = true;
    }

    private void SetGlow(Color color)
    {
        if (GlowRenderers == null)
        {
            return;
        }

        foreach (var r in GlowRenderers)
        {
            if (!r)
            {
                continue;
            }

            r.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", color);
            r.SetPropertyBlock(block);
        }
    }
}
