using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Cửa bản lề. Chạy hoàn toàn bằng vật lý (HingeJoint) nên:
/// - đẩy bằng physics hands, húc vai, bắn vào đều làm cửa động;
/// - đạp / nổ phá được khoá (<see cref="Breach(float)"/>);
/// - AI hoặc script nhiệm vụ mở được bằng <see cref="MovablePart.Open"/>.
///
/// Yêu cầu khi dựng: object cửa phải được author ở TRẠNG THÁI ĐÓNG, vì mốc 0 độ của HingeJoint
/// chính là pose lúc joint được tạo. Dùng Tools &gt; VR SWAT &gt; Environment Setup để dựng cho nhanh.
/// </summary>
[RequireComponent(typeof(HingeJoint))]
public class HingedDoor : MovablePart
{
    [Header("Bản lề")]
    [Tooltip("Góc mở hết (độ). Dấu quyết định chiều mở: +90 mở một chiều, -90 mở chiều ngược lại.")]
    [SerializeField] private float openAngle = 95f;
    [Tooltip("Cửa đẩy được cả hai chiều (cửa thông phòng, cửa quán bar).")]
    [SerializeField] private bool swingBothWays;
    [Tooltip("Khoảng cách góc (độ) mà solver bắt đầu phanh trước giới hạn. Lớn hơn = ít vọt qua giới hạn.")]
    [SerializeField] private float limitContactDistance = 2f;

    [Header("Điểm đẩy / đạp cửa")]
    [Tooltip("Điểm xa bản lề nhất, thường là chỗ tay nắm. Để trống sẽ lấy tâm collider của cửa.")]
    [SerializeField] private Transform pushAnchor;

    [Header("Mở bằng script (AI, nhiệm vụ)")]
    [Tooltip("Tốc độ mở tối đa (độ/giây).")]
    [SerializeField] private float driveSpeed = 140f;
    [Tooltip("Hệ số bám góc đích. Cao = tới đích nhanh nhưng dễ vọt.")]
    [SerializeField] private float driveGain = 4f;
    [Tooltip("Lực motor (N·m). Phải lớn hơn ma sát và trọng lượng cửa.")]
    [SerializeField] private float driveForce = 300f;

    [Header("Tự đóng (door closer)")]
    [Tooltip("Cửa có lò xo tự kéo về vị trí đóng.")]
    [SerializeField] private bool useCloser;
    [SerializeField] private float closerSpring = 10f;
    [SerializeField] private float closerDamper = 3f;

    [Header("Phá cửa (breach)")]
    [Tooltip("Xung lực tối thiểu (kg·m/s) để phá được khoá. Đạp cửa cỡ 8-15, nổ thì lớn hơn nhiều.")]
    [SerializeField] private float breachImpulse = 10f;
    [Tooltip("Vật nặng đập vào cửa cũng phá được khoá (lựu đạn, ghế ném vào, người húc).")]
    [SerializeField] private bool breachOnHeavyImpact = true;
    [SerializeField] private AudioClip breachClip;

    [Header("Đóng sầm")]
    [Tooltip("Tốc độ góc (độ/giây) trở lên khi về tới vị trí đóng thì tính là đóng sầm.")]
    [SerializeField] private float slamSpeed = 170f;
    [Tooltip("Đóng sầm là chốt cài luôn, không nảy ra lại.")]
    [SerializeField] private bool latchOnSlam = true;
    [SerializeField] private AudioClip slamClip;

    [Header("Sự kiện")]
    [Tooltip("Khoá đã bị phá.")]
    public UnityEvent OnBreached;
    public UnityEvent OnSlammed;

    private HingeJoint hinge;
    private Collider bodyCollider;
    private Quaternion closedLocalRotation;
    private Vector3 hingeAxisInParent;

    public HingeJoint Hinge => hinge;

    /// <summary>Góc cửa hiện tại so với lúc đóng (độ). Dấu theo chiều quay quanh trục bản lề.</summary>
    public float CurrentAngle
    {
        get
        {
            Quaternion delta = transform.localRotation * Quaternion.Inverse(closedLocalRotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);

            if (axis.sqrMagnitude < 1e-6f || float.IsNaN(axis.x) || float.IsInfinity(axis.x))
                return 0f;

            if (angle > 180f)
                angle -= 360f;

            return Vector3.Dot(axis.normalized, hingeAxisInParent) < 0f ? -angle : angle;
        }
    }

    public override float OpenAmount =>
        Mathf.Clamp01(Mathf.Abs(CurrentAngle) / Mathf.Max(1f, Mathf.Abs(openAngle)));

    /// <summary>Điểm trên cửa dùng để đẩy / đạp, tính theo world space.</summary>
    public Vector3 PushWorldPoint
    {
        get
        {
            if (pushAnchor != null)
                return pushAnchor.position;

            return bodyCollider != null ? bodyCollider.bounds.center : transform.position;
        }
    }

    public Vector3 HingeWorldPoint => transform.TransformPoint(hinge.anchor);

    /// <summary>Hướng world mà <see cref="PushWorldPoint"/> đi khi cửa mở ra.</summary>
    public Vector3 OpenDirection
    {
        get
        {
            Vector3 axisWorld = transform.TransformDirection(hinge.axis).normalized;
            Vector3 arm = PushWorldPoint - HingeWorldPoint;

            // Vận tốc của một điểm khi quay quanh trục: v = omega x r.
            Vector3 tangent = Vector3.Cross(axisWorld, arm);

            if (tangent.sqrMagnitude < 1e-6f)
                return transform.forward;

            return (openAngle >= 0f ? tangent : -tangent).normalized;
        }
    }

    protected override void Awake()
    {
        hinge = GetComponent<HingeJoint>();
        bodyCollider = GetComponent<Collider>();

        base.Awake();
    }

    protected override void CacheClosedPose()
    {
        closedLocalRotation = transform.localRotation;
        hingeAxisInParent = (closedLocalRotation * hinge.axis).normalized;
    }

    protected override void ConfigureJoint()
    {
        float span = Mathf.Abs(openAngle);
        JointLimits limits = hinge.limits;

        if (swingBothWays)
        {
            limits.min = -span;
            limits.max = span;
        }
        else if (openAngle >= 0f)
        {
            limits.min = 0f;
            limits.max = span;
        }
        else
        {
            limits.min = -span;
            limits.max = 0f;
        }

        limits.bounciness = 0f;
        limits.bounceMinVelocity = 0f;
        limits.contactDistance = Mathf.Max(0f, limitContactDistance);
        hinge.limits = limits;
        hinge.useLimits = true;

        JointSpring spring = hinge.spring;
        spring.spring = useCloser ? closerSpring : 0f;
        spring.damper = useCloser ? closerDamper : 0f;
        spring.targetPosition = 0f;
        hinge.spring = spring;
        hinge.useSpring = useCloser;

        hinge.useMotor = false;
    }

    protected override void SnapToClosed()
    {
        transform.localRotation = closedLocalRotation;
    }

    protected override void DriveTowards(float target)
    {
        float targetAngle = openAngle * target;
        float error = targetAngle - CurrentAngle;

        JointMotor motor = hinge.motor;
        motor.targetVelocity = Mathf.Clamp(error * driveGain, -driveSpeed, driveSpeed);
        motor.force = driveForce;
        motor.freeSpin = false;
        hinge.motor = motor;
        hinge.useMotor = true;
    }

    protected override void OnDriveStopped()
    {
        hinge.useMotor = false;
    }

    protected override void OnReachedClosed()
    {
        float speedDegrees = Body != null ? Body.angularVelocity.magnitude * Mathf.Rad2Deg : 0f;

        if (speedDegrees < slamSpeed)
            return;

        PlayClip(slamClip);
        OnSlammed?.Invoke();

        if (latchOnSlam)
            Latch();
    }

    // ------------------------------------------------------------------ Phá cửa

    /// <summary>Đạp cửa ở <see cref="PushWorldPoint"/> theo chiều mở.</summary>
    /// <param name="impulse">Xung lực (kg·m/s).</param>
    public bool Breach(float impulse)
    {
        return Breach(impulse, PushWorldPoint, OpenDirection);
    }

    /// <summary>
    /// Đạp / nổ / húc cửa. Nếu cửa đang khoá mà xung lực chưa đủ thì chỉ rung ổ khoá và trả về
    /// false. Đủ mạnh thì khoá bật, chốt nhả, và cửa nhận luôn xung lực đó.
    /// </summary>
    public bool Breach(float impulse, Vector3 worldPoint, Vector3 direction)
    {
        if (IsLocked)
        {
            if (impulse < breachImpulse)
            {
                Rattle(null);
                return false;
            }

            Unlock();
            PlayClip(breachClip);
            OnBreached?.Invoke();
        }

        // Unlatch trả isKinematic về false, phải gọi trước khi add lực.
        Unlatch();

        if (Body != null && !Body.isKinematic)
            Body.AddForceAtPosition(direction.normalized * impulse, worldPoint, ForceMode.Impulse);

        return true;
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Chỉ xét khi cửa còn đang cài chốt: cửa đã mở thì vật lý tự lo.
        if (!breachOnHeavyImpact || !IsLatched || collision.contactCount == 0)
            return;

        float impulse = collision.impulse.magnitude;

        if (impulse < breachImpulse)
            return;

        // Đẩy tại điểm va chạm nhưng theo chiều bản lề cho phép, không theo normal va chạm:
        // dấu của normal trong cặp collider không ổn định, còn cửa thì chỉ mở được một chiều.
        Breach(impulse, collision.GetContact(0).point, OpenDirection);
    }
}
