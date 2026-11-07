using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// Biến bàn tay thành vật thể vật lý thật để nó bị tường / bàn chặn lại.
///
/// Vì sao cần: tay của Oculus Hands Physics là Rigidbody KINEMATIC (isKinematic = 1) treo dưới
/// Controller. Kinematic nghĩa là tracking gán thẳng transform mỗi frame và PhysX không có quyền
/// can thiệp - nó dịch chuyển tức thời qua mọi vật trên đường. 24 capsule trên tay chỉ để đẩy vật
/// khác và để grab, chứ bản thân tay không bao giờ bị chặn. Không setting nào sửa được chuyện đó.
///
/// Cách làm (kiểu physics hands chuẩn):
/// - Tách tay ra khỏi Controller, nhưng vẫn để dưới XR Origin để mấy chỗ kiểm tra "cái này có
///   phải người chơi không" (HeldItemCollisionFilter, PlayerBodyCollision) tiếp tục đúng.
///   Rigidbody động thì không bị transform cha kéo, nên treo ở đâu cũng không ảnh hưởng mô phỏng.
/// - Để lại một target rỗng đúng chỗ cũ dưới Controller: đó là "tay thật" của người chơi.
/// - Mỗi FixedUpdate set vận tốc cho tay bay về target. Đụng tường thì solver giữ tay lại, tay
///   ảo tách khỏi tay thật - đúng như mọi game VR có physics hands.
/// - Lệch quá <see cref="maxSnapDistance"/> (chui sâu vào tường, hoặc vừa teleport) thì nhảy
///   thẳng về target, kẻo tay kẹt lại sau bức tường vĩnh viễn.
///
/// Gắn trên GameObject của XR Origin.
/// </summary>
[DisallowMultipleComponent]
public class PhysicsHands : MonoBehaviour
{
    [Header("Rig")]
    [Tooltip("Để trống sẽ tự tìm trên cùng GameObject rồi tới cả scene.")]
    [SerializeField] private XROrigin xrOrigin;

    [Tooltip("Tắt để trả tay về kiểu cũ (xuyên qua mọi thứ). Chỉ đọc lúc Start.")]
    [SerializeField] private bool enablePhysicsHands = true;

    [Tooltip("Để trống sẽ tự tìm: mọi Rigidbody kinematic có collider đặc nằm dưới XR Origin.")]
    [SerializeField] private Rigidbody[] handBodies;

    [Header("Thông số tay")]
    [Tooltip("Khối lượng tay (kg). Nặng hơn thì đẩy được vật nặng hơn, nhưng đập vào vật nhẹ mạnh hơn.")]
    [SerializeField] private float handMass = 2f;

    [Tooltip("Lệch quá mức này (m) thì nhảy thẳng về vị trí tay thật. Để tay không kẹt sau tường, " +
             "và để theo kịp teleport. Nhỏ quá thì ấn nhẹ đã xuyên, lớn quá thì tay kẹt lâu.")]
    [SerializeField] private float maxSnapDistance = 0.4f;

    [Tooltip("Giới hạn tốc độ bật ra khi tay lún vào vật khác (m/s). Thấp = không bị văng.")]
    [SerializeField] private float maxDepenetrationVelocity = 3f;

    [Tooltip("Cho hai tay xuyên qua nhau. Tắt thì hai tay va vào nhau, chắp tay là đẩy nhau ra.")]
    [SerializeField] private bool ignoreCollisionBetweenHands = true;

    private sealed class Hand
    {
        public Rigidbody Body;
        public Transform Target;
        public Collider[] Colliders;
    }

    private readonly List<Hand> hands = new List<Hand>();

    // Nhớ lại origin đã dùng lúc Start: RefreshSelfCollisionIgnores cần nó, mà lúc đó tay đã
    // được tháo khỏi Controller nên không suy lại từ hierarchy được.
    private Transform cachedOrigin;

    private Transform OriginTransform
    {
        get
        {
            if (xrOrigin == null)
                return null;

            return xrOrigin.Origin != null ? xrOrigin.Origin.transform : xrOrigin.transform;
        }
    }

    private void Awake()
    {
        if (xrOrigin == null)
            xrOrigin = GetComponent<XROrigin>();

        if (xrOrigin == null)
            xrOrigin = FindFirstObjectByType<XROrigin>();
    }

    // Start chứ không phải Awake: để XRInputModalityManager kịp bật/tắt model tay trước.
    private void Start()
    {
        if (!enablePhysicsHands)
            return;

        Transform origin = OriginTransform;

        if (origin == null)
        {
            Debug.LogWarning(nameof(PhysicsHands) + ": không tìm thấy XR Origin, tự tắt.", this);
            enabled = false;
            return;
        }

        foreach (Rigidbody body in CollectHandBodies(origin))
            Setup(body, origin);

        if (hands.Count == 0)
        {
            Debug.LogWarning(nameof(PhysicsHands) + ": không tìm thấy bàn tay nào. Gán tay vào " +
                             "Hand Bodies nếu model tay được spawn lúc runtime.", this);
            enabled = false;
            return;
        }

        cachedOrigin = origin;
        IgnoreSelfCollisions(origin);
    }

    /// <summary>
    /// Đặt lại cờ bỏ va chạm giữa tay và thân người chơi / giữa hai tay.
    ///
    /// Phải gọi lại sau khi có collider của tay bị disable rồi enable: cờ
    /// <see cref="Physics.IgnoreCollision"/> KHÔNG bền - Unity xoá cờ của một collider ngay khi
    /// collider đó bị tắt rồi bật lại. <see cref="HandPoseDriver"/> tắt 16 capsule ngón tay
    /// trong lúc đang cầm súng, nên không gọi lại thì ngay sau lần cầm đầu tiên ngón tay bắt đầu
    /// đập vào CharacterController của chính người chơi và đập vào bàn tay kia.
    /// </summary>
    public void RefreshSelfCollisionIgnores()
    {
        if (hands.Count == 0)
            return;

        // Danh sách collider có thể đã đổi (model tay bật/tắt theo chế độ nhập liệu).
        foreach (Hand hand in hands)
        {
            if (hand.Body != null)
                hand.Colliders = hand.Body.GetComponentsInChildren<Collider>(true);
        }

        IgnoreSelfCollisions(cachedOrigin != null ? cachedOrigin : OriginTransform);
    }

    private IEnumerable<Rigidbody> CollectHandBodies(Transform origin)
    {
        if (handBodies != null && handBodies.Length > 0)
        {
            foreach (Rigidbody body in handBodies)
            {
                if (body != null)
                    yield return body;
            }

            yield break;
        }

        foreach (Rigidbody body in origin.GetComponentsInChildren<Rigidbody>(true))
        {
            // Tay là kinematic; vật đang cầm thì không (đang Velocity Tracking) nên không dính.
            if (!body.isKinematic)
                continue;

            if (HasSolidCollider(body))
                yield return body;
        }
    }

    private static bool HasSolidCollider(Component root)
    {
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            if (!collider.isTrigger && !(collider is CharacterController))
                return true;
        }

        return false;
    }

    private void Setup(Rigidbody body, Transform origin)
    {
        Transform hand = body.transform;
        Transform parent = hand.parent;

        if (parent == null)
            return;

        // Target đứng đúng chỗ tay đang treo: đây là "tay thật", vẫn bị tracking kéo như cũ.
        var target = new GameObject(hand.name + " Tracking Target").transform;
        target.SetParent(parent, false);
        target.localPosition = hand.localPosition;
        target.localRotation = hand.localRotation;
        target.localScale = Vector3.one;

        // Treo tay lên thẳng XR Origin (giữ nguyên pose world). Vẫn là con của origin nên
        // HeldItemCollisionFilter và PlayerBodyCollision tiếp tục nhận ra đây là người chơi.
        hand.SetParent(origin, true);

        body.isKinematic = false;
        body.useGravity = false;
        body.mass = Mathf.Max(0.1f, handMass);
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.maxDepenetrationVelocity = Mathf.Max(0.1f, maxDepenetrationVelocity);
        body.constraints = RigidbodyConstraints.None;

        hands.Add(new Hand
        {
            Body = body,
            Target = target,
            Colliders = body.GetComponentsInChildren<Collider>(true),
        });
    }

    /// <summary>Tắt va chạm giữa tay với thân người chơi, và (tuỳ chọn) giữa hai tay với nhau.</summary>
    private void IgnoreSelfCollisions(Transform origin)
    {
        // Không có dòng này thì tay đập vào CharacterController của chính mình, không đưa ra trước
        // mặt được.
        var characterController = origin != null ? origin.GetComponent<CharacterController>() : null;

        foreach (Hand hand in hands)
        {
            foreach (Collider collider in hand.Colliders)
            {
                if (!IsUsable(collider))
                    continue;

                if (characterController != null && characterController.enabled)
                    Physics.IgnoreCollision(collider, characterController, true);
            }
        }

        if (!ignoreCollisionBetweenHands)
            return;

        for (int i = 0; i < hands.Count; i++)
        {
            for (int j = i + 1; j < hands.Count; j++)
            {
                foreach (Collider a in hands[i].Colliders)
                {
                    if (!IsUsable(a))
                        continue;

                    foreach (Collider b in hands[j].Colliders)
                    {
                        if (IsUsable(b))
                            Physics.IgnoreCollision(a, b, true);
                    }
                }
            }
        }
    }

    private static bool IsUsable(Collider collider)
    {
        return collider != null && !collider.isTrigger;
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        if (dt <= 0f)
            return;

        foreach (Hand hand in hands)
        {
            if (hand.Body == null || hand.Target == null)
                continue;

            // XRInputModalityManager tắt model tay khi đổi giữa controller và hand-tracking.
            if (!hand.Body.gameObject.activeInHierarchy)
                continue;

            Drive(hand, dt);
        }
    }

    private void Drive(Hand hand, float dt)
    {
        Rigidbody body = hand.Body;
        Vector3 targetPosition = hand.Target.position;
        Quaternion targetRotation = hand.Target.rotation;

        Vector3 offset = targetPosition - body.position;

        // Lệch quá xa: tay đang kẹt sau tường, hoặc người chơi vừa teleport. Nhảy thẳng về,
        // không thì tay nằm lại đó mãi.
        if (offset.sqrMagnitude > maxSnapDistance * maxSnapDistance)
        {
            body.position = targetPosition;
            body.rotation = targetRotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            return;
        }

        // Đặt thẳng vận tốc (không AddForce) để tay bám sát tracking: solver vẫn được quyền chặn
        // lại khi đụng tường, nhưng lúc trống đường thì theo kịp 1:1.
        body.linearVelocity = offset / dt;

        Quaternion delta = targetRotation * Quaternion.Inverse(body.rotation);
        delta.ToAngleAxis(out float angleInDegrees, out Vector3 axis);

        if (angleInDegrees > 180f)
            angleInDegrees -= 360f;

        if (Mathf.Abs(angleInDegrees) > Mathf.Epsilon && !float.IsInfinity(axis.x))
            body.angularVelocity = axis.normalized * (angleInDegrees * Mathf.Deg2Rad / dt);
        else
            body.angularVelocity = Vector3.zero;
    }
}
