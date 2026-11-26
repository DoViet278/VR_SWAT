using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Chốt khoá của băng đạn. Gắn lên chính băng đạn (MagazineSwapController tự thêm lúc runtime
/// nếu prefab chưa có).
///
/// Đây là một select filter của XRI: XRInteractionManager hỏi filter này trước mỗi lần có ai
/// muốn "chọn" (cầm / cắm) băng đạn, nên chặn ở đây là chặn tận gốc, không phải tắt collider hay
/// tắt XRGrabInteractable (tắt interactable thì socket cũng nhả băng ra luôn).
///
/// Hai trạng thái:
/// - LockedInGun: băng đang cắm trong súng. Chỉ socket được giữ, tay người chơi bị từ chối nên
///   không rút băng ra được nữa.
/// - BlockSocketsFor: băng vừa bị nhả ra khỏi súng. Mọi socket bị từ chối trong vài phần giây để
///   băng cũ kịp rơi khỏi khe, không bị chính khe băng hút lại.
/// </summary>
[DisallowMultipleComponent]
public class MagazineLock : MonoBehaviour, IXRSelectFilter
{
    private XRBaseInteractable interactable;
    private bool registered;
    private bool lockedInGun;
    private float socketBlockedUntil = -1f;

    /// <summary>Băng đang bị khoá trong súng (tay không rút ra được).</summary>
    public bool LockedInGun => lockedInGun;

    public bool canProcess => isActiveAndEnabled;

    private void Awake()
    {
        interactable = GetComponent<XRBaseInteractable>();
    }

    private void OnEnable()
    {
        if (interactable == null || registered)
            return;

        interactable.selectFilters.Add(this);
        registered = true;
    }

    private void OnDisable()
    {
        if (interactable != null && registered)
            interactable.selectFilters.Remove(this);

        registered = false;

        // Băng bị tắt / huỷ / trả về pool thì đừng giữ lại trạng thái khoá cũ.
        lockedInGun = false;
        socketBlockedUntil = -1f;
    }

    public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactableObject)
    {
        bool isSocket = interactor is XRSocketInteractor;

        if (isSocket)
            return socketBlockedUntil < 0f || Time.time >= socketBlockedUntil;

        // Tay người chơi: chỉ cho cầm khi băng không nằm trong súng.
        return !lockedInGun;
    }

    public void SetLockedInGun(bool locked)
    {
        lockedInGun = locked;
    }

    /// <summary>Từ chối mọi socket trong <paramref name="seconds"/> giây tới.</summary>
    public void BlockSocketsFor(float seconds)
    {
        if (seconds <= 0f)
        {
            socketBlockedUntil = -1f;
            return;
        }

        socketBlockedUntil = Time.time + seconds;
    }

    /// <summary>Bỏ ngay lệnh chặn socket trước thời hạn.</summary>
    public void AllowSocketsNow()
    {
        socketBlockedUntil = -1f;
    }
}
