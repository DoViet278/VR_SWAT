using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class Gun : MonoBehaviour
{
    [Header("Gun Shot Effect")]
    [SerializeField] private GameObject gunShotEffect;
    [SerializeField] private float gunShotLiveTime = 0.1f;

    [Header("Shell")]
    [SerializeField] private Rigidbody usedShellTemplate;
    [SerializeField] private Transform muzzlePoint;
    [SerializeField] private float shellSpeedMin = 20f;
    [SerializeField] private float shellSpeedMax = 30f;

    [Header("Ammo")]
    [SerializeField] private GunAmmoManager ammoManager;
    [SerializeField] private AudioSource dryFireAudioSource;

    [Header("Gun Shot Sound")]
    [SerializeField] private AudioSource gunShotAudioSource;
    [SerializeField] private AudioClip gunShotClip;

    [Header("Ballistics")]
    [SerializeField] private float maxRange = 100f;
    [SerializeField] private float damage = 25f;
    [SerializeField] private LayerMask hittableLayers = ~0;
    [SerializeField] private GameObject impactEffect;
    [SerializeField] private float impactEffectLiveTime = 2f;

    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabInteractable;
    private GunRecoilController recoilController;

    private void Awake()
    {
        grabInteractable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        recoilController = GetComponent<GunRecoilController>();

        if (ammoManager == null)
            ammoManager = GetComponent<GunAmmoManager>();
    }

    void Start()
    {
        grabInteractable.activated.AddListener(Shoot);
    }

    private void Shoot(ActivateEventArgs args)
    {
        if (ammoManager != null && !ammoManager.TryFireRound())
        {
            PlayDryFireEffect();
            return;
        }

        FireRaycast();
        PlayGunShotSound();
        PlayGunShotEffect();
        recoilController?.RegisterShot();
        // EjectShell();
    }

    private void FireRaycast()
    {
        Transform origin = muzzlePoint != null ? muzzlePoint : transform;
        Vector3 direction = ApplySpread(origin.forward);

        if (Physics.Raycast(origin.position, direction, out RaycastHit hit, maxRange, hittableLayers, QueryTriggerInteraction.Ignore))
        {
            IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
            damageable?.ApplyDamage(damage, hit);
            PlayImpactEffect(hit);
        }
    }

    private Vector3 ApplySpread(Vector3 forward)
    {
        float spreadDegrees = recoilController != null ? recoilController.CurrentSpreadDegrees : 0f;

        if (spreadDegrees <= 0f)
            return forward;

        float angle = Random.Range(0f, spreadDegrees);
        float rotation = Random.Range(0f, 360f);
        Quaternion spreadRotation = Quaternion.AngleAxis(rotation, forward) * Quaternion.AngleAxis(angle, Vector3.up);
        return spreadRotation * forward;
    }

    private void PlayImpactEffect(RaycastHit hit)
    {
        if (impactEffect == null)
            return;

        GameObject instance = Instantiate(impactEffect, hit.point, Quaternion.LookRotation(hit.normal));
        Destroy(instance, impactEffectLiveTime);
    }

    private void PlayDryFireEffect()
    {
        if (dryFireAudioSource != null)
            dryFireAudioSource.Play();
    }

    private void PlayGunShotSound()
    {
        Debug.Log($"Shot sound: src={gunShotAudioSource}, clip={gunShotClip}, mute={gunShotAudioSource?.mute}, vol={gunShotAudioSource?.volume}");
        if (gunShotAudioSource != null && gunShotClip != null)
            gunShotAudioSource.PlayOneShot(gunShotClip);
    }

    private void PlayGunShotEffect()
    {
        if (gunShotEffect == null)
            return;

        GameObject instance = Instantiate(gunShotEffect, gunShotEffect.transform.position, gunShotEffect.transform.rotation, gunShotEffect.transform.parent);
        instance.SetActive(true);
        Destroy(instance, gunShotLiveTime);
    }

    private void EjectShell()
    {
        if (usedShellTemplate == null)
            return;

        Transform origin = muzzlePoint != null ? muzzlePoint : transform;
        Rigidbody shellInstance = Instantiate(usedShellTemplate, origin.position, origin.rotation);

        shellInstance.gameObject.SetActive(true);
        shellInstance.transform.SetParent(null);
        shellInstance.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        shellInstance.linearVelocity = origin.forward * Random.Range(shellSpeedMin, shellSpeedMax);
    }

    private void OnDestroy()
    {
        if (grabInteractable != null)
        {
            grabInteractable.activated.RemoveListener(Shoot);
        }
    }
}
