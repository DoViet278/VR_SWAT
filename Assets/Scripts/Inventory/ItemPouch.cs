using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Túi đồ đeo quanh hông: tự dựng các ô socket lúc runtime, nhồi sẵn một bộ trang bị lúc vào game
/// (súng + băng đạn), và cho người chơi rút ra / nhét lại.
///
/// Khác <see cref="MagazinePouch"/> ở ba điểm:
/// 1. Chứa được nhiều loại vật phẩm, không chỉ băng đạn: súng, băng đạn, và bất cứ thứ gì có
///    component <see cref="PouchItem"/>.
/// 2. KHÔNG tự sinh lại vật phẩm. Rút khẩu súng ra là ô đó trống cho tới khi có người nhét đồ vào.
///    Không có kho dự trữ, không có respawn.
/// 3. Nhặt được hàng ở ngoài thì nhét vào ô trống nào cũng được, y như XRI socket bình thường.
///
/// Gắn component này lên GameObject túi (con của HipPouchAnchor).
/// </summary>
public class ItemPouch : MonoBehaviour, IXRSelectFilter, IXRHoverFilter
{
    public enum SlotLayout
    {
        Ring,
        Arc,
        Row,
        Anchors
    }

    [System.Serializable]
    public class LoadoutEntry
    {
        [Tooltip("Prefab vật phẩm cấp sẵn lúc vào game.")]
        public GameObject prefab;

        [Tooltip("Ô chứa vật này, 0 là ô đầu tiên. Để -1 thì tự xếp vào ô trống kế tiếp.")]
        public int slotIndex = -1;
    }

    [Header("Trang bị ban đầu")]
    [Tooltip("Vật phẩm spawn sẵn trong túi lúc vào game. Túi KHÔNG sinh thêm sau đó.")]
    [SerializeField] private List<LoadoutEntry> startingItems = new List<LoadoutEntry>();

    [Tooltip("Chờ bấy nhiêu giây sau khi vào game mới nhồi trang bị, để XRI kịp đăng ký socket.")]
    [SerializeField] private float initialSpawnDelay = 0.25f;

    [Tooltip("Băng đạn trong bộ trang bị ban đầu được nạp đầy lúc spawn.")]
    [SerializeField] private bool fillMagazinesOnSpawn = true;

    [Header("Số ô")]
    [Tooltip("Số ô dựng quanh hông. Layout Anchors có nhiều anchor hơn thì lấy theo số anchor, " +
             "nhưng không bao giờ ít hơn số này.")]
    [SerializeField] private int maxSlots = 5;

    [Header("Nhận lại vật phẩm")]
    [Tooltip("Cho nhét súng (có component Gun) vào túi.")]
    [SerializeField] private bool acceptGuns = true;

    [Tooltip("Cho nhét băng đạn (có component Magazine) vào túi.")]
    [SerializeField] private bool acceptMagazines = true;

    [Tooltip("Cho nhét lựu đạn (có component VRGrenade) vào túi.")]
    [SerializeField] private bool acceptGrenades = true;

    [Tooltip("Nhận mọi vật grab được, bỏ qua mọi điều kiện ở trên. Bật cái này thì cái gì thả gần " +
             "hông cũng bị hút vào túi, kể cả vỏ đạn hay tay cầm cửa.")]
    [SerializeField] private bool acceptAnyGrabbable = false;

    [Tooltip("Nhét băng đạn vào túi thì nó được nạp đầy lại. Mặc định TẮT: túi này không tự sinh " +
             "đạn, nên refill ở đây là đạn vô hạn trá hình.")]
    [SerializeField] private bool refillMagazineOnStore = false;

    [Header("Mỗi ô một loại")]
    [Tooltip("Mỗi ô chỉ nhận đúng loại khai ở Slot Allowed Kinds. Tắt thì về luật chung ở trên.")]
    [SerializeField] private bool restrictSlotsByKind = true;

    [Tooltip("Loại vật phẩm từng ô nhận, theo đúng thứ tự ô. Một ô tick được nhiều loại " +
             "(ví dụ ô súng dài: Rifle + Shotgun). Ô nào để None, hoặc thiếu phần tử, thì nhận theo luật chung.")]
    [SerializeField] private List<PouchItemKind> slotAllowedKinds = new List<PouchItemKind>();

    [Header("Slot Layout")]
    [SerializeField] private SlotLayout layout = SlotLayout.Arc;
    [Tooltip("Bán kính vòng đai quanh hông (Ring / Arc).")]
    [SerializeField] private float slotRingRadius = 0.18f;
    [Tooltip("Góc mở của cung chứa các ô, tính bằng độ (Arc).")]
    [SerializeField] private float arcSpanDegrees = 150f;
    [Tooltip("Góc tâm của cung so với hướng forward của túi (Arc).")]
    [SerializeField] private float arcCenterDegrees = 0f;
    [Tooltip("Khoảng cách giữa hai ô khi xếp thành hàng thẳng (Row).")]
    [SerializeField] private float rowSpacing = 0.09f;
    [Tooltip("Dịch cao/thấp cho toàn bộ các ô.")]
    [SerializeField] private float slotHeightOffset = 0f;
    [Tooltip("Vị trí từng ô do bạn tự đặt (Anchors).")]
    [SerializeField] private List<Transform> slotAnchors = new List<Transform>();
    [Tooltip("Bật: mỗi ô xoay hướng ra ngoài theo góc của nó trên cung. Tắt: mọi ô dùng chung một " +
             "hướng (slotLocalEuler), chỉ khác vị trí.")]
    [SerializeField] private bool alignSlotsToArc = false;
    [Tooltip("Hướng chung của vật phẩm trong ô.")]
    [SerializeField] private Vector3 slotLocalEuler = Vector3.zero;
    [Tooltip("Ghi đè góc xoay riêng cho từng ô, theo đúng thứ tự ô. Ô nào không có phần tử tương " +
             "ứng thì dùng slotLocalEuler.")]
    [SerializeField] private List<Vector3> slotEulerOverrides = new List<Vector3>();
    [Tooltip("Ghi đè dịch vị trí riêng cho từng ô, cộng thêm vào vị trí tính theo layout.")]
    [SerializeField] private List<Vector3> slotPositionOffsets = new List<Vector3>();
    [Tooltip("Kích thước vùng trigger của ô. Để lớn quá thì hai ô cạnh nhau tranh nhau vật phẩm.")]
    [SerializeField] private Vector3 slotTriggerSize = new Vector3(0.1f, 0.1f, 0.1f);
    [Tooltip("Interaction Layer của ô. PHẢI trùng ít nhất một bit với vật phẩm: súng đang ở layer " +
             "Default (bit 0), băng đạn ở Default + Magazine (bit 0 và 1), nên để cả hai bit.")]
    [SerializeField] private InteractionLayerMask itemInteractionLayers = (1 << 0) | (1 << 1);

    [Header("Thu nhỏ vật trong ô")]
    [Tooltip("Thu nhỏ vật phẩm trong lúc nó nằm trong túi, để khẩu rifle không to quá bên hông. " +
             "XRI trả lại kích thước gốc khi vật rời ô.")]
    [SerializeField] private bool scaleStoredItems = false;
    [SerializeField] private Vector3 storedItemScale = Vector3.one;

    [Header("Va chạm của đồ trong túi")]
    [Tooltip("Đồ đang nằm trong túi không sinh va chạm vật lý với bất cứ thứ gì: không đẩy người " +
             "chơi, không đẩy bàn tay, không cấn tường, không cấn lẫn nhau. Tay vẫn rút ra được " +
             "bình thường vì query của interactor không bị ảnh hưởng.\n\n" +
             "BẮT BUỘC bật. Túi nằm ngay trên trục capsule ở tầm hông, mà slotRingRadius (0.18) " +
             "lại nhỏ hơn bán kính CharacterController (0.2), nên collider của đồ trong túi lún " +
             "xuyên vách capsule. Overlap recovery của CharacterController.Move() đẩy capsule ra " +
             "khỏi chúng mỗi frame, mà chúng đi theo người chơi nên overlap không bao giờ hết -> " +
             "người chơi bị đẩy lùi vô tận. Mọi ô đều nằm trong cung -75..+75 độ quanh hướng " +
             "forward nên lực đẩy luôn hướng về sau.")]
    [SerializeField] private bool storedItemsIgnorePhysics = true;

    [Tooltip("Chu kỳ quét lại collider của đồ trong túi (giây), để bắt collider mới sinh ra hoặc " +
             "mới được bật lên sau khi nhét vào — súng sinh collider lúc chạy (băng đạn bên trong, " +
             "socket preview) nên quét một lần lúc nhét vào là không đủ.")]
    [SerializeField] private float refreshInterval = 0.5f;

    [Tooltip("Capsule người chơi, dùng để biết lúc nào đồ vừa rút ra đã rời khỏi người. Để trống " +
             "sẽ tự tìm CharacterController trên cha của túi.")]
    [SerializeField] private CharacterController playerCollider;

    [Tooltip("Rút đồ ra khỏi túi: chờ tối đa bao lâu (giây) cho nó rời khỏi capsule người chơi " +
             "trước khi trả lại va chạm bằng mọi giá. Từng collider được trả lại ngay khi bounds " +
             "của nó tách khỏi bounds capsule.\n\n" +
             "0 = chờ vô hạn. Nên để 0: đặt một mốc hết hạn nghĩa là vật nào bị giữ sát người lâu " +
             "hơn mốc đó sẽ đẩy người chơi lùi lại y như cũ.")]
    [SerializeField] private float releaseRestoreTimeout = 0f;

    [Header("Interaction")]
    [SerializeField] private XRInteractionManager interactionManager;

    [Header("Events")]
    public UnityEvent<GameObject> OnItemStored;
    public UnityEvent<GameObject> OnItemTaken;
    public UnityEvent OnPouchEmpty;

    private readonly List<Slot> slots = new List<Slot>();

    // Mỗi vật giữ riêng danh sách collider đã tắt va chạm, kèm mask cũ để trả lại đúng từng cái.
    private readonly Dictionary<Transform, ItemIgnore> itemIgnores =
        new Dictionary<Transform, ItemIgnore>();

    private readonly List<Transform> scratchItems = new List<Transform>();

    /// <summary>Số vật phẩm đang nằm trong túi.</summary>
    public int StoredCount
    {
        get
        {
            int count = 0;

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].socket != null && slots[i].socket.hasSelection)
                    count++;
            }

            return count;
        }
    }

    public int SlotCount => slots.Count;

    /// <summary>Danh sách trang bị ban đầu, chỉ đọc. Editor đọc cái này để vẽ preview.</summary>
    public IReadOnlyList<LoadoutEntry> StartingItems => startingItems;

    /// <summary>Editor đọc hai cái này để preview đúng kích thước vật lúc nằm trong ô.</summary>
    public bool ScaleStoredItems => scaleStoredItems;

    public Vector3 StoredItemScale => storedItemScale;

    /// <summary>
    /// Số ô túi sẽ dựng với thông số hiện tại. Tính được cả ngoài Play.
    ///
    /// Layout Anchors lấy số lớn hơn giữa Max Slots và số anchor, KHÔNG lấy thẳng số anchor nữa.
    /// Trước đó lấy thẳng, nên một mảng Slot Anchors 5 phần tử TOÀN NULL cũng âm thầm khoá túi
    /// còn 5 ô và Max Slots bị bỏ qua không một lời cảnh báo — đồ xệp cuối danh sách không có chỗ
    /// nào để spawn.
    /// </summary>
    public int ResolvedSlotCount => layout == SlotLayout.Anchors
        ? Mathf.Max(1, Mathf.Max(maxSlots, slotAnchors.Count))
        : Mathf.Max(1, maxSlots);

    /// <summary>
    /// Tư thế của ô thứ index, tính theo trục của túi đồ — đúng cái mà BuildSlots sẽ dùng.
    /// Tách riêng ra để ItemPouchEditor dùng chung một phép tính với lúc chạy, khỏi vẽ preview
    /// một chỗ mà vào Play đồ nằm một chỗ.
    /// </summary>
    public void GetSlotLocalPose(int index, out Vector3 localPosition, out Quaternion localRotation)
    {
        if (layout == SlotLayout.Anchors && index >= 0 && index < slotAnchors.Count && slotAnchors[index] != null)
        {
            Transform anchor = slotAnchors[index];
            localPosition = transform.InverseTransformPoint(anchor.position);
            localRotation = Quaternion.Inverse(transform.rotation) * anchor.rotation;
            return;
        }

        int slotCount = ResolvedSlotCount;
        localPosition = GetSlotLocalPosition(index, slotCount);
        localRotation = GetSlotLocalRotation(index, slotCount);
    }

    public bool IsEmpty => StoredCount == 0;

    public bool IsFull => slots.Count > 0 && StoredCount >= slots.Count;

    private void Awake()
    {
        if (playerCollider == null)
            playerCollider = GetComponentInParent<CharacterController>();
    }

    private void Start()
    {
        BuildSlots();
        StartCoroutine(SpawnLoadoutRoutine());
        StartCoroutine(RefreshIgnoresRoutine());
    }

    /// <summary>
    /// Manager của ô túi. Để trống field interactionManager là tốt nhất: XRI tự gán đúng
    /// manager mặc định mà tay người chơi cũng đăng ký vào.
    ///
    /// Đừng tự đi FindAnyObjectByType&lt;XRInteractionManager&gt;(): scene này có TỚI 2 XR Interaction
    /// Manager (một trong prefab "XR All", một rời trong scene), mà interactor đăng ký ở manager
    /// khác với interactable thì KHÔNG tương tác được với nhau. Rebuild/lưu scene là thứ tự
    /// FindAnyObjectByType đảo, túi rơi sang manager còn lại và đồ nằm trong ô nhưng tay không
    /// rút ra được nữa.
    /// </summary>
    private XRInteractionManager ResolveManager(XRSocketInteractor socket)
    {
        if (interactionManager != null)
            return interactionManager;

        return socket != null ? socket.interactionManager : null;
    }

    private void BuildSlots()
    {
        int slotCount = ResolvedSlotCount;

        for (int i = 0; i < slotCount; i++)
        {
            GameObject slotObject = new GameObject($"PouchSlot {i}");
            slotObject.SetActive(false);
            slotObject.transform.SetParent(transform, false);

            if (layout == SlotLayout.Anchors && i < slotAnchors.Count && slotAnchors[i] != null)
            {
                slotObject.transform.SetPositionAndRotation(slotAnchors[i].position, slotAnchors[i].rotation);
            }
            else
            {
                slotObject.transform.localPosition = GetSlotLocalPosition(i, slotCount);
                slotObject.transform.localRotation = GetSlotLocalRotation(i, slotCount);
            }

            BoxCollider trigger = slotObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = slotTriggerSize;

            // Điểm neo riêng, để tư thế của từng vật phẩm (PouchItem) chỉnh được mà không phải
            // xoay cả cái ô: collider trigger của ô giữ nguyên chỗ cũ.
            GameObject attachObject = new GameObject("Attach");
            attachObject.transform.SetParent(slotObject.transform, false);

            XRSocketInteractor socket = slotObject.AddComponent<XRSocketInteractor>();

            // Chỉ gán khi người dùng tự kéo manager vào field; để trống thì XRI tự tìm đúng
            // manager mặc định (xem ResolveManager).
            if (interactionManager != null)
                socket.interactionManager = interactionManager;

            socket.interactionLayers = itemInteractionLayers;
            socket.attachTransform = attachObject.transform;
            socket.showInteractableHoverMeshes = false;
            socket.recycleDelayTime = 0f;

            if (scaleStoredItems)
            {
                socket.socketScaleMode = SocketScaleMode.Fixed;
                socket.fixedScale = storedItemScale;
            }
            else
            {
                socket.socketScaleMode = SocketScaleMode.None;
            }

            socket.selectEntered.AddListener(HandleSlotFilled);
            socket.selectExited.AddListener(HandleSlotEmptied);

            // Lọc theo loại vật phẩm. Interaction Layer của ô buộc phải chứa cả layer Default
            // (vì súng nằm ở đó), nên nếu không lọc thì mọi thứ grab được thả gần hông đều bị
            // hút vào túi.
            socket.hoverFilters.Add(this);
            socket.selectFilters.Add(this);

            slotObject.SetActive(true);

            slots.Add(new Slot
            {
                socket = socket,
                attach = attachObject.transform
            });
        }
    }

    private float GetSlotAngle(int index, int slotCount)
    {
        if (layout == SlotLayout.Ring)
            return slotCount <= 1 ? 0f : 360f / slotCount * index;

        if (slotCount <= 1)
            return arcCenterDegrees;

        float t = index / (float)(slotCount - 1);
        return arcCenterDegrees - arcSpanDegrees * 0.5f + arcSpanDegrees * t;
    }

    private Vector3 GetSlotLocalPosition(int index, int slotCount)
    {
        Vector3 height = Vector3.up * slotHeightOffset;
        Vector3 extraOffset = index < slotPositionOffsets.Count ? slotPositionOffsets[index] : Vector3.zero;

        if (layout == SlotLayout.Row)
        {
            float offset = (slotCount - 1) * 0.5f;
            return new Vector3((index - offset) * rowSpacing, 0f, 0f) + height + extraOffset;
        }

        float angle = GetSlotAngle(index, slotCount);
        return Quaternion.Euler(0f, angle, 0f) * (Vector3.forward * slotRingRadius) + height + extraOffset;
    }

    private Quaternion GetSlotLocalRotation(int index, int slotCount)
    {
        // Góc riêng của ô này được ưu tiên tuyệt đối, bỏ qua cả việc xoay theo cung.
        if (index < slotEulerOverrides.Count)
            return Quaternion.Euler(slotEulerOverrides[index]);

        Quaternion extra = Quaternion.Euler(slotLocalEuler);

        if (!alignSlotsToArc || layout == SlotLayout.Row)
            return extra;

        return Quaternion.Euler(0f, GetSlotAngle(index, slotCount), 0f) * extra;
    }

    private IEnumerator SpawnLoadoutRoutine()
    {
        if (initialSpawnDelay > 0f)
            yield return new WaitForSeconds(initialSpawnDelay);

        SpawnLoadout();
    }

    /// <summary>
    /// Nhồi bộ trang bị ban đầu vào các ô trống. Gọi lại được (hồi sinh, bắt đầu màn mới).
    /// Ô nào đang có đồ thì bỏ qua, không nhân bản thêm.
    /// </summary>
    [ContextMenu("Spawn Starting Loadout")]
    public void SpawnLoadout()
    {
        for (int i = 0; i < startingItems.Count; i++)
        {
            LoadoutEntry entry = startingItems[i];

            if (entry == null || entry.prefab == null)
                continue;

            Slot slot = ResolveSlot(entry.slotIndex, entry.prefab.transform);

            if (slot == null)
            {
                Debug.LogWarning($"{name}: khong co o nao nhan {entry.prefab.name} (loai " +
                                 $"{PouchItemKinds.Classify(entry.prefab.transform)}). O da day, " +
                                 "hay o do khong nhan loai nay?", this);
                continue;
            }

            SpawnInto(slot, entry.prefab);
        }
    }

    /// <summary>
    /// Ô được chỉ định nếu còn trống VÀ nhận đúng loại vật này, không thì ô trống đầu tiên
    /// nhận được nó. Không ô nào nhận thì null.
    ///
    /// Phải xét loại ngay ở đây: SpawnInto gọi thẳng manager.SelectEnter, lệnh đó bỏ qua select
    /// filter, nhưng mỗi frame XRI lại gọi lại CanSelect -> filter từ chối -> vật bị đẩy ra ngay
    /// frame sau. Nhồi vào ô sai loại là thấy đồ rơi xuống đất chứ không nằm trong túi.
    /// </summary>
    private Slot ResolveSlot(int slotIndex, Transform itemTransform)
    {
        if (slotIndex >= 0 && slotIndex < slots.Count && !IsOccupied(slots[slotIndex])
            && CanStoreInSlot(slotIndex, itemTransform))
            return slots[slotIndex];

        for (int i = 0; i < slots.Count; i++)
        {
            if (!IsOccupied(slots[i]) && CanStoreInSlot(i, itemTransform))
                return slots[i];
        }

        return null;
    }

    private static bool IsOccupied(Slot slot) => slot.socket == null || slot.socket.hasSelection;

    private GameObject SpawnInto(Slot slot, GameObject prefab)
    {
        XRInteractionManager manager = ResolveManager(slot.socket);

        if (manager == null)
            return null;

        Transform anchor = slot.attach != null ? slot.attach : slot.socket.transform;
        GameObject item = Instantiate(prefab, anchor.position, anchor.rotation);
        item.SetActive(true);

        // Chặn ngay tại đây, trước cả SelectEnter: vật được sinh ra ĐÚNG trong lòng capsule người
        // chơi, nên không được để nó tồn tại dù chỉ một bước physics với collider còn sinh lực.
        IgnoreItemCollisions(item.transform);

        XRBaseInteractable interactable = item.GetComponent<XRBaseInteractable>();

        if (interactable == null)
        {
            Debug.LogWarning($"{name}: prefab {prefab.name} khong co XRBaseInteractable.", this);

            // Bỏ ghi nhận trước khi Destroy: xong rồi thì Transform đã chết, không tra cứu lại
            // được nữa và cái key hỏng sẽ nằm lại trong dictionary.
            RestoreItemCollisionsNow(item.transform);
            Destroy(item);
            return null;
        }

        if (fillMagazinesOnSpawn)
        {
            Magazine magazine = item.GetComponent<Magazine>();

            if (magazine != null)
                magazine.Refill();
        }

        // Chỉ đổi manager của vật khi field được chỉ rõ; mặc định để nguyên cái XRI đã tự đăng ký
        // trong OnEnable, cùng manager với socket và với tay người chơi.
        if (interactionManager != null)
            interactable.interactionManager = interactionManager;

        manager.SelectEnter((IXRSelectInteractor)slot.socket, (IXRSelectInteractable)interactable);

        return item;
    }

    /// <summary>
    /// Nhét một vật phẩm có sẵn trong scene vào ô trống đầu tiên (dùng cho script nhặt đồ, phần
    /// thưởng cuối màn...). Vật đang nằm trên tay người chơi hoặc trong socket khác thì không nhận:
    /// phải nhả nó ra trước.
    /// </summary>
    public bool TryStore(GameObject item)
    {
        if (item == null)
            return false;

        XRBaseInteractable interactable = item.GetComponent<XRBaseInteractable>();

        if (interactable == null || interactable.isSelected)
            return false;

        for (int i = 0; i < slots.Count; i++)
        {
            Slot slot = slots[i];

            if (IsOccupied(slot) || !CanStoreInSlot(i, item.transform))
                continue;

            XRInteractionManager manager = ResolveManager(slot.socket);

            if (manager == null)
                continue;

            manager.SelectEnter((IXRSelectInteractor)slot.socket, (IXRSelectInteractable)interactable);
            return true;
        }

        return false;
    }

    /// <summary>Vật phẩm đang nằm trong ô, null nếu ô trống.</summary>
    public GameObject GetStoredItem(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= slots.Count)
            return null;

        XRSocketInteractor socket = slots[slotIndex].socket;

        if (socket == null || !socket.hasSelection)
            return null;

        return socket.interactablesSelected[0].transform.gameObject;
    }

    /// <summary>
    /// Huỷ hết đồ đang nằm trong túi (đồ người chơi đang cầm không bị đụng tới).
    ///
    /// Gọi được ngay trong lúc Play (chuột phải lên component -> Clear Pouch) để thử xem lỗi
    /// giật lùi có phải do đồ trong túi hay không: túi rỗng mà vẫn bị đẩy lùi thì nguyên nhân
    /// nằm ở chỗ khác.
    /// </summary>
    [ContextMenu("Clear Pouch")]
    public void ClearPouch()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            GameObject item = GetStoredItem(i);

            if (item == null)
                continue;

            // Trả trước khi Destroy: xong rồi thì Transform đã chết, không tra cứu lại được nữa.
            RestoreItemCollisionsNow(item.transform);
            Destroy(item);
        }
    }

    /// <summary>Dọn túi rồi nhồi lại bộ trang bị ban đầu.</summary>
    public void ResetLoadout()
    {
        ClearPouch();
        SpawnLoadout();
    }

    /// <summary>
    /// Dựng lại toàn bộ ô theo thông số hiện tại. Gọi được ngay trong lúc Play (chuột phải lên
    /// component → Rebuild Slots) để xem kết quả chỉnh góc mà không cần thoát Play.
    /// </summary>
    [ContextMenu("Rebuild Slots")]
    public void RebuildSlots()
    {
        if (!Application.isPlaying)
            return;

        ClearPouch();

        for (int i = 0; i < slots.Count; i++)
        {
            XRSocketInteractor socket = slots[i].socket;

            if (socket == null)
                continue;

            DetachSocket(socket);
            Destroy(socket.gameObject);
        }

        slots.Clear();
        BuildSlots();
        SpawnLoadout();
    }

    private Slot FindSlot(object interactor)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (ReferenceEquals(slots[i].socket, interactor))
                return slots[i];
        }

        return null;
    }

    private void HandleSlotFilled(SelectEnterEventArgs args)
    {
        Transform itemTransform = args.interactableObject.transform;
        Slot slot = FindSlot(args.interactorObject);

        ApplyItemPose(slot, itemTransform);
        IgnoreItemCollisions(itemTransform);

        if (refillMagazineOnStore)
        {
            Magazine magazine = itemTransform.GetComponent<Magazine>();

            if (magazine != null)
                magazine.Refill();
        }

        OnItemStored?.Invoke(itemTransform.gameObject);
    }

    private void HandleSlotEmptied(SelectExitEventArgs args)
    {
        Slot slot = FindSlot(args.interactorObject);

        RestoreItemCollisions(args.interactableObject.transform);

        // Trả điểm neo về tư thế mặc định của ô, kẻo vật kế tiếp thừa hưởng tư thế của vật cũ.
        if (slot != null && slot.attach != null)
            slot.attach.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

        OnItemTaken?.Invoke(args.interactableObject.transform.gameObject);

        if (IsEmpty)
            OnPouchEmpty?.Invoke();
    }

    /// <summary>Vật phẩm tự khai tư thế của nó trong ô (xem <see cref="PouchItem"/>).</summary>
    private void ApplyItemPose(Slot slot, Transform itemTransform)
    {
        if (slot == null || slot.attach == null)
            return;

        PouchItem pouchItem = itemTransform != null ? itemTransform.GetComponentInChildren<PouchItem>() : null;

        if (pouchItem != null && pouchItem.OverrideSocketPose)
            slot.attach.SetLocalPositionAndRotation(pouchItem.PositionOffset, pouchItem.RotationOffset);
        else
            slot.attach.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    /// <summary>Vật này có được phép nằm trong túi không.</summary>
    private bool CanStore(Transform itemTransform)
    {
        if (itemTransform == null)
            return false;

        if (acceptAnyGrabbable)
            return true;

        if (itemTransform.GetComponentInChildren<PouchItem>() != null)
            return true;

        if (acceptGuns && itemTransform.GetComponent<Gun>() != null)
            return true;

        if (acceptMagazines && itemTransform.GetComponent<Magazine>() != null)
            return true;

        if (acceptGrenades && itemTransform.GetComponent<VRGrenade>() != null)
            return true;

        return false;
    }

    private void DetachSocket(XRSocketInteractor socket)
    {
        socket.selectEntered.RemoveListener(HandleSlotFilled);
        socket.selectExited.RemoveListener(HandleSlotEmptied);
        socket.hoverFilters.Remove(this);
        socket.selectFilters.Remove(this);
    }

    private void OnDisable()
    {
        // Coroutine chờ-tách-bounds chết theo lúc tắt component, nên phải trả va chạm ngay tại đây,
        // kẻo vật dính cờ ignore vĩnh viễn và đi xuyên người chơi mãi.
        RestoreAllItemCollisionsNow();
    }

    private void OnDestroy()
    {
        RestoreAllItemCollisionsNow();

        for (int i = 0; i < slots.Count; i++)
        {
            XRSocketInteractor socket = slots[i].socket;

            if (socket == null)
                continue;

            DetachSocket(socket);
        }
    }

    private void OnDrawGizmosSelected()
    {
        int slotCount = ResolvedSlotCount;

        Gizmos.color = Color.cyan;

        for (int i = 0; i < slotCount; i++)
        {
            if (layout == SlotLayout.Anchors)
            {
                if (i < slotAnchors.Count && slotAnchors[i] != null)
                    Gizmos.DrawWireCube(slotAnchors[i].position, slotTriggerSize);

                continue;
            }

            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix *
                Matrix4x4.TRS(GetSlotLocalPosition(i, slotCount), GetSlotLocalRotation(i, slotCount), Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, slotTriggerSize);
            Gizmos.matrix = previous;
        }
    }

    // --- Lọc vật phẩm cho mọi ô của túi (IXRSelectFilter / IXRHoverFilter) ---

    public bool canProcess => isActiveAndEnabled;

    public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
    {
        return CanStoreInSlot(IndexOfSocket(interactor), interactable?.transform);
    }

    public bool Process(IXRHoverInteractor interactor, IXRHoverInteractable interactable)
    {
        return CanStoreInSlot(IndexOfSocket(interactor), interactable?.transform);
    }

    /// <summary>Loại vật phẩm mà ô thứ slotIndex nhận. None = chưa khai, dùng luật chung.</summary>
    public PouchItemKind GetAllowedKinds(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < slotAllowedKinds.Count
            ? slotAllowedKinds[slotIndex]
            : PouchItemKind.None;
    }

    /// <summary>Ô thứ slotIndex có nhận vật này không. Editor gọi được cả ngoài Play.</summary>
    public bool CanStoreInSlot(int slotIndex, Transform itemTransform)
    {
        if (itemTransform == null)
            return false;

        if (!restrictSlotsByKind)
            return CanStore(itemTransform);

        PouchItemKind allowed = GetAllowedKinds(slotIndex);

        // Ô chưa khai loại thì không khoá gì cả, cứ theo luật chung: bật Restrict Slots By Kind
        // lên mà chưa điền xong danh sách thì túi không tự nhiên khoá sạch.
        if (allowed == PouchItemKind.None)
            return CanStore(itemTransform);

        return (allowed & PouchItemKinds.Classify(itemTransform)) != PouchItemKind.None;
    }

    private int IndexOfSocket(object interactor)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (ReferenceEquals(slots[i].socket, interactor))
                return i;
        }

        return -1;
    }

    // --- Va chạm của đồ trong túi ---

    /// <summary>
    /// Loại mọi collider đặc của vật khỏi TOÀN BỘ các cặp va chạm, suốt thời gian nó còn nằm
    /// trong ô. Đồ trong túi thành vật trang trí: không đẩy người chơi, không đẩy bàn tay, không
    /// cấn vào tường, không cấn lẫn nhau. Nhưng query của interactor vẫn thấy nên tay vẫn rút ra
    /// được bình thường — đúng cách ChargingHandle làm với collider tay cò.
    ///
    /// Vì sao phải loại cả mọi layer chứ không chỉ layer người chơi: chặn riêng layer người chơi
    /// vẫn còn sót lực đẩy, vì tập collider của vật không cố định — súng sinh thêm collider lúc
    /// chạy (băng đạn bên trong, socket preview), và collider đang tắt lúc nhét vào túi có thể
    /// bật lên sau. Nên hàm này còn được gọi lại định kỳ (refreshInterval); chặn hết thì không
    /// cần biết collider mới thuộc layer nào.
    ///
    /// KHÔNG dùng Physics.IgnoreCollision: cờ đó không chặn được overlap recovery của
    /// CharacterController, lại bị Unity xoá mỗi khi collider disable rồi enable lại (xem
    /// HandPoseDriver.RestoreColliders). KHÔNG dùng isTrigger: SphereInteractionCaster của
    /// Near-Far Interactor bỏ qua trigger nên tay sẽ không rút được đồ ra.
    /// </summary>
    private void IgnoreItemCollisions(Transform itemTransform)
    {
        if (!storedItemsIgnorePhysics || itemTransform == null)
            return;

        // Nhét lại vật đang chờ trả va chạm: huỷ lượt chờ, giữ nguyên những gì đã đặt.
        if (itemIgnores.TryGetValue(itemTransform, out ItemIgnore pending))
        {
            if (pending.restore != null)
            {
                StopCoroutine(pending.restore);
                pending.restore = null;
            }
        }
        else
        {
            pending = new ItemIgnore();
            itemIgnores[itemTransform] = pending;
        }

        foreach (Collider collider in itemTransform.GetComponentsInChildren<Collider>(true))
        {
            if (!IsSolid(collider) || pending.Contains(collider))
                continue;

            int original = collider.excludeLayers;

            // Đã bị loại sẵn khỏi mọi layer (tay cò của súng, ChargingHandle đặt Physics.AllLayers)
            // thì không ghi nhận: lúc trả lại ta sẽ xoá mất cờ của script khác.
            if (original == Physics.AllLayers)
                continue;

            collider.excludeLayers = Physics.AllLayers;
            pending.entries.Add(new ColliderMask { collider = collider, original = original });
        }

        if (pending.entries.Count == 0)
            itemIgnores.Remove(itemTransform);
    }

    /// <summary>
    /// Quét lại đồ đang nằm trong túi để bắt collider mới sinh ra hoặc mới được bật lên sau khi
    /// nhét vào. Không có vòng quét này thì collider súng sinh lúc chạy sẽ lọt lưới và người chơi
    /// vẫn bị đẩy một chút.
    /// </summary>
    private IEnumerator RefreshIgnoresRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(Mathf.Max(0.1f, refreshInterval));

            if (!storedItemsIgnorePhysics)
                continue;

            for (int i = 0; i < slots.Count; i++)
            {
                GameObject item = GetStoredItem(i);

                if (item != null)
                    IgnoreItemCollisions(item.transform);
            }
        }
    }

    /// <summary>
    /// Collider này có sinh va chạm vật lý không. Trigger thì không nên để nguyên — và cũng đừng
    /// bao giờ đổi vật phẩm sang trigger để chữa va chạm: interactor sẽ không thấy nó nữa.
    /// GameObject đang tắt (vỏ đạn template trong súng) thì bỏ qua: clone của nó sẽ thừa hưởng
    /// excludeLayers, thành ra vỏ đạn bay ra không đụng được gì cả.
    /// </summary>
    private static bool IsSolid(Collider collider)
    {
        if (collider == null || collider.isTrigger || collider is CharacterController)
            return false;

        return collider.gameObject.activeInHierarchy;
    }

    /// <summary>
    /// Rút vật ra khỏi túi: chờ nó rời khỏi capsule người chơi rồi mới trả lại va chạm.
    ///
    /// Không trả lại ngay được: lúc vừa rút, vật vẫn còn lún trong capsule. Trả ngay là PhysX
    /// phải giải một overlap sâu với capsule kinematic (khối lượng vô hạn) nên vật bị đá bật ra
    /// khỏi tay.
    /// </summary>
    private void RestoreItemCollisions(Transform itemTransform)
    {
        if (itemTransform == null || !itemIgnores.TryGetValue(itemTransform, out ItemIgnore entry))
            return;

        if (entry.restore != null)
            StopCoroutine(entry.restore);

        // Component đang tắt thì không chạy được coroutine chờ, phải trả ngay.
        if (!isActiveAndEnabled || playerCollider == null)
        {
            RestoreNow(entry);
            itemIgnores.Remove(itemTransform);
            return;
        }

        entry.restore = StartCoroutine(RestoreRoutine(itemTransform, entry));
    }

    private IEnumerator RestoreRoutine(Transform itemTransform, ItemIgnore entry)
    {
        bool hasDeadline = releaseRestoreTimeout > 0f;
        float deadline = Time.time + releaseRestoreTimeout;

        while (entry.entries.Count > 0 && playerCollider != null &&
               (!hasDeadline || Time.time < deadline))
        {
            Bounds playerBounds = playerCollider.bounds;

            for (int i = entry.entries.Count - 1; i >= 0; i--)
            {
                Collider collider = entry.entries[i].collider;

                // Vật bị Destroy (ClearPouch, nổ...) thì chẳng còn gì để trả.
                if (collider == null)
                {
                    entry.entries.RemoveAt(i);
                    continue;
                }

                if (collider.bounds.Intersects(playerBounds))
                    continue;

                RestoreOne(entry.entries[i]);
                entry.entries.RemoveAt(i);
            }

            yield return null;
        }

        entry.restore = null;
        RestoreNow(entry);
        itemIgnores.Remove(itemTransform);
    }

    /// <summary>Trả va chạm cho một vật ngay lập tức, không chờ nó rời capsule.</summary>
    private void RestoreItemCollisionsNow(Transform itemTransform)
    {
        if (itemTransform == null || !itemIgnores.TryGetValue(itemTransform, out ItemIgnore entry))
            return;

        if (entry.restore != null)
        {
            StopCoroutine(entry.restore);
            entry.restore = null;
        }

        RestoreNow(entry);
        itemIgnores.Remove(itemTransform);
    }

    private void RestoreAllItemCollisionsNow()
    {
        scratchItems.Clear();

        foreach (KeyValuePair<Transform, ItemIgnore> pair in itemIgnores)
        {
            if (pair.Value.restore != null)
            {
                StopCoroutine(pair.Value.restore);
                pair.Value.restore = null;
            }

            RestoreNow(pair.Value);
            scratchItems.Add(pair.Key);
        }

        for (int i = 0; i < scratchItems.Count; i++)
            itemIgnores.Remove(scratchItems[i]);

        scratchItems.Clear();
    }

    private void RestoreNow(ItemIgnore entry)
    {
        for (int i = 0; i < entry.entries.Count; i++)
            RestoreOne(entry.entries[i]);

        entry.entries.Clear();
    }

    private static void RestoreOne(ColliderMask entry)
    {
        if (entry.collider == null)
            return;

        // Trả về đúng mask cũ. Collider nào vốn đã là AllLayers thì ta không nhận từ đầu, nên
        // không có chuyện ghi đè cờ của ChargingHandle ở đây.
        entry.collider.excludeLayers = entry.original;
    }

    private struct ColliderMask
    {
        public Collider collider;
        public int original;
    }

    private class ItemIgnore
    {
        public readonly List<ColliderMask> entries = new List<ColliderMask>();
        public Coroutine restore;

        public bool Contains(Collider collider)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].collider == collider)
                    return true;
            }

            return false;
        }
    }

    private class Slot
    {
        public XRSocketInteractor socket;
        public Transform attach;
    }
}
