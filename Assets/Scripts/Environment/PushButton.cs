using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Nút bấm / công tắc trên tường: mở đèn, gọi thang máy, mở cửa điện, báo động.
/// Bấm được bằng Poke Interactor (chạm ngón tay) hoặc bằng grip khi tay chạm vào.
///
/// <see cref="isToggle"/> = false: nút nhấn nhả, giữ là bật, thả là tắt.
/// <see cref="isToggle"/> = true: công tắc, mỗi lần bấm đổi trạng thái.
/// </summary>
public class PushButton : XRBaseInteractable, IEnvironmentInteractable
{
    [System.Serializable]
    public class BoolEvent : UnityEvent<bool>
    {
    }

    [Header("Danh tính")]
    [Tooltip("Tên hiện lên UI gợi ý. Để trống sẽ lấy tên GameObject.")]
    [SerializeField] private string displayName;
    [Tooltip("Vào màn là đã khoá (mất điện, chưa có thẻ từ).")]
    [SerializeField] private bool startLocked;

    [Header("Kiểu nút")]
    [Tooltip("Bật: công tắc, mỗi lần bấm đổi trạng thái. Tắt: nút nhấn nhả.")]
    [SerializeField] private bool isToggle;
    [Tooltip("Trạng thái ban đầu của công tắc.")]
    [SerializeField] private bool startOn;

    [Header("Phần lún xuống")]
    [Tooltip("Object lún xuống khi bấm. Để trống = không có animation.")]
    [SerializeField] private Transform plunger;
    [Tooltip("Độ lún khi bấm, local space của plunger.")]
    [SerializeField] private Vector3 pressOffset = new Vector3(0f, -0.006f, 0f);
    [SerializeField] private float pressTime = 0.04f;
    [SerializeField] private float releaseTime = 0.09f;

    [Header("Âm thanh")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip pressClip;
    [SerializeField] private AudioClip releaseClip;
    [Tooltip("Tiếng báo nút đang khoá / không ăn.")]
    [SerializeField] private AudioClip lockedClip;

    [Header("Rung tay")]
    [SerializeField] private EnvironmentHaptics.Pulse pressPulse =
        new EnvironmentHaptics.Pulse { amplitude = 0.35f, duration = 0.03f, frequency = 220f };
    [SerializeField] private EnvironmentHaptics.Pulse lockedPulse =
        new EnvironmentHaptics.Pulse { amplitude = 0.15f, duration = 0.05f, frequency = 70f };

    [Header("Sự kiện")]
    public UnityEvent OnPressed;
    public UnityEvent OnReleased;
    [Tooltip("Chỉ dùng khi isToggle. Tham số là trạng thái mới.")]
    public BoolEvent OnToggled;
    [Tooltip("Bấm nhưng nút đang khoá.")]
    public UnityEvent OnLockedPress;

    private Vector3 plungerRestPosition;
    private float pressAmount;
    private bool held;
    private bool locked;
    private bool isOn;

    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
    public bool IsLocked => locked;
    public bool CanInteract => !locked;

    /// <summary>Trạng thái công tắc. Nút nhấn nhả thì bằng true trong lúc đang bị giữ.</summary>
    public bool IsOn => isToggle ? isOn : held;

    protected override void Awake()
    {
        base.Awake();

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (plunger != null)
            plungerRestPosition = plunger.localPosition;

        locked = startLocked;
        isOn = isToggle && startOn;
    }

    protected override void OnSelectEntered(SelectEnterEventArgs args)
    {
        base.OnSelectEntered(args);
        TryInteract(args.interactorObject);
    }

    protected override void OnSelectExited(SelectExitEventArgs args)
    {
        base.OnSelectExited(args);

        if (!held)
            return;

        held = false;

        if (!isToggle)
        {
            PlayClip(releaseClip);
            OnReleased?.Invoke();
        }
    }

    public bool TryInteract(object interactorObject)
    {
        if (locked)
        {
            PlayClip(lockedClip);
            EnvironmentHaptics.Play(interactorObject, lockedPulse);
            OnLockedPress?.Invoke();
            return false;
        }

        held = true;

        PlayClip(pressClip);
        EnvironmentHaptics.Play(interactorObject, pressPulse);
        OnPressed?.Invoke();

        if (isToggle)
        {
            isOn = !isOn;
            OnToggled?.Invoke(isOn);
        }

        return true;
    }

    public void Lock()
    {
        locked = true;
    }

    public void Unlock()
    {
        locked = false;
    }

    /// <summary>Đặt trạng thái công tắc bằng script, không bắn OnPressed.</summary>
    public void SetOn(bool value)
    {
        if (!isToggle || isOn == value)
            return;

        isOn = value;
        OnToggled?.Invoke(isOn);
    }

    private void Update()
    {
        // Công tắc giữ nguyên độ lún theo trạng thái; nút nhấn nhả lún khi đang bị giữ.
        float target = isToggle ? (isOn ? 1f : 0f) : (held ? 1f : 0f);
        float duration = target > pressAmount ? pressTime : releaseTime;

        pressAmount = Mathf.MoveTowards(pressAmount, target, Time.deltaTime / Mathf.Max(0.01f, duration));

        ApplyPlungerPosition();
    }

    private void ApplyPlungerPosition()
    {
        if (plunger == null)
            return;

        Vector3 target = plungerRestPosition + pressOffset * pressAmount;

        // Chỉ ghi khi thực sự đổi, tránh sync transform sang physics mỗi frame vô ích.
        if ((plunger.localPosition - target).sqrMagnitude <= 1e-12f)
            return;

        plunger.localPosition = target;
    }

    private void PlayClip(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }
}
