using UnityEngine;

public class Magazine : MonoBehaviour
{
    [SerializeField] private int capacity = 30;
    [SerializeField] private bool startFull = true;

    private int currentAmmo;

    public int Capacity => capacity;
    public int CurrentAmmo => currentAmmo;
    public bool IsEmpty => currentAmmo <= 0;
    public bool IsFull => currentAmmo >= capacity;

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
}
