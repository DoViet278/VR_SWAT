using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Một viên đạn shotgun rời, cầm được bằng tay và nhét được vào cửa nạp của súng.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class ShotgunShell : MonoBehaviour
{
    [Tooltip("Đầu viên đạn: trục +Z phải chỉ ra ngoài theo hướng nhét vào súng. Để trống sẽ dùng chính transform này.")]
    [SerializeField] private Transform noseTip;
    [Tooltip("Đạn thật (nạp được). Tắt với vỏ đạn đã bắn.")]
    [SerializeField] private bool isLive = true;

    [Header("Chống va chạm khi nạp vào súng")]
    [Tooltip("Tắt va chạm giữa viên đạn và thân súng trong lúc viên đạn đang được cầm trên tay.\n\n" +
             "Vì sao cần: collider thân súng (Collision, MeshCollider convex...) bọc kín cả vùng " +
             "cửa nạp, PhysX không thấy cái khe nào cả. Đẩy viên đạn vào là nó đâm vào khối đặc đó " +
             "nên không bao giờ chạm tới đủ độ sâu requiredDepth của ShellLoadPort.\n\n" +
             "Collider trigger (ShellLoadPort, MagazineSocket) không bị đụng tới, nên cửa nạp vẫn " +
             "nhận đạn bình thường.")]
    [SerializeField] private bool ignoreGunCollisionWhileHeld = true;

    [Tooltip("Thả đạn ra rồi chờ bao lâu mới bật va chạm lại (giây), để viên đạn kịp rời khỏi thân súng.")]
    [SerializeField] private float restoreCollisionDelay = 0.5f;

    public bool IsLive => isLive;
    public bool IsConsumed { get; private set; }

    public Transform NoseTip => noseTip != null ? noseTip : transform;
    public Rigidbody Body { get; private set; }
    public XRGrabInteractable Grab { get; private set; }

    public bool IsHeld => Grab != null && Grab.isSelected;

    private readonly List<Collider> ownColliders = new List<Collider>();
    private readonly List<Collider> ignoredGunColliders = new List<Collider>();

    // Hẹn giờ bằng mốc thời gian chứ không dùng Coroutine: viên đạn có thể bị Destroy ngay trong
    // lúc còn đang được cầm, lúc đó StartCoroutine không chạy được nữa.
    private float restoreCollisionAt = -1f;

    private void Awake()
    {
        Body = GetComponent<Rigidbody>();
        Grab = GetComponent<XRGrabInteractable>();

        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
        {
            if (!collider.isTrigger)
                ownColliders.Add(collider);
        }
    }

    private void OnEnable()
    {
        if (Grab == null)
            return;

        Grab.selectEntered.AddListener(OnSelectEntered);
        Grab.selectExited.AddListener(OnSelectExited);

        // Đạn được dispenser nhét thẳng vào tay: selectEntered có thể đã bắn trước OnEnable.
        if (ignoreGunCollisionWhileHeld && Grab.isSelected)
            SetIgnoreGunCollision(true);
    }

    private void OnDisable()
    {
        if (Grab != null)
        {
            Grab.selectEntered.RemoveListener(OnSelectEntered);
            Grab.selectExited.RemoveListener(OnSelectExited);
        }

        restoreCollisionAt = -1f;

        // Trả va chạm lại, kẻo súng dính trạng thái ignore với collider này vĩnh viễn.
        SetIgnoreGunCollision(false);
    }

    private void Update()
    {
        if (restoreCollisionAt < 0f || Time.time < restoreCollisionAt)
            return;

        restoreCollisionAt = -1f;

        // Vừa nhả tay này đã cầm sang tay kia thì giữ nguyên trạng thái tắt va chạm.
        if (Grab != null && Grab.isSelected)
            return;

        SetIgnoreGunCollision(false);
    }

    /// <summary>Chốt lại là viên đạn này đã được nhận, không cửa nạp nào nhận lại lần nữa.</summary>
    public void MarkConsumed()
    {
        IsConsumed = true;
    }

    /// <summary>Viên đạn đã trượt vào trong súng: biến mất khỏi thế giới.</summary>
    public void Consume()
    {
        MarkConsumed();

        if (Grab != null)
            Grab.enabled = false;

        Destroy(gameObject);
    }

    /// <summary>Chuẩn bị cho animation trượt vào súng: tắt vật lý và va chạm.</summary>
    public void PrepareForInsertion()
    {
        if (Grab != null)
            Grab.enabled = false;

        if (Body != null)
        {
            Body.isKinematic = true;
            Body.detectCollisions = false;
        }

        // Bỏ cờ ignore trước khi tắt collider: Physics.IgnoreCollision không gọi được trên
        // collider đang tắt, để sót là nó dính trạng thái ignore cho tới lúc bị huỷ.
        SetIgnoreGunCollision(false);
        restoreCollisionAt = -1f;

        foreach (Collider shellCollider in GetComponentsInChildren<Collider>())
            shellCollider.enabled = false;
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (!ignoreGunCollisionWhileHeld)
            return;

        restoreCollisionAt = -1f;
        SetIgnoreGunCollision(true);
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        if (!ignoreGunCollisionWhileHeld)
            return;

        restoreCollisionAt = Time.time + Mathf.Max(0f, restoreCollisionDelay);
    }

    private void SetIgnoreGunCollision(bool ignore)
    {
        if (!ignore)
        {
            foreach (Collider gunCollider in ignoredGunColliders)
                ApplyIgnore(gunCollider, false);

            ignoredGunColliders.Clear();
            return;
        }

        // Quét lại mỗi lần cầm: súng có thể được spawn sau, hoặc viên đạn vừa được túi đạn tạo ra.
        ignoredGunColliders.Clear();

        foreach (GunAmmoManager gun in FindObjectsByType<GunAmmoManager>(FindObjectsSortMode.None))
        {
            foreach (Collider gunCollider in gun.GetComponentsInChildren<Collider>(true))
            {
                // Trigger phải giữ nguyên: ShellLoadPort chính là trigger, tắt nó đi thì không bao
                // giờ nạp được đạn nữa.
                if (gunCollider.isTrigger)
                    continue;

                if (gunCollider.transform.IsChildOf(transform))
                    continue;

                ignoredGunColliders.Add(gunCollider);
            }
        }

        foreach (Collider gunCollider in ignoredGunColliders)
            ApplyIgnore(gunCollider, true);
    }

    private void ApplyIgnore(Collider gunCollider, bool ignore)
    {
        if (!IsUsable(gunCollider))
            return;

        foreach (Collider ownCollider in ownColliders)
        {
            if (IsUsable(ownCollider))
                Physics.IgnoreCollision(ownCollider, gunCollider, ignore);
        }
    }

    // Physics.IgnoreCollision báo lỗi nếu một trong hai collider đang tắt.
    private static bool IsUsable(Collider collider)
    {
        return collider != null && collider.enabled && collider.gameObject.activeInHierarchy;
    }
}
