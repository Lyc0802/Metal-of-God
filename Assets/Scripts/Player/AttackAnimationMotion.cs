using UnityEngine;

// Displacement baked from the supplied animation, not an authored movement speed.
// The physics body applies that displacement with collision limits.
[DefaultExecutionOrder(100)]
public sealed class AttackAnimationMotion : MonoBehaviour
{
    public AnimationCurve LocalX=AnimationCurve.Constant(0,1,0);
    public AnimationCurve LocalZ=AnimationCurve.Constant(0,1,0);
    public AnimationCurve ReturnLocalX=AnimationCurve.Constant(0,1,0);
    public AnimationCurve ReturnLocalZ=AnimationCurve.Constant(0,1,0);
    public Transform Visual;
    private PlayerCombat combat;
    private Rigidbody body;
    private bool active;
    private int sequence=-1;
    private Vector3 previous;
    private Quaternion facing;
    private void OnEnable(){combat=GetComponent<PlayerCombat>();body=GetComponent<Rigidbody>();active=false;}
    private void FixedUpdate()
    {
        if(!combat.IsAttacking){active=false;return;}
        var x=combat.AttackIndex==1?ReturnLocalX:LocalX;
        var z=combat.AttackIndex==1?ReturnLocalZ:LocalZ;
        if(!active || sequence!=combat.SwingSequence){active=true;sequence=combat.SwingSequence;previous=new Vector3(x.Evaluate(0),0,z.Evaluate(0));facing=body.rotation;}
        var sample=new Vector3(x.Evaluate(combat.SwingProgress),0,z.Evaluate(combat.SwingProgress));
        var delta=facing*((sample-previous)*(Visual?Visual.lossyScale.y:1));previous=sample;
        float distance=delta.magnitude;
        if(distance<.00001f)return;
        foreach(var hit in body.SweepTestAll(delta.normalized,distance+.015f,QueryTriggerInteraction.Ignore))
            if(!hit.collider.transform.IsChildOf(transform)&&Mathf.Abs(hit.normal.y)<.8f)
                distance=Mathf.Min(distance,Mathf.Max(0,hit.distance-.015f));
        body.MovePosition(body.position+delta.normalized*distance);
    }
}
