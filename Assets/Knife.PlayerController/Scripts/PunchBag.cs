using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PunchBag : MonoBehaviour
{
    public Transform CenterPoint;
    public string[] HitsLeft;
    public string[] HitsRight;
    public Animator TargetAnimator;
    public BaseHittableObject Hittable;

    private void Awake()
    {
        Hittable.DamagedEvent.AddListener(damaged);
    }

    private void damaged(DamageData damage)
    {
        Vector3 localPosition = CenterPoint.InverseTransformPoint(damage.HitPosition);

        if(localPosition.x <= 0)
        {
            TargetAnimator.Play(HitsRight[Random.Range(0, HitsRight.Length)], 0, 0);
        } else if(localPosition.x > 0)
        {
            TargetAnimator.Play(HitsLeft[Random.Range(0, HitsRight.Length)], 0, 0);
        }
    }
}
