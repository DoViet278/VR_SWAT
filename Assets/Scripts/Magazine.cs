using UnityEngine;

public class Magazine : MonoBehaviour
{
    [SerializeField] private int capacity = 30;
    [SerializeField] private bool startFull = true;

    private int currentAmmo;

    public int Capacity => capacity;
    public int CurrentAmmo => currentAmmo;
    public bool IsEmpty => currentAmmo <= 0;

    private void Awake()
    {
        currentAmmo = startFull ? capacity : 0;
    }

    public bool TryConsumeRound()
    {
        if (currentAmmo <= 0)
            return false;

        currentAmmo--;
        return true;
    }

    public void Refill()
    {
        currentAmmo = capacity;
    }
}
