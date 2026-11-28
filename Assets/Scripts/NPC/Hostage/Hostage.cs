using System.Collections.Generic;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

/// <summary>
/// Con tin (Hostage): giữ trạng thái + đồng bộ với Behavior Graph "Hostage" (Assets/Behaviour Graphs/Hostage).
///
/// Hành vi (graph chọn theo thứ tự ưu tiên, giống graph của NPC):
///   1. Bị còng (Restrained)      : quỳ, tay sau lưng, đứng yên tới hết màn; nói "Thank god you're here!" một lần.
///   2. Đầu hàng (Surrender)      : bị hô đủ số lần (random 1-3, Compliance.Order) -> quỳ giơ tay, chờ còng.
///   3. Bị khống chế (Held)       : nghi phạm giữ làm con tin -> đứng yên ôm đầu, van xin. Được buông mà kẻ giữ còn
///                                  nguy hiểm thì chạy khỏi hắn (nếu không thấy cảnh sát).
///   4. Chạy khỏi tiếng súng (Flee): nghe súng / nổ trong FleeHearRadius mà chưa thấy cảnh sát -> chạy tới chỗ khuất.
///   5. Ngồi ôm đầu (Cower)       : súng nổ trong CowerRadius / nổ gần -> ngồi ôm đầu tại chỗ tới khi yên CowerQuietTime.
///   6. Sợ hãi (Scared)           : thấy người chơi / đồng đội -> co người ôm đầu, kêu lên, quay về phía họ.
///   7. Kêu cứu (CallForHelp)     : chưa thấy ai -> thỉnh thoảng quỳ xuống gọi "Help!".
///   8. Di chuyển (MoveToWayPoint): đi qua các WayPoints (nếu có).
///   9. Đứng chờ (Idle).
///
/// Blackboard (gán trên BehaviorGraphAgent) là nguồn chính cho Target / WayPoints, Inspector dùng khi Blackboard trống
/// (cùng quy ước với NPC). Animator: Assets/Animators/Hostage/Hostage.controller. Di chuyển dùng NPCLocomotion.
/// </summary>
[RequireComponent(typeof(BehaviorGraphAgent), typeof(NavMeshAgent), typeof(NPCLocomotion))]
public class Hostage : MonoBehaviour, IDamageable
{
    public const string TargetVar = "Target";
    public const string WayPointsVar = "WayPoints";

    private static readonly int ScaredHash = Animator.StringToHash("Scared");
    private static readonly int CallHelpHash = Animator.StringToHash("CallHelp");
    private static readonly int SurrenderHash = Animator.StringToHash("Surrender");
    private static readonly int SurrenderedHash = Animator.StringToHash("Surrendered");
    private static readonly int RestrainHash = Animator.StringToHash("Restrain");
    private static readonly int FlinchHash = Animator.StringToHash("Flinch");
    private static readonly int DieFrontHash = Animator.StringToHash("DieFront");
    private static readonly int DieBackHash = Animator.StringToHash("DieBack");
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");

    [Header("Health")]
    [SerializeField] private float _maxHealth = 50f;

    [Header("Target")]
    [Tooltip("Để trống: tự tìm object có tag TargetTag (người chơi).")]
    [SerializeField] private GameObject _target;
    [SerializeField] private string _targetTag = "Player";

    [Header("Di chuyển (dùng khi WayPoints trên Blackboard trống)")]
    [SerializeField] private List<GameObject> _wayPoints = new();

    [Header("Sợ hãi")]
    [Tooltip("Nhìn thấy người chơi trong khoảng này (m).")]
    [SerializeField] private float _viewDistance = 12f;
    [SerializeField, Range(1f, 360f)] private float _viewAngle = 140f;
    [Tooltip("Người chơi lại gần trong khoảng này thì biết ngay, kể cả sau lưng.")]
    [SerializeField] private float _senseDistance = 3f;
    [Tooltip("Mất dấu người chơi bao lâu (giây) thì hết sợ.")]
    [SerializeField] private float _scaredMemory = 6f;
    [Tooltip("Tốc độ quay mặt về phía người chơi khi sợ (độ/giây).")]
    [SerializeField] private float _faceThreatSpeed = 120f;

    [Header("Kêu cứu")]
    [SerializeField] private bool _callForHelp = true;
    [Tooltip("Khoảng nghỉ giữa 2 lần kêu cứu (giây, random trong khoảng).")]
    [SerializeField] private Vector2 _helpInterval = new(8f, 15f);
    [Tooltip("Thời gian một lần kêu cứu (giây), khớp với anim CallHelp.")]
    [SerializeField] private float _callForHelpDuration = 4f;

    [Header("Đầu hàng")]
    [Tooltip("Số lần người chơi hô thì con tin giơ tay.")]
    [SerializeField, Min(1)] private int _shoutsToComply = 1;
    [Tooltip("Như SWAT 4: số lần cần hô random trong ShoutsToComply..giá trị này. Nhỏ hơn ShoutsToComply = cố định.")]
    [SerializeField, Min(1)] private int _shoutsToComplyMax = 3;
    [Tooltip("Nhìn thấy người chơi liên tục bao lâu thì tự giơ tay (giây). 0 = chỉ giơ tay khi bị hô.")]
    [SerializeField] private float _autoComplyTime;
    [Tooltip("Chiều cao collider thân khi quỳ.")]
    [SerializeField] private float _kneelColliderHeight = 1.1f;

    [Header("Phản ứng tiếng súng / nổ (NPCHearing)")]
    [Tooltip("Nghe súng / nổ mà chưa thấy cảnh sát thì chạy ra xa nguồn tiếng.")]
    [SerializeField] private bool _fleeFromGunfire = true;
    [Tooltip("Tiếng súng / nổ trong khoảng này (m) thì bỏ chạy (xa hơn CowerRadius).")]
    [SerializeField] private float _fleeHearRadius = 15f;
    [Tooltip("Chạy tới chỗ cách nguồn tiếng ít nhất ngần này (m), ưu tiên chỗ khuất.")]
    [SerializeField] private float _fleeDistance = 8f;
    [Tooltip("Chỉ chạy vì tiếng nghe trong ngần này giây trở lại.")]
    [SerializeField] private float _fleeMemory = 4f;
    [Tooltip("Súng nổ trong khoảng này (m): ngồi ôm đầu tại chỗ thay vì chạy.")]
    [SerializeField] private float _cowerRadius = 5f;
    [Tooltip("Vụ nổ trong khoảng này (m): ngồi ôm đầu.")]
    [SerializeField] private float _explosionCowerRadius = 10f;
    [Tooltip("Ngồi ôm đầu tới khi yên ngần này giây.")]
    [SerializeField] private float _cowerQuietTime = 3f;
    [Tooltip("Chạy tới chỗ khuất rồi thì nấp (ngồi ôm đầu) ngần này giây, không đi tuần tra quay lại; thấy cảnh sát thì thôi.")]
    [SerializeField] private float _hideAfterFleeTime = 30f;

    [Header("Giọng nói (NPCVoice + bộ giọng Audios/NPC/Hostage/Hostage_Male_Voice)")]
    [Tooltip("Trống: lấy NPCVoice trên object này (chưa có thì tự thêm).")]
    [SerializeField] private NPCVoice _voice;
    [Tooltip("Đang sợ: rên / kêu lại sau mỗi khoảng này (giây, random trong khoảng).")]
    [SerializeField] private Vector2 _scaredVoiceInterval = new(5f, 9f);

    [Header("Events")]
    public UnityEvent OnScared;
    public UnityEvent OnSurrendered;
    public UnityEvent OnRestrained;
    public UnityEvent OnDied;

    [SerializeField] private BehaviorGraphAgent _behaviorAgent;

    private const float PerceptionInterval = 0.1f;

    private Animator _animator;
    private CapsuleCollider _body;
    private float _health;
    private float _lastSeenTime = float.NegativeInfinity;
    private float _seenSince = -1f;
    private float _nextPerception;
    private float _nextHelpTime;
    private int _shoutsHeard;
    private int _shoutsNeeded = 1;
    private NPC _heldBy;
    private float _nextHeldVoice;
    private bool _wasScared;
    private bool _scaredAnim;
    private bool _faceThreat = true;
    private float _nextScaredVoice;
    private bool _seesTeammate;
    private GameObject _seenPolice; // người chơi / đồng đội vừa thấy (quay mặt về phía họ khi sợ)
    private float _lastCloseViolence = float.NegativeInfinity;
    private float _fleeNoiseTime = float.NegativeInfinity;
    private float _fledNoiseTime = float.NegativeInfinity;
    private Vector3 _fleeFrom;
    private float _hideUntil = float.NegativeInfinity;
    private float _rescuedAt = -1f;

    /// <summary>Mọi con tin đang bật trong scene.</summary>
    public static readonly List<Hostage> Active = new();

    public float Health => _health;
    public bool IsDead => _health <= 0f;
    public GameObject Target => _target;
    public List<GameObject> WayPoints => _wayPoints;
    public float CallForHelpDuration => _callForHelpDuration;

    /// <summary>Waypoint đang đi tới (GetNextWayPoint ghi, MoveToWayPoint đọc khi ô chưa nối Blackboard).</summary>
    public GameObject CurrentWayPoint { get; set; }

    public bool CanSeeTarget { get; private set; }
    public bool IsSurrendered { get; private set; }
    public bool IsRestrained { get; private set; }

    /// <summary>Đã bị hô đủ số lần (hoặc nhìn thấy đủ lâu): graph chuyển sang nhánh đầu hàng.</summary>
    public bool ComplyOrdered { get; private set; }

    /// <summary>Đang sợ: thấy người chơi, hoặc vừa thấy trong ScaredMemory giây.</summary>
    public bool IsScared => !IsDead && !IsSurrendered && _target != null && Time.time - _lastSeenTime <= _scaredMemory;

    /// <summary>Đến lúc kêu cứu: không sợ, chưa đầu hàng và đã hết thời gian nghỉ.</summary>
    public bool ShouldCallForHelp => _callForHelp && !IsDead && !IsSurrendered && !ComplyOrdered && !IsScared && Time.time >= _nextHelpTime;

    /// <summary>Có việc gấp hơn đi lại / đứng chờ (để Action đang chạy tự dừng cho graph chọn lại).</summary>
    public bool NeedsAttention => IsDead || IsSurrendered || ComplyOrdered || IsScared || ShouldCallForHelp || IsHeld || ShouldFlee || ShouldCower;

    /// <summary>Thấy người chơi hoặc một đồng đội (cảnh sát) trong tầm nhìn.</summary>
    public bool SeesPolice => CanSeeTarget || _seesTeammate;

    /// <summary>Súng nổ sát bên / nổ gần trong CowerQuietTime giây.</summary>
    public bool IsCloseViolence => !IsDead && !IsSurrendered && !ComplyOrdered && !IsHeld && Time.time - _lastCloseViolence < _cowerQuietTime;

    /// <summary>Đang nấp sau khi chạy khỏi tiếng súng (HideAfterFleeTime giây, tới khi thấy cảnh sát).</summary>
    public bool IsHiding => !IsDead && !IsSurrendered && !ComplyOrdered && !IsHeld && !SeesPolice && Time.time < _hideUntil;

    /// <summary>Ngồi ôm đầu tại chỗ: súng nổ sát bên / nổ gần, hoặc đang nấp sau khi chạy.</summary>
    public bool ShouldCower => IsCloseViolence || IsHiding;

    /// <summary>HostageFleeAction chạy tới nơi: nấp lại ở đó.</summary>
    public void BeginHiding() => _hideUntil = Time.time + _hideAfterFleeTime;

    /// <summary>Nghe súng / nổ trong FleeHearRadius (chưa xử lý), chưa thấy cảnh sát: bỏ chạy khỏi nguồn tiếng.</summary>
    public bool ShouldFlee => _fleeFromGunfire && !IsDead && !IsSurrendered && !ComplyOrdered && !IsHeld && !IsCloseViolence
                              && !SeesPolice && _fleeNoiseTime > _fledNoiseTime && Time.time - _fleeNoiseTime <= _fleeMemory;

    /// <summary>Nguồn tiếng cần chạy khỏi (tiếng súng / nổ gần nhất, hoặc kẻ vừa buông mình).</summary>
    public Vector3 FleeFrom => _fleeFrom;
    public float FleeDistance => _fleeDistance;

    /// <summary>HostageFleeAction đã xử lý tiếng hiện tại (đã chạy hoặc không có chỗ chạy).</summary>
    public void MarkFled() => _fledNoiseTime = _fleeNoiseTime;

    /// <summary>Đang bị nghi phạm khống chế làm con tin (EnemyTactics): đứng yên, van xin, không giơ tay được.</summary>
    public bool IsHeld => _heldBy != null && !_heldBy.IsDead && !_heldBy.IsSurrendered;
    public NPC HeldBy => IsHeld ? _heldBy : null;

    /// <summary>Nghi phạm bắt / buông con tin. Trả false nếu không bắt được (đã chết / giơ tay / bị còng / người khác giữ).</summary>
    public bool SetHeld(bool held, NPC by)
    {
        if (held)
        {
            if (IsDead || IsSurrendered || IsRestrained || (IsHeld && _heldBy != by)) return false;
            _heldBy = by;
            _lastSeenTime = Time.time;
            if (NPCActionUtils.TryGetAgent(gameObject, out NavMeshAgent agent)) { agent.ResetPath(); agent.isStopped = true; }
            Say(NPCVoiceLine.Pleading);
            return true;
        }
        if (_heldBy != by && by != null) return false;
        // Được buông mà kẻ giữ vẫn còn nguy hiểm: chạy khỏi hắn (thấy cảnh sát thì ở lại sợ / chờ hô).
        if (_heldBy != null && !_heldBy.IsDead && !_heldBy.IsSurrendered) MarkThreatNoise(_heldBy.transform.position);
        _heldBy = null;
        if (!IsSurrendered && NPCActionUtils.TryGetAgent(gameObject, out NavMeshAgent a)) a.isStopped = false;
        return true;
    }

    // Lệnh hô của người chơi / đồng đội tới qua Compliance.Order -> ReceiveComplianceOrder (không nghe PlayerShout.Shouted
    // nữa, để không bị tính 2 lần).
    private void OnEnable()
    {
        Active.Add(this);
        NPCHearing.AnyHeard += OnHeard;
    }

    private void OnDisable()
    {
        Active.Remove(this);
        NPCHearing.AnyHeard -= OnHeard;
    }

    /// <summary>Nghe súng / nổ: rất gần thì ngồi ôm đầu, xa hơn (trong FleeHearRadius) thì ghi lại để bỏ chạy.</summary>
    private void OnHeard(NPCHearing hearing, HeardNoise noise)
    {
        if (hearing == null || hearing.gameObject != gameObject || !noise.IsViolent || IsDead) return;
        Vector3 d = noise.Position - transform.position;
        float dist = new Vector2(d.x, d.z).magnitude;
        float cower = noise.Type == NoiseType.Explosion ? _explosionCowerRadius : _cowerRadius;
        if (dist <= cower) _lastCloseViolence = Time.time;
        else if (dist <= _fleeHearRadius) MarkThreatNoise(noise.Position);
    }

    private void MarkThreatNoise(Vector3 from)
    {
        _fleeFrom = from;
        _fleeNoiseTime = Time.time;
    }

    private void Reset() => _behaviorAgent = GetComponent<BehaviorGraphAgent>();

    private void Awake()
    {
        if (_behaviorAgent == null) _behaviorAgent = GetComponent<BehaviorGraphAgent>();
        _animator = GetComponentInChildren<Animator>();
        _body = GetComponent<CapsuleCollider>();
        if (_voice == null && !TryGetComponent(out _voice)) _voice = gameObject.AddComponent<NPCVoice>();
        _health = _maxHealth;
        _shoutsNeeded = Random.Range(_shoutsToComply, Mathf.Max(_shoutsToComply, _shoutsToComplyMax) + 1);
        ScheduleNextHelp();
        NPCWorldListener.Ensure(); // tiếng súng người chơi / tiếng cửa -> NPCHearing
    }

    private void Start() => SyncWithBlackboard();

    private void Update()
    {
        if (IsDead) return;
        UpdatePerception();

        if (_heldBy != null && !IsHeld) SetHeld(false, null); // người giữ chết / đầu hàng: tự do
        if (IsHeld)
        {
            _lastSeenTime = Time.time; // bị khống chế: luôn sợ
            if (NPCActionUtils.TryGetAgent(gameObject, out NavMeshAgent agent) && !agent.isStopped) { agent.ResetPath(); agent.isStopped = true; }
            if (Time.time >= _nextHeldVoice)
            {
                _nextHeldVoice = Time.time + Random.Range(4f, 7f);
                if (!Say(NPCVoiceLine.Pleading)) Say(NPCVoiceLine.Scared);
            }
        }

        if (_scaredAnim && _faceThreat && !IsSurrendered) FaceThreat();

        // Còng xong một lúc: "Thank god you're here!" (một lần; đang nói câu khác thì thử lại, tối đa 6 s).
        if (_rescuedAt > 0f && Time.time >= _rescuedAt)
        {
            if (Say(NPCVoiceLine.Rescued) || Time.time > _rescuedAt + 6f) _rescuedAt = -1f;
        }
    }

    // ---------- Nhận biết người chơi ----------

    private void UpdatePerception()
    {
        if (Time.time < _nextPerception) return;
        _nextPerception = Time.time + PerceptionInterval;
        // Đồng đội (cảnh sát AI) cũng làm con tin sợ / đứng lại chờ lệnh như người chơi.
        _seesTeammate = false;
        if (!IsSurrendered)
            foreach (Teammate mate in Teammate.Active)
                if (mate != null && (NPCConditionUtils.CanSee(gameObject, mate.gameObject, _viewDistance, _viewAngle)
                                     || NPCConditionUtils.IsWithin(transform.position, mate.transform.position, _senseDistance)))
                {
                    _seesTeammate = true;
                    _seenPolice = mate.gameObject;
                    _lastSeenTime = Time.time;
                    break;
                }
        if (IsSurrendered || _target == null) { CanSeeTarget = false; return; }

        CanSeeTarget = NPCConditionUtils.CanSee(gameObject, _target, _viewDistance, _viewAngle)
                       || NPCConditionUtils.IsWithin(transform.position, _target.transform.position, _senseDistance);
        if (CanSeeTarget)
        {
            _seenPolice = _target;
            _lastSeenTime = Time.time;
            if (_seenSince < 0f) _seenSince = Time.time;
            if (_autoComplyTime > 0f && Time.time - _seenSince >= _autoComplyTime) ComplyOrdered = true;
        }
        else
        {
            _seenSince = -1f;
        }

        bool scared = IsScared;
        if (scared && !_wasScared)
        {
            Say(NPCVoiceLine.Scared);
            ScheduleScaredVoice();
            OnScared?.Invoke();
        }
        else if (scared && !ComplyOrdered && Time.time >= _nextScaredVoice)
        {
            // Sợ lâu: thỉnh thoảng kêu / rên lại (đang nói câu khác thì để lượt sau).
            if (_voice == null || !_voice.IsSpeaking) Say(NPCVoiceLine.Scared);
            ScheduleScaredVoice();
        }
        _wasScared = scared;
    }

    /// <summary>Bị đe dọa (nổ gần, đạn sượt qua...): sợ ngay như khi thấy người chơi.</summary>
    public void NotifyThreat()
    {
        if (IsDead || IsSurrendered) return;
        _lastSeenTime = Time.time;
    }

    /// <summary>NPCStun báo vừa bị choáng: sợ, kêu lên.</summary>
    public void NotifyStunned()
    {
        if (IsDead || IsSurrendered) return;
        _lastSeenTime = Time.time;
        Say(NPCVoiceLine.Scared);
    }

    /// <summary>
    /// Bị hô "Police! Get on the ground!" (người chơi hoặc đồng đội, qua Compliance.Order đã lọc khoảng cách / tường).
    /// Như SWAT 4: phải hô đủ số lần (random ShoutsToComply..ShoutsToComplyMax) mới giơ tay; đang bị choáng thì giơ tay
    /// ngay. Trả true nếu lần này chịu giơ tay.
    /// </summary>
    public bool ReceiveComplianceOrder(Vector3 issuerPosition)
    {
        if (IsDead || IsSurrendered || ComplyOrdered) return false;
        NotifyThreat();
        if (IsHeld)
        {
            Say(NPCVoiceLine.Pleading); // đang bị khống chế: không dám giơ tay
            return false;
        }
        bool stunned = NPCStun.IsStunnedObject(gameObject);
        if (stunned || ++_shoutsHeard >= _shoutsNeeded)
        {
            ComplyOrdered = true;
            // Graph đang dừng vì choáng: tự giơ tay luôn (graph chạy lại sẽ vào nhánh chờ còng).
            if (stunned) Surrender();
            return true;
        }
        if (!Say(NPCVoiceLine.Pleading)) Say(NPCVoiceLine.Scared); // chưa chịu giơ tay: van xin / hoảng lên
        return false;
    }

    private void FaceThreat()
    {
        GameObject threat = _seenPolice != null && _seenPolice.activeInHierarchy ? _seenPolice : _target;
        if (threat == null || _faceThreatSpeed <= 0f) return;
        Vector3 dir = threat.transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), _faceThreatSpeed * Time.deltaTime);
    }

    // ---------- Các hành động (Action của graph gọi) ----------

    /// <summary>Bật / tắt anim co người sợ hãi. faceThreat: quay mặt về phía người chơi (ngồi ôm đầu vì súng thì không).</summary>
    public void SetScared(bool value, bool faceThreat = true)
    {
        _scaredAnim = value && !IsSurrendered && !IsDead;
        _faceThreat = faceThreat;
        if (_animator != null) _animator.SetBool(ScaredHash, _scaredAnim);
    }

    /// <summary>Quỳ xuống gọi cứu (anim + tiếng). Trả thời gian kêu cứu.</summary>
    public float CallForHelp()
    {
        ScheduleNextHelp();
        if (IsDead || IsSurrendered) return 0f;
        if (_animator != null) _animator.SetTrigger(CallHelpHash);
        Say(NPCVoiceLine.Help);
        return _callForHelpDuration;
    }

    private void ScheduleNextHelp() =>
        _nextHelpTime = Time.time + Random.Range(_helpInterval.x, Mathf.Max(_helpInterval.x, _helpInterval.y));

    private void ScheduleScaredVoice() =>
        _nextScaredVoice = Time.time + Random.Range(_scaredVoiceInterval.x, Mathf.Max(_scaredVoiceInterval.x, _scaredVoiceInterval.y));

    /// <summary>Giơ tay đầu hàng: dừng di chuyển, quỳ giơ tay, chờ còng. Trả false nếu đã chết / đã đầu hàng.</summary>
    public bool Surrender()
    {
        if (IsDead || IsSurrendered) return false;
        IsSurrendered = true;
        ComplyOrdered = true;
        CanSeeTarget = false;
        SetScared(false);

        StopMoving();
        if (_animator != null)
        {
            _animator.SetFloat(MoveXHash, 0f);
            _animator.SetFloat(MoveYHash, 0f);
            _animator.SetBool(SurrenderedHash, true);
            _animator.SetTrigger(SurrenderHash);
        }
        ShrinkBody();
        Say(NPCVoiceLine.Comply);
        OnSurrendered?.Invoke();
        return true;
    }

    /// <summary>Còng tay con tin đã đầu hàng (HostageCuffable gọi). Trả false nếu chưa đầu hàng / đã còng / đã chết.</summary>
    public bool Restrain()
    {
        if (IsDead || !IsSurrendered || IsRestrained) return false;
        IsRestrained = true;
        if (_animator != null) _animator.SetTrigger(RestrainHash);
        Say(NPCVoiceLine.Cuffed);
        _rescuedAt = Time.time + 2.5f; // sau câu Cuffed: "Thank god you're here!"
        OnRestrained?.Invoke();
        return true;
    }

    /// <summary>Giật mình (nổ gần) khi đang quỳ.</summary>
    public void Flinch()
    {
        if (IsDead || !IsSurrendered) return;
        if (_animator != null) _animator.SetTrigger(FlinchHash);
        Say(NPCVoiceLine.Scared);
    }

    private void StopMoving()
    {
        if (TryGetComponent(out NPCLocomotion locomotion)) locomotion.enabled = false;
        if (NPCActionUtils.TryGetAgent(gameObject, out NavMeshAgent agent))
        {
            agent.ResetPath();
            agent.velocity = Vector3.zero;
            agent.isStopped = true;
        }
    }

    private void ShrinkBody()
    {
        if (_body == null) return;
        _body.height = Mathf.Min(_body.height, _kneelColliderHeight);
        _body.center = new Vector3(_body.center.x, _body.height * 0.5f, _body.center.z);
    }

    /// <summary>Nói một câu (NPCVoice lo ưu tiên, cooldown, không lặp clip).</summary>
    private bool Say(NPCVoiceLine line) => _voice != null && _voice.Play(line);

    // ---------- Sát thương ----------

    /// <summary>Hệ thống cũ: SendMessage("TakeDamage", float) (coi như đạn, vùng random).</summary>
    public void TakeDamage(float amount) => ApplyDamage(new DamageInfo { Amount = amount, Type = DamageType.Bullet });

    /// <summary>Súng / lựu đạn của người chơi (IDamageable): quả VRGrenade vừa nổ gần đó = nổ, không thì đạn (vùng trúng thật).</summary>
    void IDamageable.ApplyDamage(float damage, RaycastHit hit)
    {
        if (DamageSources.TryFindExplodingGrenade(hit.point, out Vector3 center))
        {
            ApplyDamage(DamageInfo.Explosion(damage, center));
            return;
        }
        Transform head = Teammate.PlayerHead;
        Vector3 from = head != null ? head.position : _target != null ? _target.transform.position : hit.point + hit.normal;
        ApplyDamage(DamageInfo.Bullet(damage, from).WithPoint(hit.point));
    }

    /// <summary>Nhận sát thương (hệ thống mới): vùng trúng (đầu x3, tay chân x0.6), con tin không có giáp.</summary>
    public void ApplyDamage(DamageInfo info)
    {
        if (IsDead || info.Amount <= 0f) return;
        if (!info.ZoneKnown)
            info = info.WithZone(info.HasPoint && info.Type == DamageType.Bullet ? DamageModel.ZoneOf(gameObject, info.Point)
                : info.Type == DamageType.Bullet ? DamageModel.RollAimedZone(0.5f) : HitZone.Torso);
        LastDamage = info;
        ApplyDamage(DamageModel.Resolve(info, 0f, 0f), info.HasFrom ? info.From : (Vector3?)null);
    }

    /// <summary>Lần trúng gần nhất.</summary>
    public DamageInfo LastDamage { get; private set; }

    private void ApplyDamage(float amount, Vector3? hitFrom)
    {
        if (IsDead || amount <= 0f) return;
        _health = Mathf.Max(0f, _health - amount);
        NotifyThreat();
        if (!IsDead)
        {
            Say(NPCVoiceLine.Hurt);
            return;
        }

        // Chết: tắt graph, di chuyển, collider. Animator: Surrendered chọn anim chết đứng / quỳ.
        Debug.Log($"[Hostage] {name} chết ({LastDamage}).", this);
        if (_behaviorAgent != null && _behaviorAgent.enabled) _behaviorAgent.End();
        if (_behaviorAgent != null) _behaviorAgent.enabled = false;
        SetScared(false);
        StopMoving();
        if (TryGetComponent(out NavMeshAgent agent)) agent.enabled = false;
        foreach (Collider col in GetComponentsInChildren<Collider>()) col.enabled = false;
        Say(NPCVoiceLine.Death); // ưu tiên cao nhất: cắt mọi câu đang nói, sau đó im hẳn

        bool fromBack = hitFrom.HasValue && transform.InverseTransformPoint(hitFrom.Value).z < 0f;
        if (_animator != null) _animator.SetTrigger(fromBack ? DieBackHash : DieFrontHash);
        OnDied?.Invoke();
    }

    // ---------- Blackboard ----------

    private void SyncWithBlackboard()
    {
        _wayPoints.RemoveAll(w => w == null);
        if (TryGetVariable(WayPointsVar, out BlackboardVariable wayPoints))
        {
            if (wayPoints.ObjectValue is List<GameObject> list && list.Exists(w => w != null))
                _wayPoints = list.FindAll(w => w != null);
            Set(WayPointsVar, new List<GameObject>(_wayPoints));
        }

        if (TryGetVariable(TargetVar, out BlackboardVariable target)
            && target.ObjectValue is GameObject go && go != null && go.scene.IsValid())
            _target = go;
        if (_target == null && !string.IsNullOrEmpty(_targetTag)) _target = GameObject.FindWithTag(_targetTag);
        if (target != null) Set(TargetVar, _target);

        if (_target == null)
            Debug.LogWarning($"[Hostage] {name}: chưa có Target (không tìm thấy object tag '{_targetTag}').", this);
        if (TryGetComponent(out NavMeshAgent agent) && agent.enabled && !agent.isOnNavMesh)
            Debug.LogWarning($"[Hostage] {name}: không đứng trên NavMesh nên không di chuyển được. Hãy bake NavMesh cho scene.", this);
    }

    private bool Set<T>(string variable, T value) => _behaviorAgent != null && _behaviorAgent.SetVariableValue(variable, value);

    private bool TryGetVariable(string variable, out BlackboardVariable value)
    {
        value = null;
        return _behaviorAgent != null && _behaviorAgent.GetVariable(variable, out value);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 pos = transform.position;
        Gizmos.color = Color.yellow;
        Vector3 eye = pos + Vector3.up * NPCConditionUtils.EyeHeight;
        Vector3 left = Quaternion.Euler(0f, -_viewAngle * 0.5f, 0f) * transform.forward;
        Vector3 right = Quaternion.Euler(0f, _viewAngle * 0.5f, 0f) * transform.forward;
        Gizmos.DrawLine(eye, eye + left * _viewDistance);
        Gizmos.DrawLine(eye, eye + right * _viewDistance);
        Gizmos.DrawWireSphere(pos, _senseDistance);

        Gizmos.color = Color.green;
        for (int i = 0; i < _wayPoints.Count; i++)
        {
            if (_wayPoints[i] == null) continue;
            GameObject next = _wayPoints[(i + 1) % _wayPoints.Count];
            Gizmos.DrawSphere(_wayPoints[i].transform.position, 0.2f);
            if (next != null) Gizmos.DrawLine(_wayPoints[i].transform.position, next.transform.position);
        }
    }
}
