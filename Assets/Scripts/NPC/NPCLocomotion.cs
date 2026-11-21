using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Di chuyển NPC_M4A1 kiểu "quay rồi đi thẳng", chỉ dùng clip đi tới.
///
/// MoveX/MoveY là hướng đi theo WORLD (Y của tham số = trục Z của scene). Độ lớn 1 = đi bộ, 2 = chạy.
/// Blend tree Locomotion/Crouch đặt clip đi tới ở mọi hướng nên hướng nào cũng phát clip đi tới.
///
/// - Lệch hướng lớn: dừng lại, phát TurnLeft / TurnRight / Turn180 và xoay thân theo tiến độ clip.
/// - Lệch hướng nhỏ: xoay mượt.
/// - NPC luôn tiến theo hướng thân đang quay mặt, không đi ngang hay đi lùi.
/// </summary>
[RequireComponent(typeof(Animator))]
public class NPCLocomotion : MonoBehaviour
{
    private const float MaxMoveMagnitude = 2f;
    private const float TurnEnterTimeout = 0.5f;

    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int IsCrouchingHash = Animator.StringToHash("IsCrouching");
    private static readonly int TurnLeftHash = Animator.StringToHash("TurnLeft");
    private static readonly int TurnRightHash = Animator.StringToHash("TurnRight");
    private static readonly int Turn180Hash = Animator.StringToHash("Turn180");
    private static readonly int LocomotionStateHash = Animator.StringToHash("Locomotion");
    private static readonly int CrouchStateHash = Animator.StringToHash("Crouch");

    [Header("Input")]
    [Tooltip("Bật: lấy hướng đi từ NavMeshAgent và tự ghi vào MoveX/MoveY. Tắt: chỉnh MoveX/MoveY trong Animator.")]
    [SerializeField] private bool _driveFromAgent;
    [Tooltip("Tốc độ (m/s) ứng với độ lớn 1 (đi bộ). Độ lớn 2 (chạy) = 2x giá trị này.")]
    [SerializeField] private float _walkSpeed = 1.5f;
    [Tooltip("Khi dùng MoveX/MoveY: di chuyển NPC thật (root motion tắt nên nếu không bật NPC sẽ đi tại chỗ).")]
    [SerializeField] private bool _moveTransform = true;

    [Header("Rotation")]
    [Tooltip("Hướng quay mặt khi ĐỨNG YÊN. Khi đang đi, NPC luôn quay theo hướng đi.")]
    [SerializeField] private Transform _lookTarget;
    [Tooltip("Tốc độ xoay tối đa (độ/giây) khi lệch hướng nhỏ.")]
    [SerializeField] private float _turnSpeed = 360f;
    [Tooltip("Thời gian làm mượt khi lệch hướng nhỏ.")]
    [SerializeField] private float _turnSmoothTime = 0.12f;
    [Tooltip("Độ lớn input để BẮT ĐẦU coi là đang đi.")]
    [SerializeField] private float _moveStartThreshold = 0.2f;
    [Tooltip("Độ lớn input để coi là ĐÃ DỪNG. Nhỏ hơn ngưỡng bắt đầu để tránh nhấp nháy.")]
    [SerializeField] private float _moveDeadZone = 0.1f;

    [Header("Turn clips")]
    [SerializeField] private bool _useTurnClips = true;
    [Tooltip("Góc lệch (độ) tối thiểu để phát clip quay. Nhỏ hơn thì xoay mượt.")]
    [SerializeField] private float _turnAngleThreshold = 60f;
    [Tooltip("Góc lệch (độ) từ mức này trở lên dùng Turn180.")]
    [SerializeField] private float _turn180Threshold = 135f;
    [Tooltip("Thời gian chờ giữa 2 lần quay để tránh lặp.")]
    [SerializeField] private float _turnCooldown = 0.2f;
    [Tooltip("normalizedTime mà clip quay kết thúc (khớp exit time 0.9 trong controller).")]
    [SerializeField, Range(0.1f, 1f)] private float _turnEndTime = 0.9f;
    [Tooltip("Tắt Root Motion của Animator để chỉ script này xoay thân.")]
    [SerializeField] private bool _disableRootMotion = true;

    [Header("Smoothing")]
    [Tooltip("Thời gian làm mượt tốc độ di chuyển (và MoveX/MoveY khi dùng agent).")]
    [SerializeField] private float _dampTime = 0.1f;

    private Animator _animator;
    private NavMeshAgent _agent;

    private float _speed;
    private float _speedVelocity;
    private float _yawVelocity;
    private bool _isMoving;

    private bool _turning;
    private bool _turnEntered;
    private bool _stoppedAgentForTurn;
    private int _turnTrigger;
    private int _turnStateHash;
    private float _turnTotal;
    private float _turnApplied;
    private float _turnWait;
    private float _nextTurnTime;

    public bool IsTurning => _turning;

    /// <summary>Tốc độ (m/s) ứng với clip đi bộ. Action dùng để đặt agent.speed khớp anim.</summary>
    public float WalkSpeed => _walkSpeed;

    public bool DriveFromAgent
    {
        get => _driveFromAgent;
        set => _driveFromAgent = value;
    }

    public Transform LookTarget
    {
        get => _lookTarget;
        set => _lookTarget = value;
    }

    public void SetMoveInput(Vector2 worldMove)
    {
        _animator.SetFloat(MoveXHash, worldMove.x);
        _animator.SetFloat(MoveYHash, worldMove.y);
    }

    private bool AgentReady => _agent != null && _agent.enabled && _agent.isOnNavMesh;
    private bool UsingAgent => _driveFromAgent && AgentReady;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _agent = GetComponent<NavMeshAgent>();
        if (_disableRootMotion) _animator.applyRootMotion = false;
        if (_agent != null) _agent.updateRotation = false; // script này là nơi duy nhất xoay thân
    }

    private void OnDisable()
    {
        if (_turning) EndTurn();
    }

    private void Update()
    {
        if (_turning)
        {
            TickTurn();
            UpdateMovement(Vector3.zero);
            return;
        }

        if (!IsInMovableState())
        {
            _isMoving = false;
            _yawVelocity = 0f;
            UpdateMovement(Vector3.zero);
            return;
        }

        Vector3 worldMove = GetWorldMove();
        _isMoving = worldMove.magnitude > (_isMoving ? _moveDeadZone : _moveStartThreshold);
        if (!_isMoving) worldMove = Vector3.zero;

        Vector3 faceDir = Vector3.zero;
        if (_isMoving) faceDir = worldMove;
        else if (_lookTarget != null) faceDir = _lookTarget.position - transform.position;
        faceDir.y = 0f;

        if (faceDir.sqrMagnitude > 0.0001f)
        {
            float angle = Vector3.SignedAngle(transform.forward, faceDir, Vector3.up);
            if (Mathf.Abs(angle) >= _turnAngleThreshold)
            {
                if (CanStartTurnClip())
                {
                    StartTurn(angle);
                    UpdateMovement(Vector3.zero);
                    return;
                }

                // Chờ clip quay (cooldown/transition): không đi lệch khỏi hướng đang quay mặt.
                if (_useTurnClips && !_animator.GetBool(IsCrouchingHash))
                {
                    UpdateMovement(Vector3.zero);
                    return;
                }
            }
            RotateSmooth(angle);
        }
        else
        {
            _yawVelocity = 0f;
        }

        UpdateMovement(worldMove);
    }

    private Vector3 GetWorldMove()
    {
        Vector3 move = UsingAgent
            ? _agent.desiredVelocity / Mathf.Max(0.01f, _walkSpeed)
            : new Vector3(_animator.GetFloat(MoveXHash), 0f, _animator.GetFloat(MoveYHash));
        move.y = 0f;
        return Vector3.ClampMagnitude(move, MaxMoveMagnitude);
    }

    private bool IsInMovableState()
    {
        int state = _animator.IsInTransition(0)
            ? _animator.GetNextAnimatorStateInfo(0).shortNameHash
            : _animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        return state == LocomotionStateHash || state == CrouchStateHash;
    }

    private void RotateSmooth(float signedAngle)
    {
        float newOffset = Mathf.SmoothDampAngle(signedAngle, 0f, ref _yawVelocity, _turnSmoothTime, _turnSpeed);
        transform.Rotate(0f, signedAngle - newOffset, 0f, Space.World);
    }

    // Luôn tiến theo hướng thân. Khi dùng MoveX/MoveY thì script chỉ đọc, không ghi đè giá trị bạn chỉnh.
    private void UpdateMovement(Vector3 worldMove)
    {
        _speed = Mathf.SmoothDamp(_speed, worldMove.magnitude, ref _speedVelocity, _dampTime);

        if (UsingAgent)
        {
            _animator.SetFloat(MoveXHash, worldMove.x, _dampTime, Time.deltaTime);
            _animator.SetFloat(MoveYHash, worldMove.z, _dampTime, Time.deltaTime);
            return;
        }

        if (!_moveTransform) return;

        Vector3 delta = transform.forward * (_speed * _walkSpeed * Time.deltaTime);
        if (AgentReady) _agent.Move(delta);
        else transform.position += delta;
    }

    private bool CanStartTurnClip()
    {
        if (!_useTurnClips || Time.time < _nextTurnTime) return false;
        if (_animator.GetBool(IsCrouchingHash) || _animator.IsInTransition(0)) return false;
        return _animator.GetCurrentAnimatorStateInfo(0).shortNameHash == LocomotionStateHash;
    }

    private void StartTurn(float signedAngle)
    {
        string stateName;
        if (Mathf.Abs(signedAngle) >= _turn180Threshold) { _turnTrigger = Turn180Hash; stateName = "Turn180"; }
        else if (signedAngle > 0f) { _turnTrigger = TurnRightHash; stateName = "TurnRight"; }
        else { _turnTrigger = TurnLeftHash; stateName = "TurnLeft"; }

        _animator.SetTrigger(_turnTrigger);

        _turning = true;
        _turnEntered = false;
        _turnStateHash = Animator.StringToHash(stateName);
        _turnTotal = signedAngle;
        _turnApplied = 0f;
        _turnWait = 0f;
        _yawVelocity = 0f;

        // Chỉ dừng agent nếu nó đang chạy, và chỉ bật lại đúng phần mình đã dừng.
        if (AgentReady && !_agent.isStopped)
        {
            _agent.isStopped = true;
            _stoppedAgentForTurn = true;
        }
    }

    private void TickTurn()
    {
        bool inTransition = _animator.IsInTransition(0);
        AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(0);
        AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
        bool enteringTurn = inTransition && next.shortNameHash == _turnStateHash;
        bool inTurn = !inTransition && current.shortNameHash == _turnStateHash;

        if (enteringTurn || inTurn)
        {
            _turnEntered = true;
            // Lúc đang blend vào, thời gian của clip nằm ở "next" nên đọc từ đó để không bị giật góc.
            float time = enteringTurn ? next.normalizedTime : current.normalizedTime;
            ApplyTurnProgress(Mathf.Clamp01(time / _turnEndTime));
            if (_turnApplied == _turnTotal) EndTurn();
            return;
        }

        if (!_turnEntered)
        {
            _turnWait += Time.deltaTime;
            if (_turnWait > TurnEnterTimeout)
            {
                _animator.ResetTrigger(_turnTrigger);
                EndTurn();
            }
            return;
        }

        // Đã rời state quay: về Locomotion thì hoàn tất góc còn lại, bị ngắt (vd. chết) thì bỏ.
        int destination = inTransition ? next.shortNameHash : current.shortNameHash;
        if (destination == LocomotionStateHash) ApplyTurnProgress(1f);
        EndTurn();
    }

    private void ApplyTurnProgress(float progress)
    {
        float target = progress >= 1f ? _turnTotal : _turnTotal * progress;
        transform.Rotate(0f, target - _turnApplied, 0f, Space.World);
        _turnApplied = target;
    }

    private void EndTurn()
    {
        _turning = false;
        _nextTurnTime = Time.time + _turnCooldown;
        if (_stoppedAgentForTurn && AgentReady) _agent.isStopped = false;
        _stoppedAgentForTurn = false;
    }
}
