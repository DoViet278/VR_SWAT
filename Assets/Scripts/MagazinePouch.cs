using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Túi đạn đeo quanh hông: tự dựng các ô socket lúc runtime, nạp sẵn một số băng đạn hữu hạn,
/// và cho phép người chơi rút ra / cắm trả lại. Khi hết băng thì túi rỗng cho tới lúc nhặt được băng mới.
/// </summary>
public class MagazinePouch : MonoBehaviour
{
    public enum SlotLayout
    {
        Ring,
        Arc,
        Row,
        Anchors
    }

    [Header("Magazine")]
    [SerializeField] private Magazine magazineTemplate;
    [SerializeField] private bool refillOnStore = true;

    [Header("Capacity")]
    [Tooltip("Số ô túi dựng quanh hông.")]
    [SerializeField] private int maxMagazines = 5;
    [Tooltip("Tổng số băng đạn cấp sẵn lúc vào game. Phần vượt quá số ô sẽ nằm trong kho dự trữ.")]
    [SerializeField] private int startingMagazines = 5;
    [Tooltip("Bật để túi không bao giờ hết đạn (chế độ tập bắn).")]
    [SerializeField] private bool infiniteSupply = false;
    [Tooltip("Băng rơi ra ngoài bị dọn dẹp sẽ được trả về kho dự trữ, tránh kẹt game vì hết đạn.")]
    [SerializeField] private bool recoverLostMagazines = true;
    [SerializeField] private float respawnDelay = 0.5f;
    [SerializeField] private float initialSpawnDelay = 0.25f;

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
    [Tooltip("Bật: mỗi ô xoay hướng ra ngoài theo góc của nó trên cung. Tắt: mọi ô dùng chung một hướng (slotLocalEuler), chỉ khác vị trí.")]
    [SerializeField] private bool alignSlotsToArc = false;
    [Tooltip("Hướng chung của băng đạn trong ô.")]
    [SerializeField] private Vector3 slotLocalEuler = Vector3.zero;
    [Tooltip("Ghi đè góc xoay riêng cho từng ô, theo đúng thứ tự ô. Ô nào không có phần tử tương ứng thì dùng slotLocalEuler.")]
    [SerializeField] private List<Vector3> slotEulerOverrides = new List<Vector3>();
    [Tooltip("Ghi đè dịch vị trí riêng cho từng ô, cộng thêm vào vị trí tính theo layout.")]
    [SerializeField] private List<Vector3> slotPositionOffsets = new List<Vector3>();
    [SerializeField] private Vector3 slotTriggerSize = new Vector3(0.1f, 0.1f, 0.1f);
    [SerializeField] private InteractionLayerMask magazineInteractionLayers = 1 << 1;

    [Header("Cleanup")]
    [SerializeField] private float droppedMagazineLifetime = 30f;

    [Header("Interaction")]
    [SerializeField] private XRInteractionManager interactionManager;

    [Header("Events")]
    public UnityEvent OnMagazineTaken;
    public UnityEvent OnMagazineSpawned;
    public UnityEvent OnPouchEmpty;

    private readonly List<Slot> slots = new List<Slot>();
    private readonly List<Magazine> spawnedMagazines = new List<Magazine>();
    private int reserveMagazines;

    /// <summary>Số băng đang nằm trong các ô túi.</summary>
    public int MagazineCount
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

    /// <summary>Số băng còn trong kho dự trữ, chưa hiện ra ô nào.</summary>
    public int ReserveCount => reserveMagazines;

    /// <summary>Tổng số băng người chơi còn lại trên người (trong ô + dự trữ).</summary>
    public int TotalMagazinesLeft => infiniteSupply ? int.MaxValue : MagazineCount + reserveMagazines;

    public int SlotCount => slots.Count;

    private void Start()
    {
        if (interactionManager == null)
            interactionManager = FindAnyObjectByType<XRInteractionManager>();

        reserveMagazines = Mathf.Max(0, startingMagazines);
        BuildSlots();
    }

    private void BuildSlots()
    {
        int slotCount = layout == SlotLayout.Anchors && slotAnchors.Count > 0
            ? slotAnchors.Count
            : Mathf.Max(1, maxMagazines);

        for (int i = 0; i < slotCount; i++)
        {
            GameObject slotObject = new GameObject($"MagazineSlot {i}");
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

            XRSocketInteractor socket = slotObject.AddComponent<XRSocketInteractor>();
            socket.interactionManager = interactionManager;
            socket.interactionLayers = magazineInteractionLayers;
            socket.showInteractableHoverMeshes = false;
            socket.recycleDelayTime = 0f;
            socket.socketScaleMode = SocketScaleMode.None;
            socket.selectEntered.AddListener(HandleSlotFilled);
            socket.selectExited.AddListener(HandleSlotEmptied);

            slotObject.SetActive(true);

            slots.Add(new Slot
            {
                socket = socket,
                respawnTimer = initialSpawnDelay + i * 0.05f
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

    private void Update()
    {
        if (recoverLostMagazines && !infiniteSupply)
            RecoverDestroyedMagazines();

        for (int i = 0; i < slots.Count; i++)
        {
            Slot slot = slots[i];

            if (slot.socket == null)
                continue;

            if (slot.socket.hasSelection)
            {
                slot.respawnTimer = respawnDelay;
                continue;
            }

            if (!infiniteSupply && reserveMagazines <= 0)
                continue;

            slot.respawnTimer -= Time.deltaTime;

            if (slot.respawnTimer <= 0f)
                SpawnMagazineInto(slot);
        }
    }

    private void RecoverDestroyedMagazines()
    {
        for (int i = spawnedMagazines.Count - 1; i >= 0; i--)
        {
            if (spawnedMagazines[i] != null)
                continue;

            spawnedMagazines.RemoveAt(i);
            reserveMagazines++;
        }
    }

    private void SpawnMagazineInto(Slot slot)
    {
        slot.respawnTimer = respawnDelay;

        if (magazineTemplate == null || interactionManager == null)
            return;

        if (!infiniteSupply && reserveMagazines <= 0)
            return;

        Transform slotTransform = slot.socket.transform;
        Magazine magazine = Instantiate(magazineTemplate, slotTransform.position, slotTransform.rotation);
        magazine.gameObject.SetActive(true);
        magazine.Refill();

        if (droppedMagazineLifetime > 0f)
            magazine.gameObject.AddComponent<SpawnedMagazineCleanup>().Configure(droppedMagazineLifetime);

        XRBaseInteractable interactable = magazine.GetComponent<XRBaseInteractable>();

        if (interactable == null)
        {
            Destroy(magazine.gameObject);
            return;
        }

        if (!infiniteSupply)
        {
            reserveMagazines--;
            spawnedMagazines.Add(magazine);
        }

        interactable.interactionManager = interactionManager;
        interactionManager.SelectEnter((IXRSelectInteractor)slot.socket, (IXRSelectInteractable)interactable);

        OnMagazineSpawned?.Invoke();
    }

    private void HandleSlotFilled(SelectEnterEventArgs args)
    {
        Magazine magazine = args.interactableObject.transform.GetComponent<Magazine>();

        if (magazine == null)
            return;

        // Băng cắm trả lại túi được coi là băng của người chơi, kể cả khi nhặt ở đâu đó.
        if (!infiniteSupply && !spawnedMagazines.Contains(magazine))
            spawnedMagazines.Add(magazine);

        if (refillOnStore)
            magazine.Refill();
    }

    private void HandleSlotEmptied(SelectExitEventArgs args)
    {
        OnMagazineTaken?.Invoke();

        if (!infiniteSupply && TotalMagazinesLeft <= 0)
            OnPouchEmpty?.Invoke();
    }

    /// <summary>Nạp thêm băng vào kho dự trữ (nhặt được hộp đạn, hồi sinh, v.v.).</summary>
    public void AddMagazines(int amount)
    {
        if (amount <= 0)
            return;

        reserveMagazines += amount;
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

        for (int i = 0; i < slots.Count; i++)
        {
            XRSocketInteractor socket = slots[i].socket;

            if (socket == null)
                continue;

            socket.selectEntered.RemoveListener(HandleSlotFilled);
            socket.selectExited.RemoveListener(HandleSlotEmptied);
            Destroy(socket.gameObject);
        }

        slots.Clear();
        ResetToStartingLoad();
        BuildSlots();
    }

    /// <summary>Đưa túi về trạng thái đầu màn: đầy lại số băng cấp sẵn.</summary>
    public void ResetToStartingLoad()
    {
        for (int i = spawnedMagazines.Count - 1; i >= 0; i--)
        {
            if (spawnedMagazines[i] != null)
                Destroy(spawnedMagazines[i].gameObject);
        }

        spawnedMagazines.Clear();
        reserveMagazines = Mathf.Max(0, startingMagazines);

        for (int i = 0; i < slots.Count; i++)
            slots[i].respawnTimer = initialSpawnDelay + i * 0.05f;
    }

    private void OnDestroy()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            XRSocketInteractor socket = slots[i].socket;

            if (socket == null)
                continue;

            socket.selectEntered.RemoveListener(HandleSlotFilled);
            socket.selectExited.RemoveListener(HandleSlotEmptied);
        }
    }

    private void OnDrawGizmosSelected()
    {
        int slotCount = layout == SlotLayout.Anchors && slotAnchors.Count > 0
            ? slotAnchors.Count
            : Mathf.Max(1, maxMagazines);

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

    private class Slot
    {
        public XRSocketInteractor socket;
        public float respawnTimer;
    }
}
