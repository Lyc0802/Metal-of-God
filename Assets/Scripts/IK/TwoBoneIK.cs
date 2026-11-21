using UnityEngine;

namespace MetalOfGod.IK
{
    /// <summary>
    /// 解析式兩節 IK（上臂 / 前臂 / 手，或大腿 / 小腿 / 腳）。
    /// 取代 MetalAbilityReachIK 原本使用的 Final IK IKSolverLimb 子集：
    /// 目標位置 + 權重、彎曲目標點（手肘 / 膝蓋方向）。末端骨骼的區域旋轉不變（跟隨上一節）。
    /// 在動畫之後（LateUpdate）每幀呼叫 Update()。
    /// </summary>
    public sealed class TwoBoneIK
    {
        public Transform Upper { get; private set; }
        public Transform Lower { get; private set; }
        public Transform End { get; private set; }

        /// <summary>末端（手 / 腳）的目標世界座標。</summary>
        public Vector3 IKPosition;
        /// <summary>0 = 維持動畫，1 = 完全到達目標。</summary>
        public float IKPositionWeight = 1;

        private Vector3 bendGoal;
        private float bendGoalWeight;

        public bool Initiated => Upper && Lower && End;

        public bool SetChain(Transform upper, Transform lower, Transform end)
        {
            Upper = upper;
            Lower = lower;
            End = end;
            return Initiated;
        }

        /// <summary>
        /// 中間關節（手肘 / 膝蓋）朝向的世界座標點。weight 決定相對動畫彎曲方向的偏移程度。
        /// 只作用於下一次 Update()。
        /// </summary>
        public void SetBendGoalPosition(Vector3 goalPosition, float weight)
        {
            bendGoal = goalPosition;
            bendGoalWeight = Mathf.Clamp01(weight);
        }

        public void Update()
        {
            float weight = Mathf.Clamp01(IKPositionWeight);
            if (!Initiated || weight <= 0)
            {
                bendGoalWeight = 0;
                return;
            }

            var root = Upper.position;
            float a = Vector3.Distance(root, Lower.position);
            float b = Vector3.Distance(Lower.position, End.position);
            if (a < 1e-6f || b < 1e-6f) return;

            // Target blends from the animated end position, like Final IK's weighting.
            var target = Vector3.Lerp(End.position, IKPosition, weight);
            var toTarget = target - root;
            float distance = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(a - b) + 1e-5f, a + b - 1e-5f);
            var axis = toTarget.sqrMagnitude > 1e-12f ? toTarget.normalized : (End.position - root).normalized;

            // Bend plane: animated direction of the middle joint, turned toward the bend goal.
            var animatedBend = Vector3.ProjectOnPlane(Lower.position - root, axis);
            var bend = animatedBend;
            if (bendGoalWeight > 0)
            {
                var goalBend = Vector3.ProjectOnPlane(bendGoal - root, axis);
                if (goalBend.sqrMagnitude > 1e-10f)
                {
                    bend = animatedBend.sqrMagnitude > 1e-10f
                        ? Vector3.Slerp(animatedBend.normalized, goalBend.normalized, bendGoalWeight * weight)
                        : goalBend;
                }
            }

            if (bend.sqrMagnitude < 1e-10f) bend = Vector3.ProjectOnPlane(Upper.forward, axis);
            if (bend.sqrMagnitude < 1e-10f) bend = Vector3.ProjectOnPlane(Upper.up, axis);
            bend.Normalize();

            // Law of cosines: place the middle joint, then aim each segment.
            float along = (a * a + distance * distance - b * b) / (2 * distance);
            float height = Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
            var middle = root + axis * along + bend * height;
            var end = root + axis * distance;
            Upper.rotation = Quaternion.FromToRotation(Lower.position - root, middle - root) * Upper.rotation;
            Lower.rotation = Quaternion.FromToRotation(End.position - Lower.position, end - Lower.position) * Lower.rotation;
            bendGoalWeight = 0;
        }
    }
}
