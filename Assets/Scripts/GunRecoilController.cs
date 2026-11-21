using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

[RequireComponent(typeof(XRGrabInteractable))]
public class GunRecoilController : XRBaseGrabTransformer
{
    [Header("Recoil (theo bảng thông số vũ khí)")]
    [Tooltip("Lực giật tổng của khẩu súng, 1.0 = mạnh nhất trong bảng. Điều khiển độ lùi về sau " +
             "của thân súng. KHÔNG nhân vào góc giật: các góc bên dưới đã là giá trị thực tế rồi.")]
    [SerializeField] private float recoilStrength = 0.5f;
    [Tooltip("Góc hất nòng lên mỗi phát (độ).")]
    [SerializeField] private float verticalRecoil = 2f;
    [Tooltip("Góc lệch ngang ngẫu nhiên mỗi phát (± độ).")]
    [SerializeField] private float horizontalRecoil = 0.5f;
    [Tooltip("Thời gian kéo súng về vị trí cũ sau khi giật (giây).")]
    [SerializeField] private float recoveryTime = 0.3f;
    [Tooltip("Thời gian cú giật đi từ lúc bắn đến đỉnh (giây). Ngắn = giật đánh, dài = giật đẩy.")]
    [SerializeField] private float recoilDuration = 0.15f;
    [Tooltip("Hệ số nhân tổng lên mọi thành phần recoil. 1.0 = dùng y nguyên bảng thông số.")]
    [SerializeField] private float recoilIntensity = 1f;

    [Header("Recoil (không có trong bảng)")]
    [Tooltip("Độ lùi về sau của thân súng (mét) khi recoilStrength = 1. Bảng thông số chỉ có góc " +
             "giật chứ không có độ lùi, nên số này là phần tự điều chỉnh.")]
    [SerializeField] private float kickBackDistance = 0.03f;
    [Tooltip("Trần góc ngóc nòng khi bắn liên thanh (độ). Chặn việc nòng leo vô hạn lúc giữ cò. " +
             "0 = không giới hạn.")]
    [SerializeField] private float maxVerticalClimb = 12f;

    [Header("Camera Kick (MẶC ĐỊNH TẮT — đọc kỹ trước khi bật)")]
    [Tooltip("CẢNH BÁO VR: xoay góc nhìn của người chơi bằng code là nguyên nhân gây say (motion " +
             "sickness) hàng đầu trong VR, vì ảnh mắt dịch chuyển mà tai trong không thấy gì. " +
             "Trong game bắn màn hình thì bình thường, trong VR thì nên để TẮT.")]
    [SerializeField] private bool applyCameraKick;
    [Tooltip("Transform trung gian giữa XR Origin và Camera để xoay. KHÔNG được gán thẳng Camera: " +
             "pose của Camera bị hệ thống tracking ghi đè mỗi frame nên xoay vào đó không ăn thua.")]
    [SerializeField] private Transform cameraRecoilPivot;
    [Tooltip("Góc ngóc tầm nhìn lên mỗi phát (độ).")]
    [SerializeField] private float cameraPitch = 0.4f;
    [Tooltip("Góc lệch tầm nhìn ngang mỗi phát (± độ).")]
    [SerializeField] private float cameraYaw = 0.15f;

    [Header("Spread (Heat)")]
    [SerializeField] private float heatPerShot = 0.15f;
    [SerializeField] private float heatDecayPerSecond = 0.6f;
    [SerializeField] private float minSpreadDegrees = 0.15f;
    [SerializeField] private float maxSpreadDegrees = 4f;

    [Header("Two-Handed Stability")]
    [SerializeField] [Range(0f, 1f)] private float twoHandedRecoilMultiplier = 0.45f;
    [SerializeField] [Range(0f, 1f)] private float twoHandedSpreadMultiplier = 0.5f;
    [SerializeField] [Range(0f, 1f)] private float twoHandedStabilizationBoost = 0.6f;

    [Header("Tuning (Jitter Stabilization)")]
    [SerializeField] private bool stabilizeJitter = true;
    [SerializeField] private float jitterAngleThreshold = 1.2f;
    [SerializeField] [Range(0f, 1f)] private float minFollowFactor = 0.25f;
    [SerializeField] [Range(0f, 1f)] private float maxFollowFactor = 1f;

    protected override RegistrationMode registrationMode => RegistrationMode.SingleAndMultiple;

    private XRGrabInteractable grabInteractable;

    private Vector3 positionOffset;
    private Vector3 positionOffsetVelocity;
    private Vector3 rotationOffsetEuler;
    private Vector3 rotationOffsetVelocity;
    private Vector2 cameraOffsetEuler;
    private Vector2 cameraOffsetVelocity;

    // Cú giật hiện tại: chạy từ mốc lúc bóp cò (kickStart*) tới đỉnh (kickPeak*) trong đúng
    // recoilDuration giây, rồi mới chuyển sang pha hồi về 0 trong recoveryTime giây.
    // kickTimer < 0 nghĩa là đang ở pha hồi.
    private Vector3 kickStartPosition;
    private Vector3 kickPeakPosition;
    private Vector3 kickStartRotation;
    private Vector3 kickPeakRotation;
    private Vector2 kickStartCamera;
    private Vector2 kickPeakCamera;
    private float kickTimer = -1f;

    private Quaternion cameraPivotRestRotation = Quaternion.identity;

    private float currentHeat;

    private Quaternion filteredRotation;
    private bool hasFilteredRotation;

    public bool IsTwoHanded => grabInteractable != null && grabInteractable.interactorsSelecting.Count >= 2;
    public float CurrentSpreadDegrees { get; private set; }

    private void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();

        if (cameraRecoilPivot != null)
            cameraPivotRestRotation = cameraRecoilPivot.localRotation;
    }

    public override void OnLink(XRGrabInteractable grabInteractableArg)
    {
        base.OnLink(grabInteractableArg);

        int singleIndex = grabInteractableArg.singleGrabTransformersCount - 1;
        if (singleIndex >= 0)
            grabInteractableArg.MoveSingleGrabTransformerTo(this, singleIndex);

        int multipleIndex = grabInteractableArg.multipleGrabTransformersCount - 1;
        if (multipleIndex >= 0)
            grabInteractableArg.MoveMultipleGrabTransformerTo(this, multipleIndex);
    }

    private void Update()
    {
        if (grabInteractable == null || !grabInteractable.isSelected)
        {
            ResetState();
            return;
        }

        currentHeat = Mathf.Max(0f, currentHeat - heatDecayPerSecond * Time.deltaTime);
        float stabilityMultiplier = IsTwoHanded ? twoHandedSpreadMultiplier : 1f;
        CurrentSpreadDegrees = Mathf.Lerp(minSpreadDegrees, maxSpreadDegrees, currentHeat) * stabilityMultiplier;

        if (kickTimer >= 0f)
        {
            // Pha giật: đi từ mốc lúc bóp cò tới đỉnh trong đúng recoilDuration giây.
            kickTimer += Time.deltaTime;

            float t = Mathf.Clamp01(kickTimer / Mathf.Max(0.0001f, recoilDuration));
            float eased = 1f - (1f - t) * (1f - t);

            positionOffset = Vector3.Lerp(kickStartPosition, kickPeakPosition, eased);
            rotationOffsetEuler = Vector3.Lerp(kickStartRotation, kickPeakRotation, eased);
            cameraOffsetEuler = Vector2.Lerp(kickStartCamera, kickPeakCamera, eased);

            if (t >= 1f)
                kickTimer = -1f;
        }
        else
        {
            float recover = Mathf.Max(0.0001f, recoveryTime);
            positionOffset = Vector3.SmoothDamp(positionOffset, Vector3.zero, ref positionOffsetVelocity, recover);
            rotationOffsetEuler = Vector3.SmoothDamp(rotationOffsetEuler, Vector3.zero, ref rotationOffsetVelocity, recover);
            cameraOffsetEuler = Vector2.SmoothDamp(cameraOffsetEuler, Vector2.zero, ref cameraOffsetVelocity, recover);
        }

        ApplyCameraKick();
    }

    public override void Process(XRGrabInteractable grabInteractableArg, XRInteractionUpdateOrder.UpdatePhase updatePhase, ref Pose targetPose, ref Vector3 localScale)
    {
        if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic &&
            updatePhase != XRInteractionUpdateOrder.UpdatePhase.OnBeforeRender)
            return;

        ApplyJitterStabilization(grabInteractableArg, ref targetPose);

        targetPose.position += targetPose.rotation * positionOffset;
        targetPose.rotation *= Quaternion.Euler(rotationOffsetEuler);
    }

    public void RegisterShot()
    {
        float scale = Mathf.Max(0f, recoilIntensity) * (IsTwoHanded ? twoHandedRecoilMultiplier : 1f);

        // X âm = nòng ngóc lên, Y = lệch ngang. Z (roll) để yên: bảng thông số không có.
        float pitchKick = -verticalRecoil * scale;
        float yawKick = Random.Range(-horizontalRecoil, horizontalRecoil) * scale;
        float backKick = -kickBackDistance * Mathf.Max(0f, recoilStrength) * scale;

        // Cộng dồn lên cú giật đang chạy dở, nhờ vậy bắn liên thanh thì nòng leo dần lên.
        kickStartPosition = positionOffset;
        kickStartRotation = rotationOffsetEuler;
        kickStartCamera = cameraOffsetEuler;

        kickPeakPosition = positionOffset + new Vector3(0f, 0f, backKick);
        kickPeakRotation = rotationOffsetEuler + new Vector3(pitchKick, yawKick, 0f);
        kickPeakCamera = cameraOffsetEuler + new Vector2(
            -cameraPitch * scale,
            Random.Range(-cameraYaw, cameraYaw) * scale);

        if (maxVerticalClimb > 0f)
            kickPeakRotation.x = Mathf.Max(kickPeakRotation.x, -maxVerticalClimb);

        kickTimer = 0f;
        positionOffsetVelocity = Vector3.zero;
        rotationOffsetVelocity = Vector3.zero;
        cameraOffsetVelocity = Vector2.zero;

        currentHeat = Mathf.Clamp01(currentHeat + heatPerShot);
    }

    private void ApplyCameraKick()
    {
        if (!applyCameraKick || cameraRecoilPivot == null)
            return;

        cameraRecoilPivot.localRotation =
            cameraPivotRestRotation * Quaternion.Euler(cameraOffsetEuler.x, cameraOffsetEuler.y, 0f);
    }

    private void ApplyJitterStabilization(XRGrabInteractable grabInteractableArg, ref Pose targetPose)
    {
        Quaternion rawRotation = targetPose.rotation;

        if (!stabilizeJitter || !hasFilteredRotation)
        {
            filteredRotation = rawRotation;
            hasFilteredRotation = true;
            targetPose.rotation = filteredRotation;
            return;
        }

        float angularSpeed = Quaternion.Angle(filteredRotation, rawRotation) / Mathf.Max(Time.deltaTime, 0.0001f);

        float t = Mathf.InverseLerp(0f, jitterAngleThreshold, angularSpeed);
        float followFactor = Mathf.Lerp(minFollowFactor, maxFollowFactor, t);

        if (grabInteractableArg.interactorsSelecting.Count >= 2)
            followFactor *= 1f - twoHandedStabilizationBoost;

        followFactor = Mathf.Clamp(followFactor, 0.01f, 1f);

        filteredRotation = Quaternion.Slerp(filteredRotation, rawRotation, followFactor);
        targetPose.rotation = filteredRotation;
    }

    private void ResetState()
    {
        positionOffset = Vector3.zero;
        positionOffsetVelocity = Vector3.zero;
        rotationOffsetEuler = Vector3.zero;
        rotationOffsetVelocity = Vector3.zero;
        cameraOffsetEuler = Vector2.zero;
        cameraOffsetVelocity = Vector2.zero;
        kickTimer = -1f;
        ApplyCameraKick();
        hasFilteredRotation = false;
        currentHeat = 0f;
        CurrentSpreadDegrees = minSpreadDegrees;
    }
}
