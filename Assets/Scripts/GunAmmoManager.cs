using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class GunAmmoManager : MonoBehaviour
{
    [Header("Magazine Socket")]
    [SerializeField] private XRSocketInteractor magazineSocket;

    [Header("Events")]
    public UnityEvent OnMagazineInserted;
    public UnityEvent OnMagazineRemoved;
    public UnityEvent OnAmmoEmpty;
    public UnityEvent OnAmmoChanged;

    private Magazine currentMagazine;

    public bool HasMagazine => currentMagazine != null;
    public bool HasAmmo => currentMagazine != null && !currentMagazine.IsEmpty;
    public int CurrentAmmo => currentMagazine != null ? currentMagazine.CurrentAmmo : 0;
    public int Capacity => currentMagazine != null ? currentMagazine.Capacity : 0;

    private void Awake()
    {
        if (magazineSocket == null)
            magazineSocket = GetComponentInChildren<XRSocketInteractor>();
    }

    private void OnEnable()
    {
        if (magazineSocket == null)
            return;

        magazineSocket.selectEntered.AddListener(HandleMagazineInserted);
        magazineSocket.selectExited.AddListener(HandleMagazineRemoved);
    }

    private void OnDisable()
    {
        if (magazineSocket == null)
            return;

        magazineSocket.selectEntered.RemoveListener(HandleMagazineInserted);
        magazineSocket.selectExited.RemoveListener(HandleMagazineRemoved);
    }

    private void HandleMagazineInserted(SelectEnterEventArgs args)
    {
        currentMagazine = args.interactableObject.transform.GetComponent<Magazine>();

        if (currentMagazine != null)
        {
            OnMagazineInserted?.Invoke();
            OnAmmoChanged?.Invoke();
        }
    }

    private void HandleMagazineRemoved(SelectExitEventArgs args)
    {
        currentMagazine = null;
        OnMagazineRemoved?.Invoke();
        OnAmmoChanged?.Invoke();
    }

    public bool TryFireRound()
    {
        if (currentMagazine == null || currentMagazine.IsEmpty)
        {
            OnAmmoEmpty?.Invoke();
            return false;
        }

        bool fired = currentMagazine.TryConsumeRound();

        if (fired)
            OnAmmoChanged?.Invoke();

        return fired;
    }
}
