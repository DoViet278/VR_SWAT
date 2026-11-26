using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class Magazine : MonoBehaviour
{
    [SerializeField] private int capacity = 30;
    [SerializeField] private bool startFull = true;

    [Header("Loại băng")]
    [Tooltip("Băng này chỉ cắm được vào súng đúng loại. Khe băng của súng tự lấy loại từ " +
             "component Gun của nó, nên băng rifle không cắm được vào pistol và ngược lại.")]
    [SerializeField] private WeaponType magazineType = WeaponType.Rifle;

    [Tooltip("Băng chưa biết mình thuộc súng nào: khẩu đầu tiên nhận nó sẽ đóng dấu loại cho nó, " +
             "và trước đó thì khẩu nào cũng nhận. Dùng cho băng đặt sẵn trong súng lúc dựng scene: " +
             "không phải chọn tay từng cái. " +
             "Prefab băng (băng để trong túi đạn, băng nhặt được) nên TẮT cái này và chọn " +
             "Magazine Type rõ ràng, kẻo cầm băng rifle cắm vào pistol cũng được.")]
    [SerializeField] private bool typeFromFirstGun = true;

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

    [Header("Tư thế khi nằm trong socket")]
    [Tooltip("Tách điểm neo của socket ra khỏi điểm neo của tay.\n\n" +
             "Vì sao cần: XRGrabInteractable chỉ có MỘT Attach Transform, mà XRI dùng chung nó " +
             "cho cả tay lẫn socket. Riêng với socket thì XRI LUÔN dùng attach tĩnh — Use Dynamic " +
             "Attach bị bỏ qua (xem XRGrabInteractable.ShouldMatchAttachRotation). Nên khi xoay " +
             "Attach Transform cho khớp hand pose, băng bị xoay y hệt lúc cắm vào khe băng: băng " +
             "nằm ngang thay vì thẳng.\n\n" +
             "Bật cái này: lúc socket giữ băng thì dùng Socket Attach Transform, lúc tay cầm thì " +
             "trả lại Attach Transform cũ. Hand pose giữ nguyên.")]
    [SerializeField] private bool separateSocketAttach = true;

    [Tooltip("Điểm neo dành riêng cho socket (khe băng của súng, túi đạn). Để trống = dùng chính " +
             "gốc toạ độ của băng, tức là băng nằm đúng bằng pose của attach transform trên socket " +
             "(hoặc của chính GameObject socket, khi socket không gán attach transform).")]
    [SerializeField] private Transform socketAttachTransform;

    private int currentAmmo;

    private XRGrabInteractable grab;

    // Attach Transform gốc của băng (cái đã chỉnh cho hand pose). Nhớ lại từ Awake để còn trả về
    // sau khi băng rời khỏi socket.
    private Transform handAttachTransform;
    private bool handAttachCaptured;

    private readonly List<Collider> ownColliders = new List<Collider>();
    private readonly List<Collider> ignoredGunColliders = new List<Collider>();

    // Hẹn giờ bằng mốc thời gian chứ không dùng Coroutine: XRBaseInteractable.OnDisable() bắn
    // selectExited ngay trong lúc GameObject đang bị huỷ (MagazinePouch / SpawnedMagazineCleanup
    // gọi Destroy trên băng còn đang được cầm), mà lúc đó StartCoroutine không chạy được nữa.
    private float restoreCollisionAt = -1f;

    // Danh sách băng đang sống trong scene, để MagazineSwapController không phải quét
    // FindObjectsByType mỗi frame.
    private static readonly List<Magazine> activeMagazines = new List<Magazine>();

    public static IReadOnlyList<Magazine> ActiveMagazines => activeMagazines;

    public WeaponType MagazineType => magazineType;

    /// <summary>Băng chưa được đóng dấu loại, khẩu súng đầu tiên nhận nó sẽ đóng dấu.</summary>
    public bool TypeIsAuto => typeFromFirstGun;

    /// <summary>Băng này có cắm được vào súng loại <paramref name="weapon"/> hay không.</summary>
    public bool Fits(WeaponType weapon)
    {
        return typeFromFirstGun || magazineType == weapon;
    }

    /// <summary>Khẩu súng đầu tiên nhận băng này đóng dấu loại cho nó. Gọi nhiều lần cũng chỉ ăn lần đầu.</summary>
    public void AdoptTypeFrom(WeaponType weapon)
    {
        if (!typeFromFirstGun)
            return;

        magazineType = weapon;
        typeFromFirstGun = false;
    }

    public int Capacity => capacity;
    public int CurrentAmmo => currentAmmo;
    public bool IsEmpty => currentAmmo <= 0;
    public bool IsFull => currentAmmo >= capacity;

    /// <summary>Đang nằm trên tay người chơi (không tính socket của súng hay của túi đạn).</summary>
    public bool IsHeldByHand
    {
        get
        {
            if (grab == null)
                return false;

            List<IXRSelectInteractor> interactors = grab.interactorsSelecting;

            for (int i = 0; i < interactors.Count; i++)
            {
                if (!(interactors[i] is XRSocketInteractor))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Điểm neo mà socket dùng để đặt băng, tách khỏi neo của tay (Attach Transform trên
    /// XRGrabInteractable). MagazineSwapController đọc cái này để vẽ bóng ma trong khe băng
    /// đúng bằng chỗ băng sẽ nằm.
    /// </summary>
    public Transform SocketAttachPoint
    {
        get
        {
            if (!separateSocketAttach)
                return grab != null && grab.attachTransform != null ? grab.attachTransform : transform;

            return socketAttachTransform != null ? socketAttachTransform : transform;
        }
    }

    /// <summary>Khoảng cách từ mép collider của băng tới một điểm trong không gian.</summary>
    public float DistanceToPoint(Vector3 worldPoint)
    {
        float best = Vector3.Distance(transform.position, worldPoint);

        for (int i = 0; i < ownColliders.Count; i++)
        {
            Collider collider = ownColliders[i];

            if (!IsUsable(collider))
                continue;

            // Dùng bounds (AABB) chứ không dùng Collider.ClosestPoint: ClosestPoint báo lỗi với
            // MeshCollider không convex, mà băng đạn mỗi khẩu súng có thể dùng collider khác nhau.
            float distance = Vector3.Distance(collider.bounds.ClosestPoint(worldPoint), worldPoint);

            if (distance < best)
                best = distance;
        }

        return best;
    }

    private void Awake()
    {
        currentAmmo = startFull ? capacity : 0;

        grab = GetComponent<XRGrabInteractable>();

        if (grab != null)
        {
            handAttachTransform = grab.attachTransform;
            handAttachCaptured = true;
        }

        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
        {
            if (!collider.isTrigger)
                ownColliders.Add(collider);
        }
    }

    private void OnEnable()
    {
        if (!activeMagazines.Contains(this))
            activeMagazines.Add(this);

        // Ống đạn cố định trong thân súng (shotgun) không có grab, bỏ qua.
        if (grab == null)
            return;

        grab.selectEntered.AddListener(OnSelectEntered);
        grab.selectExited.AddListener(OnSelectExited);

        // Hover cũng phải theo dõi: socket vẽ bóng ma (hover mesh) bằng chính attach transform của
        // băng, và băng phải sẵn neo socket TRƯỚC khi socket chọn nó.
        grab.hoverEntered.AddListener(OnHoverEntered);
        grab.hoverExited.AddListener(OnHoverExited);

        RefreshAttachTransform(null);
    }

    private void OnDisable()
    {
        activeMagazines.Remove(this);

        if (grab != null)
        {
            grab.selectEntered.RemoveListener(OnSelectEntered);
            grab.selectExited.RemoveListener(OnSelectExited);
            grab.hoverEntered.RemoveListener(OnHoverEntered);
            grab.hoverExited.RemoveListener(OnHoverExited);

            RestoreHandAttachTransform();
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
        RefreshAttachTransform(null);

        if (!ignoreGunCollisionWhileHeld)
            return;

        restoreCollisionAt = -1f;
        SetIgnoreGunCollision(true);
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        RefreshAttachTransform(args.interactorObject);

        if (!ignoreGunCollisionWhileHeld)
            return;

        restoreCollisionAt = Time.time + Mathf.Max(0f, restoreCollisionDelay);
    }

    private void OnHoverEntered(HoverEnterEventArgs args)
    {
        RefreshAttachTransform(null);
    }

    private void OnHoverExited(HoverExitEventArgs args)
    {
        RefreshAttachTransform(args.interactorObject);
    }

    /// <summary>
    /// Đặt lại Attach Transform cho đúng với "ai đang giữ băng": socket thì dùng neo riêng của
    /// socket, tay thì dùng neo gốc (neo đã chỉnh theo hand pose).
    ///
    /// Đổi lúc chạy là hợp lệ: XRI đọc lại GetAttachTransform mỗi frame trong ProcessInteractable
    /// (XRSocketGrabTransformer.GetTargetPoseForInteractable), mà XRInteractionManager bắn
    /// select / hover event TRƯỚC bước đó trong cùng một frame, nên đổi ở đây là kịp.
    /// </summary>
    /// <param name="leaving">Interactor vừa rời ra. Lúc event bắn nó có thể vẫn còn trong danh
    /// sách của interactable, nên phải loại nó ra khi xét.</param>
    private void RefreshAttachTransform(IXRInteractor leaving)
    {
        if (grab == null || !handAttachCaptured || !separateSocketAttach)
            return;

        Transform desired = SocketIsDriving(leaving)
            ? (socketAttachTransform != null ? socketAttachTransform : transform)
            : handAttachTransform;

        if (grab.attachTransform != desired)
            grab.attachTransform = desired;
    }

    private void RestoreHandAttachTransform()
    {
        if (grab != null && handAttachCaptured && grab.attachTransform != handAttachTransform)
            grab.attachTransform = handAttachTransform;
    }

    /// <summary>Tư thế của băng lúc này do socket quyết định hay do tay người chơi quyết định.</summary>
    private bool SocketIsDriving(IXRInteractor leaving)
    {
        List<IXRSelectInteractor> selecting = grab.interactorsSelecting;
        bool socketSelecting = false;

        for (int i = 0; i < selecting.Count; i++)
        {
            IXRSelectInteractor interactor = selecting[i];

            if (ReferenceEquals(interactor, leaving))
                continue;

            // Tay đang cầm thì luôn ưu tiên neo của tay, kẻo băng nhảy khỏi hand pose.
            if (!(interactor is XRSocketInteractor))
                return false;

            socketSelecting = true;
        }

        if (socketSelecting)
            return true;

        // Không ai cầm: băng đang lơ lửng trước khe băng thì đã dùng neo socket luôn, để bóng ma
        // của socket vẽ đúng tư thế băng sẽ nằm, và để neo đã đúng sẵn trước lúc socket hút băng
        // vào (băng bật Use Dynamic Attach khởi tạo neo động ngay trong OnSelectEntering, sớm hơn
        // mọi event mà script bắt được).
        List<IXRHoverInteractor> hovering = grab.interactorsHovering;

        for (int i = 0; i < hovering.Count; i++)
        {
            if (!ReferenceEquals(hovering[i], leaving) && hovering[i] is XRSocketInteractor)
                return true;
        }

        return false;
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
