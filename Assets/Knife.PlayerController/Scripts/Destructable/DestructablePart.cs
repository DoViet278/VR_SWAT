using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DestructablePart : BaseHittableObject
{
    public Rigidbody Body;
    public float DamageMultiplier = 1f;
    public float MinRandomHealth = -20f;
    public float MaxRandomHealth = 0f;
    
    public bool Destructed
    {
        get
        {
            return !Body.isKinematic;
        }
    }

    protected virtual void Start()
    {
        float health = Random.Range(MinRandomHealth, MaxRandomHealth);
        SetHealth(health, health);
    }

    [ContextMenu("Get start params")]
    public void GetStartParams()
    {
        Body = GetComponent<Rigidbody>();
    }

    public override void TakeDamage(DamageData damage)
    {
        if (Invulnerable)
            return;

        if (Destructed)
        {
            Vector3 point = damage.HitPosition;
            Vector3 direction = damage.HitDirection;

            Body.isKinematic = false;
            Body.AddForceAtPosition(direction * damage.DamageAmount * DamageMultiplier, point);
        } else
        {
            if (damage.HitType == DamageData.DamageType.KnifeHit)
                return;
        }

        base.TakeDamage(damage);
    }

    public override void Die(DamageData damage)
    {
        if (damage.HitType == DamageData.DamageType.KnifeHit)
            return;

        Vector3 point = damage.HitPosition;
        Vector3 direction = damage.HitDirection;

        Body.isKinematic = false;
        Body.AddForceAtPosition(direction * damage.DamageAmount * DamageMultiplier, point);

        base.Die(damage);
    }
}
