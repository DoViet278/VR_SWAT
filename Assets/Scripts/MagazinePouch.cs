using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class MagazinePouch : MonoBehaviour
{
    [Header("Magazine")]
    [SerializeField] private Magazine magazineTemplate;
    [SerializeField] private bool refillOnStore = true;

    [Header("Capacity")]
    [SerializeField] private int maxMagazines = 1;
    [SerializeField] private float respawnDelay = 1f;
    [SerializeField] private float initialSpawnDelay = 0.25f;

    [Header("Slot Layout")]
    [SerializeField] private float slotRingRadius = 0.07f;
    [SerializeField] private Vector3 slotLocalEuler = Vector3.zero;
    [SerializeField] private Vector3 slotTriggerSize = new Vector3(0.1f, 0.1f, 0.1f);
    [SerializeField] private InteractionLayerMask magazineInteractionLayers = 1 << 1;

    [Header("Cleanup")]
    [SerializeField] private float droppedMagazineLifetime = 30f;

    [Header("Interaction")]
    [SerializeField] private XRInteractionManager interactionManager;

    [Header("Events")]
    public UnityEvent OnMagazineTaken;
    public UnityEvent OnMagazineSpawned;

    private readonly List<Slot> slots = new List<Slot>();

    public int MagazineCount
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
        for (int i = 0; i < maxMagazines; i++)
        {
            GameObject slotObject = new GameObject($"MagazineSlot {i}");
            slotObject.SetActive(false);
            slotObject.transform.SetParent(transform, false);
            slotObject.transform.localPosition = GetSlotLocalPosition(i);
            slotObject.transform.localRotation = Quaternion.Euler(slotLocalEuler);

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

    private Vector3 GetSlotLocalPosition(int index)
    {
        if (maxMagazines <= 1)
            return Vector3.zero;

        float angle = 360f / maxMagazines * index;
        return Quaternion.Euler(0f, angle, 0f) * (Vector3.forward * slotRingRadius);
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
                SpawnMagazineInto(slot);
        }
    }

    private void SpawnMagazineInto(Slot slot)
    {
        slot.respawnTimer = respawnDelay;

        if (magazineTemplate == null || interactionManager == null)
            return;

        Transform slotTransform = slot.socket.transform;
        Magazine magazine = Instantiate(magazineTemplate, slotTransform.position, slotTransform.rotation);
        magazine.gameObject.SetActive(true);
        magazine.Refill();

        magazine.gameObject.AddComponent<SpawnedMagazineCleanup>().Configure(droppedMagazineLifetime);

        XRBaseInteractable interactable = magazine.GetComponent<XRBaseInteractable>();

        if (interactable == null)
            return;

        interactable.interactionManager = interactionManager;
        interactionManager.SelectEnter((IXRSelectInteractor)slot.socket, (IXRSelectInteractable)interactable);

        OnMagazineSpawned?.Invoke();
    }

    private void HandleSlotFilled(SelectEnterEventArgs args)
    {
        if (!refillOnStore)
            return;

        Magazine magazine = args.interactableObject.transform.GetComponent<Magazine>();
        magazine?.Refill();
    }

    private void HandleSlotEmptied(SelectExitEventArgs args)
    {
        OnMagazineTaken?.Invoke();
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

    private class Slot
    {
        public XRSocketInteractor socket;
        public float respawnTimer;
    }
}
