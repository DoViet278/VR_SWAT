using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class Gun : MonoBehaviour
{
    public enum FireMode
    {
        Single,
        Auto
    }

    [Header("Weapon")]
    [Tooltip("Loại súng, gửi kèm sự kiện PLAYER_SHOOT_TARGET để target biết bị bắn bằng súng gì.")]
    [SerializeField] private WeaponType weaponType = WeaponType.Pistol;
    public WeaponType Type => weaponType;

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

    [Header("Physics (chống rung khi súng rơi / đặt xuống)")]
    [Tooltip("Tự chỉnh Rigidbody của súng lúc Awake cho tiếp đất êm, không nảy lập bập.")]
    [SerializeField] private bool stabilizeBodyPhysics = true;
    [Tooltip("Khối lượng súng (kg). 0 = giữ nguyên giá trị đang set trên Rigidbody.")]
    [SerializeField] private float bodyMass;
    [Tooltip("Số vòng giải va chạm. Cao hơn = nằm yên chắc hơn trên mặt bàn.")]
    [SerializeField] private int solverIterations = 16;
    [SerializeField] private int solverVelocityIterations = 4;
    [Tooltip("Giới hạn tốc độ đẩy ra khi bị lún vào vật khác (m/s). Thấp = không bị bật nảy sau khi " +
             "rơi, nhưng ĐỪNG hạ xuống quá thấp: mỗi lần lún sâu (lòng bàn tay lúc thả, băng đạn " +
             "trong khoang) sẽ bị kéo thành một đợt rung dài nhiều bước physics thay vì một cú bật " +
             "nhanh. Mặc định Unity là 10; 3 vừa đủ êm mà vẫn thoát overlap gọn.")]
    [SerializeField] private float maxDepenetrationVelocity = 3f;
    [Tooltip("Cản tịnh tiến / cản quay tối thiểu, giúp súng tắt dao động thừa và ngủ được.")]
    [SerializeField] private float minLinearDamping = 0.05f;
    [SerializeField] private float minAngularDamping = 0.5f;

    [Header("Physics (chống súng xuyên bàn / tường khi đang cầm)")]
    [Tooltip("Giới hạn mức tăng vận tốc mỗi bước physics lúc đang cầm. Tay là kinematic nên chui " +
             "thẳng vào bàn được; không có giới hạn này thì XRI set vận tốc súng = quãng cách / " +
             "dt, vọt lên vài chục m/s và xuyên qua mặt bàn trong đúng 1 bước physics.")]
    [SerializeField] private bool limitHeldVelocity = true;
    [Tooltip("m/s mỗi bước physics. 10 ~ 11 cm/bước ở 90 Hz, mỏng hơn mọi vật trong scene.")]
    [SerializeField] private float maxLinearVelocityDelta = 10f;
    [Tooltip("rad/s mỗi bước physics.")]
    [SerializeField] private float maxAngularVelocityDelta = 20f;

    [Header("Ballistics")]
    [SerializeField] private float maxRange = 100f;
    [Tooltip("Sát thương MỖI tia bắn ra. Với đạn ghém (Pellets Per Shot > 1) đây là sát thương " +
             "của 1 hạt ghém, nên muốn giữ nguyên tổng sát thương một phát thì chia cho số hạt.")]
    [SerializeField] private float damage = 25f;
    [SerializeField] private LayerMask hittableLayers = ~0;
    [SerializeField] private GameObject impactEffect;
    [SerializeField] private float impactEffectLiveTime = 2f;

    [Header("Buckshot (đạn ghém)")]
    [Tooltip("Số hạt ghém mỗi phát. 1 = súng bắn 1 viên (rifle, pistol). Shotgun thường 8 - 9 hạt.")]
    [SerializeField] [Min(1)] private int pelletsPerShot = 1;
    [Tooltip("Nửa góc nón tản của chùm ghém (độ) - độ lệch tối đa của 1 hạt so với trục nòng. " +
             "3 - 5 độ là chùm shotgun điển hình. Chỉ dùng khi Pellets Per Shot > 1.")]
    [SerializeField] private float pelletSpreadDegrees = 4f;
    [Tooltip("Độ dồn ghém về tâm chùm. 1 = rải đều trên mặt đĩa; > 1 = dày ở tâm, thưa ở rìa " +
             "(giống chùm đạn thật hơn).")]
    [SerializeField] [Min(0.1f)] private float pelletCenterBias = 1.5f;
    [Tooltip("Chỉ phát tiếng va chạm cho hạt ghém đầu tiên trúng đích. Tắt đi thì cả chùm cùng " +
             "kêu, tranh nhau pool AudioSource và nghe thành tiếng rè ngắt quãng.")]
    [SerializeField] private bool singleImpactSoundPerShot = true;

    private XRGrabInteractable grabInteractable;
    private GunRecoilController recoilController;
    private GunHapticFeedback hapticFeedback;

    private bool triggerHeld;
    private object activeInteractor;
    private float nextFireTime;

    private float FireInterval => 60f / Mathf.Max(1f, roundsPerMinute);

    private void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        recoilController = GetComponent<GunRecoilController>();
        hapticFeedback = GetComponent<GunHapticFeedback>();

        if (ammoManager == null)
            ammoManager = GetComponent<GunAmmoManager>();

        if (chargingHandle == null)
            chargingHandle = GetComponentInChildren<ChargingHandle>(true);

        if (stabilizeBodyPhysics)
            StabilizeBodyPhysics();

        if (limitHeldVelocity)
            LimitHeldVelocity();
    }

    // Đặt bằng code chứ không chỉ tick trong Inspector: cả 3 khẩu dùng chung Gun.cs nên khỏi phải
    // nhớ tick từng khẩu, và không mất khi scene bị ghi đè.
    private void LimitHeldVelocity()
    {
        if (grabInteractable == null)
            return;

        // Chỉ Velocity Tracking mới chạy qua solver vật lý. Kinematic / Instantaneous là dịch
        // thẳng transform nên không gì chặn được, có set gì cũng vẫn xuyên.
        if (grabInteractable.movementType !=
            XRBaseInteractable.MovementType.VelocityTracking)
        {
            Debug.LogWarning(
                $"{name}: Movement Type đang là {grabInteractable.movementType}, súng sẽ xuyên qua " +
                "bàn/tường khi cầm. Đổi sang Velocity Tracking trên XR Grab Interactable.", this);
            return;
        }

        grabInteractable.limitLinearVelocity = true;
        grabInteractable.limitAngularVelocity = true;
        grabInteractable.maxLinearVelocityDelta = Mathf.Max(0.1f, maxLinearVelocityDelta);
        grabInteractable.maxAngularVelocityDelta = Mathf.Max(0.1f, maxAngularVelocityDelta);
    }

    // Mặc định của Unity (Discrete + Interpolate None + mass 1 + maxDepenetrationVelocity 10) làm
    // súng rơi từ cao xuống lún sâu vào mặt bàn trong 1 bước physics, rồi bị đẩy ngược lên rất
    // mạnh -> nảy lại nhiều lần. Siết mấy thông số này lại thì súng chạm bàn là nằm yên.
    private void StabilizeBodyPhysics()
    {
        Rigidbody body = GetComponent<Rigidbody>();

        if (body == null)
            return;

        if (bodyMass > 0f)
            body.mass = bodyMass;

        body.linearDamping = Mathf.Max(body.linearDamping, minLinearDamping);
        body.angularDamping = Mathf.Max(body.angularDamping, minAngularDamping);
        body.solverIterations = Mathf.Max(body.solverIterations, solverIterations);
        body.solverVelocityIterations = Mathf.Max(body.solverVelocityIterations, solverVelocityIterations);
        body.maxDepenetrationVelocity = Mathf.Max(0.1f, maxDepenetrationVelocity);
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
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

    /// <summary>
    /// Bắn ra Pellets Per Shot tia trong 1 phát. Trục chùm đã gồm độ lệch do recoil/heat,
    /// mỗi hạt ghém lệch thêm trong hình nón Pellet Spread Degrees quanh trục đó.
    /// </summary>
    private void FireRaycast()
    {
        Transform origin = muzzlePoint != null ? muzzlePoint : transform;
        Vector3 axis = ApplySpread(origin.forward);
        int pellets = Mathf.Max(1, pelletsPerShot);
        bool impactSoundPlayed = false;

        for (int i = 0; i < pellets; i++)
        {
            Vector3 direction = pellets > 1 ? ApplyPelletSpread(axis) : axis;

            if (!Physics.Raycast(origin.position, direction, out RaycastHit hit, maxRange, hittableLayers, QueryTriggerInteraction.Ignore))
                continue;

            IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
            damageable?.ApplyDamage(damage, hit);

            // Báo cho LightTarget / PopUpTarget biết bia nào bị trúng, bởi súng nào
            ObserverManager.InvokeEvent(CONST.PLAYER_SHOOT_TARGET, this, hit.collider.gameObject, hit);

            PlayImpactEffect(hit, !singleImpactSoundPerShot || !impactSoundPlayed);
            impactSoundPlayed = true;
        }
    }

    /// <summary>Độ lệch của cả chùm theo độ nóng nòng (recoil), áp 1 lần cho mỗi phát bắn.</summary>
    private Vector3 ApplySpread(Vector3 forward)
    {
        float spreadDegrees = recoilController != null ? recoilController.CurrentSpreadDegrees : 0f;

        if (spreadDegrees <= 0f)
            return forward;

        return Deflect(forward, Random.Range(0f, spreadDegrees));
    }

    /// <summary>Lệch 1 hạt ghém ra khỏi trục chùm, rải trong hình nón Pellet Spread Degrees.</summary>
    private Vector3 ApplyPelletSpread(Vector3 axis)
    {
        if (pelletSpreadDegrees <= 0f)
            return axis;

        // Lấy mẫu trên mặt đĩa: số mũ 0.5 cho phân bố đều theo diện tích (Random.value thẳng sẽ
        // dồn cục ở tâm), số mũ cao hơn thì kéo dần chùm về tâm.
        float unit = Mathf.Pow(Random.value, 0.5f * Mathf.Max(0.1f, pelletCenterBias));
        return Deflect(axis, unit * pelletSpreadDegrees);
    }

    /// <summary>
    /// Nghiêng axis đi angle độ theo một hướng ngẫu nhiên quanh nó. Trục nghiêng lấy vuông góc
    /// với axis chứ không dùng Vector3.up: bắn thẳng lên/xuống thì world up suy biến, mất tản.
    /// </summary>
    private static Vector3 Deflect(Vector3 axis, float angle)
    {
        if (angle <= 0f)
            return axis;

        Vector3 reference = Mathf.Abs(axis.y) < 0.99f ? Vector3.up : Vector3.right;
        Vector3 tiltAxis = Vector3.Cross(axis, reference).normalized;

        if (tiltAxis.sqrMagnitude < 0.0001f)
            return axis;

        return Quaternion.AngleAxis(Random.Range(0f, 360f), axis)
               * Quaternion.AngleAxis(angle, tiltAxis)
               * axis;
    }

    private void PlayImpactEffect(RaycastHit hit, bool playImpactSound)
    {
        // Lỗ đạn + tiếng va chạm: tự bỏ qua nếu scene không có BulletDecalManager.
        BulletDecalManager.SpawnImpact(hit, playImpactSound);

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
