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

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip chargeClip;

    public UnityEvent OnCharged;

    private Vector3 restLocal;
    private Vector3 refillLocal;
    private Transform handleParent;
    private bool grabbed;
    private bool chargedThisPull;

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
            FollowHand(interactorsSelecting[0].GetAttachTransform(this).position);
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
