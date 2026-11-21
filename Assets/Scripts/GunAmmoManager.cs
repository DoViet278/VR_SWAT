using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class GunAmmoManager : MonoBehaviour
{
    public enum ChamberState
    {
        Empty,
        Live,
        Spent
    }

    [Header("Magazine Socket")]
    [SerializeField] private XRSocketInteractor magazineSocket;

    [Header("Built-in Magazine")]
    [Tooltip("Ống đạn cố định trong thân súng (shotgun). Gán cái này thì không cần Magazine Socket.")]
    [SerializeField] private Magazine builtInMagazine;

    [Header("Events")]
    public UnityEvent OnMagazineInserted;
    public UnityEvent OnMagazineRemoved;
    public UnityEvent OnAmmoEmpty;
    public UnityEvent OnAmmoChanged;
    [Tooltip("Nhồi được 1 viên vào ống đạn.")]
    public UnityEvent OnRoundLoaded;
    [Tooltip("Ống đạn đã đầy, không nhồi thêm được.")]
    public UnityEvent OnMagazineFull;
    [Tooltip("Vỏ đạn đã bắn vừa bị nhả ra khỏi buồng đạn.")]
    public UnityEvent OnSpentCaseEjected;
    [Tooltip("Viên đạn còn nguyên bị kéo pump nhả ra ngoài.")]
    public UnityEvent OnLiveRoundEjected;

    [Header("Charging")]
    [SerializeField] private bool requireCharge = true;
    [Tooltip("Bật: bắn xong tự lên viên kế tiếp (rifle, pistol). Tắt: phải kéo pump bằng tay (shotgun).")]
    [SerializeField] private bool autoCycleOnFire = true;
    [Tooltip("Vào scene là đã có sẵn 1 viên nằm trong buồng đạn, KHÔNG rút từ ống đạn / băng. " +
             "Dùng cho cấu hình 7+1 của shotgun: 7 viên trong ống tiếp đạn + 1 viên đã lên nòng " +
             "= 8 viên, bắn được ngay mà không phải kéo pump trước.")]
    [SerializeField] private bool startChambered;

    private Magazine currentMagazine;
    private ChamberState chamber;

    public ChamberState ChamberStatus => chamber;
    public bool IsChambered => chamber == ChamberState.Live;
    public bool HasSpentCase => chamber == ChamberState.Spent;

    public bool HasMagazine => currentMagazine != null;
    public bool HasAmmo => currentMagazine != null && !currentMagazine.IsEmpty;
    public bool IsMagazineFull => currentMagazine != null && currentMagazine.IsFull;
    public int CurrentAmmo => currentMagazine != null ? currentMagazine.CurrentAmmo : 0;
    public int Capacity => currentMagazine != null ? currentMagazine.Capacity : 0;
    public int TotalAmmo => CurrentAmmo + (IsChambered ? 1 : 0);

    private void Awake()
    {
        // Ống đạn cố định: không có hộp tiếp đạn rời để tháo ra cắm vào.
        if (builtInMagazine != null)
            currentMagazine = builtInMagazine;

        if (magazineSocket == null && builtInMagazine == null)
            magazineSocket = GetComponentInChildren<XRSocketInteractor>();
    }

    // Magazine.Awake và Awake ở đây không có thứ tự xác định, nên số đạn chỉ đáng tin từ Start.
    private void Start()
    {
        // Gán thẳng chứ không gọi Chamber(): Chamber() rút 1 viên ra khỏi ống đạn, nên cấu hình
        // 7+1 sẽ bị thành 6+1 = 7 viên thay vì 8.
        if (startChambered && chamber == ChamberState.Empty)
            chamber = ChamberState.Live;

        OnAmmoChanged?.Invoke();
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

    // Called by the charging handle: feeds a round from the magazine into the chamber.
    public void Chamber()
    {
        if (chamber != ChamberState.Empty || currentMagazine == null || !currentMagazine.TryConsumeRound())
            return;

        chamber = ChamberState.Live;
        OnAmmoChanged?.Invoke();
    }

    /// <summary>Nhồi 1 viên vào ống đạn (cửa nạp shotgun gọi). Trả về false khi đã đầy.</summary>
    public bool TryAddRoundToMagazine()
    {
        if (currentMagazine == null || !currentMagazine.TryAddRound())
        {
            OnMagazineFull?.Invoke();
            return false;
        }

        OnRoundLoaded?.Invoke();
        OnAmmoChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Nhịp kéo pump về sau: moi sạch buồng đạn và cho biết thứ vừa bị nhả ra.
    /// </summary>
    public ChamberState ExtractChamber()
    {
        ChamberState ejected = chamber;
        chamber = ChamberState.Empty;

        if (ejected == ChamberState.Spent)
            OnSpentCaseEjected?.Invoke();
        else if (ejected == ChamberState.Live)
            OnLiveRoundEjected?.Invoke();

        if (ejected != ChamberState.Empty)
            OnAmmoChanged?.Invoke();

        return ejected;
    }

    /// <summary>Một chu kỳ hoàn chỉnh: nhả thứ đang nằm trong buồng đạn rồi lên viên kế tiếp.</summary>
    public ChamberState CycleAction()
    {
        ChamberState ejected = ExtractChamber();
        Chamber();
        return ejected;
    }

    public bool TryFireRound()
    {
        if (requireCharge)
        {
            if (chamber != ChamberState.Live)
            {
                OnAmmoEmpty?.Invoke();
                return false;
            }

            if (autoCycleOnFire)
            {
                // Firing cycles the action: the next round (if any) is chambered automatically.
                chamber = ChamberState.Empty;
                Chamber();
            }
            else
            {
                // Pump action: vỏ đạn nằm lại trong buồng đạn tới khi người chơi kéo pump.
                chamber = ChamberState.Spent;
            }

            OnAmmoChanged?.Invoke();
            return true;
        }

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
