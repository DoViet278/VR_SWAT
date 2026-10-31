using UnityEngine;
using UnityEngine.Events;
using Unity.XR.CoreUtils;

[RequireComponent(typeof(XROrigin))]
public class CrouchDetector : MonoBehaviour
{
    [Header("Calibration")]
    [Tooltip("Chiều cao đứng mặc định (m) trước khi calibrate xong.")]
    [SerializeField] private float defaultStandingHeight = 1.6f;
    [Tooltip("Chiều cao đầu thấp hơn mức này thì bỏ qua khi calibrate (headset chưa đeo).")]
    [SerializeField] private float minValidHeight = 0.4f;
    [Tooltip("Tự cập nhật chiều cao đứng khi người chơi đứng cao hơn giá trị đã lưu.")]
    [SerializeField] private bool autoRaiseStandingHeight = true;

    [Header("Threshold (tỉ lệ so với chiều cao đứng)")]
    [SerializeField, Range(0.3f, 0.95f)] private float crouchRatio = 0.75f;
    [Tooltip("Phải đứng lên quá crouchRatio + giá trị này mới tính là đứng dậy (tránh nhấp nháy).")]
    [SerializeField, Range(0f, 0.2f)] private float hysteresis = 0.05f;

    [Header("Events")]
    public UnityEvent<bool> OnCrouchChanged;

    private XROrigin origin;
    private float standingHeight;

    public bool IsCrouching { get; private set; }
    public float CurrentHeight => origin != null ? origin.CameraInOriginSpaceHeight : 0f;
    public float StandingHeight => standingHeight;

    private void Awake()
    {
        origin = GetComponent<XROrigin>();
        standingHeight = defaultStandingHeight;
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
    }

    private void Update()
    {
        float height = CurrentHeight;
        if (height < minValidHeight)
            return;

        if (autoRaiseStandingHeight && !IsCrouching && height > standingHeight)
            standingHeight = height;

        float enter = standingHeight * crouchRatio;
        float exit = enter + standingHeight * hysteresis;

        bool crouching = IsCrouching ? height < exit : height < enter;
        if (crouching == IsCrouching)
            return;

        IsCrouching = crouching;
        OnCrouchChanged?.Invoke(IsCrouching);
    }

    [ContextMenu("Recalibrate Standing Height")]
    public void Recalibrate()
    {
        float height = CurrentHeight;
        if (height >= minValidHeight)
            standingHeight = height;
    }
}
