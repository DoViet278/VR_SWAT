using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

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

    [Header("Effects")]
    [SerializeField] private GameObject explosionEffect;
    [SerializeField] private float explosionEffectLiveTime = 3f;
    [SerializeField] private AudioClip explosionClip;
    [SerializeField] private AudioClip landingClip;
    [SerializeField] private float landingMinSpeed = 1f;

    private XRGrabInteractable grabInteractable;
    private Rigidbody body;
    private bool pinPulled;
    private bool fuseStarted;
    private float lastLandingTime;

    public bool PinPulled => pinPulled;
    public bool IsHeld => grabInteractable != null && grabInteractable.isSelected;

    private void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        body = GetComponent<Rigidbody>();
    }

    private void OnEnable()
    {
        grabInteractable.selectExited.AddListener(HandleReleased);
    }

    private void OnDisable()
    {
        grabInteractable.selectExited.RemoveListener(HandleReleased);
    }

    public void OnPinPulled()
    {
        pinPulled = true;
    }

    private void HandleReleased(SelectExitEventArgs args)
    {
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
        PlayEffects(center);
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
