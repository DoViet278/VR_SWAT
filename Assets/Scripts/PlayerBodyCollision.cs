using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Chặn người chơi đi xuyên qua vật thể bằng chính chuyển động vật lý (đi bộ thật / dịch người /
/// rướn đầu), thứ mà XRI không xử lý.
///
/// Vì sao cần: XR Body Transformer + CharacterController của XR Origin chỉ chặn được phần di
/// chuyển do locomotion (cần analog, teleport, trọng lực) vì nó đi qua CharacterController.Move.
/// Còn khi người chơi bước thật trong phòng hoặc rướn người qua cái bàn, camera bị tracking kéo
/// đi - không qua Move - nên không gì chặn lại: đầu và thân lọt thẳng vào trong bàn / tường.
///
/// Cách làm: mỗi LateUpdate (sau khi tracking đã đặt camera) dựng một capsule "thân người" ngay
/// dưới camera, tìm vật đang chồng lên nó rồi đẩy XR Origin ra đúng bằng độ lún
/// (Physics.ComputePenetration). Người chơi rướn vào bàn thì tầm nhìn bị trượt ra - đúng kiểu
/// body collision chuẩn của VR - thay vì lọt qua.
///
/// Gắn trên GameObject của XR Origin.
/// </summary>
[DisallowMultipleComponent]
// Chạy sau TrackedPoseDriver / XROrigin để đọc được pose camera của frame này.
[DefaultExecutionOrder(1000)]
public class PlayerBodyCollision : MonoBehaviour
{
    [Header("Rig")]
    [Tooltip("Để trống sẽ tự tìm trên cùng GameObject rồi tới cả scene.")]
    [SerializeField] private XROrigin xrOrigin;

    [Header("Chặn đi bộ thật")]
    [Tooltip("Tắt nếu muốn tạm cho người chơi đi xuyên (debug).")]
    [SerializeField] private bool blockPhysicalWalking = true;

    [Tooltip("Bán kính thân người (m). Hẹp hơn vai thật một chút cho đỡ bí khi qua cửa.")]
    [SerializeField] private float bodyRadius = 0.15f;

    [Tooltip("Đáy capsule cách sàn bao nhiêu (m). Để chân vẫn bước qua được bậc / vật thấp.")]
    [SerializeField] private float bodyBottomOffset = 0.3f;

    [Tooltip("Chiều cao thân tối thiểu (m), tính từ sàn tới camera. Chặn trường hợp camera " +
             "chưa có tracking (y = 0) làm capsule bị bóp dẹt.")]
    [SerializeField] private float minBodyHeight = 0.4f;

    [Tooltip("Những layer được phép chặn người chơi.")]
    [SerializeField] private LayerMask blockingLayers = ~0;

    [Tooltip("Bỏ qua vật thể động (súng, cửa, thùng đạn...) để người chơi đẩy chúng chứ không bị " +
             "chúng đẩy ngược lại.")]
    [SerializeField] private bool ignoreDynamicRigidbodies = true;

    [Tooltip("Chỉ đẩy theo phương ngang. Phương dọc để Gravity Provider lo, kẻo bị bắn lên nóc bàn.")]
    [SerializeField] private bool horizontalOnly = true;

    [Tooltip("Dùng cùng với Horizontal Only: vật mà hướng thoát ra dốc hơn mức này (|dot| với trục " +
             "đứng) coi như sàn / mặt bàn và được bỏ qua hẳn, cho người chơi rướn qua. 0.7 ~ 45 độ.")]
    [Range(0.1f, 0.99f)]
    [SerializeField] private float maxVerticalSeparationDot = 0.7f;

    [Tooltip("Vùng chết (m). Lún ít hơn mức này thì không đẩy, tránh rung / trôi vì sai số contact offset.")]
    [SerializeField] private float skinTolerance = 0.005f;

    [Tooltip("Bỏ qua vật đang được cầm hoặc đang cắm trong socket (súng trên tay, băng đạn ở hông). " +
             "Chúng luôn dính sát người nên nếu tính vào sẽ đẩy người chơi trôi đi liên tục.")]
    [SerializeField] private bool ignoreSelectedInteractables = true;

    [Tooltip("Giới hạn độ đẩy mỗi frame (m), tránh giật mạnh gây say khi lún sâu.")]
    [SerializeField] private float maxPushPerFrame = 0.5f;

    [Tooltip("Số vòng giải va chạm mỗi frame. 2 là đủ cho góc tường.")]
    [SerializeField] private int solverIterations = 2;

    private const float k_MinPushSqr = 1e-10f;

    private readonly Collider[] overlaps = new Collider[32];

    // Collider chỉ dùng để tính độ lún, không tham gia mô phỏng: excludeLayers = tất cả layer nên
    // PhysX không sinh contact cho nó. (Collider đang tắt thì ComputePenetration không dùng được,
    // nên không thể chỉ disable nó.)
    private CapsuleCollider probe;
    private Transform probeTransform;

    private void Awake()
    {
        if (xrOrigin == null)
            xrOrigin = GetComponent<XROrigin>();

        if (xrOrigin == null)
            xrOrigin = FindFirstObjectByType<XROrigin>();
    }

    private void OnEnable()
    {
        if (xrOrigin == null)
            xrOrigin = GetComponent<XROrigin>() ?? FindFirstObjectByType<XROrigin>();

        if (OriginTransform == null || CameraTransform == null)
        {
            Debug.LogWarning(nameof(PlayerBodyCollision) + ": thiếu XR Origin hoặc camera, tự tắt.", this);
            enabled = false;
            return;
        }

        EnsureProbe();
    }

    private void OnDisable()
    {
        if (probe != null)
            probe.enabled = false;
    }

    private void OnDestroy()
    {
        if (probeTransform != null)
            Destroy(probeTransform.gameObject);
    }

    private Transform OriginTransform
    {
        get
        {
            if (xrOrigin == null)
                return null;

            return xrOrigin.Origin != null ? xrOrigin.Origin.transform : xrOrigin.transform;
        }
    }

    private Transform CameraTransform
    {
        get
        {
            if (xrOrigin == null)
                return null;

            return xrOrigin.Camera != null ? xrOrigin.Camera.transform : null;
        }
    }

    private void EnsureProbe()
    {
        if (probe != null)
        {
            probe.enabled = true;
            return;
        }

        var go = new GameObject("PlayerBodyCollisionProbe")
        {
            // Không dùng DontSave: object tạo lúc runtime vốn không được serialize, mà cờ DontSave
            // còn làm nó sống sót qua lần load scene kế tiếp.
            hideFlags = HideFlags.HideInHierarchy | HideFlags.NotEditable,

            // Ignore Raycast: không lọt vào raycast ngắm / bắn của súng.
            layer = 2,
        };

        probeTransform = go.transform;
        probeTransform.SetParent(OriginTransform, false);

        probe = go.AddComponent<CapsuleCollider>();
        probe.direction = 1;
        probe.center = Vector3.zero;
        probe.radius = Mathf.Max(0.01f, bodyRadius);
        probe.excludeLayers = Physics.AllLayers;
    }

    private void LateUpdate()
    {
        if (!blockPhysicalWalking)
            return;

        Transform origin = OriginTransform;
        Transform cam = CameraTransform;

        if (origin == null || cam == null || probe == null)
            return;

        float radius = Mathf.Max(0.01f, bodyRadius);
        int iterations = Mathf.Max(1, solverIterations);

        for (int i = 0; i < iterations; i++)
        {
            if (!Resolve(origin, cam, radius))
                break;
        }
    }

    /// <summary>Đẩy origin ra khỏi vật đang chồng lên thân người. Trả về true nếu có đẩy.</summary>
    private bool Resolve(Transform origin, Transform cam, float radius)
    {
        // Thân người: capsule thẳng đứng trong local space của origin, nằm ngay dưới camera.
        Vector3 camLocal = origin.InverseTransformPoint(cam.position);
        float headHeight = Mathf.Max(camLocal.y, minBodyHeight);
        float lowCenter = bodyBottomOffset + radius;
        float highCenter = Mathf.Max(lowCenter, headHeight - radius);

        var lowLocal = new Vector3(camLocal.x, lowCenter, camLocal.z);
        var highLocal = new Vector3(camLocal.x, highCenter, camLocal.z);

        probe.radius = radius;
        probe.height = (highCenter - lowCenter) + radius * 2f;
        probeTransform.localPosition = (lowLocal + highLocal) * 0.5f;
        probeTransform.localRotation = Quaternion.identity;

        Vector3 low = origin.TransformPoint(lowLocal);
        Vector3 high = origin.TransformPoint(highLocal);

        int count = Physics.OverlapCapsuleNonAlloc(low, high, radius, overlaps, blockingLayers,
            QueryTriggerInteraction.Ignore);

        Vector3 up = origin.up;
        Vector3 push = Vector3.zero;

        for (int i = 0; i < count; i++)
        {
            Collider other = overlaps[i];

            if (!IsBlocking(other, origin))
                continue;

            if (!Physics.ComputePenetration(
                    probe, probeTransform.position, probeTransform.rotation,
                    other, other.transform.position, other.transform.rotation,
                    out Vector3 direction, out float distance))
            {
                continue;
            }

            // Vùng chết: lún trong khoảng sai số thì bỏ, kẻo đẩy li ti mỗi frame thành trôi.
            distance -= Mathf.Max(0f, skinTolerance);

            if (distance <= 0f)
                continue;

            if (!horizontalOnly)
            {
                push += direction * distance;
                continue;
            }

            float vertical = Vector3.Dot(direction, up);

            // Hướng thoát gần như thẳng đứng = sàn hoặc mặt bàn mỏng: BỎ HẲN vật này.
            //
            // Đây là chỗ gây trôi: ví dụ mặt bàn lựu dày 5 cm, rướn người vào thì đường ra ngắn
            // nhất là đi lên 5 cm chứ không phải lùi 35 cm. Nếu chỉ xoá thành phần dọc rồi vẫn đẩy
            // phần ngang còn lại, phần đó bé tí và không bao giờ tách được capsule ra, nên frame
            // nào cũng đẩy tiếp -> người chơi trôi mãi. Bỏ qua mới đúng: chỉ vật cản theo phương
            // NGANG mới được chặn di chuyển ngang, còn phương dọc là việc của Gravity Provider.
            if (Mathf.Abs(vertical) > maxVerticalSeparationDot)
                continue;

            Vector3 horizontalDirection = direction - up * vertical;
            float horizontalLength = horizontalDirection.magnitude;

            if (horizontalLength < 1e-4f)
                continue;

            // Chia lại cho phần ngang để bù đúng độ lún dù chỉ được đi ngang (tối đa ~1.4x ở 45 độ).
            push += (horizontalDirection / horizontalLength) * (distance / horizontalLength);
        }

        if (push.sqrMagnitude < k_MinPushSqr)
            return false;

        origin.position += Vector3.ClampMagnitude(push, Mathf.Max(0f, maxPushPerFrame));
        return true;
    }

    private bool IsBlocking(Collider other, Transform origin)
    {
        if (other == null || other == probe || other.isTrigger)
            return false;

        // Chính rig người chơi: tay, CharacterController, collider gắn trên camera...
        if (other.transform.IsChildOf(origin))
            return false;

        Rigidbody body = other.attachedRigidbody;

        if (body != null)
        {
            // Vật động thì người chơi đẩy nó, không phải nó đẩy người chơi.
            if (ignoreDynamicRigidbodies && !body.isKinematic)
                return false;

            // Vật đang cầm / đang cắm socket bị XRI set isKinematic nên lọt qua filter trên, mà nó
            // thì luôn dính sát người (súng trên tay, băng đạn ở túi hông) -> đẩy trôi liên tục.
            if (ignoreSelectedInteractables && IsHeld(body))
                return false;
        }

        return true;
    }

    private static bool IsHeld(Rigidbody body)
    {
        return body.TryGetComponent(out XRBaseInteractable interactable) && interactable.isSelected;
    }
}
