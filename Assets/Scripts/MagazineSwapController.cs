using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Thay băng kiểu "hot swap" cho súng có khe băng rời.
///
/// 1. Băng đã cắm vào súng là khoá luôn: tay người chơi không grab ra được nữa (MagazineLock).
/// 2. Muốn thay thì cầm băng mới đưa lại gần khe băng: băng cũ tự rớt ra khỏi súng, để trống chỗ.
/// 3. Băng mới vẫn nằm trên tay người chơi. Nhả tay ở ngay khe băng thì socket hút nó vào như
///    bình thường của XRI, rồi nó bị khoá lại.
/// 4. Khe băng chỉ nhận băng đúng loại súng: băng rifle không cắm được vào pistol. Loại lấy từ
///    component Gun của chính khẩu súng, nên không phải khai báo thêm.
///
/// Component này KHÔNG tự rút băng khỏi tay người chơi: việc cắm vào là do người chơi nhả tay.
///
/// Gắn component này lên chính GameObject của súng (cạnh GunAmmoManager).
/// </summary>
public class MagazineSwapController : MonoBehaviour, IXRSelectFilter, IXRHoverFilter
{
    [Header("Tham chiếu")]
    [Tooltip("Khe băng của súng. Để trống thì tự tìm XRSocketInteractor trong các con.")]
    [SerializeField] private XRSocketInteractor magazineSocket;

    [Tooltip("Grab của khẩu súng, chỉ dùng khi bật Require Gun Held. Để trống thì tự tìm.")]
    [SerializeField] private XRGrabInteractable gunGrab;

    [Header("Loại băng")]
    [Tooltip("Khe băng chỉ nhận băng cùng loại (xem Magazine Type trên băng đạn). Tắt thì khẩu " +
             "nào cũng nhận mọi băng như trước.")]
    [SerializeField] private bool restrictMagazineType = true;

    [Tooltip("Bật: lấy loại từ component Gun trên cùng GameObject (không có Gun thì dùng giá trị " +
             "bên dưới). Tắt: tự chọn loại băng mà khe này nhận.")]
    [SerializeField] private bool typeFromGunComponent = true;

    [SerializeField] private WeaponType acceptedMagazineType = WeaponType.Rifle;

    [Header("Khoá băng trong súng")]
    [Tooltip("Băng cắm vào súng thì tay không rút ra được nữa; chỉ còn cách đưa băng mới vào để " +
             "đẩy băng cũ ra. Tắt cái này thì vẫn rút băng ra bằng tay như mặc định của XRI.")]
    [SerializeField] private bool lockMagazineWhenInserted = true;

    [Header("Thay băng nóng")]
    [SerializeField] private bool hotSwapEnabled = true;

    [Tooltip("Khoảng cách (mét) từ khe băng tới băng đang cầm trên tay để băng cũ bị đẩy ra. " +
             "Đo theo mép collider của băng chứ không phải tâm, nên không phụ thuộc pivot của " +
             "model. Chọn component này trong Scene để thấy quả cầu gizmo của bán kính.")]
    [SerializeField] private float swapRadius = 0.12f;

    [Tooltip("Chỉ cho thay băng khi súng đang nằm trên tay, tránh việc đi ngang khẩu súng dưới " +
             "đất mà băng cũ của nó bị đẩy ra.")]
    [SerializeField] private bool requireGunHeld = false;

    [Tooltip("Thời gian nghỉ giữa hai lần đẩy băng (giây).")]
    [SerializeField] private float swapCooldown = 0.3f;

    [Tooltip("Đặt Recycle Delay Time của khe băng về 0 lúc chạy.\n\n" +
             "Vì sao cần: recycle delay của XRI chặn socket nhận vật trong mấy giây đầu sau khi " +
             "có vật rời khỏi nó (mặc định 1 giây) — nó sinh ra để vật bạn vừa rút ra không bị " +
             "hút vào lại ngay. Ở đây băng cũ không phải do tay rút mà do script đẩy ra, mà băng " +
             "cũ đã bị cấm cắm lại bằng Eject Socket Block Time rồi, nên giữ recycle delay chỉ " +
             "làm hỏng nhịp thay băng: nhả tay trong vòng 1 giây sau khi băng cũ rớt là băng mới " +
             "rơi xuống đất thay vì vào súng.")]
    [SerializeField] private bool clearSocketRecycleDelay = true;

    [Header("Nhả băng cũ")]
    [Tooltip("Hướng băng cũ rớt ra, tính theo hệ trục của khe băng. (0, -1, 0) = tụt xuống dưới.")]
    [SerializeField] private Vector3 ejectLocalDirection = Vector3.down;

    [Tooltip("Tốc độ ban đầu đẩy băng cũ ra (m/s).")]
    [SerializeField] private float ejectSpeed = 0.7f;

    [Tooltip("Băng cũ cộng thêm vận tốc của súng, để lúc đang chạy thì băng không bị bỏ lại sau.")]
    [SerializeField] private bool inheritGunVelocity = true;

    [Tooltip("Băng vừa rớt ra bị cấm cắm vào mọi socket trong bao lâu (giây), để nó không bị " +
             "chính khe băng hút lại ngay.")]
    [SerializeField] private float ejectSocketBlockTime = 0.8f;

    [Header("Bóng ma trong khe băng")]
    [Tooltip("Tự vẽ bóng ma (hover mesh) của băng thay cho XRSocketInteractor.\n\n" +
             "Vì sao cần: XRI vẽ bóng ma bằng chính Attach Transform của băng, mà neo đó đã bị xoay " +
             "cho khớp hand pose, nên bóng ma hiện nghiêng dù băng cắm vào vẫn thẳng (Magazine đổi " +
             "sang neo riêng cho socket lúc cắm). Bật cái này thì bóng ma dùng đúng neo ấy, nên nó " +
             "nằm trùng với chỗ băng sẽ nằm.\n\n" +
             "Tắt cái này thì trả bóng ma lại cho XRI (và nó lại hiện nghiêng).")]
    [SerializeField] private bool drawOwnSocketPreview = true;

    [Header("Events")]
    [Tooltip("Băng vừa được khoá vào súng.")]
    public UnityEvent OnMagazineLocked;
    [Tooltip("Băng cũ vừa bị đẩy ra khỏi súng.")]
    public UnityEvent OnMagazineEjected;

    private Rigidbody gunBody;
    private float nextEjectAt = -1f;

    private Magazine previewMagazine;
    private MeshFilter[] previewMeshes;
    private static readonly List<MeshFilter> previewMeshBuffer = new List<MeshFilter>();

    public XRSocketInteractor MagazineSocket => magazineSocket;

    private void Awake()
    {
        if (magazineSocket == null)
            magazineSocket = GetComponentInChildren<XRSocketInteractor>(true);

        if (gunGrab == null)
            gunGrab = GetComponentInParent<XRGrabInteractable>();

        gunBody = GetComponent<Rigidbody>();

        if (gunBody == null && gunGrab != null)
            gunBody = gunGrab.GetComponent<Rigidbody>();

        if (clearSocketRecycleDelay && magazineSocket != null)
            magazineSocket.recycleDelayTime = 0f;

        // Bóng ma của XRI dùng Attach Transform của băng (neo của tay) nên luôn hiện nghiêng;
        // tắt nó đi để LateUpdate tự vẽ lại bằng neo socket.
        if (drawOwnSocketPreview && magazineSocket != null)
            magazineSocket.showInteractableHoverMeshes = false;

        if (typeFromGunComponent)
        {
            Gun gun = GetComponent<Gun>();

            if (gun != null)
                acceptedMagazineType = gun.Type;
        }
    }

    private void OnEnable()
    {
        if (magazineSocket == null)
            return;

        magazineSocket.selectEntered.AddListener(HandleMagazineInserted);
        magazineSocket.selectExited.AddListener(HandleMagazineRemoved);

        // Chặn bằng filter của XRI chứ không chặn bằng interaction layer: hover filter làm băng
        // sai loại không hiện cả bóng ma trong khe, còn select filter chặn nốt việc cắm vào.
        magazineSocket.hoverFilters.Add(this);
        magazineSocket.selectFilters.Add(this);
    }

    private void OnDisable()
    {
        if (magazineSocket == null)
            return;

        magazineSocket.selectEntered.RemoveListener(HandleMagazineInserted);
        magazineSocket.selectExited.RemoveListener(HandleMagazineRemoved);

        magazineSocket.hoverFilters.Remove(this);
        magazineSocket.selectFilters.Remove(this);
    }

    // Băng cắm sẵn trong súng từ lúc dựng scene cũng phải bị khoá.
    private void Start()
    {
        Magazine socketed = GetSocketedMagazine();

        if (socketed != null && lockMagazineWhenInserted)
            EnsureLock(socketed).SetLockedInGun(true);
    }

    private void Update()
    {
        if (!hotSwapEnabled || magazineSocket == null)
            return;

        if (nextEjectAt > 0f && Time.time < nextEjectAt)
            return;

        // Khe đang trống: không có gì phải đẩy ra, cứ để XRI hút băng vào lúc người chơi nhả tay.
        Magazine current = GetSocketedMagazine();

        if (current == null)
            return;

        if (requireGunHeld && (gunGrab == null || !gunGrab.isSelected))
            return;

        Magazine incoming = FindIncomingMagazine();

        if (incoming == null || incoming == current)
            return;

        Eject(current);
        nextEjectAt = Time.time + Mathf.Max(0f, swapCooldown);
    }

    private void LateUpdate()
    {
        if (!drawOwnSocketPreview || magazineSocket == null)
            return;

        // Giữ cho bóng ma gốc luôn tắt, kể cả khi có ai bật lại lúc chạy.
        if (magazineSocket.showInteractableHoverMeshes)
            magazineSocket.showInteractableHoverMeshes = false;

        DrawSocketPreview();
    }

    /// <summary>
    /// Vẽ bóng ma của băng đang lơ lửng trước khe, đúng bằng tư thế băng sẽ nằm sau khi cắm.
    ///
    /// Vì sao phải tự vẽ: XRSocketInteractor.GetHoverMeshMatrix() lấy
    /// XRGrabInteractable.GetAttachTransform(socket), tức là Attach Transform đang dùng lúc đó.
    /// Trong lúc băng còn trên tay thì đó là neo của tay (đã xoay theo hand pose), nên bóng ma
    /// hiện nghiêng dù lúc cắm vào Magazine đã đổi sang neo riêng cho socket. Ở đây dùng thẳng
    /// Magazine.SocketAttachPoint nên bóng ma và chỗ cắm thật luôn khớp nhau.
    /// </summary>
    private void DrawSocketPreview()
    {
        Magazine magazine = FindPreviewMagazine();

        if (magazine == null)
        {
            previewMagazine = null;
            previewMeshes = null;
            return;
        }

        // XRI đổi màu bóng ma khi khe đã có băng (đỏ = chưa cắm được), giữ nguyên quy ước đó.
        Material material = magazineSocket.hasSelection
            ? magazineSocket.interactableCantHoverMeshMaterial
            : magazineSocket.interactableHoverMeshMaterial;

        if (material == null)
            return;

        if (previewMagazine != magazine)
        {
            previewMagazine = magazine;
            previewMeshBuffer.Clear();
            magazine.GetComponentsInChildren(true, previewMeshBuffer);
            previewMeshes = previewMeshBuffer.ToArray();
        }

        if (previewMeshes == null || previewMeshes.Length == 0)
            return;

        Transform socketAttach = magazineSocket.attachTransform != null
            ? magazineSocket.attachTransform
            : magazineSocket.transform;

        Transform magazineAttach = magazine.SocketAttachPoint;

        if (magazineAttach == null)
            return;

        // Phép dời hình đưa neo socket của băng về đúng neo của khe băng. Chỉ xoay và tịnh tiến,
        // không đụng tới scale, nên bóng ma giữ nguyên kích thước thật của băng.
        Quaternion deltaRotation = socketAttach.rotation * Quaternion.Inverse(magazineAttach.rotation);
        Vector3 deltaPosition = socketAttach.position - deltaRotation * magazineAttach.position;
        Matrix4x4 delta = Matrix4x4.TRS(deltaPosition, deltaRotation, Vector3.one);

        for (int i = 0; i < previewMeshes.Length; i++)
        {
            MeshFilter meshFilter = previewMeshes[i];

            if (meshFilter == null || meshFilter.sharedMesh == null)
                continue;

            if (!meshFilter.gameObject.activeInHierarchy)
                continue;

            Renderer meshRenderer = meshFilter.GetComponent<Renderer>();

            if (meshRenderer == null || !meshRenderer.enabled)
                continue;

            Mesh mesh = meshFilter.sharedMesh;
            Matrix4x4 matrix = delta * meshFilter.transform.localToWorldMatrix;

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                Graphics.DrawMesh(mesh, matrix, material, magazineSocket.gameObject.layer, null, submesh);
        }
    }

    /// <summary>Băng đang hover khe và sẽ được vẽ bóng ma. Bỏ qua chính băng đang cắm trong khe.</summary>
    private Magazine FindPreviewMagazine()
    {
        List<IXRHoverInteractable> hovered = magazineSocket.interactablesHovered;
        Magazine socketed = GetSocketedMagazine();

        for (int i = 0; i < hovered.Count; i++)
        {
            IXRHoverInteractable interactable = hovered[i];

            if (interactable == null)
                continue;

            Magazine magazine = interactable.transform.GetComponent<Magazine>();

            if (magazine == null || magazine == socketed)
                continue;

            // Băng sai loại không cắm được, đừng hứa hẹn bằng bóng ma.
            if (!Accepts(magazine))
                continue;

            return magazine;
        }

        return null;
    }

    /// <summary>Băng gần khe nhất đang nằm trên tay người chơi, trong tầm swapRadius.</summary>
    private Magazine FindIncomingMagazine()
    {
        IReadOnlyList<Magazine> magazines = Magazine.ActiveMagazines;
        Vector3 socketPoint = SocketPoint;
        Magazine best = null;
        float bestDistance = swapRadius;

        for (int i = 0; i < magazines.Count; i++)
        {
            Magazine magazine = magazines[i];

            if (magazine == null || !magazine.IsHeldByHand)
                continue;

            // Băng sai loại thì đừng đẩy băng cũ ra: đẩy xong cũng không cắm được băng mới vào,
            // chỉ làm người chơi mất không băng đang dùng.
            if (!Accepts(magazine))
                continue;

            float distance = magazine.DistanceToPoint(socketPoint);

            if (distance > bestDistance)
                continue;

            bestDistance = distance;
            best = magazine;
        }

        return best;
    }

    /// <summary>Đẩy băng đang cắm ra khỏi súng và trả lại quyền cầm cho người chơi.</summary>
    public void Eject(Magazine magazine)
    {
        if (magazine == null || magazineSocket == null)
            return;

        MagazineLock magazineLock = EnsureLock(magazine);
        magazineLock.SetLockedInGun(false);
        magazineLock.BlockSocketsFor(ejectSocketBlockTime);

        IXRSelectInteractable interactable = magazine.GetComponent<XRBaseInteractable>() as IXRSelectInteractable;
        XRInteractionManager manager = GetManager(interactable);

        if (interactable != null && manager != null && magazineSocket.IsSelecting(interactable))
            manager.SelectExit(magazineSocket, interactable);

        // Socket giữ băng ở chế độ Instantaneous (kinematic), phải trả lại cho physics rồi mới
        // đặt được vận tốc rơi.
        Rigidbody body = magazine.GetComponent<Rigidbody>();

        if (body != null)
        {
            body.isKinematic = false;
            body.useGravity = true;

            Vector3 direction = magazineSocket.transform.TransformDirection(ejectLocalDirection.normalized);
            Vector3 velocity = direction * ejectSpeed;

            if (inheritGunVelocity && gunBody != null && !gunBody.isKinematic)
                velocity += gunBody.linearVelocity;

            body.linearVelocity = velocity;
        }

        OnMagazineEjected?.Invoke();
    }

    public Magazine GetSocketedMagazine()
    {
        if (magazineSocket == null || !magazineSocket.hasSelection)
            return null;

        IXRSelectInteractable selected = magazineSocket.firstInteractableSelected;

        return selected == null ? null : selected.transform.GetComponent<Magazine>();
    }

    private void HandleMagazineInserted(SelectEnterEventArgs args)
    {
        Magazine magazine = args.interactableObject.transform.GetComponent<Magazine>();

        if (magazine == null)
            return;

        // Băng chưa có loại (băng đặt sẵn trong súng lúc dựng scene) thì nhận luôn loại của khẩu
        // đã nhận nó, từ lần sau không cắm sang khẩu khác được nữa.
        if (restrictMagazineType)
            magazine.AdoptTypeFrom(acceptedMagazineType);

        if (!lockMagazineWhenInserted)
            return;

        EnsureLock(magazine).SetLockedInGun(true);
        OnMagazineLocked?.Invoke();
    }

    private void HandleMagazineRemoved(SelectExitEventArgs args)
    {
        Magazine magazine = args.interactableObject.transform.GetComponent<Magazine>();

        if (magazine == null)
            return;

        MagazineLock magazineLock = magazine.GetComponent<MagazineLock>();

        if (magazineLock != null)
            magazineLock.SetLockedInGun(false);
    }

    /// <summary>Khe băng này có nhận cái băng này hay không (chỉ xét loại băng).</summary>
    public bool Accepts(Magazine magazine)
    {
        if (!restrictMagazineType || magazine == null)
            return true;

        return magazine.Fits(acceptedMagazineType);
    }

    public bool canProcess => isActiveAndEnabled;

    public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
    {
        return AcceptsInteractable(interactable);
    }

    public bool Process(IXRHoverInteractor interactor, IXRHoverInteractable interactable)
    {
        return AcceptsInteractable(interactable);
    }

    private bool AcceptsInteractable(IXRInteractable interactable)
    {
        if (!restrictMagazineType || interactable == null)
            return true;

        Magazine magazine = interactable.transform.GetComponent<Magazine>();

        // Không phải băng đạn thì luật này không có ý kiến, để XRI quyết như bình thường.
        return magazine == null || magazine.Fits(acceptedMagazineType);
    }

    // Lấy manager từ chính socket trước, chứ không FindAnyObjectByType: scene có thể có nhiều
    // XR Interaction Manager, mà socket với băng phải đăng ký cùng một manager mới cho phép cắm.
    private XRInteractionManager GetManager(IXRSelectInteractable interactable)
    {
        if (magazineSocket != null && magazineSocket.interactionManager != null)
            return magazineSocket.interactionManager;

        if (interactable is XRBaseInteractable baseInteractable)
            return baseInteractable.interactionManager;

        return null;
    }

    private static MagazineLock EnsureLock(Magazine magazine)
    {
        MagazineLock existing = magazine.GetComponent<MagazineLock>();

        return existing != null ? existing : magazine.gameObject.AddComponent<MagazineLock>();
    }

    private Vector3 SocketPoint
    {
        get
        {
            if (magazineSocket == null)
                return transform.position;

            Transform attach = magazineSocket.attachTransform;

            return attach != null ? attach.position : magazineSocket.transform.position;
        }
    }

    private void OnDrawGizmosSelected()
    {
        XRSocketInteractor socket = magazineSocket != null
            ? magazineSocket
            : GetComponentInChildren<XRSocketInteractor>(true);

        if (socket == null)
            return;

        Transform attach = socket.attachTransform;
        Vector3 point = attach != null ? attach.position : socket.transform.position;

        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(point, swapRadius);
        Gizmos.DrawLine(point, point + socket.transform.TransformDirection(ejectLocalDirection.normalized) * 0.15f);
    }
}
