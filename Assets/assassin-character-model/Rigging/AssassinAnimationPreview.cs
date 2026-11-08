using UnityEngine;
using UnityEngine.InputSystem;

// Preview-only controls. Does not replace or modify the gameplay Player controller.
/// <summary>
/// 既有預覽場景的動畫操作元件；正式遊戲使用 PlayerCharacterAnimation。
/// </summary>
public sealed class AssassinAnimationPreview : MonoBehaviour
{
    public Animator Character;
    [Range(0, 1)]
    public float Speed;
    public Vector2 Direction = Vector2.up;
    public bool Run;
    public bool RepeatAttack;
    [Min(.8f)]
    public float AttackInterval = 1.6f;
    public bool ShowControls = true;
    private float nextAttack, attackStarted;
    private bool attacking;
    private float attackDuration = 1.1f;
    private void StartAttack()
    {
        attacking = true;
        foreach(var clip in Character.runtimeAnimatorController.animationClips)
            if(clip.name=="SwordAttack") { attackDuration=clip.length; break; }
        attackStarted = Time.time;
        Character.SetBool("IsAttacking", true);
        Character.SetFloat("AttackProgress", 0);
        Character.CrossFadeInFixedTime("Sword Attack",.065f,0,0);
    }

    private void OnEnable()
    {
        nextAttack = Time.time + .4f;
        if(Character) Character.Play("Locomotion",0,0);
    }

    private void OnGUI()
    {
        if (!ShowControls || !Character)
        {
            return;
        }

        GUILayout.BeginArea(new Rect(18, 18, 370, 130), GUI.skin.box);
        GUILayout.Label("ASSASSIN - ANIMATION PREVIEW");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("1  Idle"))
        {
            Speed = 0;
            RepeatAttack = false;
        }

        if (GUILayout.Button("2  Walk"))
        {
            Speed = 1;
            RepeatAttack = false;
        }

        if (GUILayout.Button("3  Attack"))
        {
            StartAttack();
        }

        GUILayout.EndHorizontal();
        RepeatAttack = GUILayout.Toggle(RepeatAttack, "Repeat attack");
        GUILayout.Label("Press 1 / 2 / 3 to preview animations.");
        GUILayout.EndArea();
    }

    private void Update()
    {
        if (!Character)
        {
            return;
        }

        Character.SetFloat("Speed", Speed);
        Character.SetFloat("Moving",Speed);
        Character.SetFloat("MoveX",Direction.normalized.x);
        Character.SetFloat("MoveY",Direction.normalized.y);
        Character.SetFloat("RunBlend",Run?1:0);
        Character.SetFloat("WalkRate", 1);
        if (attacking)
        {
            float progress = (Time.time - attackStarted) / attackDuration;
            Character.SetFloat("AttackProgress", Mathf.Clamp01(progress));
            if (progress >= 1)
            {
                attacking = false;
                Character.SetBool("IsAttacking", false);
                Character.CrossFadeInFixedTime("Locomotion",.15f,0,0);
            }
        }

        if (RepeatAttack && Time.time >= nextAttack)
        {
            StartAttack();
            nextAttack = Time.time + AttackInterval;
        }

        if (Keyboard.current == null)
        {
            return;
        }

        if (Keyboard.current.digit1Key.wasPressedThisFrame)
        {
            Speed = 0;
            RepeatAttack = false;
        }

        if (Keyboard.current.digit2Key.wasPressedThisFrame)
        {
            Speed = 1;
            RepeatAttack = false;
        }

        if (Keyboard.current.digit3Key.wasPressedThisFrame)
        {
            StartAttack();
        }
    }
}
