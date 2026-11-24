using System.Collections.Generic;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

/// <summary>
/// Con tin (Hostage): giữ trạng thái + đồng bộ với Behavior Graph "Hostage" (Assets/Behaviour Graphs/Hostage).
///
/// Hành vi (graph chọn theo thứ tự ưu tiên, giống graph của NPC):
///   1. Bị còng (Restrained)      : quỳ, tay sau lưng, đứng yên tới hết màn.
///   2. Đầu hàng (Surrender)      : khi bị người chơi hô (PlayerShout, phím F) -> quỳ giơ tay, chờ còng.
///   3. Sợ hãi (Scared)           : thấy người chơi -> co người ôm đầu, kêu lên, quay về phía người chơi.
///   4. Kêu cứu (CallForHelp)     : chưa thấy ai -> thỉnh thoảng quỳ xuống gọi "Help!".
///   5. Di chuyển (MoveToWayPoint): đi qua các WayPoints (nếu có).
///   6. Đứng chờ (Idle).
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
    [Tooltip("Nhìn thấy người chơi liên tục bao lâu thì tự giơ tay (giây). 0 = chỉ giơ tay khi bị hô.")]
    [SerializeField] private float _autoComplyTime;
    [Tooltip("Chiều cao collider thân khi quỳ.")]
    [SerializeField] private float _kneelColliderHeight = 1.1f;

    [Header("Âm thanh (Audios/Hostage)")]
    [SerializeField] private AudioSource _voice;
    [SerializeField] private AudioClip[] _helpClips;
    [SerializeField] private AudioClip[] _scaredClips;
    [SerializeField] private AudioClip[] _complyClips;
    [SerializeField] private AudioClip[] _cuffedClips;
    [SerializeField] private AudioClip[] _hurtClips;
    [SerializeField] private AudioClip[] _deathClips;

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
    private bool _wasScared;
    private bool _scaredAnim;

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
    public bool NeedsAttention => IsDead || IsSurrendered || ComplyOrdered || IsScared || ShouldCallForHelp;

    private void OnEnable()
    {
        Active.Add(this);
        PlayerShout.Shouted += OnPlayerShout;
    }

    private void OnDisable()
    {
        Active.Remove(this);
        PlayerShout.Shouted -= OnPlayerShout;
    }

    private void Reset() => _behaviorAgent = GetComponent<BehaviorGraphAgent>();

    private void Awake()
    {
        if (_behaviorAgent == null) _behaviorAgent = GetComponent<BehaviorGraphAgent>();
        _animator = GetComponentInChildren<Animator>();
        _body = GetComponent<CapsuleCollider>();
        if (_voice == null) _voice = GetComponent<AudioSource>();
        _health = _maxHealth;
        ScheduleNextHelp();
    }

    private void Start() => SyncWithBlackboard();

    private void Update()
    {
        if (IsDead) return;
        UpdatePerception();

        if (_scaredAnim && !IsSurrendered) FaceThreat();
    }

    // ---------- Nhận biết người chơi ----------

    private void UpdatePerception()
    {
        if (Time.time < _nextPerception) return;
        _nextPerception = Time.time + PerceptionInterval;
        if (IsSurrendered || _target == null) { CanSeeTarget = false; return; }

        CanSeeTarget = NPCConditionUtils.CanSee(gameObject, _target, _viewDistance, _viewAngle)
                       || NPCConditionUtils.IsWithin(transform.position, _target.transform.position, _senseDistance);
        if (CanSeeTarget)
        {
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
            PlayVoice(_scaredClips, true);
            OnScared?.Invoke();
        }
        _wasScared = scared;
    }

    /// <summary>Bị đe dọa (nổ gần, đạn sượt qua...): sợ ngay như khi thấy người chơi.</summary>
    public void NotifyThreat()
    {
        if (IsDead || IsSurrendered) return;
        _lastSeenTime = Time.time;
    }

    private void OnPlayerShout(Vector3 position, float radius, float throughWallsRadius)
    {
        if (IsDead || IsSurrendered || ComplyOrdered) return;
        Vector3 ear = NPCConditionUtils.EyeOf(gameObject);
        if (!NPCConditionUtils.IsWithin(ear, position, radius)) return;
        bool near = NPCConditionUtils.IsWithin(ear, position, throughWallsRadius);
        if (!near && Physics.Linecast(ear, position, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && !(_target != null && hit.transform.IsChildOf(_target.transform)) && !hit.transform.IsChildOf(transform))
            return; // bị tường chắn

        NotifyThreat();
        if (++_shoutsHeard >= _shoutsToComply) ComplyOrdered = true;
    }

    private void FaceThreat()
    {
        if (_target == null || _faceThreatSpeed <= 0f) return;
        Vector3 dir = _target.transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), _faceThreatSpeed * Time.deltaTime);
    }

    // ---------- Các hành động (Action của graph gọi) ----------

    /// <summary>Bật / tắt anim co người sợ hãi.</summary>
    public void SetScared(bool value)
    {
        _scaredAnim = value && !IsSurrendered && !IsDead;
        if (_animator != null) _animator.SetBool(ScaredHash, _scaredAnim);
    }

    /// <summary>Quỳ xuống gọi cứu (anim + tiếng). Trả thời gian kêu cứu.</summary>
    public float CallForHelp()
    {
        ScheduleNextHelp();
        if (IsDead || IsSurrendered) return 0f;
        if (_animator != null) _animator.SetTrigger(CallHelpHash);
        PlayVoice(_helpClips, false);
        return _callForHelpDuration;
    }

    private void ScheduleNextHelp() =>
        _nextHelpTime = Time.time + Random.Range(_helpInterval.x, Mathf.Max(_helpInterval.x, _helpInterval.y));

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
        PlayVoice(_complyClips, true);
        OnSurrendered?.Invoke();
        return true;
    }

    /// <summary>Còng tay con tin đã đầu hàng (HostageCuffable gọi). Trả false nếu chưa đầu hàng / đã còng / đã chết.</summary>
    public bool Restrain()
    {
        if (IsDead || !IsSurrendered || IsRestrained) return false;
        IsRestrained = true;
        if (_animator != null) _animator.SetTrigger(RestrainHash);
        PlayVoice(_cuffedClips, true);
        OnRestrained?.Invoke();
        return true;
    }

    /// <summary>Giật mình (nổ gần) khi đang quỳ.</summary>
    public void Flinch()
    {
        if (!IsDead && IsSurrendered && _animator != null) _animator.SetTrigger(FlinchHash);
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

    private void PlayVoice(AudioClip[] clips, bool interrupt)
    {
        if (_voice == null || clips == null || clips.Length == 0) return;
        if (_voice.isPlaying && !interrupt) return;
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip == null) return;
        _voice.Stop();
        _voice.clip = clip;
        _voice.Play();
    }

    // ---------- Sát thương ----------

    public void TakeDamage(float amount) => ApplyDamage(amount, null);

    void IDamageable.ApplyDamage(float damage, RaycastHit hit)
    {
        Vector3 from = _target != null ? _target.transform.position : hit.point + hit.normal;
        ApplyDamage(damage, from);
    }

    private void ApplyDamage(float amount, Vector3? hitFrom)
    {
        if (IsDead || amount <= 0f) return;
        _health = Mathf.Max(0f, _health - amount);
        NotifyThreat();
        if (!IsDead)
        {
            PlayVoice(_hurtClips, true);
            return;
        }

        // Chết: tắt graph, di chuyển, collider. Animator: Surrendered chọn anim chết đứng / quỳ.
        if (_behaviorAgent != null) _behaviorAgent.enabled = false;
        SetScared(false);
        StopMoving();
        if (TryGetComponent(out NavMeshAgent agent)) agent.enabled = false;
        foreach (Collider col in GetComponentsInChildren<Collider>()) col.enabled = false;
        if (_voice != null) _voice.Stop();
        PlayVoice(_deathClips, true);

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
