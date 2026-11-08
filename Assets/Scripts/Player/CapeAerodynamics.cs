using UnityEngine;

// Cloth is a one-way solver; this never applies forces to the player Rigidbody.
[DefaultExecutionOrder(120)]
public sealed class CapeAerodynamics : MonoBehaviour
{
    public Cloth[] Panels;
    public Rigidbody PlayerBody;
    [Range(0,4)] public float AirDrag = 1.4f;
    [Range(0,1)] public float AccelerationResponse = .15f;
    [Min(1)] public float MaximumAcceleration = 18;
    private Vector3 lastVelocity, lastPosition, filtered;
    private bool initialized;
    private void OnEnable() { initialized=false; }
    private void LateUpdate()
    {
        if (!PlayerBody) PlayerBody=GetComponentInParent<Rigidbody>();
        if (!PlayerBody) return;
        var velocity=PlayerBody.linearVelocity;
        if (!initialized || Vector3.Distance(transform.position,lastPosition)>3)
        {
            foreach(var cloth in Panels) if(cloth) cloth.ClearTransformMotion();
            lastVelocity=velocity; filtered=Vector3.zero; initialized=true;
        }
        float dt=Mathf.Max(.008f,Time.deltaTime);
        var acceleration=(velocity-lastVelocity)/dt;
        var force=Vector3.ClampMagnitude(-velocity*AirDrag-acceleration*AccelerationResponse,MaximumAcceleration);
        filtered=Vector3.Lerp(filtered,force,1-Mathf.Exp(-10*dt));
        foreach(var cloth in Panels) if(cloth) cloth.externalAcceleration=filtered;
        lastVelocity=velocity; lastPosition=transform.position;
    }
}
