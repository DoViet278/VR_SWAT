using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class Gun : MonoBehaviour
{
    public enum FireMode
    {
        Single,
        Auto
    }

    [Header("Fire Mode")]
    [Tooltip("Single: mỗi lần kéo cò bắn 1 viên. Auto: giữ cò bắn liên tục đến khi hết đạn.")]
    [SerializeField] private FireMode fireMode = FireMode.Single;
    [Tooltip("Tốc độ bắn ở chế độ Auto (viên/phút).")]
    [SerializeField] private float roundsPerMinute = 700f;

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

    [Header("Charging Handle")]
    [Tooltip("Tay cò tự giật về PositionRefill mỗi phát. Để trống sẽ tự tìm trong children.")]
    [SerializeField] private ChargingHandle chargingHandle;
    [Tooltip("Bật cho súng pump: không tự giật tay cò, người chơi phải tự kéo pump để lên nòng.")]
    [SerializeField] private bool manualCycleOnly;

    [Header("Ballistics")]
    [SerializeField] private float maxRange = 100f;
    [SerializeField] private float damage = 25f;
    [SerializeField] private LayerMask hittableLayers = ~0;
    [SerializeField] private GameObject impactEffect;
    [SerializeField] private float impactEffectLiveTime = 2f;

    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabInteractable;
    private GunRecoilController recoilController;
    private GunHapticFeedback hapticFeedback;

    private bool triggerHeld;
    private object activeInteractor;
    private float nextFireTime;

    private float FireInterval => 60f / Mathf.Max(1f, roundsPerMinute);

    private void Awake()
    {
        grabInteractable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        recoilController = GetComponent<GunRecoilController>();
        hapticFeedback = GetComponent<GunHapticFeedback>();

        if (ammoManager == null)
            ammoManager = GetComponent<GunAmmoManager>();

        if (chargingHandle == null)
            chargingHandle = GetComponentInChildren<ChargingHandle>(true);
    }

    void Start()
    {
        grabInteractable.activated.AddListener(OnTriggerPressed);
        grabInteractable.deactivated.AddListener(OnTriggerReleased);
        grabInteractable.selectExited.AddListener(OnSelectExited);
    }

    private void Update()
    {
        if (fireMode != FireMode.Auto || !triggerHeld)
            return;

        if (grabInteractable == null || !grabInteractable.isSelected)
        {
            triggerHeld = false;
            return;
        }

        while (Time.time >= nextFireTime)
        {
            if (!TryShoot(activeInteractor))
            {
                triggerHeld = false;
                return;
            }

            nextFireTime += FireInterval;
        }
    }

    private void OnTriggerPressed(ActivateEventArgs args)
    {
        activeInteractor = args.interactorObject;
        triggerHeld = true;
        nextFireTime = Time.time + FireInterval;

        if (!TryShoot(activeInteractor))
            triggerHeld = false;
    }

    private void OnTriggerReleased(DeactivateEventArgs args)
    {
        triggerHeld = false;
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        triggerHeld = false;
    }

    /// <summary>Bắn 1 viên. Trả về false khi không còn đạn (đã phát dry fire).</summary>
    private bool TryShoot(object interactorObject)
    {
        if (ammoManager != null && !ammoManager.TryFireRound())
        {
            PlayDryFireEffect();
            hapticFeedback?.PlayDryFire(interactorObject);
            return false;
        }

        FireRaycast();
        PlayGunShotSound();
        PlayGunShotEffect();
        recoilController?.RegisterShot();
        hapticFeedback?.PlayShot(interactorObject);
        if (!manualCycleOnly)
        {
            bool emptyAfterShot = ammoManager != null && !ammoManager.IsChambered && !ammoManager.HasAmmo;
            float cycleBudget = fireMode == FireMode.Auto && !emptyAfterShot ? FireInterval : 0f;
            chargingHandle?.PlayCycle(emptyAfterShot, cycleBudget);
        }

        // EjectShell();
        return true;
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
        // Lỗ đạn + tiếng va chạm: tự bỏ qua nếu scene không có BulletDecalManager.
        BulletDecalManager.SpawnImpact(hit);

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
            grabInteractable.activated.RemoveListener(OnTriggerPressed);
            grabInteractable.deactivated.RemoveListener(OnTriggerReleased);
            grabInteractable.selectExited.RemoveListener(OnSelectExited);
        }
    }
}
