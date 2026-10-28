using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRGrabInteractable))]
public class GrenadePin : MonoBehaviour
{
    [SerializeField] private VRGrenade grenade;
    [SerializeField] private Collider pinCollider;
    [SerializeField] private float pullDistance = 0.08f;
    [SerializeField] private float droppedPinLifetime = 10f;

    private XRGrabInteractable pinInteractable;
    private XRGrabInteractable grenadeInteractable;
    private Rigidbody pinBody;
    private Vector3 restLocalPosition;
    private Quaternion restLocalRotation;
    private bool pulled;

    private void Awake()
    {
        pinInteractable = GetComponent<XRGrabInteractable>();
        pinBody = GetComponent<Rigidbody>();

        if (grenade == null)
            grenade = GetComponentInParent<VRGrenade>();

        grenadeInteractable = grenade.GetComponent<XRGrabInteractable>();
        restLocalPosition = transform.localPosition;
        restLocalRotation = transform.localRotation;
    }

    private void OnEnable()
    {
        grenadeInteractable.selectEntered.AddListener(HandleGrenadeGrabbed);
        grenadeInteractable.selectExited.AddListener(HandleGrenadeReleased);
        pinInteractable.selectExited.AddListener(HandlePinReleased);
        SetPinGrabbable(grenadeInteractable.isSelected);
    }

    private void OnDisable()
    {
        grenadeInteractable.selectEntered.RemoveListener(HandleGrenadeGrabbed);
        grenadeInteractable.selectExited.RemoveListener(HandleGrenadeReleased);
        pinInteractable.selectExited.RemoveListener(HandlePinReleased);
    }

    private void Update()
    {
        if (pulled || !pinInteractable.isSelected)
            return;

        Vector3 restWorld = grenade.transform.TransformPoint(restLocalPosition);

        if (Vector3.Distance(transform.position, restWorld) > pullDistance)
            Pull();
    }

    private void HandleGrenadeGrabbed(SelectEnterEventArgs args)
    {
        SetPinGrabbable(true);
    }

    private void HandleGrenadeReleased(SelectExitEventArgs args)
    {
        SetPinGrabbable(false);
    }

    private void HandlePinReleased(SelectExitEventArgs args)
    {
        if (pulled)
            return;

        transform.SetLocalPositionAndRotation(restLocalPosition, restLocalRotation);
    }

    private void SetPinGrabbable(bool grabbable)
    {
        if (pulled)
            return;

        if (pinCollider != null)
            pinCollider.enabled = grabbable;
    }

    private void Pull()
    {
        pulled = true;
        grenade.OnPinPulled();

        if (pinInteractable.interactionManager != null)
            pinInteractable.interactionManager.CancelInteractableSelection((IXRSelectInteractable)pinInteractable);

        pinInteractable.enabled = false;
        StartCoroutine(DropRoutine());
    }

    private IEnumerator DropRoutine()
    {
        yield return null;

        transform.SetParent(null);
        pinBody.isKinematic = false;
        pinBody.useGravity = true;
        pinCollider.enabled = true;
        Destroy(gameObject, droppedPinLifetime);
    }
}
