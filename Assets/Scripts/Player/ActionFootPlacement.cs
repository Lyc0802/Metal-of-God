using System.Linq;
using UnityEngine;

// Generic-rig two-bone foot placement after Animator; never writes the root.
[DefaultExecutionOrder(20)]
public sealed class ActionFootPlacement : MonoBehaviour
{
    public Animator Animator;
    [Range(0,1)] public float Weight=.8f;
    [Min(0)] public float MaximumCorrection=.16f;
    private PlayerMovement movement;
    private PlayerCombat combat;
    private Transform[,] bones=new Transform[2,3];
    private Vector3[] planted=new Vector3[2];
    private bool[] holding=new bool[2];
    private void OnEnable()
    {
        holding[0]=holding[1]=false;
        movement=GetComponent<PlayerMovement>(); combat=GetComponent<PlayerCombat>();
        if(!Animator) Animator=GetComponentInChildren<Animator>();
        if(!Animator) {enabled=false;return;}
        var all=Animator.GetComponentsInChildren<Transform>();
        for(int i=0;i<2;i++) for(int j=0;j<3;j++)
            bones[i,j]=all.First(t=>t.name==(i==0?"Left":"Right")+new[]{"UpperLeg","LowerLeg","Foot"}[j]);
    }
    private void LateUpdate()
    {
        if(!Animator || !movement.IsGrounded || combat.IsAttacking || movement.IsPreparingJump)
        {holding[0]=holding[1]=false;return;}
        float phase=Mathf.Repeat(Animator.GetCurrentAnimatorStateInfo(0).normalizedTime,1);
        float stance=Mathf.Lerp(.62f,.36f,Animator.GetFloat("RunBlend"));
        float moving=Animator.GetFloat("Moving");
        for(int i=0;i<2;i++)
        {
            var foot=bones[i,2]; float p=Mathf.Repeat(phase+i*.5f,1);
            bool contact=moving<.1f || p<stance;
            if(!contact) {holding[i]=false;continue;}
            // Foot pivot to sole is 0.062 model units on this rig.
            float sole=.062f*Animator.transform.lossyScale.y;
            RaycastHit ground=default; bool found=false;
            foreach(var hit in Physics.RaycastAll(foot.position+Vector3.up*.3f,Vector3.down,.75f, movement.GroundMask,QueryTriggerInteraction.Ignore))
                if(!hit.collider.transform.IsChildOf(transform) && hit.normal.y>.55f && (!found||hit.distance<ground.distance)) {ground=hit;found=true;}
            if(!found) {holding[i]=false;continue;}
            Vector3 desired=foot.position; desired.y=ground.point.y+sole;
            if(!holding[i] || Vector3.Distance(planted[i],desired)>.3f) planted[i]=desired;
            holding[i]=true;
            if(moving<.1f) planted[i]=desired;
            var target=foot.position+Vector3.ClampMagnitude(planted[i]-foot.position,MaximumCorrection)*Weight;
            Solve(bones[i,0],bones[i,1],foot,target);
        }
    }
    private static void Solve(Transform upper,Transform lower,Transform end,Vector3 target)
    {
        var rotation=end.rotation;
        float a=Vector3.Distance(upper.position,lower.position),b=Vector3.Distance(lower.position,end.position);
        var axis=(target-upper.position).normalized;
        float d=Mathf.Clamp(Vector3.Distance(target,upper.position),.001f,a+b-.0001f);
        var bend=Vector3.ProjectOnPlane(lower.position-upper.position,axis).normalized;
        float along=(a*a+d*d-b*b)/(2*d),height=Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
        var knee=upper.position+axis*along+bend*height;
        upper.rotation=Quaternion.FromToRotation(lower.position-upper.position,knee-upper.position)*upper.rotation;
        lower.rotation=Quaternion.FromToRotation(end.position-lower.position,target-lower.position)*lower.rotation;
        end.rotation=rotation;
    }
}
