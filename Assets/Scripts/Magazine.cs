using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class Magazine : MonoBehaviour
{
    [SerializeField] private int capacity = 30;
    [SerializeField] private bool startFull = true;

    [Header("Chống va chạm khi tra vào súng")]
    [Tooltip("Tắt va chạm giữa băng và thân súng trong lúc băng đang được cầm hoặc đang cắm trong " +
             "súng.\n\n" +
             "Vì sao cần: MeshCollider của súng để Convex = true, tức là PhysX chỉ thấy bao lồi của " +
             "khẩu súng chứ KHÔNG có khe băng. Băng đạn không có đường nào chui vào, nên đẩy băng " +
             "lên là nó đâm thẳng vào khối đặc đó rồi hất khẩu súng văng đi.\n\n" +
             "Collider trigger (MagazineSocket, LoadPort) không bị đụng tới, nên socket vẫn nhận " +
             "băng bình thường.")]
    [SerializeField] private bool ignoreGunCollisionWhileHeld = true;

    [Tooltip("Thả băng ra rồi chờ bao lâu mới bật va chạm lại (giây), để băng kịp rời khỏi thân súng. " +
             "Nếu lúc đó băng vẫn đang cắm trong súng thì giữ nguyên trạng thái tắt.")]
    [SerializeField] private float restoreCollisionDelay = 0.5f;

    private int currentAmmo;

    private XRGrabInteractable grab;
    private readonly List<Collider> ownColliders = new List<Collider>();
    private readonly List<Collider> ignoredGunColliders = new List<Collider>();

    // Hẹn giờ bằng mốc thời gian chứ không dùng Coroutine: XRBaseInteractable.OnDisable() bắn
    // selectExited ngay trong lúc GameObject đang bị huỷ (MagazinePouch / SpawnedMagazineCleanup
    // gọi Destroy trên băng còn đang được cầm), mà lúc đó StartCoroutine không chạy được nữa.
    private float restoreCollisionAt = -1f;

    public int Capacity => capacity;
    public int CurrentAmmo => currentAmmo;
    public bool IsEmpty => currentAmmo <= 0;
    public bool IsFull => currentAmmo >= capacity;

    private void Awake()
    {
        currentAmmo = startFull ? capacity : 0;

        grab = GetComponent<XRGrabInteractable>();

        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
        {
            if (!collider.isTrigger)
                ownColliders.Add(collider);
        }
    }

    private void OnEnable()
    {
        // Ống đạn cố định trong thân súng (shotgun) không có grab, bỏ qua.
        if (grab == null)
            return;

        grab.selectEntered.AddListener(OnSelectEntered);
        grab.selectExited.AddListener(OnSelectExited);
    }

    private void OnDisable()
    {
        if (grab != null)
        {
            grab.selectEntered.RemoveListener(OnSelectEntered);
            grab.selectExited.RemoveListener(OnSelectExited);
        }

        restoreCollisionAt = -1f;

        // Trả va chạm lại, kẻo băng dính trạng thái ignore vĩnh viễn.
        SetIgnoreGunCollision(false);
    }

    private void Update()
    {
        if (restoreCollisionAt < 0f || Time.time < restoreCollisionAt)
            return;

        restoreCollisionAt = -1f;

        // Rời tay nhưng vào socket của súng: vẫn đang nằm trong thân súng, giữ nguyên tắt va chạm.
        if (grab != null && grab.isSelected)
            return;

        SetIgnoreGunCollision(false);
    }

    public bool TryConsumeRound()
    {
        if (currentAmmo <= 0)
            return false;

        currentAmmo--;
        return true;
    }

    /// <summary>Nhồi 1 viên vào. Trả về false khi đã đầy.</summary>
    public bool TryAddRound()
    {
        if (currentAmmo >= capacity)
            return false;

        currentAmmo++;
        return true;
    }

    public void SetAmmo(int rounds)
    {
        currentAmmo = Mathf.Clamp(rounds, 0, capacity);
    }

    public void Refill()
    {
        currentAmmo = capacity;
    }

    // Cả tay cầm lẫn socket của súng đều bắn selectEntered, nên một luật là đủ cho cả hai:
    // băng đang được "chọn" bởi bất cứ thứ gì thì không va chạm với súng.
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

        // Quét lại mỗi lần cầm: súng có thể được spawn sau, hoặc băng vừa được dispenser tạo ra.
        ignoredGunColliders.Clear();

        foreach (GunAmmoManager gun in FindObjectsByType<GunAmmoManager>(FindObjectsSortMode.None))
        {
            foreach (Collider gunCollider in gun.GetComponentsInChildren<Collider>(true))
            {
                // Trigger phải giữ nguyên: MagazineSocket chính là trigger, tắt nó đi thì không
                // bao giờ tra được băng vào súng nữa.
                if (gunCollider.isTrigger)
                    continue;

                // Băng đã cắm trong súng thì nằm trong hierarchy của súng, đừng tự tắt với chính mình.
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
