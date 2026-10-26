using UnityEngine;
using UnityEngine.Events;

public class DamageableTarget : MonoBehaviour, IDamageable
{
    [System.Serializable]
    public class DamageEvent : UnityEvent<float> { }

    [SerializeField] private float maxHealth = 100f;

    public DamageEvent OnDamaged;
    public UnityEvent OnDeath;

    private float currentHealth;
    private bool isDead;

    public float CurrentHealth => currentHealth;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    public void ApplyDamage(float damage, RaycastHit hit)
    {
        if (isDead)
            return;

        currentHealth = Mathf.Max(0f, currentHealth - damage);
        OnDamaged?.Invoke(currentHealth);

        if (currentHealth <= 0f)
        {
            isDead = true;
            OnDeath?.Invoke();
        }
    }
}
