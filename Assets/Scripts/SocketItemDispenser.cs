using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Hộp/thùng chứa vật phẩm rút ra được: tự dựng socket lúc runtime, nhồi vật phẩm vào, và hồi sinh
/// vật phẩm sau khi người chơi lấy đi. Dùng cho thùng đạn shotgun.
/// Cùng cơ chế với MagazinePouch nhưng không gắn chặt với type Magazine.
/// </summary>
public class SocketItemDispenser : MonoBehaviour
{
    [Header("Item")]
    [SerializeField] private GameObject itemTemplate;

    [Header("Slots")]
    [Tooltip("Vị trí từng ô. Để trống sẽ tự xếp lưới theo thông số bên dưới.")]
    [SerializeField] private List<Transform> slotAnchors = new List<Transform>();
    [SerializeField] private Vector2Int grid = new Vector2Int(3, 2);
    [SerializeField] private Vector3 slotSpacing = new Vector3(0.035f, 0f, 0.039f);
    [SerializeField] private Vector3 slotLocalEuler = Vector3.zero;
    [SerializeField] private Vector3 slotTriggerSize = new Vector3(0.03f, 0.03f, 0.03f);
    [SerializeField] private InteractionLayerMask itemInteractionLayers = 1 << 1;

    [Header("Timing")]
    [SerializeField] private float respawnDelay = 1f;
    [SerializeField] private float initialSpawnDelay = 0.25f;

    [Header("Cleanup")]
    [SerializeField] private float droppedItemLifetime = 30f;

    [Header("Interaction")]
    [SerializeField] private XRInteractionManager interactionManager;

    [Header("Events")]
    public UnityEvent<GameObject> OnItemSpawned;
    public UnityEvent OnItemTaken;

    private readonly List<Slot> slots = new List<Slot>();

    public int ItemCount
    {
        get
        {
            int count = 0;

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].socket.hasSelection)
                    count++;
            }

            return count;
        }
    }

    private void Start()
    {
        if (interactionManager == null)
            interactionManager = FindAnyObjectByType<XRInteractionManager>();

        BuildSlots();
    }

    private void BuildSlots()
    {
        int slotCount = slotAnchors.Count > 0 ? slotAnchors.Count : Mathf.Max(1, grid.x) * Mathf.Max(1, grid.y);

        for (int i = 0; i < slotCount; i++)
        {
            GameObject slotObject = new GameObject($"ItemSlot {i}");
            slotObject.SetActive(false);
            slotObject.transform.SetParent(transform, false);

            if (slotAnchors.Count > 0 && slotAnchors[i] != null)
            {
                slotObject.transform.SetPositionAndRotation(slotAnchors[i].position, slotAnchors[i].rotation);
            }
            else
            {
                slotObject.transform.localPosition = GetGridLocalPosition(i);
                slotObject.transform.localRotation = Quaternion.Euler(slotLocalEuler);
            }

            BoxCollider trigger = slotObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = slotTriggerSize;

            XRSocketInteractor socket = slotObject.AddComponent<XRSocketInteractor>();
            socket.interactionManager = interactionManager;
            socket.interactionLayers = itemInteractionLayers;
            socket.showInteractableHoverMeshes = false;
            socket.recycleDelayTime = 0f;
            socket.socketScaleMode = SocketScaleMode.None;
            socket.selectExited.AddListener(HandleSlotEmptied);

            slotObject.SetActive(true);

            slots.Add(new Slot
            {
                socket = socket,
                respawnTimer = initialSpawnDelay + i * 0.05f
            });
        }
    }

    private Vector3 GetGridLocalPosition(int index)
    {
        int columns = Mathf.Max(1, grid.x);
        int column = index % columns;
        int row = index / columns;

        return new Vector3(column * slotSpacing.x, row * slotSpacing.y, row * slotSpacing.z);
    }

    private void Update()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            Slot slot = slots[i];

            if (slot.socket.hasSelection)
            {
                slot.respawnTimer = respawnDelay;
                continue;
            }

            slot.respawnTimer -= Time.deltaTime;

            if (slot.respawnTimer <= 0f)
                SpawnItemInto(slot);
        }
    }

    private void SpawnItemInto(Slot slot)
    {
        slot.respawnTimer = respawnDelay;

        if (itemTemplate == null || interactionManager == null)
            return;

        Transform slotTransform = slot.socket.transform;
        GameObject item = Instantiate(itemTemplate, slotTransform.position, slotTransform.rotation);
        item.SetActive(true);

        XRBaseInteractable interactable = item.GetComponent<XRBaseInteractable>();

        if (interactable == null)
            return;

        if (droppedItemLifetime > 0f)
            item.AddComponent<SpawnedMagazineCleanup>().Configure(droppedItemLifetime);

        interactable.interactionManager = interactionManager;
        interactionManager.SelectEnter((IXRSelectInteractor)slot.socket, (IXRSelectInteractable)interactable);

        OnItemSpawned?.Invoke(item);
    }

    private void HandleSlotEmptied(SelectExitEventArgs args)
    {
        OnItemTaken?.Invoke();
    }

    private void OnDestroy()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            XRSocketInteractor socket = slots[i].socket;

            if (socket == null)
                continue;

            socket.selectExited.RemoveListener(HandleSlotEmptied);
        }
    }

    private class Slot
    {
        public XRSocketInteractor socket;
        public float respawnTimer;
    }
}
