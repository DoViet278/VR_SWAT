using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRSwat.Grenades;

[RequireComponent(typeof(Rigidbody))]
public class VRGrenade : MonoBehaviour
{
    [Header("Pin")]
    [SerializeField] private GrenadePin pin;

    [Header("Fuse")]
    [SerializeField] private float fuseTime = 3f;

    [Header("Blast")]
    [SerializeField] private float blastRadius = 6f;
    [SerializeField] private float blastDamage = 100f;
    [SerializeField] private float blastForce = 8f;
    [SerializeField] private AnimationCurve damageFalloff = AnimationCurve.Linear(0f, 1f, 1f, 0f);
    [SerializeField] private LayerMask hittableLayers = ~0;
    [SerializeField] private LayerMask lineOfSightLayers = ~0;

    [Header("Flashbang")]
    [Tooltip("Phần gây chói của flashbang. Để trống thì tự lấy component Flashbang trên cùng " +
             "object; không có thì quả này chỉ là lựu nổ thường.")]
    [SerializeField] private Flashbang flashbang;

    [Header("Effects")]
    [SerializeField] private GameObject explosionEffect;
    [SerializeField] private float explosionEffectLiveTime = 3f;
    [SerializeField] private AudioClip explosionClip;
    [SerializeField] private AudioClip landingClip;
    [SerializeField] private float landingMinSpeed = 1f;

    [Tooltip("Tiếng rơi chỉ phát khi quả này vừa được ném ra khỏi tay người chơi. " +
             "Tắt thì mọi cú va đập đủ mạnh đều kêu: lúc spawn vào túi đồ và đụng đồ bên cạnh, lúc đang " +
             "cầm mà quẹt vào tường, hay lúc nằm sẵn trong scene mà bị đi vấp.")]
    [SerializeField] private bool landingSoundOnlyWhenThrown = true;

    private XRGrabInteractable grabInteractable;
    private Rigidbody body;
    private bool pinPulled;
    private bool fuseStarted;
    private float lastLandingTime;
    private bool thrownByHand;

    public bool PinPulled => pinPulled;
    public bool IsHeld => grabInteractable != null && grabInteractable.isSelected;

    private void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        body = GetComponent<Rigidbody>();

        if (flashbang == null)
            flashbang = GetComponent<Flashbang>();
    }

    private void OnEnable()
    {
        grabInteractable.selectEntered.AddListener(HandleGrabbed);
        grabInteractable.selectExited.AddListener(HandleReleased);
    }

    private void OnDisable()
    {
        grabInteractable.selectEntered.RemoveListener(HandleGrabbed);
        grabInteractable.selectExited.RemoveListener(HandleReleased);
    }

    public void OnPinPulled()
    {
        pinPulled = true;
    }

    /// <summary>
    /// Vào tay người chơi hoặc vào ô túi đồ: quên cú ném trước đó đi, nên quả lựu đạn
    /// nằm trong túi mà bị va đập thì không kêu.
    /// </summary>
    private void HandleGrabbed(SelectEnterEventArgs args)
    {
        thrownByHand = false;
    }

    private void HandleReleased(SelectExitEventArgs args)
    {
        // Rời khỏi ô túi đồ (socket) không phải là ném: chỉ tính khi tay người chơi nhả ra.
        if (!(args.interactorObject is XRSocketInteractor))
            thrownByHand = true;

        if (pinPulled && !fuseStarted)
            StartCoroutine(FuseRoutine());
    }

    private IEnumerator FuseRoutine()
    {
        fuseStarted = true;
        yield return new WaitForSeconds(fuseTime);
        Explode();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (landingSoundOnlyWhenThrown && !thrownByHand)
            return;

        if (landingClip == null || collision.relativeVelocity.magnitude < landingMinSpeed)
            return;

        if (Time.time - lastLandingTime < 0.15f)
            return;

        lastLandingTime = Time.time;
        AudioSource.PlayClipAtPoint(landingClip, transform.position);
    }

    private void Explode()
    {
        Vector3 center = transform.position;

        foreach (Collider own in GetComponentsInChildren<Collider>())
            own.enabled = false;

        ApplyBlast(center);
        // Morale NPC: nổ gần (kể cả ngoài vùng sát thương) làm NPC hoảng.
        NPCMorale.ReportExplosion(center, blastRadius * 2f);
        PlayEffects(center);

        // Gọi trước khi Destroy cũng không sao: Detonate chỉ đẩy cường độ sang receiver trên
        // đầu người chơi, phần đếm thời gian chói chạy bên đó.
        if (flashbang != null)
            flashbang.Detonate(center);

        Destroy(gameObject);
    }

    private void ApplyBlast(Vector3 center)
    {
        Collider[] colliders = Physics.OverlapSphere(center, blastRadius, hittableLayers, QueryTriggerInteraction.Ignore);
        HashSet<IDamageable> damaged = new HashSet<IDamageable>();
        HashSet<Rigidbody> pushed = new HashSet<Rigidbody>();

        foreach (Collider target in colliders)
        {
            Rigidbody targetBody = target.attachedRigidbody;

            if (targetBody != null && targetBody != body && pushed.Add(targetBody))
                targetBody.AddExplosionForce(blastForce, center, blastRadius, 0.5f, ForceMode.Impulse);

            IDamageable damageable = target.GetComponentInParent<IDamageable>();

            if (damageable == null || damaged.Contains(damageable))
                continue;

            if (!TryGetBlastHit(center, target, damageable, out RaycastHit hit, out float distance))
                continue;

            damaged.Add(damageable);
            float damage = blastDamage * damageFalloff.Evaluate(Mathf.Clamp01(distance / blastRadius));
            damageable.ApplyDamage(damage, hit);
        }
    }

    private bool TryGetBlastHit(Vector3 center, Collider target, IDamageable damageable, out RaycastHit hit, out float distance)
    {
        Vector3 toTarget = target.bounds.center - center;
        distance = toTarget.magnitude;
        hit = default;

        if (distance < 0.001f)
            return true;

        if (!Physics.Raycast(center, toTarget / distance, out hit, distance + 0.05f, lineOfSightLayers, QueryTriggerInteraction.Ignore))
            return true;

        if (hit.collider == target)
            return true;

        return hit.collider.GetComponentInParent<IDamageable>() == damageable;
    }

    private void PlayEffects(Vector3 center)
    {
        if (explosionEffect != null)
        {
            GameObject effect = Instantiate(explosionEffect, center, Quaternion.identity);
            effect.SetActive(true);
            Destroy(effect, explosionEffectLiveTime);
        }

        if (explosionClip != null)
            AudioSource.PlayClipAtPoint(explosionClip, center);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, blastRadius);
    }
}
