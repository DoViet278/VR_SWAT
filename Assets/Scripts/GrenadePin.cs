using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Chốt lựu đạn: kéo ra thì lựu đạn bắt đầu cháy ngòi.
///
/// Chốt là một XRGrabInteractable RIÊNG, có Rigidbody riêng (kinematic lúc còn gắn trên quả lựu
/// đạn). Chính chỗ đó sinh ra lỗi người chơi bị đẩy lùi khi cầm lựu đạn lại gần người:
/// XRGrabInteractable.StartIgnoringCharacterCollision của quả lựu đạn chỉ tắt va chạm cho những
/// collider có attachedRigidbody đúng bằng Rigidbody của chính nó (xem XRGrabInteractable.Awake,
/// chỗ lọc m_RigidbodyColliders). Collider của chốt thuộc Rigidbody của chốt, nên không bao giờ
/// được tắt — mà nó lại là Rigidbody kinematic, tức khối lượng vô hạn. Đưa quả lựu đạn vào trong
/// capsule CharacterController là overlap recovery của CharacterController.Move() đẩy người chơi
/// ra khỏi cái chốt đó, mỗi frame một chút.
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
public class GrenadePin : MonoBehaviour
{
    [SerializeField] private VRGrenade grenade;
    [SerializeField] private Collider pinCollider;
    [SerializeField] private float pullDistance = 0.08f;
    [SerializeField] private float droppedPinLifetime = 10f;

    [Tooltip("Chốt chỉ cần GRAB được, không cần sinh va chạm: nó được tay kéo đi bằng transform " +
             "(Movement Type = Instantaneous), và việc phát hiện đã kéo ra chỉ là đo khoảng cách. " +
             "Bật cái này thì chốt bị loại khỏi mọi cặp va chạm trong lúc còn gắn trên quả lựu " +
             "đạn, nên không đẩy được người chơi nữa.\n\n" +
             "Không dùng isTrigger: SphereInteractionCaster của Near-Far Interactor bỏ qua trigger " +
             "nên tay sẽ không kéo được chốt. Không dùng Physics.IgnoreCollision: cờ đó không chặn " +
             "được overlap recovery của CharacterController, lại bị Unity xoá mỗi khi collider " +
             "disable rồi enable lại — mà SetPinGrabbable bật/tắt collider này liên tục. " +
             "excludeLayers thì PhysX không sinh contact, mà query của interactor vẫn thấy (đúng " +
             "cách ChargingHandle làm với collider tay cò).")]
    [SerializeField] private bool pinColliderIgnoresPhysics = true;

    private XRGrabInteractable pinInteractable;
    private XRGrabInteractable grenadeInteractable;
    private Rigidbody pinBody;
    private Vector3 restLocalPosition;
    private Quaternion restLocalRotation;
    private int pinColliderExcludeLayers;
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

        if (pinCollider != null)
        {
            pinColliderExcludeLayers = pinCollider.excludeLayers;

            if (pinColliderIgnoresPhysics)
                pinCollider.excludeLayers = Physics.AllLayers;
        }
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

    /// <summary>
    /// Rút chốt bằng code, không cần tay kéo (dùng cho SimulatorTestHelper khi test trên XR Device
    /// Simulator). Đi đúng đường Pull() như khi kéo tay: báo lựu đạn, thả chốt rơi xuống.
    /// </summary>
    public void ForcePull()
    {
        if (!pulled)
            Pull();
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

        // Chốt đã rời quả lựu đạn và thành vật rơi tự do: trả lại va chạm để nó nằm được trên
        // sàn. Lúc này nó là Rigidbody động, khối lượng hữu hạn, nên không đẩy người chơi nữa —
        // CharacterController đẩy nó mới đúng.
        pinCollider.excludeLayers = pinColliderExcludeLayers;

        Destroy(gameObject, droppedPinLifetime);
    }
}
