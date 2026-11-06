using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Tắt va chạm giữa vật đang cầm và collider của chính người chơi (bàn tay, thân), bật lại khi thả.
///
/// Vì sao cần: model tay trong Oculus Hands Physics có 24 capsule collider mỗi bên. Khi súng đổi
/// sang Velocity Tracking nó thành Rigidbody động, va ngay vào mấy capsule đó rồi rung bắn liên
/// tục ngay trên tay.
///
/// Lúc THẢ thì không được bật va chạm lại ngay: vật vẫn đang lún sâu trong lòng bàn tay (các capsule
/// ngón tay nắm quanh thân súng). Bật lại ngay là PhysX phải giải một overlap rất sâu với capsule
/// kinematic (khối lượng vô hạn) — vật bị đẩy bật ra, mà Gun.StabilizeBodyPhysics còn kẹp
/// maxDepenetrationVelocity nhỏ nên nó bị đẩy ra từ từ qua nhiều bước physics, trong khi tay vẫn
/// bám theo tracking và ngón tay vẫn animate nên lún lại liên tục -> súng lập bập đúng lúc thả.
/// Nên phải chờ tới khi bounds của hai collider đã tách nhau mới bỏ cờ ignore cho đúng cặp đó.
///
/// Va chạm với CharacterController thì KHÔNG phải xử lý ở đây: XRGrabInteractable của XRI đã tự
/// gọi StartIgnoringCharacterCollision khi cầm và trả lại khi vật đã ra khỏi capsule người chơi —
/// chính cơ chế chờ-tách-bounds mà file này áp dụng cho collider bàn tay.
/// </summary>
[DisallowMultipleComponent]
public class HeldItemCollisionFilter : MonoBehaviour
{
    [Header("Rig")]
    [Tooltip("Để trống sẽ tự tìm trên cùng GameObject rồi tới cả scene.")]
    [SerializeField] private XROrigin xrOrigin;

    [Tooltip("Gốc để quét collider của người chơi. Để trống sẽ dùng chính XR Origin.")]
    [SerializeField] private Transform playerColliderRoot;

    [Header("Tuỳ chọn")]
    [Tooltip("Chu kỳ quét lại collider người chơi khi đang cầm (giây). XRInputModalityManager bật/tắt " +
             "model tay giữa chế độ controller và hand-tracking, nên collider có thể xuất hiện muộn. " +
             "0 = không quét lại.")]
    [SerializeField] private float refreshInterval = 0.5f;

    [Tooltip("Thả vật ra: chờ tối đa bao lâu (giây) cho nó rời khỏi tay trước khi bật lại va chạm " +
             "bằng mọi giá. Từng cặp collider được trả lại ngay khi bounds của chúng tách nhau, " +
             "nên giá trị này chỉ là chốt an toàn cho trường hợp vật nằm luôn trong lòng bàn tay.\n\n" +
             "0 = bật lại ngay lúc thả (hành vi cũ, gây rung lập bập).")]
    [SerializeField] private float releaseTimeout = 3f;

    private readonly List<Collider> playerColliders = new List<Collider>();
    private readonly List<IXRSelectInteractor> interactors = new List<IXRSelectInteractor>();

    // Đếm số tay đang cầm: cầm 2 tay thì selectEntered bắn 2 lần, chỉ trả va chạm khi thả hết.
    private readonly Dictionary<IXRSelectInteractable, int> heldCounts =
        new Dictionary<IXRSelectInteractable, int>();

    // Mỗi vật giữ riêng danh sách cặp collider đã tắt va chạm, để trả lại đúng từng cặp.
    private readonly Dictionary<IXRSelectInteractable, IgnoreSet> ignoreSets =
        new Dictionary<IXRSelectInteractable, IgnoreSet>();

    private readonly List<IXRSelectInteractable> scratchKeys = new List<IXRSelectInteractable>();
    private readonly List<Collider> itemColliders = new List<Collider>();

    private float nextRefreshTime;

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

        Transform root = ColliderRoot;

        if (root == null)
        {
            Debug.LogWarning(nameof(HeldItemCollisionFilter) + ": không tìm thấy rig người chơi, tự tắt.", this);
            enabled = false;
            return;
        }

        CollectPlayerColliders();
        Subscribe(root);
    }

    private void OnDisable()
    {
        Unsubscribe();

        // Trả lại va chạm cho mọi thứ còn đang cầm / đang trả dở, kẻo nó dính trạng thái ignore
        // vĩnh viễn. Lúc này không chờ tách bounds được nữa nên trả ngay.
        foreach (IgnoreSet set in ignoreSets.Values)
            set.RestoreNow();

        ignoreSets.Clear();
        heldCounts.Clear();
        playerColliders.Clear();
    }

    private Transform ColliderRoot
    {
        get
        {
            if (playerColliderRoot != null)
                return playerColliderRoot;

            if (xrOrigin == null)
                return null;

            return xrOrigin.Origin != null ? xrOrigin.Origin.transform : xrOrigin.transform;
        }
    }

    private void CollectPlayerColliders()
    {
        playerColliders.Clear();

        Transform root = ColliderRoot;

        if (root == null)
            return;

        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            // Trigger không gây va chạm vật lý; CharacterController để XRI tự xử lý.
            if (collider.isTrigger || collider is CharacterController)
                continue;

            playerColliders.Add(collider);
        }
    }

    private void CollectItemColliders(Component item)
    {
        itemColliders.Clear();

        foreach (Collider collider in item.GetComponentsInChildren<Collider>(true))
        {
            if (collider.isTrigger)
                continue;

            itemColliders.Add(collider);
        }
    }

    private void Subscribe(Transform root)
    {
        interactors.Clear();

        foreach (XRBaseInteractor interactor in root.GetComponentsInChildren<XRBaseInteractor>(true))
        {
            if (!(interactor is IXRSelectInteractor selectInteractor))
                continue;

            interactor.selectEntered.AddListener(OnSelectEntered);
            interactor.selectExited.AddListener(OnSelectExited);
            interactors.Add(selectInteractor);
        }
    }

    private void Unsubscribe()
    {
        foreach (IXRSelectInteractor selectInteractor in interactors)
        {
            if (selectInteractor is XRBaseInteractor interactor && interactor != null)
            {
                interactor.selectEntered.RemoveListener(OnSelectEntered);
                interactor.selectExited.RemoveListener(OnSelectExited);
            }
        }

        interactors.Clear();
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        IXRSelectInteractable interactable = args.interactableObject;

        if (interactable == null)
            return;

        heldCounts.TryGetValue(interactable, out int count);
        heldCounts[interactable] = count + 1;

        if (count == 0)
        {
            CollectPlayerColliders();
            ApplyIgnore(interactable);
        }
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        IXRSelectInteractable interactable = args.interactableObject;

        if (interactable == null || !heldCounts.TryGetValue(interactable, out int count))
            return;

        count--;

        if (count > 0)
        {
            heldCounts[interactable] = count;
            return;
        }

        heldCounts.Remove(interactable);

        if (!ignoreSets.TryGetValue(interactable, out IgnoreSet set))
            return;

        // Bắt đầu trả lại va chạm theo từng cặp, chờ tách bounds trước.
        if (set.BeginRestore(releaseTimeout))
            ignoreSets.Remove(interactable);
    }

    private void Update()
    {
        AdvanceRestores();
        RefreshHeldIgnores();
    }

    private void AdvanceRestores()
    {
        if (ignoreSets.Count == 0)
            return;

        scratchKeys.Clear();
        scratchKeys.AddRange(ignoreSets.Keys);

        foreach (IXRSelectInteractable interactable in scratchKeys)
        {
            if (!ignoreSets.TryGetValue(interactable, out IgnoreSet set) || !set.IsRestoring)
                continue;

            if (set.Advance())
                ignoreSets.Remove(interactable);
        }
    }

    // Model tay có thể được bật muộn (đổi giữa controller và hand-tracking), lúc đó collider mới
    // xuất hiện và chưa được ignore. Quét lại định kỳ cho chắc; Physics.IgnoreCollision chỉ set
    // một bảng cờ nên gọi lại không tốn gì.
    private void RefreshHeldIgnores()
    {
        if (refreshInterval <= 0f || heldCounts.Count == 0 || Time.time < nextRefreshTime)
            return;

        nextRefreshTime = Time.time + refreshInterval;

        CollectPlayerColliders();

        foreach (IXRSelectInteractable interactable in heldCounts.Keys)
            ApplyIgnore(interactable);
    }

    private void ApplyIgnore(IXRSelectInteractable interactable)
    {
        if (!(interactable is Component component) || component == null)
            return;

        if (!ignoreSets.TryGetValue(interactable, out IgnoreSet set))
        {
            set = new IgnoreSet();
            ignoreSets[interactable] = set;
        }

        CollectItemColliders(component);
        set.Ignore(itemColliders, playerColliders);
    }

    // Physics.IgnoreCollision báo lỗi nếu một trong hai collider đang tắt.
    private static bool IsUsable(Collider collider)
    {
        return collider != null && collider.enabled && collider.gameObject.activeInHierarchy;
    }

    /// <summary>
    /// Nhóm cặp collider đang bị tắt va chạm cho MỘT vật, kèm cách trả lại va chạm an toàn: mỗi cặp
    /// chỉ được bỏ cờ ignore khi bounds của hai collider đã tách nhau (hoặc khi hết thời gian chờ).
    /// </summary>
    private sealed class IgnoreSet
    {
        private struct Pair
        {
            public Collider Item;
            public Collider Player;
        }

        private readonly List<Pair> pairs = new List<Pair>();
        private readonly List<Pair> restoring = new List<Pair>();
        private readonly HashSet<long> known = new HashSet<long>();

        private float deadline;

        public bool IsRestoring { get; private set; }

        /// <summary>Tắt va chạm giữa mọi collider của vật và mọi collider của người chơi.</summary>
        public void Ignore(List<Collider> item, List<Collider> player)
        {
            // Cầm lại giữa lúc đang trả dở: ignore lại toàn bộ.
            CancelRestore();

            foreach (Collider itemCollider in item)
            {
                if (!IsUsable(itemCollider))
                    continue;

                foreach (Collider playerCollider in player)
                {
                    if (!IsUsable(playerCollider) || playerCollider == itemCollider)
                        continue;

                    Physics.IgnoreCollision(itemCollider, playerCollider, true);
                    Remember(itemCollider, playerCollider);
                }
            }
        }

        /// <summary>
        /// Bắt đầu trả lại va chạm. Trả về true nếu đã trả xong ngay trong lần gọi này.
        /// </summary>
        public bool BeginRestore(float timeout)
        {
            restoring.Clear();
            restoring.AddRange(pairs);
            deadline = timeout > 0f ? Time.time + timeout : float.NegativeInfinity;
            IsRestoring = true;

            return Advance();
        }

        /// <summary>Trả lại toàn bộ va chạm ngay, kể cả khi hai vật còn đang lún vào nhau.</summary>
        public void RestoreNow()
        {
            BeginRestore(0f);
        }

        /// <summary>Gọi mỗi frame trong lúc trả. Trả về true khi đã trả hết.</summary>
        public bool Advance()
        {
            if (!IsRestoring)
                return true;

            bool force = Time.time >= deadline;

            for (int i = restoring.Count - 1; i >= 0; i--)
            {
                Pair pair = restoring[i];

                if (!IsUsable(pair.Item) || !IsUsable(pair.Player))
                {
                    // Collider đang tắt thì không gọi IgnoreCollision được. Thử lại frame sau; tới
                    // hạn thì đành bỏ cặp này (đúng như hành vi cũ, vốn cũng lọc collider đang tắt).
                    if (force)
                        restoring.RemoveAt(i);

                    continue;
                }

                // Còn chồng bounds nghĩa là vật vẫn nằm trong tay: bật va chạm bây giờ là bị hất.
                if (!force && pair.Item.bounds.Intersects(pair.Player.bounds))
                    continue;

                Physics.IgnoreCollision(pair.Item, pair.Player, false);
                restoring.RemoveAt(i);
            }

            if (restoring.Count > 0)
                return false;

            pairs.Clear();
            known.Clear();
            IsRestoring = false;
            return true;
        }

        private void CancelRestore()
        {
            if (!IsRestoring)
                return;

            IsRestoring = false;
            restoring.Clear();

            // Một số cặp có thể đã được trả lại rồi: ignore lại toàn bộ cho chắc.
            foreach (Pair pair in pairs)
            {
                if (IsUsable(pair.Item) && IsUsable(pair.Player))
                    Physics.IgnoreCollision(pair.Item, pair.Player, true);
            }
        }

        private void Remember(Collider item, Collider player)
        {
            long key = ((long)item.GetInstanceID() << 32) ^ (uint)player.GetInstanceID();

            if (known.Add(key))
                pairs.Add(new Pair { Item = item, Player = player });
        }
    }
}
