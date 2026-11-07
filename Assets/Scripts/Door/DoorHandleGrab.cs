using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

namespace VRSwat.Doors
{
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class DoorHandleGrab : MonoBehaviour
    {
        [SerializeField] DoorController door;
        [SerializeField] float breakDistance = 0.4f;   // tay cách tay nắm xa hơn mức này thì tự buông

        XRSimpleInteractable interactable;
        IXRSelectInteractor interactor;
        Transform hand;
        HapticImpulsePlayer haptics;
        float grabOffset;

        void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            if (!door) door = GetComponentInParent<DoorController>();
        }

        void OnEnable()
        {
            interactable.selectEntered.AddListener(OnGrab);
            interactable.selectExited.AddListener(OnRelease);
            door.LockedRattle += OnRattle;
        }

        void OnDisable()
        {
            interactable.selectEntered.RemoveListener(OnGrab);
            interactable.selectExited.RemoveListener(OnRelease);
            door.LockedRattle -= OnRattle;
        }

        void OnGrab(SelectEnterEventArgs args)
        {
            var t = args.interactorObject.transform;
            // chặn grab bằng tia từ xa: tay phải ở ngay tay nắm
            if (Vector3.Distance(t.position, transform.position) > breakDistance || !door.BeginGrab(this))
            {
                interactable.interactionManager.SelectExit(args.interactorObject, (IXRSelectInteractable)interactable);
                return;
            }
            interactor = args.interactorObject;
            hand = t;
            haptics = hand.GetComponentInParent<HapticImpulsePlayer>();
            grabOffset = door.Angle - HandAngle();
        }

        void OnRelease(SelectExitEventArgs args)
        {
            if (hand == null) return;
            door.EndGrab(this);
            hand = null;
            interactor = null;
        }

        void Update()
        {
            if (hand == null) return;

            if (Vector3.Distance(hand.position, transform.position) > breakDistance)
            {
                interactable.interactionManager.SelectExit(interactor, (IXRSelectInteractable)interactable);
                return;
            }

            float wanted = HandAngle() + grabOffset;
            float target = door.Angle + Mathf.DeltaAngle(door.Angle, wanted);
            door.ProposeAngle(this, target);
        }

        // Góc của tay quanh trục Y của bản lề, trong không gian của DoorRoot.
        // Khi đóng, cánh cửa nằm dọc +X tính từ bản lề.
        float HandAngle()
        {
            Vector3 local = door.transform.InverseTransformPoint(hand.position) - door.HingeLocalPosition;
            return Mathf.Atan2(-local.z, local.x) * Mathf.Rad2Deg;   // + = cửa đi về -Z, - = cửa đi về +Z
        }

        void OnRattle(DoorController d)
        {
            if (hand != null && haptics) haptics.SendHapticImpulse(0.6f, 0.08f);
        }
    }
}