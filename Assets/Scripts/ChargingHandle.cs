using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class ChargingHandle : XRBaseInteractable
{
    public enum CycleMode
    {
        /// <summary>Kéo về quá ngưỡng là lên nòng luôn (rifle, pistol).</summary>
        ChargeOnPull,
        /// <summary>Hai nhịp: kéo về sau nhả vỏ, đẩy lên mới lên nòng (shotgun pump).</summary>
        PumpAction
    }

    [Header("Charging Handle")]
    [Tooltip("Object that slides. Defaults to this transform.")]
    [SerializeField] private Transform handle;
    [Tooltip("Rest position of the handle. Defaults to the handle's starting position.")]
    [SerializeField] private Transform restPoint;
    [Tooltip("Fully pulled back position (PositionRefill).")]
    [SerializeField] private Transform refillPoint;
    [SerializeField] private GunAmmoManager ammoManager;

    [Header("Tuning")]
    [Tooltip("PumpAction: kéo về sau nhả vỏ, đẩy lên mới lên nòng. Dùng cho shotgun.")]
    [SerializeField] private CycleMode cycleMode = CycleMode.ChargeOnPull;
    [Range(0.5f, 1f)] [SerializeField] private float chargeThreshold = 0.95f;
    [Tooltip("PumpAction: đẩy pump về dưới ngưỡng này thì tính là đã lên nòng.")]
    [Range(0f, 0.5f)] [SerializeField] private float pumpReturnThreshold = 0.2f;
    [Tooltip("Return speed in units per second after release.")]
    [SerializeField] private float returnSpeed = 0.6f;

    [Header("Auto Cycle (khi bắn)")]
    [Tooltip("Thời gian tay cò giật về PositionRefill sau mỗi phát (giây).")]
    [SerializeField] private float cycleOutTime = 0.08f;
    [Tooltip("Thời gian trả về vị trí nghỉ (giây).")]
    [SerializeField] private float cycleReturnTime = 0.12f;
    [Tooltip("Quãng đường giật, 1 = chạy hết tới PositionRefill.")]
    [Range(0f, 1f)] [SerializeField] private float cycleTravel = 1f;
    [Tooltip("Hết đạn thì tay cò khóa lại ở PositionRefill cho tới khi người chơi kéo lại.")]
    [SerializeField] private bool lockOpenWhenEmpty = true;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip chargeClip;
    [Tooltip("PumpAction: tiếng kéo pump về sau (nhả vỏ).")]
    [SerializeField] private AudioClip ejectClip;
    [Tooltip("PumpAction: tiếng đẩy pump lên (lên nòng).")]
    [SerializeField] private AudioClip chamberClip;

    [Header("Haptics")]
    [SerializeField] private GunHapticFeedback hapticFeedback;

    public UnityEvent OnCharged;
    [Tooltip("PumpAction: vừa kéo pump hết về sau.")]
    public UnityEvent OnPumpPulled;

    public bool IsLockedOpen => lockedOpen;

    private Vector3 restLocal;
    private Vector3 refillLocal;
    private Transform handleParent;
    private bool grabbed;
    private bool chargedThisPull;
    private bool pumpPulled;
    private bool cycling;
    private float cycleTime;
    private bool pendingLockOpen;
    private bool lockedOpen;
    private float cycleSpeedFactor = 1f;

    protected override void Awake()
    {
        base.Awake();

        if (handle == null)
            handle = transform;

        handleParent = handle.parent;

        restLocal = restPoint != null ? ToParentLocal(restPoint.position) : handle.localPosition;
        refillLocal = refillPoint != null ? ToParentLocal(refillPoint.position) : restLocal;

        if (ammoManager == null)
            ammoManager = GetComponentInParent<GunAmmoManager>();

        if (hapticFeedback == null)
            hapticFeedback = GetComponentInParent<GunHapticFeedback>();

        IgnoreGunColliders();
    }

    // The handle must not collide with the gun body it is attached to.
    private void IgnoreGunColliders()
    {
        Transform root = ammoManager != null ? ammoManager.transform : transform.root;
        Collider[] gunColliders = root.GetComponentsInChildren<Collider>(true);
        Collider[] handleColliders = handle.GetComponentsInChildren<Collider>(true);

        foreach (Collider handleCollider in handleColliders)
        {
            foreach (Collider gunCollider in gunColliders)
            {
                if (gunCollider != handleCollider)
                    Physics.IgnoreCollision(handleCollider, gunCollider, true);
            }
        }
    }

    protected override void OnSelectEntered(SelectEnterEventArgs args)
    {
        base.OnSelectEntered(args);
        grabbed = true;
        chargedThisPull = false;
    }

    protected override void OnSelectExited(SelectExitEventArgs args)
    {
        base.OnSelectExited(args);
        grabbed = false;
    }

    private void Update()
    {
        if (grabbed && interactorsSelecting.Count > 0)
        {
            // Kéo tay cò là nhả chốt khóa.
            cycling = false;
            lockedOpen = false;
            FollowHand(interactorsSelecting[0].GetAttachTransform(this).position);
        }
        else if (cycling)
            AdvanceCycle();
        else if (lockedOpen)
            handle.localPosition = restLocal + (refillLocal - restLocal) * cycleTravel;
        else
            ReturnToRest();
    }

    private void FollowHand(Vector3 handWorld)
    {
        Vector3 axis = refillLocal - restLocal;
        float length = axis.magnitude;
        if (length < 1e-5f)
            return;

        // Project the hand onto the rest -> refill line, clamped to its ends.
        float t = Mathf.Clamp01(Vector3.Dot(ToParentLocal(handWorld) - restLocal, axis / length) / length);
        handle.localPosition = restLocal + axis * t;

        if (cycleMode == CycleMode.PumpAction)
        {
            UpdatePumpStroke(t);
            return;
        }

        if (t >= chargeThreshold && !chargedThisPull)
            Charge();
        else if (t < chargeThreshold * 0.5f)
            chargedThisPull = false;
    }

    // Shotgun: kéo về sau nhả vỏ, đẩy lên mới lên nòng. Hai nhịp tách biệt để người chơi cảm nhận được.
    private void UpdatePumpStroke(float t)
    {
        if (!pumpPulled && t >= chargeThreshold)
        {
            pumpPulled = true;
            ammoManager?.ExtractChamber();
            PlayClip(ejectClip);
            hapticFeedback?.PlayPumpPull(CurrentInteractor);
            OnPumpPulled?.Invoke();
        }
        else if (pumpPulled && t <= pumpReturnThreshold)
        {
            pumpPulled = false;
            ammoManager?.Chamber();
            PlayClip(chamberClip);
            hapticFeedback?.PlayPumpPush(CurrentInteractor);
            OnCharged?.Invoke();
        }
    }

    private object CurrentInteractor => interactorsSelecting.Count > 0 ? interactorsSelecting[0] : null;

    private void PlayClip(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }

    // Called by Gun on each shot: the action cycles itself, no manual pull needed.
    public void PlayCycle()
    {
        PlayCycle(false);
    }

    /// <param name="emptyAfterShot">Phát vừa rồi là phát cuối: khóa tay cò ở PositionRefill.</param>
    /// <param name="maxDuration">Thời gian tối đa cho một chu kỳ (nhịp bắn liên thanh). 0 = không giới hạn.</param>
    public void PlayCycle(bool emptyAfterShot, float maxDuration = 0f)
    {
        if (grabbed)
            return;

        cycling = true;
        cycleTime = 0f;
        pendingLockOpen = emptyAfterShot && lockOpenWhenEmpty;

        // Bắn liên thanh: ép chu kỳ gọn trong nhịp bắn để không bị reset giữa đường.
        float total = Mathf.Max(0.0001f, cycleOutTime + cycleReturnTime);
        cycleSpeedFactor = maxDuration > 0f && total > maxDuration ? maxDuration / total : 1f;
    }

    private void AdvanceCycle()
    {
        cycleTime += Time.deltaTime;

        float outTime = Mathf.Max(0.0001f, cycleOutTime * cycleSpeedFactor);
        float returnTime = Mathf.Max(0.0001f, cycleReturnTime * cycleSpeedFactor);
        float t;

        if (cycleTime < outTime)
        {
            t = cycleTime / outTime;
        }
        else if (pendingLockOpen)
        {
            cycling = false;
            lockedOpen = true;
            t = 1f;
        }
        else if (cycleTime < outTime + returnTime)
        {
            t = 1f - (cycleTime - outTime) / returnTime;
        }
        else
        {
            cycling = false;
            handle.localPosition = restLocal;
            return;
        }

        handle.localPosition = restLocal + (refillLocal - restLocal) * (t * cycleTravel);
    }

    private void ReturnToRest()
    {
        handle.localPosition = Vector3.MoveTowards(handle.localPosition, restLocal, returnSpeed * Time.deltaTime);

        // Nhả tay giữa cú pump: lò xo đẩy pump về chỗ nghỉ, nhịp lên nòng vẫn phải chạy.
        if (cycleMode == CycleMode.PumpAction && pumpPulled)
            UpdatePumpStroke(CurrentTravel);
    }

    /// <summary>Vị trí hiện tại của tay cò trên trục rest -> refill, 0 = nghỉ, 1 = kéo hết.</summary>
    private float CurrentTravel
    {
        get
        {
            Vector3 axis = refillLocal - restLocal;
            float length = axis.magnitude;

            if (length < 1e-5f)
                return 0f;

            return Mathf.Clamp01(Vector3.Dot(handle.localPosition - restLocal, axis / length) / length);
        }
    }

    private void Charge()
    {
        chargedThisPull = true;
        ammoManager?.Chamber();

        if (audioSource != null && chargeClip != null)
            audioSource.PlayOneShot(chargeClip);

        OnCharged?.Invoke();
    }

    private Vector3 ToParentLocal(Vector3 world)
    {
        return handleParent != null ? handleParent.InverseTransformPoint(world) : world;
    }
}
