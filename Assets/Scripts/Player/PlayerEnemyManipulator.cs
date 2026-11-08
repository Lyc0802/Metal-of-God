using UnityEngine;

/// <summary>
/// 把玩家選取敵人時的輸入轉成 Zinc 或 Brass Action；反應由敵人決定。
/// </summary>
[RequireComponent(typeof(PlayerMetalInventory), typeof(PlayerMetalSelector))]
public class PlayerEnemyManipulator : MonoBehaviour
{
    public bool HasActivatedThisHold { get; private set; }
    public Enemy Target { get; private set; }

    private PlayerMetalInventory inventory;
    private PlayerMetalSelector selector;
    private void Awake()
    {
        inventory = GetComponent<PlayerMetalInventory>();
        selector = GetComponent<PlayerMetalSelector>();
    }

    public bool HandleInput(bool held, float scroll, Enemy selected, Vector3 hitPoint)
    {
        if (!held)
        {
            Release();
            return false;
        }

        if (HasActivatedThisHold || scroll == 0 || !selected || selected.IsDead || !selected.isActiveAndEnabled
            || !selector.IsPointInRange(hitPoint))
        {
            return false;
        }

        bool zinc = scroll > 0;
        if (!inventory.CanUse(zinc ? MetalType.Zinc : MetalType.Brass))
        {
            return false;
        }

        if (!selected.TryExecuteAction(zinc ? EnemyActionType.Zinc : EnemyActionType.Brass))
        {
            return false;
        }

        Target = selected;
        HasActivatedThisHold = true;
        CombatFeedbackEvents.PublishEmotionalInfluence(selected.TargetPoint,
            zinc ? EnemyActionType.Zinc : EnemyActionType.Brass);
        return true;
    }

    // Releasing the input never cancels the timed effect owned by the enemy AI.
    public void Release()
    {
        Target = null;
        HasActivatedThisHold = false;
    }

    private void OnDisable()
    {
        Release();
    }
}
