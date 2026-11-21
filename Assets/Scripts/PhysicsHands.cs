using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

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
/// KHOÁ TAY LÚC ĐANG CẦM (xem <see cref="lockHandWhileHolding"/>): cầm súng thì tay vật lý lại
/// thành vấn đề chứ không còn là tính năng. Tay bị mép tường / thành bàn chặn lại trong khi khẩu
/// súng vẫn bám theo controller, thế là súng rời khỏi lòng bàn tay, rung, giật, ngắm thì lệch.
/// Mấy capsule lòng bàn tay còn hẩy bay đồ đạc xung quanh. Nên khi một bàn tay cầm vật, bàn tay
/// đó được "khoá": quay về kinematic bám tracking 1:1 (không ai chặn được nữa) và tắt collider
/// đặc (không đụng gì, không đẩy gì). Thả ra là trả lại nguyên trạng - tay vật lý như cũ.
///
/// DÁN TAY VÀO VẬT ĐANG CẦM (xem <see cref="weldHandToHeldItem"/>): khoá tay rồi thì tay bám
/// controller 1:1, nhưng khẩu súng thì KHÔNG - nó để Movement Type = Velocity Tracking nên là
/// vật thể động chạy đuổi theo tay, luôn trễ và luôn lệch một chút. Dáng ngón đã cố định nên
/// cú lệch đó đọc ra thành "bàn tay xoay trong lòng báng". Nên bàn tay đang cầm không bám
/// controller nữa mà bám thẳng ĐIỂM BÁM CỦA VẬT: súng xoay bao nhiêu thì tay xoay theo đúng bấy
/// nhiêu, không tự xoay thêm được. Việc này cũng kéo luôn bàn tay thứ hai về foregrip.
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

    [Header("Khoá tay khi đang cầm vật")]
    [Tooltip("Cầm vật (súng, băng đạn, lựu) thì bàn tay đó quay về kinematic bám tracking 1:1.\n\n" +
             "Vì sao: tay vật lý bị tường chặn trong khi vật đang cầm vẫn bám theo controller, nên " +
             "vật rời khỏi lòng bàn tay và rung giật. Khoá tay là hết.\n\n" +
             "Thả vật là tay trở lại vật lý bình thường.")]
    [SerializeField] private bool lockHandWhileHolding = true;

    [Tooltip("Lúc khoá thì tắt luôn collider đặc của bàn tay đó.\n\n" +
             "Khoá mà vẫn để collider thì tay là kinematic khối lượng vô hạn: không bị chặn nữa " +
             "nhưng vẫn hẩy bay mọi vật nó quét qua. Tắt đi là tay cầm súng không đụng gì cả.\n\n" +
             "Tắt tuỳ chọn này nếu vẫn muốn dùng bàn tay để đẩy/gạt đồ trong lúc cầm súng.")]
    [SerializeField] private bool disableCollidersWhileLocked = true;

    [Tooltip("Thả vật ra rồi chờ bao lâu mới bật lại collider bàn tay (giây). Chờ để vật kịp rời " +
             "khỏi lòng bàn tay, kẻo bật lại lúc còn lún là vật bị hất bay.")]
    [SerializeField] private float colliderRestoreDelay = 0.2f;

    [Tooltip("Chốt an toàn: vật nằm luôn trong lòng bàn tay (bị kẹp giữa tay và tường chẳng hạn) " +
             "thì sau bấy nhiêu giây vẫn bật lại collider bằng mọi giá.")]
    [SerializeField] private float colliderRestoreTimeout = 3f;

    [Tooltip("Chỉ khoá tay khi vật là XRGrabInteractable (vật cầm trên tay). Tắt thì mọi thứ " +
             "select được - tay cầm nắm, tay nắm vào môi trường - đều khoá tay.")]
    [SerializeField] private bool onlyLockForGrabInteractables = true;

    [Header("Dán bàn tay vào vật đang cầm")]
    [Tooltip("Trong lúc cầm, bàn tay bám theo ĐIỂM BÁM CỦA VẬT chứ không bám theo controller nữa.\n\n" +
             "Vì sao: khoá tay thôi chưa đủ để bàn tay thành một khối với khẩu súng. Súng để " +
             "Movement Type = Velocity Tracking (Gun.cs còn cảnh báo nếu đổi) nên lúc cầm nó là " +
             "vật thể động chạy đuổi theo tay: xoay cổ tay nhanh là súng xoay chậm hơn, và vì " +
             "dáng ngón đã cố định nên cú lệch đó đọc ra thành 'bàn tay xoay trong lòng báng'. " +
             "Súng hai tay thì lệch hẳn - hướng súng do vị trí hai tay cùng quyết, không còn khớp " +
             "với hướng cổ tay nào cả. Recoil và attach ease-in cũng cùng một kiểu lệch.\n\n" +
             "Dán vào điểm bám thì súng xoay bao nhiêu, bàn tay xoay theo đúng bấy nhiêu - không " +
             "tự xoay thêm được một độ nào.")]
    [SerializeField] private bool weldHandToHeldItem = true;

    [Tooltip("Dán cả vị trí chứ không chỉ hướng.\n\n" +
             "Tắt thì bàn tay vẫn đứng đúng chỗ controller và chỉ lấy hướng từ vật: hết xoay " +
             "được, nhưng vật trượt khỏi lòng bàn tay thì tay không đi theo.")]
    [SerializeField] private bool weldHandPosition = true;

    [Tooltip("Thời gian chuyển từ bám controller sang dán vào vật (giây).\n\n" +
             "Vì sao cần: lúc vừa bấm nút cầm, khẩu súng còn nằm ở chỗ của nó - Attach Ease In " +
             "Time cộng velocity tracking phải mất chừng ấy giây mới kéo nó vào tay. Dán ngay từ " +
             "frame đầu là bàn tay bị kéo NGƯỢC về phía khẩu súng rồi mới cùng nhau quay lại: " +
             "một cú giật thấy rõ mỗi lần cầm. Để bằng Attach Ease In Time của vật là vừa.\n\n" +
             "0 = dán ngay.")]
    [SerializeField] private float weldBlendTime = 0.15f;

    [Tooltip("Chỉ dán khi vật có điểm bám riêng cho từng tay (HandPosedGrabInteractable).\n\n" +
             "Tắt (mặc định) thì dán theo đúng điểm bám XRI đang dùng cho tay đó, nên vật còn là " +
             "XRGrabInteractable thường vẫn được dán.")]
    [SerializeField] private bool weldOnlyToHandAttachTransforms;

    [Tooltip("Lệch quá bấy nhiêu mét so với tay thật thì thôi không dán nữa (0 = không giới hạn).\n\n" +
             "Chốt an toàn cho trường hợp vật bị kẹt (velocity tracking kéo không nổi, súng mắc " +
             "trong tường): thà để bàn tay về chỗ controller còn hơn để nó bay theo vật.")]
    [SerializeField] private float weldMaxDistance = 0.5f;

    [Header("Làm mới cờ bỏ va chạm")]
    [Tooltip("Chu kỳ đặt lại cờ bỏ va chạm giữa tay và thân người chơi / giữa hai tay (giây).\n\n" +
             "Vì sao cần: Physics.IgnoreCollision KHÔNG bền - Unity xoá cờ của một collider ngay " +
             "khi collider đó bị tắt rồi bật lại, mà HandPoseDriver và phần khoá tay ở file này " +
             "tắt/bật collider liên tục. XRInputModalityManager thì bật/tắt cả model tay.\n\n" +
             "0 = không quét lại.")]
    [SerializeField] private float selfIgnoreRefreshInterval = 1f;

    private sealed class Hand
    {
        public Rigidbody Body;
        public Transform Target;
        public Collider[] Colliders;

        /// <summary>Bên tay, để tra từ interactor sang đúng bàn tay. False = không dò ra.</summary>
        public HandSide Side;
        public bool SideKnown;

        /// <summary>Số vật đang cầm bằng tay này. Cầm 2 vật thì thả 1 vẫn còn khoá.</summary>
        public int LockCount;

        /// <summary>Mức dán vào vật đang cầm, 0..1. Blend để lúc vừa cầm tay không bị giật.</summary>
        public float WeldWeight;

        /// <summary>Pose dán được lần gần nhất, để lúc mất chỗ dán còn rút về từ từ.</summary>
        public Vector3 LastWeldPosition;
        public Quaternion LastWeldRotation = Quaternion.identity;

        public bool Locked => LockCount > 0;

        // Trạng thái Rigidbody lúc chưa khoá, để trả lại y nguyên.
        public RigidbodyInterpolation SavedInterpolation;
        public CollisionDetectionMode SavedCollisionMode;

        public readonly List<Collider> DisabledColliders = new List<Collider>();

        /// <summary>Collider của vật vừa thả, để chờ nó rời khỏi lòng bàn tay.</summary>
        public readonly List<Collider> ReleasedItemColliders = new List<Collider>();

        /// <summary>Bán kính bọc toàn bộ collider bàn tay, đo lúc còn bật.</summary>
        public float PalmRadius;

        public float ColliderRestoreTime = float.PositiveInfinity;
        public float ColliderRestoreDeadline;
    }

    /// <summary>Một lần cầm đang giữ khoá: cặp (tay nào của người chơi, vật nào, bàn tay nào).</summary>
    private readonly struct HeldLock
    {
        public readonly IXRSelectInteractor Interactor;
        public readonly IXRSelectInteractable Interactable;
        public readonly Hand TargetHand;

        public HeldLock(IXRSelectInteractor interactor, IXRSelectInteractable interactable, Hand hand)
        {
            Interactor = interactor;
            Interactable = interactable;
            TargetHand = hand;
        }
    }

    private readonly List<Hand> hands = new List<Hand>();
    private readonly List<HeldLock> heldLocks = new List<HeldLock>();
    private readonly List<XRBaseInteractor> subscribedInteractors = new List<XRBaseInteractor>();

    // Nhớ lại origin đã dùng lúc Start: RefreshSelfCollisionIgnores cần nó, mà lúc đó tay đã
    // được tháo khỏi Controller nên không suy lại từ hierarchy được.
    private Transform cachedOrigin;

    private bool ready;
    private float nextSelfIgnoreRefreshTime;
    private bool warnedNoHandForInteractor;

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
        ready = true;
        IgnoreSelfCollisions(origin);
        SubscribeInteractors(origin);
    }

    private void OnEnable()
    {
        // Bật lại sau khi bị tắt: Start không chạy lại nên phải tự nối lại sự kiện.
        if (ready)
            SubscribeInteractors(cachedOrigin);
    }

    private void OnDisable()
    {
        UnsubscribeInteractors();
        ReleaseAllLocks();
    }

    /// <summary>
    /// Đặt lại cờ bỏ va chạm giữa tay và thân người chơi / giữa hai tay.
    ///
    /// Phải gọi lại sau khi có collider của tay bị disable rồi enable: cờ
    /// <see cref="Physics.IgnoreCollision"/> KHÔNG bền - Unity xoá cờ của một collider ngay khi
    /// collider đó bị tắt rồi bật lại. <see cref="HandPoseDriver"/> tắt 16 capsule ngón tay
    /// trong lúc đang cầm súng, và phần khoá tay ở file này tắt cả 24 capsule, nên không gọi lại
    /// thì ngay sau lần cầm đầu tiên ngón tay bắt đầu đập vào CharacterController của chính người
    /// chơi và đập vào bàn tay kia.
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

    /// <summary>Bàn tay bên này có đang bị khoá (đang cầm vật) hay không.</summary>
    public bool IsHandLocked(HandSide side)
    {
        foreach (Hand hand in hands)
        {
            if (hand.SideKnown && hand.Side == side && hand.Locked)
                return true;
        }

        return false;
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

        // Dò bên tay TRƯỚC khi tháo khỏi Controller: cách dò dự phòng đi theo tên trong chuỗi cha
        // ("Left Controller"), mà tháo rồi thì chuỗi đó mất.
        bool sideKnown = TryResolveHandSide(body, out HandSide side);

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
            Side = side,
            SideKnown = sideKnown,
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

    // Physics.IgnoreCollision báo lỗi nếu collider đang tắt hoặc GameObject của nó đang tắt - mà
    // chuyện đó xảy ra suốt: HandPoseDriver tắt capsule ngón, phần khoá tay tắt cả bàn tay,
    // XRInputModalityManager tắt hẳn model tay không dùng.
    private static bool IsUsable(Collider collider)
    {
        return collider != null &&
               !collider.isTrigger &&
               collider.enabled &&
               collider.gameObject.activeInHierarchy;
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

            // Đang cầm vật: tay bám tracking 1:1, không mô phỏng gì.
            if (hand.Locked)
            {
                SnapLockedHand(hand, false);
                continue;
            }

            Drive(hand, dt);
        }
    }

    // Tay bị khoá thì gán transform ở LateUpdate chứ không chỉ ở FixedUpdate: physics chạy
    // 50-72 Hz còn hình vẽ 90-120 Hz, bám theo nhịp physics là tay rung và trễ thấy rõ.
    private void LateUpdate()
    {
        foreach (Hand hand in hands)
        {
            if (!hand.Locked || hand.Body == null || hand.Target == null)
                continue;

            if (!hand.Body.gameObject.activeInHierarchy)
                continue;

            SnapLockedHand(hand, true);
        }
    }

    private void Update()
    {
        UpdateColliderRestores();
        RefreshSelfIgnoresPeriodically();
    }

    private static void SnapToTarget(Hand hand)
    {
        // Gán thẳng transform (không MovePosition): đúng kiểu tracking vẫn kéo bàn tay kinematic
        // trước khi có PhysicsHands, nên không thêm một bước trễ nào.
        hand.Body.transform.SetPositionAndRotation(hand.Target.position, hand.Target.rotation);
    }

    /// <summary>
    /// Đặt bàn tay đang bị khoá: dán vào điểm bám của vật nếu được, không thì bám tracking.
    ///
    /// <paramref name="advanceBlend"/> chỉ đúng ở <c>LateUpdate</c>: hàm này được gọi từ cả
    /// <c>FixedUpdate</c> lẫn <c>LateUpdate</c>, cộng thời gian ở cả hai chỗ là blend chạy nhanh
    /// gấp đôi.
    /// </summary>
    private void SnapLockedHand(Hand hand, bool advanceBlend)
    {
        bool welded = TryGetWeldedPose(hand, out Vector3 weldPosition, out Quaternion weldRotation);

        if (advanceBlend)
        {
            float step = weldBlendTime > 0f ? Time.deltaTime / weldBlendTime : 1f;
            hand.WeldWeight = Mathf.MoveTowards(hand.WeldWeight, welded ? 1f : 0f, step);
        }

        if (!welded && hand.WeldWeight <= 0f)
        {
            SnapToTarget(hand);
            return;
        }

        if (welded)
        {
            hand.LastWeldPosition = weldPosition;
            hand.LastWeldRotation = weldRotation;
        }

        // Mất chỗ dán giữa lúc đang cầm (vật kẹt quá xa, vật bị destroy) thì rút về chỗ tay thật
        // theo đúng tốc độ blend chứ không nhảy một cái: giữ pose dán cuối cùng làm điểm đi.
        Vector3 position = welded ? weldPosition : hand.LastWeldPosition;
        Quaternion rotation = welded ? weldRotation : hand.LastWeldRotation;

        if (hand.WeldWeight < 0.9999f)
        {
            position = Vector3.Lerp(hand.Target.position, position, hand.WeldWeight);
            rotation = Quaternion.Slerp(hand.Target.rotation, rotation, hand.WeldWeight);
        }

        hand.Body.transform.SetPositionAndRotation(position, rotation);
    }

    /// <summary>
    /// Pose để bàn tay dính cứng vào điểm bám của vật đang cầm. False = không dán được, cứ bám
    /// tracking như cũ.
    ///
    /// Cách tính: điểm bám của interactor và target của bàn tay đều treo dưới Controller nên lệch
    /// nhau một lượng CỐ ĐỊNH - chính là độ lệch giữa bàn tay và chỗ XRI kéo vật về. Đặt lại đúng
    /// độ lệch ấy, nhưng lần này đo từ điểm bám CỦA VẬT, thì bàn tay nằm trên báng đúng như lúc
    /// vật bám khít vào tay: vật xoay bao nhiêu, tay xoay theo bấy nhiêu, không tự xoay thêm.
    ///
    /// Tính lại mỗi frame chứ không chụp lúc cầm: dynamic attach, attach ease-in và việc XRI xếp
    /// lại hai ô điểm bám (tay chính / tay thứ hai) đều đổi điểm bám giữa lúc đang cầm.
    /// </summary>
    private bool TryGetWeldedPose(Hand hand, out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = default;

        if (!weldHandToHeldItem || hand.Target == null)
            return false;

        // Quét từ cuối: tay đang cầm 2 vật (cầm súng rồi rút băng đạn bằng chính tay đó) thì vật
        // cầm sau cùng là vật đang nằm trong lòng bàn tay.
        for (int i = heldLocks.Count - 1; i >= 0; i--)
        {
            HeldLock held = heldLocks[i];

            if (held.TargetHand != hand)
                continue;

            Transform grip = ResolveGrip(held, hand);

            if (grip == null)
                continue;

            Transform interactorAttach = held.Interactor.GetAttachTransform(held.Interactable);

            if (interactorAttach == null)
                continue;

            Quaternion inverseAttach = Quaternion.Inverse(interactorAttach.rotation);

            rotation = grip.rotation * (inverseAttach * hand.Target.rotation);

            if (!weldHandPosition)
            {
                position = hand.Target.position;
                return true;
            }

            // Nhân tay chứ không dùng TransformPoint: điểm bám của súng có thể nằm dưới một
            // GameObject có scale, mà độ lệch này đo bằng mét ở không gian thế giới.
            Vector3 localOffset = inverseAttach * (hand.Target.position - interactorAttach.position);
            position = grip.position + grip.rotation * localOffset;

            if (weldMaxDistance > 0f &&
                (position - hand.Target.position).sqrMagnitude > weldMaxDistance * weldMaxDistance)
                return false;

            return true;
        }

        return false;
    }

    /// <summary>Điểm bám trên vật mà bàn tay này phải dính vào.</summary>
    private Transform ResolveGrip(in HeldLock held, Hand hand)
    {
        // So sánh qua Component chứ không so thẳng interface: biến kiểu interface KHÔNG dùng
        // toán tử == của UnityEngine.Object, nên vật đã bị Destroy vẫn khác null.
        if (!(held.Interactable is Component interactable) || interactable == null)
            return null;

        if (!(held.Interactor is Component interactor) || interactor == null)
            return null;

        // Vật có điểm bám riêng từng tay: dùng thẳng, vì đó chính là chỗ dáng tay được tạo ra.
        if (hand.SideKnown && held.Interactable is HandPosedGrabInteractable posed)
        {
            Transform handAttach = posed.GetHandAttachTransform(hand.Side);

            if (handAttach != null)
                return handAttach;
        }

        if (weldOnlyToHandAttachTransforms)
            return null;

        // Còn lại: đúng điểm bám XRI đang kéo vật về cho tay này (dynamic attach, điểm bám tay
        // thứ hai, hoặc Attach Transform chung).
        return held.Interactable.GetAttachTransform(held.Interactor);
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

    // ---------------------------------------------------------------------------------------
    // Khoá tay khi đang cầm vật
    // ---------------------------------------------------------------------------------------

    private void SubscribeInteractors(Transform root)
    {
        if (root == null || subscribedInteractors.Count > 0)
            return;

        foreach (XRBaseInteractor interactor in root.GetComponentsInChildren<XRBaseInteractor>(true))
        {
            if (!(interactor is IXRSelectInteractor))
                continue;

            // Socket (túi đạn, giá treo) cũng select vật nhưng không phải bàn tay nào cả.
            if (interactor is XRSocketInteractor)
                continue;

            interactor.selectEntered.AddListener(OnSelectEntered);
            interactor.selectExited.AddListener(OnSelectExited);
            subscribedInteractors.Add(interactor);
        }
    }

    private void UnsubscribeInteractors()
    {
        foreach (XRBaseInteractor interactor in subscribedInteractors)
        {
            if (interactor == null)
                continue;

            interactor.selectEntered.RemoveListener(OnSelectEntered);
            interactor.selectExited.RemoveListener(OnSelectExited);
        }

        subscribedInteractors.Clear();
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (!lockHandWhileHolding)
            return;

        IXRSelectInteractor interactor = args.interactorObject;
        IXRSelectInteractable interactable = args.interactableObject;

        if (interactor == null || interactable == null)
            return;

        if (onlyLockForGrabInteractables && !(interactable is XRGrabInteractable))
            return;

        if (!TryGetInteractorSide(interactor, out HandSide side))
        {
            WarnNoHand("interactor '" + DescribeInteractor(interactor) + "' chưa đặt Handedness");
            return;
        }

        // Thường chỉ một bàn tay mỗi bên, nhưng XRInputModalityManager có thể giữ hai model
        // (controller và hand-tracking) cùng bên. Khoá cả hai: bên nào đang tắt thì vô hại.
        bool found = false;

        foreach (Hand hand in hands)
        {
            if (!hand.SideKnown || hand.Side != side)
                continue;

            heldLocks.Add(new HeldLock(interactor, interactable, hand));
            LockHand(hand);
            found = true;
        }

        if (!found)
            WarnNoHand("không có bàn tay nào dò ra là tay " + side);
    }

    // Cảnh báo một lần thôi: select bắn liên tục trong lúc chơi.
    private void WarnNoHand(string reason)
    {
        if (warnedNoHandForInteractor)
            return;

        warnedNoHandForInteractor = true;
        Debug.LogWarning(nameof(PhysicsHands) + ": không khoá được tay khi cầm vì " + reason +
                         ". Đặt Handedness cho interactor, hoặc gắn HandSkeleton (đúng " +
                         "Handedness) lên model tay.", this);
    }

    private static string DescribeInteractor(IXRSelectInteractor interactor)
    {
        return interactor is Component component && component != null ? component.name : interactor.ToString();
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        // Mở theo đúng cặp đã ghi lúc cầm, không xét lại điều kiện: mấy cái toggle ở trên có đổi
        // giữa lúc đang cầm thì số đếm vẫn khớp.
        for (int i = heldLocks.Count - 1; i >= 0; i--)
        {
            HeldLock held = heldLocks[i];

            if (held.Interactor != args.interactorObject || held.Interactable != args.interactableObject)
                continue;

            heldLocks.RemoveAt(i);
            UnlockHand(held.TargetHand, args.interactableObject as Component);
        }
    }

    private void LockHand(Hand hand)
    {
        hand.LockCount++;

        if (hand.LockCount > 1)
            return;

        Rigidbody body = hand.Body;

        if (body == null)
            return;

        // Đang chờ bật lại collider mà cầm tiếp: huỷ lịch, giữ collider tắt luôn.
        hand.ColliderRestoreTime = float.PositiveInfinity;
        hand.ReleasedItemColliders.Clear();

        hand.SavedInterpolation = body.interpolation;
        hand.SavedCollisionMode = body.collisionDetectionMode;

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.isKinematic = true;

        // Kinematic rồi thì nội suy chỉ thêm một bước physics trễ, mà tay VR trễ là thấy ngay.
        body.interpolation = RigidbodyInterpolation.None;
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;

        hand.PalmRadius = MeasurePalmRadius(hand);

        if (disableCollidersWhileLocked)
            DisableHandColliders(hand);

        // Bắt đầu một lần cầm mới thì blend lại từ chỗ tay thật.
        hand.WeldWeight = 0f;

        if (hand.Target != null)
            SnapToTarget(hand);
    }

    private void UnlockHand(Hand hand, Component releasedItem)
    {
        if (hand == null || hand.LockCount == 0)
            return;

        hand.LockCount--;

        if (hand.LockCount > 0)
            return;

        Rigidbody body = hand.Body;

        if (body != null)
        {
            body.isKinematic = false;
            body.interpolation = hand.SavedInterpolation;
            body.collisionDetectionMode = hand.SavedCollisionMode;

            // Không để lại vận tốc: Drive sẽ tự tính lại ngay FixedUpdate kế tiếp.
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        if (hand.DisabledColliders.Count == 0)
            return;

        // Chưa bật collider ngay: vật vừa thả vẫn đang nằm trong lòng bàn tay. Bật lúc này là
        // PhysX phải giải một overlap sâu với capsule kinematic -> vật bị hất bay.
        hand.ReleasedItemColliders.Clear();

        if (releasedItem != null)
        {
            foreach (Collider collider in releasedItem.GetComponentsInChildren<Collider>(true))
            {
                if (collider != null && !collider.isTrigger)
                    hand.ReleasedItemColliders.Add(collider);
            }
        }

        float delay = Mathf.Max(0f, colliderRestoreDelay);
        hand.ColliderRestoreTime = Time.time + delay;
        hand.ColliderRestoreDeadline = Time.time + Mathf.Max(delay, colliderRestoreTimeout);
    }

    private void UpdateColliderRestores()
    {
        foreach (Hand hand in hands)
        {
            if (hand.Locked || hand.DisabledColliders.Count == 0)
                continue;

            if (Time.time < hand.ColliderRestoreTime)
                continue;

            // Hết hạn chờ thì bật bằng mọi giá (vật có thể bị kẹp giữa tay và tường, không bao
            // giờ rời ra).
            bool force = Time.time >= hand.ColliderRestoreDeadline;

            if (!force && IsReleasedItemStillInPalm(hand))
                continue;

            RestoreHandColliders(hand);
        }
    }

    /// <summary>
    /// Vật vừa thả còn nằm trong lòng bàn tay không. Đo bằng quả cầu bọc bàn tay chứ không bằng
    /// bounds của collider tay: collider tay đang tắt, mà bounds của collider tắt thì không tin được.
    /// </summary>
    private static bool IsReleasedItemStillInPalm(Hand hand)
    {
        if (hand.ReleasedItemColliders.Count == 0 || hand.Body == null)
            return false;

        var palm = new Bounds(hand.Body.position, Vector3.one * (hand.PalmRadius * 2f));

        foreach (Collider collider in hand.ReleasedItemColliders)
        {
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                continue;

            if (palm.Intersects(collider.bounds))
                return true;
        }

        return false;
    }

    private static float MeasurePalmRadius(Hand hand)
    {
        if (hand.Body == null)
            return Mathf.Max(hand.PalmRadius, 0.15f);

        Vector3 center = hand.Body.position;
        float radius = 0f;

        foreach (Collider collider in hand.Body.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null || collider.isTrigger || collider is CharacterController)
                continue;

            if (!collider.enabled || !collider.gameObject.activeInHierarchy)
                continue;

            Bounds bounds = collider.bounds;
            radius = Mathf.Max(radius, Vector3.Distance(center, bounds.center) + bounds.extents.magnitude);
        }

        // Không đo được (HandPoseDriver đã tắt sạch capsule chẳng hạn): giữ số đo cũ.
        return radius > 0f ? radius : Mathf.Max(hand.PalmRadius, 0.15f);
    }

    private void DisableHandColliders(Hand hand)
    {
        if (hand.Body == null)
            return;

        // Quét lại chứ không dùng mảng cũ: model tay có thể vừa được bật/tắt theo chế độ nhập liệu.
        hand.Colliders = hand.Body.GetComponentsInChildren<Collider>(true);

        foreach (Collider collider in hand.Colliders)
        {
            if (collider == null || collider.isTrigger || collider is CharacterController)
                continue;

            // Đang tắt sẵn (HandPoseDriver tắt 16 capsule ngón khi áp dáng) thì để nguyên chủ cũ
            // của nó lo việc bật lại.
            if (!collider.enabled)
                continue;

            collider.enabled = false;

            if (!hand.DisabledColliders.Contains(collider))
                hand.DisabledColliders.Add(collider);
        }
    }

    private void RestoreHandColliders(Hand hand)
    {
        hand.ColliderRestoreTime = float.PositiveInfinity;
        hand.ReleasedItemColliders.Clear();

        if (hand.DisabledColliders.Count == 0)
            return;

        foreach (Collider collider in hand.DisabledColliders)
        {
            if (collider != null)
                collider.enabled = true;
        }

        hand.DisabledColliders.Clear();

        // Cờ IgnoreCollision của mấy collider vừa bật đã bị Unity xoá: đặt lại, kẻo tay đập vào
        // CharacterController của chính người chơi.
        RefreshSelfCollisionIgnores();
    }

    private void ReleaseAllLocks()
    {
        heldLocks.Clear();

        foreach (Hand hand in hands)
        {
            if (hand.LockCount > 0)
            {
                hand.LockCount = 1;
                UnlockHand(hand, null);
            }

            // Component bị tắt thì không còn ai bật collider lại giúp: bật ngay, kể cả khi vật
            // còn lún trong tay.
            RestoreHandColliders(hand);
        }
    }

    private void RefreshSelfIgnoresPeriodically()
    {
        if (selfIgnoreRefreshInterval <= 0f || hands.Count == 0 || Time.time < nextSelfIgnoreRefreshTime)
            return;

        nextSelfIgnoreRefreshTime = Time.time + selfIgnoreRefreshInterval;
        RefreshSelfCollisionIgnores();
    }

    private bool TryGetInteractorSide(IXRSelectInteractor interactor, out HandSide side)
    {
        switch (interactor.handedness)
        {
            case InteractorHandedness.Left:
                side = HandSide.Left;
                return true;

            case InteractorHandedness.Right:
                side = HandSide.Right;
                return true;
        }

        // Interactor chưa đặt Handedness: đoán theo tên trong chuỗi cha ("Left Controller").
        if (interactor is Component component && component != null)
            return TryGetSideFromNames(component.transform, out side);

        side = HandSide.Left;
        return false;
    }

    private static bool TryResolveHandSide(Rigidbody body, out HandSide side)
    {
        // HandSkeleton nằm trên gốc model tay - có thể là chính object có Rigidbody, có thể là
        // con hoặc cha của nó tuỳ cách prefab tay được lắp.
        HandSkeleton skeleton = body.GetComponentInChildren<HandSkeleton>(true);

        if (skeleton == null)
            skeleton = body.GetComponentInParent<HandSkeleton>(true);

        if (skeleton != null)
        {
            side = skeleton.Handedness;
            return true;
        }

        return TryGetSideFromNames(body.transform, out side);
    }

    private static bool TryGetSideFromNames(Transform start, out HandSide side)
    {
        for (Transform current = start; current != null; current = current.parent)
        {
            string name = current.name.ToLowerInvariant();

            if (name.Contains("left") || name.Contains("_l_"))
            {
                side = HandSide.Left;
                return true;
            }

            if (name.Contains("right") || name.Contains("_r_"))
            {
                side = HandSide.Right;
                return true;
            }
        }

        side = HandSide.Left;
        return false;
    }
}
