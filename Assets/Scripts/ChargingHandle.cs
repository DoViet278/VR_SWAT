using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class ChargingHandle : XRBaseInteractable
{
    [Header("Charging Handle")]
    [Tooltip("Object that slides. Defaults to this transform.")]
    [SerializeField] private Transform handle;
    [Tooltip("Rest position of the handle. Defaults to the handle's starting position.")]
    [SerializeField] private Transform restPoint;
    [Tooltip("Fully pulled back position (PositionRefill).")]
    [SerializeField] private Transform refillPoint;
    [SerializeField] private GunAmmoManager ammoManager;

    [Header("Tuning")]
    [Range(0.5f, 1f)] [SerializeField] private float chargeThreshold = 0.95f;
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

    public UnityEvent OnCharged;

    public bool IsLockedOpen => lockedOpen;

    private Vector3 restLocal;
    private Vector3 refillLocal;
    private Transform handleParent;
    private bool grabbed;
    private bool chargedThisPull;
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

        if (t >= chargeThreshold && !chargedThisPull)
            Charge();
        else if (t < chargeThreshold * 0.5f)
            chargedThisPull = false;
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
