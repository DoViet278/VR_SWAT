using UnityEngine;

/// <summary>
/// Bộ phận trượt theo một trục: cửa lùa, cửa kho, ngăn kéo, nắp hòm, cửa sổ đẩy lên.
/// Dùng ConfigurableJoint nên vẫn là vật lý thật: đẩy bằng tay, va vào đều ăn.
///
/// Yêu cầu khi dựng: author ở TRẠNG THÁI ĐÓNG, và <see cref="slideAxis"/> là trục trượt tính
/// trong local space của chính object này (cửa lùa thường là Vector3.right, ngăn kéo Vector3.forward).
/// </summary>
[RequireComponent(typeof(ConfigurableJoint))]
public class SlidingPart : MovablePart
{
    [Header("Trượt")]
    [Tooltip("Trục trượt, trong local space của object này. Chiều dương là chiều mở ra.")]
    [SerializeField] private Vector3 slideAxis = Vector3.right;
    [Tooltip("Quãng đường mở hết (mét).")]
    [SerializeField] private float travel = 0.9f;
    [Tooltip("Khe hở nhỏ ở hai đầu để vật không phải nằm tì vào giới hạn cứng của joint.")]
    [SerializeField] private float endSlack = 0.002f;

    [Header("Mở bằng script (AI, nhiệm vụ)")]
    [Tooltip("Tốc độ trượt tối đa (m/s).")]
    [SerializeField] private float driveSpeed = 1.1f;
    [Tooltip("Hệ số bám vị trí đích.")]
    [SerializeField] private float driveGain = 10f;
    [Tooltip("Độ nhạy khi bám tốc độ mong muốn.")]
    [SerializeField] private float driveResponse = 12f;
    [Tooltip("Gia tốc tối đa (m/s²). Thấp = bộ phận nặng, mở chậm rãi.")]
    [SerializeField] private float driveMaxAcceleration = 40f;

    private ConfigurableJoint joint;
    private Vector3 closedLocalPosition;
    private Vector3 slideDirectionInParent;

    public ConfigurableJoint Joint => joint;

    /// <summary>Quãng đường đã mở (mét).</summary>
    public float CurrentTravel =>
        Vector3.Dot(transform.localPosition - closedLocalPosition, slideDirectionInParent);

    public override float OpenAmount => Mathf.Clamp01(CurrentTravel / Mathf.Max(0.0001f, travel));

    private Vector3 AxisLocal
    {
        get
        {
            Vector3 axis = slideAxis;
            return axis.sqrMagnitude < 1e-6f ? Vector3.right : axis.normalized;
        }
    }

    protected override void Awake()
    {
        joint = GetComponent<ConfigurableJoint>();

        base.Awake();
    }

    protected override void CacheClosedPose()
    {
        closedLocalPosition = transform.localPosition;
        slideDirectionInParent = (transform.localRotation * AxisLocal).normalized;
    }

    protected override void ConfigureJoint()
    {
        Vector3 axisLocal = AxisLocal;

        joint.axis = axisLocal;
        joint.secondaryAxis = Vector3.Cross(axisLocal, Mathf.Abs(axisLocal.y) < 0.9f ? Vector3.up : Vector3.forward).normalized;

        joint.xMotion = ConfigurableJointMotion.Limited;
        joint.yMotion = ConfigurableJointMotion.Locked;
        joint.zMotion = ConfigurableJointMotion.Locked;
        joint.angularXMotion = ConfigurableJointMotion.Locked;
        joint.angularYMotion = ConfigurableJointMotion.Locked;
        joint.angularZMotion = ConfigurableJointMotion.Locked;

        // Giới hạn trượt của ConfigurableJoint luôn đối xứng quanh connectedAnchor, nên muốn
        // quãng chạy là [0, travel] thì phải tự đặt connectedAnchor lệch về trước nửa quãng
        // đường rồi cho limit = travel / 2, thay vì để Unity tự tính.
        float half = travel * 0.5f;
        Vector3 anchorWorld = transform.TransformPoint(joint.anchor);
        Vector3 axisWorld = transform.TransformDirection(axisLocal).normalized;
        Vector3 connectedWorld = anchorWorld + axisWorld * half;

        joint.autoConfigureConnectedAnchor = false;
        joint.connectedAnchor = joint.connectedBody != null
            ? joint.connectedBody.transform.InverseTransformPoint(connectedWorld)
            : connectedWorld;

        SoftJointLimit limit = joint.linearLimit;
        limit.limit = half + Mathf.Max(0f, endSlack);
        limit.bounciness = 0f;
        limit.contactDistance = Mathf.Max(0.001f, endSlack);
        joint.linearLimit = limit;
    }

    protected override void SnapToClosed()
    {
        transform.localPosition = closedLocalPosition;
    }

    protected override void DriveTowards(float target)
    {
        if (Body == null)
            return;

        Vector3 axisWorld = transform.TransformDirection(AxisLocal).normalized;

        float error = (target - OpenAmount) * travel;
        float desiredSpeed = Mathf.Clamp(error * driveGain, -driveSpeed, driveSpeed);
        float currentSpeed = Vector3.Dot(Body.linearVelocity, axisWorld);
        float acceleration = Mathf.Clamp(
            (desiredSpeed - currentSpeed) * driveResponse,
            -driveMaxAcceleration,
            driveMaxAcceleration);

        // Nhân khối lượng để lực đẩy không phụ thuộc vào mass đặt trên Rigidbody.
        Body.AddForce(axisWorld * acceleration * Body.mass, ForceMode.Force);
    }
}
