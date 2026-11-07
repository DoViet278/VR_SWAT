using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Tay nắm / quả đấm cửa. Gắn vào object con của một <see cref="MovablePart"/> (HingedDoor hoặc
/// SlidingPart). Nắm vào thì:
/// 1. quả đấm quay (animation), tới ngưỡng thì nhả chốt - đang khoá thì chỉ lắc ổ khoá;
/// 2. cửa đi theo tay bằng lực vật lý, nên cửa vẫn nặng, vẫn bị vật chắn sau cửa cản lại,
///    và vẫn đập vào người chơi nếu kéo mạnh.
/// </summary>
public class DoorHandle : XRBaseInteractable
{
    [Header("Bộ phận được điều khiển")]
    [Tooltip("Cửa / ngăn kéo mà tay nắm này mở. Để trống sẽ tự tìm ở các object cha.")]
    [SerializeField] private MovablePart part;

    [Header("Collider")]
    [Tooltip("Đặt collider của tay nắm thành trigger. Tay nắm là con của Rigidbody cửa, nếu collider của nó tham gia va chạm thật thì cửa sẽ bị rung khi collider bị script dời đi.")]
    [SerializeField] private bool collidersAsTriggers = true;

    [Header("Quả đấm quay")]
    [Tooltip("Object quay khi nắm. Để trống = không có animation quay.")]
    [SerializeField] private Transform knob;
    [Tooltip("Góc quay khi quay hết (độ, local euler).")]
    [SerializeField] private Vector3 knobTurnEuler = new Vector3(0f, 0f, -40f);
    [Tooltip("Thời gian quay hết (giây).")]
    [SerializeField] private float knobTurnTime = 0.12f;
    [Tooltip("Thời gian quả đấm tự trả về khi thả tay (giây).")]
    [SerializeField] private float knobReturnTime = 0.18f;
    [Tooltip("Quay tới mức này (0-1) thì nhả chốt.")]
    [Range(0.1f, 1f)] [SerializeField] private float unlatchAt = 0.7f;
    [Tooltip("Cửa đang khoá thì quả đấm chỉ quay được tới mức này rồi cứng lại.")]
    [Range(0f, 0.9f)] [SerializeField] private float lockedKnobAmount = 0.3f;

    [Header("Cửa đi theo tay")]
    [Tooltip("Điểm trên cửa mà tay kéo. Để trống = dùng transform của chính tay nắm.")]
    [SerializeField] private Transform grabAnchor;
    [Tooltip("Độ cứng khi cửa bám theo tay.")]
    [SerializeField] private float followStiffness = 70f;
    [Tooltip("Giảm dao động. Thấp quá thì cửa đuổi theo tay kiểu lò xo.")]
    [SerializeField] private float followDamping = 9f;
    [Tooltip("Gia tốc tối đa (m/s²). Thấp = cửa nặng, bị chặn là kéo không nổi.")]
    [SerializeField] private float maxFollowAcceleration = 150f;

    private Quaternion knobRestRotation;
    private float knobAmount;
    private bool triedUnlatchThisGrab;
    private Vector3 grabOffset;

    public MovablePart Part => part;

    private Transform GrabAnchor => grabAnchor != null ? grabAnchor : transform;

    private IXRSelectInteractor FirstInteractor => interactorsSelecting.Count > 0 ? interactorsSelecting[0] : null;

    protected override void Awake()
    {
        base.Awake();

        if (part == null)
            part = GetComponentInParent<MovablePart>();

        if (knob != null)
            knobRestRotation = knob.localRotation;

        if (collidersAsTriggers)
            MakeCollidersTriggers();
    }

    // Cùng lý do như tay cò súng: collider là con của một Rigidbody khác mà bị script dời vị trí
    // sẽ xoá contact cache của Rigidbody đó mỗi frame -> cửa rung lập bập khi tì vào sàn/tường.
    private void MakeCollidersTriggers()
    {
        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
        {
            // MeshCollider chỉ làm trigger được khi convex.
            if (collider is MeshCollider meshCollider && !meshCollider.convex)
                continue;

            collider.isTrigger = true;
        }
    }

    protected override void OnSelectEntered(SelectEnterEventArgs args)
    {
        base.OnSelectEntered(args);

        triedUnlatchThisGrab = false;

        if (part != null)
            part.BeginHold();

        Transform hand = args.interactorObject?.GetAttachTransform(this);

        // Ghi lại lệch giữa tay và tay nắm lúc nắm, để cửa không bị giật một cái khi vừa nắm.
        grabOffset = hand != null ? GrabAnchor.position - hand.position : Vector3.zero;
    }

    protected override void OnSelectExited(SelectExitEventArgs args)
    {
        base.OnSelectExited(args);

        if (part != null)
            part.EndHold();
    }

    private void Update()
    {
        UpdateKnob();
    }

    private void UpdateKnob()
    {
        bool locked = part != null && part.IsLocked;
        float target = 0f;

        if (isSelected)
            target = locked ? lockedKnobAmount : 1f;

        float duration = target > knobAmount ? knobTurnTime : knobReturnTime;
        knobAmount = Mathf.MoveTowards(knobAmount, target, Time.deltaTime / Mathf.Max(0.01f, duration));

        ApplyKnobRotation();

        if (!isSelected || triedUnlatchThisGrab)
            return;

        // Cửa khoá: quả đấm quay tới điểm cứng rồi báo khoá. Cửa thường: quay quá ngưỡng là nhả chốt.
        float threshold = locked ? lockedKnobAmount : unlatchAt;

        if (knobAmount >= threshold - 0.001f)
        {
            triedUnlatchThisGrab = true;
            part?.TryUnlatch(FirstInteractor);
        }
    }

    private void ApplyKnobRotation()
    {
        if (knob == null)
            return;

        Quaternion target = knobRestRotation * Quaternion.Euler(knobTurnEuler * knobAmount);

        // Chỉ ghi khi thực sự đổi: ghi transform mỗi frame sẽ đánh thức Rigidbody cửa liên tục.
        if (Quaternion.Angle(knob.localRotation, target) < 0.01f)
            return;

        knob.localRotation = target;
    }

    private void FixedUpdate()
    {
        if (!isSelected || part == null || part.IsLocked || part.IsLatched)
            return;

        Rigidbody body = part.Body;

        if (body == null || body.isKinematic)
            return;

        Transform hand = FirstInteractor?.GetAttachTransform(this);

        if (hand == null)
            return;

        Vector3 anchor = GrabAnchor.position;
        Vector3 target = hand.position + grabOffset;
        Vector3 pointVelocity = body.GetPointVelocity(anchor);

        // Lò xo - giảm chấn tính theo gia tốc rồi mới nhân mass, để đổi mass cửa không phải
        // chỉnh lại followStiffness.
        Vector3 acceleration = (target - anchor) * followStiffness - pointVelocity * followDamping;
        acceleration = Vector3.ClampMagnitude(acceleration, maxFollowAcceleration);

        body.AddForceAtPosition(acceleration * body.mass, anchor, ForceMode.Force);
    }
}
