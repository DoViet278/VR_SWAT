using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class SpawnedMagazineCleanup : MonoBehaviour
{
    [SerializeField] private float lifetime = 30f;

    private XRBaseInteractable interactable;
    private float idleTime;

    public void Configure(float newLifetime)
    {
        lifetime = newLifetime;
        idleTime = 0f;
    }

    private void Awake()
    {
        interactable = GetComponent<XRBaseInteractable>();
    }

    private void Update()
    {
        if (lifetime <= 0f)
            return;

        if (interactable != null && interactable.isSelected)
        {
            idleTime = 0f;
            return;
        }

        idleTime += Time.deltaTime;

        if (idleTime >= lifetime)
            Destroy(gameObject);
    }
}
