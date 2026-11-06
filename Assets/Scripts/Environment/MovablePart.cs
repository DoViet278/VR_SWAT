using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Base cho mọi bộ phận chuyển động được bằng vật lý trong môi trường: cửa bản lề (HingedDoor),
/// cửa lùa / ngăn kéo (SlidingPart). Lo phần dùng chung: chốt, khoá, sự kiện, âm thanh, và lệnh
/// mở/đóng bằng script cho AI hoặc nhiệm vụ.
///
/// Phân biệt CHỐT (latch) và KHOÁ (lock):
/// - Chốt: cửa đang cài, nắm tay nắm là nhả chốt ra ngay. Mô phỏng bằng isKinematic = true nên
///   cửa đứng im tuyệt đối: không rung, đụng hay bắn vào không làm nó hé ra.
/// - Khoá: nắm tay nắm cũng không nhả chốt được. Phải Unlock() (có chìa) hoặc phá cửa.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public abstract class MovablePart : MonoBehaviour, IOpenable
{
    [Header("Danh tính")]
    [Tooltip("Tên hiện lên UI gợi ý. Để trống sẽ lấy tên GameObject.")]
    [SerializeField] private string displayName;

    [Header("Chốt / Khoá")]
    [Tooltip("Vào màn là đã cài chốt.")]
    [SerializeField] private bool startLatched = true;
    [Tooltip("Vào màn là đã khoá. Khoá thì nắm tay nắm cũng không nhả chốt được.")]
    [SerializeField] private bool startLocked;
    [Tooltip("Tự cài chốt lại khi về sát vị trí đóng và đã đứng yên.")]
    [SerializeField] private bool autoLatch = true;
    [Tooltip("Tốc độ dưới mức này mới tự cài chốt, để cửa đang đập không bị khựng giữa đà.")]
    [SerializeField] private float autoLatchSpeed = 0.6f;

    [Header("Ngưỡng đóng / mở")]
    [Tooltip("OpenAmount dưới mức này coi như đã đóng.")]
    [Range(0f, 0.5f)] [SerializeField] private float closedThreshold = 0.02f;
    [Tooltip("OpenAmount trên mức này coi như đã mở - dùng để bắn sự kiện và cho AI biết đi qua được.")]
    [Range(0.05f, 1f)] [SerializeField] private float openThreshold = 0.25f;

    [Header("Mở bằng script (AI, nhiệm vụ)")]
    [Tooltip("Sai số OpenAmount để coi như đã tới đích và tắt lực đẩy.")]
    [SerializeField] private float driveTolerance = 0.02f;

    [Header("Âm thanh")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Tiếng chốt cài vào.")]
    [SerializeField] private AudioClip latchClip;
    [Tooltip("Tiếng nhả chốt.")]
    [SerializeField] private AudioClip unlatchClip;
    [Tooltip("Tiếng lắc ổ khoá khi thử mở cửa đang khoá.")]
    [SerializeField] private AudioClip lockedRattleClip;

    [Header("Rung tay")]
    [SerializeField] private EnvironmentHaptics.Pulse unlatchPulse =
        new EnvironmentHaptics.Pulse { amplitude = 0.45f, duration = 0.04f, frequency = 180f };
    [SerializeField] private EnvironmentHaptics.Pulse lockedPulse =
        new EnvironmentHaptics.Pulse { amplitude = 0.2f, duration = 0.06f, frequency = 70f };

    [Header("Sự kiện")]
    public UnityEvent OnOpened;
    public UnityEvent OnClosed;
    public UnityEvent OnLatched;
    public UnityEvent OnUnlatched;
    public UnityEvent OnLocked;
    public UnityEvent OnUnlocked;
    [Tooltip("Thử mở nhưng đang khoá. Dùng để báo cho AI trong phòng biết có người ngoài cửa.")]
    public UnityEvent OnLockedRattle;

    private Rigidbody body;
    private bool latched;
    private bool locked;
    private int holdCount;
    private bool driving;
    private float driveTarget;
    private bool wasOpen;
    private bool wasClosed;

    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
    public bool IsLocked => locked;
    public bool IsLatched => latched;
    public bool CanInteract => !locked;

    /// <summary>Đang có bàn tay nào nắm giữ (DoorHandle báo lên).</summary>
    public bool IsHeld => holdCount > 0;

    /// <summary>Đang được script đẩy về một độ mở đặt trước.</summary>
    public bool IsDriving => driving;

    public Rigidbody Body => body;

    public abstract float OpenAmount { get; }

    public bool IsOpen => OpenAmount >= openThreshold;
    public bool IsClosed => OpenAmount <= closedThreshold;

    protected virtual void Awake()
    {
        body = GetComponent<Rigidbody>();

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        CacheClosedPose();
        ConfigureJoint();

        locked = startLocked;
        ApplyLatchState(startLatched, false, null);

        wasOpen = IsOpen;
        wasClosed = IsClosed;
    }

    // virtual, không phải private: nếu lớp con cũng khai báo FixedUpdate thì Unity chỉ gọi cái của
    // lớp con và vòng lặp drive ở đây sẽ bị bỏ qua không một lời cảnh báo.
    protected virtual void FixedUpdate()
    {
        if (driving)
        {
            if (Mathf.Abs(OpenAmount - driveTarget) <= driveTolerance)
                StopDrive();
            else
                DriveTowards(driveTarget);
        }

        UpdateStateEvents();
    }

    private void UpdateStateEvents()
    {
        bool isOpen = IsOpen;
        bool isClosed = IsClosed;

        if (isOpen && !wasOpen)
            OnOpened?.Invoke();

        if (isClosed && !wasClosed)
        {
            OnClosed?.Invoke();
            OnReachedClosed();
        }

        wasOpen = isOpen;
        wasClosed = isClosed;

        if (autoLatch && !latched && isClosed && !IsHeld && !driving && IsSettled)
            Latch();
    }

    private bool IsSettled =>
        body == null ||
        (body.linearVelocity.magnitude <= autoLatchSpeed &&
         body.angularVelocity.magnitude <= autoLatchSpeed * 4f);

    // ------------------------------------------------------------------ Chốt / Khoá

    /// <summary>Cài chốt: cửa đứng im hoàn toàn cho tới khi được nhả chốt.</summary>
    public void Latch()
    {
        if (latched)
            return;

        ApplyLatchState(true, true, null);
    }

    /// <summary>Nhả chốt, bỏ qua trạng thái khoá. Dùng khi có chìa hoặc khi cửa bị phá.</summary>
    public void Unlatch()
    {
        Unlatch(null);
    }

    public void Unlatch(object interactorObject)
    {
        if (!latched)
            return;

        ApplyLatchState(false, true, interactorObject);
    }

    /// <summary>
    /// Thử nhả chốt bằng tay. Đang khoá thì không nhả được, chỉ lắc ổ khoá.
    /// DoorHandle gọi hàm này khi tay nắm quay tới ngưỡng.
    /// </summary>
    public bool TryUnlatch(object interactorObject)
    {
        if (locked)
        {
            Rattle(interactorObject);
            return false;
        }

        if (latched)
            Unlatch(interactorObject);

        return true;
    }

    private void ApplyLatchState(bool value, bool playFeedback, object interactorObject)
    {
        latched = value;

        if (value)
        {
            StopDrive();
            SnapToClosed();
        }

        if (body != null)
            body.isKinematic = value;

        if (!playFeedback)
            return;

        if (value)
        {
            PlayClip(latchClip);
            OnLatched?.Invoke();
        }
        else
        {
            PlayClip(unlatchClip);
            EnvironmentHaptics.Play(interactorObject, unlatchPulse);
            OnUnlatched?.Invoke();
        }
    }

    public void Lock()
    {
        if (locked)
            return;

        locked = true;

        if (IsClosed)
            Latch();

        OnLocked?.Invoke();
    }

    public void Unlock()
    {
        if (!locked)
            return;

        locked = false;
        OnUnlocked?.Invoke();
    }

    /// <summary>Phản hồi khi thử mở cửa đang khoá: lắc ổ khoá, rung tay, bắn sự kiện.</summary>
    public void Rattle(object interactorObject)
    {
        PlayClip(lockedRattleClip);
        EnvironmentHaptics.Play(interactorObject, lockedPulse);
        OnLockedRattle?.Invoke();
    }

    // ------------------------------------------------------- IEnvironmentInteractable

    public bool TryInteract(object interactorObject)
    {
        if (locked)
        {
            Rattle(interactorObject);
            return false;
        }

        if (latched)
        {
            Unlatch(interactorObject);
            Open();
            return true;
        }

        Toggle();
        return true;
    }

    // ------------------------------------------------------- Mở / đóng bằng script

    public void Open()
    {
        OpenTo(1f);
    }

    public void Close()
    {
        OpenTo(0f);
    }

    public void Toggle()
    {
        OpenTo(IsOpen ? 0f : 1f);
    }

    /// <summary>Đẩy bộ phận này về một độ mở cho trước (0 = đóng, 1 = mở hết).</summary>
    public void OpenTo(float openAmount)
    {
        float target = Mathf.Clamp01(openAmount);

        if (locked && target > closedThreshold)
        {
            Rattle(null);
            return;
        }

        if (latched)
            Unlatch(null);

        driveTarget = target;
        driving = true;
    }

    /// <summary>Thôi đẩy, để vật lý tự lo. DoorHandle gọi khi người chơi nắm vào.</summary>
    public void StopDrive()
    {
        if (!driving)
            return;

        driving = false;
        OnDriveStopped();
    }

    // ------------------------------------------------------- Người chơi nắm giữ

    /// <summary>DoorHandle báo lên khi bắt đầu nắm: lệnh mở bằng script nhường cho tay người chơi.</summary>
    public void BeginHold()
    {
        holdCount++;
        StopDrive();
    }

    public void EndHold()
    {
        holdCount = Mathf.Max(0, holdCount - 1);
    }

    // ------------------------------------------------------- Phần từng loại tự lo

    /// <summary>Ghi lại pose lúc đóng. Gọi ở Awake, nên phải author vật này ở trạng thái ĐÓNG.</summary>
    protected abstract void CacheClosedPose();

    /// <summary>Đồng bộ joint theo thông số trên inspector (giới hạn góc, giới hạn trượt).</summary>
    protected abstract void ConfigureJoint();

    /// <summary>Đưa về đúng pose đóng. Gọi khi cài chốt để không bị hở một khe nhỏ.</summary>
    protected abstract void SnapToClosed();

    /// <summary>Đẩy về độ mở <paramref name="target"/>. Gọi mỗi FixedUpdate khi đang drive.</summary>
    protected abstract void DriveTowards(float target);

    /// <summary>Tắt motor / lực đẩy của joint.</summary>
    protected virtual void OnDriveStopped()
    {
    }

    /// <summary>Vừa về tới vị trí đóng. HingedDoor dùng để phát hiện đóng sầm.</summary>
    protected virtual void OnReachedClosed()
    {
    }

    protected void PlayClip(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }
}
