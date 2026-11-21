using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(XRGrabInteractable))]
public class RifleChargingHandle : MonoBehaviour
{
    [SerializeField] private Transform pulledPosition;
    [SerializeField] private float returnSpeed = 0.6f;
    [SerializeField] [Range(0f, 1f)] private float chamberThreshold = 0.9f;

    public UnityEvent OnChambered;

    private XRGrabInteractable grabInteractable;
    private IXRSelectInteractor currentInteractor;
    private Vector3 restLocalPosition;
    private Vector3 pulledLocalPosition;
    private bool hasChamberedThisPull;
    private bool returningToRest;

    private void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        grabInteractable.trackPosition = false;
        grabInteractable.trackRotation = false;

        restLocalPosition = transform.localPosition;

        IgnoreParentCollisions();
    }

    private void IgnoreParentCollisions()
    {
        if (transform.parent == null)
            return;

        var myColliders = GetComponentsInChildren<Collider>();
        var parentColliders = transform.parent.GetComponentsInParent<Collider>();

        foreach (var myCollider in myColliders)
        {
            foreach (var parentCollider in parentColliders)
            {
                Physics.IgnoreCollision(myCollider, parentCollider, true);
            }
        }
    }

    private void Start()
    {
        if (pulledPosition == null)
        {
            Debug.LogWarning($"{nameof(RifleChargingHandle)} on {name} is missing a Pulled Position reference.", this);
            enabled = false;
            return;
        }

        pulledLocalPosition = transform.parent != null
            ? transform.parent.InverseTransformPoint(pulledPosition.position)
            : pulledPosition.position;
    }

    private void OnEnable()
    {
        grabInteractable.selectEntered.AddListener(OnGrabbed);
        grabInteractable.selectExited.AddListener(OnReleased);
    }

    private void OnDisable()
    {
        grabInteractable.selectEntered.RemoveListener(OnGrabbed);
        grabInteractable.selectExited.RemoveListener(OnReleased);
    }

    private void OnGrabbed(SelectEnterEventArgs args)
    {
        currentInteractor = args.interactorObject;
        returningToRest = false;
        hasChamberedThisPull = false;
    }

    private void OnReleased(SelectExitEventArgs args)
    {
        currentInteractor = null;
        returningToRest = true;
    }

    private void Update()
    {
        if (currentInteractor != null)
            FollowHandAlongRail();
        else if (returningToRest)
            ReturnToRest();
    }

    private void FollowHandAlongRail()
    {
        Vector3 handWorldPosition = currentInteractor.GetAttachTransform(grabInteractable).position;
        Vector3 handLocalPosition = transform.parent != null
            ? transform.parent.InverseTransformPoint(handWorldPosition)
            : handWorldPosition;

        Vector3 rail = pulledLocalPosition - restLocalPosition;
        float railLength = rail.magnitude;
        Vector3 railDirection = rail / railLength;

        float t = Mathf.Clamp01(Vector3.Dot(handLocalPosition - restLocalPosition, railDirection) / railLength);
        transform.localPosition = restLocalPosition + railDirection * railLength * t;

        if (!hasChamberedThisPull && t >= chamberThreshold)
        {
            hasChamberedThisPull = true;
            OnChambered?.Invoke();
        }
    }

    private void ReturnToRest()
    {
        transform.localPosition = Vector3.MoveTowards(transform.localPosition, restLocalPosition, returnSpeed * Time.deltaTime);

        if ((transform.localPosition - restLocalPosition).sqrMagnitude < 0.0000001f)
        {
            transform.localPosition = restLocalPosition;
            returningToRest = false;
        }
    }
}
