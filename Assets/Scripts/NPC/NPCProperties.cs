using System.Collections;
using System.Collections.Generic;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.Serialization;
using VRSwat.Tutorial;

/// <summary>
/// Thành phần chính của NPC: giữ máu và đồng bộ thông số với Blackboard của Behavior Graph
/// để Conditions / Actions đọc chung một nguồn.
///
/// Blackboard (giá trị gán trên BehaviorGraphAgent) là NGUỒN CHÍNH: khi chạy, NPC đọc AttackRange, Damage,
/// ViewDistance, ViewAngle, HideThreshold, SearchRadius, CoverTag, WayPoints từ Blackboard.
/// Giá trị trong Inspector của NPC chỉ dùng khi Blackboard thiếu biến đó (hoặc WayPoints không có phần tử nào khác None).
/// NPC chỉ GHI vào Blackboard: Health (máu hiện tại) và Target (khi Blackboard chưa có object trong scene).
///
/// Đầu hàng (Surrender): tắt Behavior Graph, thả súng, quỳ giơ tay (anim complyXXX). Khi đã đầu hàng, NPC tự xử lý
/// trúng đạn / chết (graph đã tắt). Restrain = bị còng. Có NPCMorale thì morale quyết định khi nào đầu hàng
/// và cách đánh (nấp nhiều hay ít, bắn chuẩn hay không).
/// </summary>
[RequireComponent(typeof(BehaviorGraphAgent), typeof(NavMeshAgent), typeof(NPCLocomotion))]
public class NPC : MonoBehaviour, IDamageable
{
    public const string HealthVar = "Health";
    public const string TargetVar = "Target";
    public const string WayPointsVar = "WayPoints";
    public const string IndexVar = "Index";
    public const string AttackRangeVar = "AttackRange";
    public const string DamageVar = "Damage";
    public const string ViewDistanceVar = "ViewDistance";
    public const string ViewAngleVar = "ViewAngle";
    public const string HideThresholdVar = "HideThreshold";
    public const string SearchRadiusVar = "SearchRadius";
    public const string CoverTagVar = "CoverTag";

    private static readonly int HitFrontHash = Animator.StringToHash("HitFront");
    private static readonly int HitBackHash = Animator.StringToHash("HitBack");
    private static readonly int HitLeftHash = Animator.StringToHash("HitLeft");
    private static readonly int HitRightHash = Animator.StringToHash("HitRight");

    [Header("Health")]
    [FormerlySerializedAs("_health")]
    [SerializeField] private float _maxHealth = 100f;
    [Tooltip("Máu hồi mỗi giây khi không bị bắn trong RegenDelay giây. 0 = không hồi.")]
    [SerializeField] private float _regenPerSecond;
    [SerializeField] private float _regenDelay = 5f;

    [Header("Target")]
    [Tooltip("Để trống: tự tìm object có tag TargetTag (vd. XR Origin của người chơi).")]
    [SerializeField] private GameObject _target;
    [SerializeField] private string _targetTag = "Player";

    [Header("Patrol (dùng khi WayPoints trên Blackboard rỗng)")]
    [SerializeField] private List<GameObject> _wayPoints = new();

    [Header("Combat (dùng khi Blackboard thiếu biến)")]
    [SerializeField] private float _attackRange = 10f;
    [SerializeField] private float _damage = 10f;
    [FormerlySerializedAs("_detectionRange")]
    [SerializeField] private float _viewDistance = 15f;
    [SerializeField, Range(1f, 360f)] private float _viewAngle = 120f;

    [Header("Cover (dùng khi Blackboard thiếu biến)")]
    [SerializeField] private float _hideThreshold = 50f;
    [SerializeField] private float _searchRadius = 10f;
    [SerializeField] private string _coverTag = "Cover";

    [Header("Awareness")]
    [Tooltip("Mất dấu Target quá số giây này thì thôi giao chiến, quay về tuần tra.")]
    [SerializeField] private float _alertMemory = 8f;

    [Header("Cover combat")]
    [Tooltip("Số phát mỗi lần đứng dậy bắn từ chỗ nấp (random trong khoảng, gồm cả 2 đầu).")]
    [SerializeField] private Vector2Int _coverShots = new(2, 4);
    [Tooltip("Thời gian ngồi nấp (giây) giữa 2 lần đứng dậy bắn (random trong khoảng).")]
    [SerializeField] private Vector2 _coverHideTime = new(3f, 5f);
    [Tooltip("Thời gian ngồi xuống lúc mới tới chỗ nấp, đủ để anim Crouch chạy hết 1 lần rồi mới đứng dậy bắn.")]
    [SerializeField] private float _crouchSettleTime = 0.6f;
    [Tooltip("Thời gian từ lúc đứng dậy tới phát bắn đầu tiên (chờ anim đứng lên).")]
    [SerializeField] private float _standUpTime = 0.4f;

    [Header("Surrender")]
    [Tooltip("Giây từ lúc bắt đầu anim đầu hàng (đứng, sDropWeapon1) tới lúc súng rời tay.")]
    [SerializeField] private float _dropWeaponTimeStanding = 1.5f;
    [Tooltip("Giây từ lúc bắt đầu anim đầu hàng (đang ngồi, cDropWeapon1) tới lúc súng rời tay.")]
    [SerializeField] private float _dropWeaponTimeCrouching = 1.2f;
    [Tooltip("Chiều cao collider thân khi quỳ đầu hàng.")]
    [SerializeField] private float _kneelColliderHeight = 1.1f;
    [Tooltip("Bị giết khi đã đầu hàng: hủy object sau số giây này (giống DestroyDelay của node Die).")]
    [SerializeField] private float _destroyDelayWhenSurrendered = 5f;

    [Header("Events")]
    public UnityEvent<float> OnDamaged;
    public UnityEvent OnDied;
    public UnityEvent OnSurrendered;
    public UnityEvent OnRestrained;

    [SerializeField] private BehaviorGraphAgent _behaviorAgent;

    private const float PerceptionInterval = 0.1f;
    private const float InCoverDistance = 1f;
    private const float CoverProtectAngle = 110f;
    private static readonly int IsCrouchingHash = Animator.StringToHash("IsCrouching");
    private static readonly int SurrenderHash = Animator.StringToHash("Surrender");
    private static readonly int SurrenderedHash = Animator.StringToHash("Surrendered");
    private static readonly int RestrainHash = Animator.StringToHash("Restrain");
    private static readonly int FlinchHash = Animator.StringToHash("Flinch");
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");

    private Animator _animator;
    private NPCWeaponHolder _weaponHolder;
    private NPCMorale _morale;
    private CapsuleCollider _body;
    private bool _surrenderPending;
    private float _health;
    private float _lastHitTime = float.NegativeInfinity;
    private float _lastSeenTime = float.NegativeInfinity;
    private float _nextPerception;
    private CoverSpot _cover;
    private bool _crouching;
    private bool _crouchInPlace;

    public float Health => _health;
    public float MaxHealth => _maxHealth;
    public bool IsDead => _health <= 0f;
    public GameObject Target => _target;
    public List<GameObject> WayPoints => _wayPoints;
    public float AttackRange => _attackRange;
    public float Damage => _damage;
    public float ViewDistance => _viewDistance;
    public float ViewAngle => _viewAngle;
    public float HideThreshold => _hideThreshold;
    public float SearchRadius => _searchRadius;
    public string CoverTag => _coverTag;

    /// <summary>Waypoint đang đi tới, do GetNextWayPoint ghi và MoveToWayPoint đọc khi ô chưa nối Blackboard.</summary>
    public GameObject CurrentWayPoint { get; set; }

    /// <summary>Hướng trúng đòn cuối ("Front"/"Back"/"Left"/"Right"), DieAction dùng để chọn anim chết.</summary>
    public string LastHitSide { get; private set; } = "Front";

    // ---------- Nhận biết Target ----------

    /// <summary>Đang nhìn thấy Target (cập nhật mỗi 0.1s).</summary>
    public bool CanSeeTarget { get; private set; }

    /// <summary>Đang giao chiến: vừa thấy Target (hoặc vừa bị bắn) trong AlertMemory giây.</summary>
    public bool IsAlerted => !IsDead && _target != null && Time.time - _lastSeenTime <= _alertMemory;

    /// <summary>Vị trí Target lần cuối nhìn thấy. Khi mất dấu, Chase chạy tới đây.</summary>
    public Vector3 LastKnownTargetPosition { get; private set; }

    /// <summary>Vị trí nên nhắm tới: vị trí thật nếu đang thấy, không thì vị trí lần cuối thấy.</summary>
    public Vector3 ThreatPosition => CanSeeTarget && _target != null ? _target.transform.position : LastKnownTargetPosition;

    /// <summary>Bị đe dọa (bị hô, đạn sượt qua, nổ gần): biết Target đang ở đâu và vào giao chiến.</summary>
    public void NotifyThreat() => Alert();

    // ---------- Morale / đầu hàng ----------

    public NPCMorale MoraleComponent => _morale;

    /// <summary>Không có NPCMorale thì coi như luôn Aggressive (hành vi cũ).</summary>
    public MoraleState MoraleState => _morale != null ? _morale.State : MoraleState.Aggressive;

    /// <summary>Hệ số độ chính xác theo morale (AttackAction nhân vào Accuracy).</summary>
    public float AccuracyScale => _morale != null ? _morale.AccuracyScale : 1f;

    public bool IsSurrendered { get; private set; }
    public bool IsRestrained { get; private set; }

    public void ForgetTarget()
    {
        _lastSeenTime = float.NegativeInfinity;
        CanSeeTarget = false;
    }

    // ---------- Nấp ----------

    public float StandUpTime => _standUpTime;

    /// <summary>Số phát cho một lần đứng dậy bắn từ chỗ nấp. Morale thấp: bắn ít hơn.</summary>
    public int RollCoverShots()
    {
        int shots = Random.Range(Mathf.Max(1, _coverShots.x), Mathf.Max(_coverShots.x, _coverShots.y) + 1);
        return MoraleState switch
        {
            MoraleState.Defensive => Mathf.Max(1, shots - 1),
            MoraleState.Panicked => Random.Range(1, 3),
            _ => shots,
        };
    }

    /// <summary>Thời gian ngồi nấp trước lần đứng dậy kế tiếp. Morale thấp: nấp lâu hơn.</summary>
    public float RollCoverHideTime()
    {
        float time = Random.Range(_coverHideTime.x, Mathf.Max(_coverHideTime.x, _coverHideTime.y));
        return MoraleState switch
        {
            MoraleState.Defensive => time * 1.5f,
            MoraleState.Panicked => time * 2.2f,
            _ => time,
        };
    }

    /// <summary>Thời điểm sớm nhất được đứng dậy bắn khi đang núp.</summary>
    public float CoverReadyTime { get; set; }

    public CoverSpot Cover => _cover;

    /// <summary>Đang đứng ở chỗ nấp và vật che vẫn chắn giữa NPC và Target.</summary>
    public bool IsInCover =>
        _cover.Cover != null && _cover.Cover.enabled && _cover.Cover.gameObject.activeInHierarchy
        && FlatDistance(transform.position, _cover.Point) <= InCoverDistance
        && TakeCoverAction.Protects(_cover.Cover, transform.position, ThreatPosition, CoverProtectAngle);

    /// <summary>Vừa tới chỗ nấp: ngồi xuống (anim Crouch chạy 1 lần), chờ CrouchSettleTime rồi mới được đứng dậy bắn.</summary>
    public void EnterCover(CoverSpot spot)
    {
        _cover = spot;
        _crouchInPlace = false;
        CoverReadyTime = Time.time + _crouchSettleTime;
        SetCrouch(true);
    }

    public void LeaveCover()
    {
        _cover = default;
        _crouchInPlace = false;
        SetCrouch(false);
    }

    /// <summary>Không có chỗ nấp nào (hết đạn, hết bom): ngồi thụp tại chỗ cho tới khi LeaveCover / EnterCover.</summary>
    public void CrouchInPlace()
    {
        _crouchInPlace = true;
        SetCrouch(true);
    }

    /// <summary>Chỉ đổi bool khi giá trị thay đổi, để mỗi lần nấp anim Crouch chỉ vào 1 lần.</summary>
    public void SetCrouch(bool value)
    {
        if (_crouching == value) return;
        _crouching = value;
        if (_animator != null) _animator.SetBool(IsCrouchingHash, value);
    }

    /// <summary>Mọi NPC đang bật trong scene (bom dùng để tránh / gây sát thương cho NPC kể cả khi NPC không có collider).</summary>
    public static readonly List<NPC> Active = new();

    private void OnEnable() => Active.Add(this);
    private void OnDisable() => Active.Remove(this);

    private void Reset() => _behaviorAgent = GetComponent<BehaviorGraphAgent>();

    private void Awake()
    {
        if (_behaviorAgent == null) _behaviorAgent = GetComponent<BehaviorGraphAgent>();
        _animator = GetComponentInChildren<Animator>();
        _weaponHolder = GetComponentInChildren<NPCWeaponHolder>();
        _morale = GetComponent<NPCMorale>();
        _body = GetComponent<CapsuleCollider>();
        _health = _maxHealth;
    }

    private void Start() => SyncWithBlackboard();

    private void Update()
    {
        if (IsDead || IsSurrendered) return;

        // Đang rút / ném bom thì chờ ném xong mới đầu hàng (không để bom kẹt trên tay).
        if (_surrenderPending)
        {
            Surrender();
            if (IsSurrendered) return;
        }

        UpdatePerception();

        // Rời chỗ nấp (bị đẩy đi, Target vòng ra sau vật che...) thì đứng dậy.
        if (_cover.Cover != null && !IsInCover) LeaveCover();
        else if (_crouching && _cover.Cover == null && !_crouchInPlace) SetCrouch(false);

        if (_regenPerSecond <= 0f || _health >= _maxHealth || Time.time - _lastHitTime < _regenDelay)
            return;

        _health = Mathf.Min(_maxHealth, _health + _regenPerSecond * Time.deltaTime);
        Set(HealthVar, _health);
    }

    private void UpdatePerception()
    {
        if (Time.time < _nextPerception) return;
        _nextPerception = Time.time + PerceptionInterval;

        CanSeeTarget = _target != null && NPCConditionUtils.CanSee(gameObject, _target, _viewDistance, _viewAngle);
        if (CanSeeTarget) Alert();
    }

    private void Alert()
    {
        if (_target == null) return;
        _lastSeenTime = Time.time;
        LastKnownTargetPosition = _target.transform.position;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y = 0f;
        return Vector3.Distance(a, b);
    }

    /// <summary>Gọi qua SendMessage("TakeDamage", float) khi không biết hướng bắn.</summary>
    public void TakeDamage(float amount) => ApplyDamage(amount, null);

    /// <summary>Gọi khi biết vị trí phát bắn, để phát anim trúng đạn / chết đúng hướng.</summary>
    /// <remarks>Đặt tên khác TakeDamage để SendMessage("TakeDamage") không bị nhầm overload.</remarks>
    public void TakeDamageFrom(float amount, Vector3 hitFrom) => ApplyDamage(amount, hitFrom);

    /// <summary>Súng / lựu đạn của người chơi (IDamageable). Hướng bắn lấy từ vị trí người chơi.</summary>
    void IDamageable.ApplyDamage(float damage, RaycastHit hit)
    {
        Vector3 from = _target != null ? _target.transform.position : hit.point + hit.normal;
        ApplyDamage(damage, from);
    }

    // ---------- Đầu hàng ----------

    /// <summary>
    /// Đầu hàng: dừng Behavior Graph, đứng yên, thả súng, quỳ giơ tay.
    /// Trả false nếu đã chết / đã đầu hàng. Đang ném bom thì hoãn tới khi ném xong (vẫn trả true).
    /// </summary>
    public bool Surrender()
    {
        if (IsDead || IsSurrendered) return false;
        if (_weaponHolder != null && _weaponHolder.IsThrowing)
        {
            _surrenderPending = true;
            return true;
        }
        _surrenderPending = false;
        IsSurrendered = true;

        // Tắt graph: từ giờ NPC tự lo anim trúng đạn / chết.
        if (_behaviorAgent != null) _behaviorAgent.enabled = false;

        if (TryGetComponent(out NPCLocomotion locomotion)) locomotion.enabled = false;
        if (NPCActionUtils.TryGetAgent(gameObject, out NavMeshAgent agent))
        {
            agent.ResetPath();
            agent.velocity = Vector3.zero;
            agent.isStopped = true;
        }

        bool crouched = _crouching;
        _cover = default;
        _crouchInPlace = false;

        if (_weaponHolder != null) _weaponHolder.PrepareSurrender();

        if (_animator != null)
        {
            _animator.SetFloat(MoveXHash, 0f);
            _animator.SetFloat(MoveYHash, 0f);
            _animator.SetBool(SurrenderedHash, true);
            _animator.SetTrigger(SurrenderHash); // IsCrouching chọn anim đứng (sDropWeapon1) hay ngồi (cDropWeapon1)
        }
        StartCoroutine(DropWeaponAfter(crouched ? _dropWeaponTimeCrouching : _dropWeaponTimeStanding));

        if (_body != null)
        {
            _body.height = Mathf.Min(_body.height, _kneelColliderHeight);
            _body.center = new Vector3(_body.center.x, _body.height * 0.5f, _body.center.z);
        }

        OnSurrendered?.Invoke();
        TutorialSignals.Raise(Sig.NpcSurrendered, this);
        NPCMorale.ReportAllyDown(this, false);
        return true;
    }

    /// <summary>Còng tay NPC đã đầu hàng. Trả false nếu chưa đầu hàng / đã còng / đã chết.</summary>
    public bool Restrain()
    {
        if (IsDead || !IsSurrendered || IsRestrained) return false;
        IsRestrained = true;
        if (_animator != null) _animator.SetTrigger(RestrainHash);
        OnRestrained?.Invoke();
        TutorialSignals.Raise(Sig.NpcRestrained, this);
        return true;
    }

    /// <summary>Giật mình (nổ gần) khi đang quỳ đầu hàng.</summary>
    public void Flinch()
    {
        if (!IsDead && IsSurrendered && _animator != null) _animator.SetTrigger(FlinchHash);
    }

    private IEnumerator DropWeaponAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (_weaponHolder != null) _weaponHolder.DropWeapon();
    }

    public void SetTarget(GameObject target)
    {
        _target = target;
        ForgetTarget();
        Set(TargetVar, _target);
    }

    private void ApplyDamage(float amount, Vector3? hitFrom)
    {
        if (IsDead || amount <= 0f) return;

        _health = Mathf.Max(0f, _health - amount);
        _lastHitTime = Time.time;
        Alert(); // bị bắn thì biết Target ở đâu, kể cả khi đang không nhìn thấy
        LastHitSide = hitFrom.HasValue ? SideOf(hitFrom.Value) : "Front";
        Set(HealthVar, _health);
        OnDamaged?.Invoke(amount);

        if (IsDead)
        {
            OnDied?.Invoke();
            NPCMorale.ReportAllyDown(this, true);
            // Bình thường Behavior Graph (IsDead -> Die) lo phần anim và dọn dẹp. Đã đầu hàng thì graph tắt, tự gọi.
            // Animator: Surrendered = true nên trigger Die* chuyển sang anim complyDie* (chết khi đang quỳ).
            if (IsSurrendered) DieAction.Kill(gameObject, _destroyDelayWhenSurrendered);
            return;
        }

        // Đang thay đạn / rút, ném bom thì không phát anim trúng đạn (cùng layer UpperBody) để anim và model không bị ngắt.
        // Đã đầu hàng: trigger Hit* chạy anim complyHit* / cuffedHit* ở Base Layer (UpperBody bỏ qua khi Surrendered).
        bool reloading = !IsSurrendered && _weaponHolder != null && (_weaponHolder.IsReloading || _weaponHolder.IsThrowing);
        if (_animator != null && !reloading) _animator.SetTrigger(HitTrigger(LastHitSide));
    }

    private string SideOf(Vector3 hitFrom)
    {
        Vector3 local = transform.InverseTransformPoint(hitFrom);
        if (Mathf.Abs(local.x) > Mathf.Abs(local.z)) return local.x > 0f ? "Right" : "Left";
        return local.z >= 0f ? "Front" : "Back";
    }

    private static int HitTrigger(string side) => side switch
    {
        "Back" => HitBackHash,
        "Left" => HitLeftHash,
        "Right" => HitRightHash,
        _ => HitFrontHash,
    };

    private void SyncWithBlackboard()
    {
        var missing = new List<string>();

        // Thông số: lấy từ Blackboard. Giá trị <= 0 hoặc chuỗi rỗng coi như chưa gán, giữ giá trị của NPC.
        PullFloat(AttackRangeVar, ref _attackRange, missing);
        PullFloat(DamageVar, ref _damage, missing);
        PullFloat(ViewDistanceVar, ref _viewDistance, missing);
        PullFloat(ViewAngleVar, ref _viewAngle, missing);
        PullFloat(HideThresholdVar, ref _hideThreshold, missing);
        PullFloat(SearchRadiusVar, ref _searchRadius, missing);

        if (!TryGetVariable(CoverTagVar, out BlackboardVariable coverTag)) missing.Add(CoverTagVar);
        else if (coverTag.ObjectValue is string tag && !string.IsNullOrEmpty(tag)) _coverTag = tag;
        else Set(CoverTagVar, _coverTag);

        // WayPoints: Blackboard của prefab không giữ được object trong scene nên thường là list toàn phần tử None.
        // Chỉ lấy list Blackboard khi có ít nhất 1 waypoint thật, không thì dùng list gán trong Inspector.
        // Luôn bỏ phần tử None và ghi lại Blackboard để graph (ô WayPoints có nối biến) đọc đúng list.
        _wayPoints.RemoveAll(w => w == null);
        if (!TryGetVariable(WayPointsVar, out BlackboardVariable wayPoints)) missing.Add(WayPointsVar);
        else
        {
            if (wayPoints.ObjectValue is List<GameObject> list && list.Exists(w => w != null))
                _wayPoints = list.FindAll(w => w != null);
            Set(WayPointsVar, new List<GameObject>(_wayPoints));
        }

        // Target: dùng object trong scene trên Blackboard; trống (hoặc là prefab asset) thì tìm theo tag rồi ghi lại.
        if (!TryGetVariable(TargetVar, out BlackboardVariable target)) missing.Add(TargetVar);
        else if (target.ObjectValue is GameObject go && go != null && go.scene.IsValid()) _target = go;
        if (_target == null && !string.IsNullOrEmpty(_targetTag)) _target = GameObject.FindWithTag(_targetTag);
        if (target != null) Set(TargetVar, _target);

        // Trạng thái lúc chạy: NPC là nguồn.
        if (!Set(HealthVar, _health)) missing.Add(HealthVar);

        if (missing.Count > 0)
            Debug.LogWarning($"[NPC] {name}: Blackboard thiếu biến (hoặc sai kiểu): {string.Join(", ", missing)}", this);
        if (_target == null)
            Debug.LogWarning($"[NPC] {name}: chưa có Target (không tìm thấy object tag '{_targetTag}').", this);
        if (TryGetComponent(out NavMeshAgent agent) && agent.enabled && !agent.isOnNavMesh)
            Debug.LogWarning($"[NPC] {name}: không đứng trên NavMesh nên không di chuyển được. Hãy bake NavMesh cho scene.", this);
    }

    private bool Set<T>(string variable, T value) => _behaviorAgent != null && _behaviorAgent.SetVariableValue(variable, value);

    // Dùng bản không generic: bản generic không hỗ trợ kiểu UnityEngine.Object (theo tài liệu của BehaviorGraphAgent).
    private bool TryGetVariable(string variable, out BlackboardVariable value)
    {
        value = null;
        return _behaviorAgent != null && _behaviorAgent.GetVariable(variable, out value);
    }

    private void PullFloat(string variable, ref float field, List<string> missing)
    {
        if (!TryGetVariable(variable, out BlackboardVariable v)) missing.Add(variable);
        else if (v.ObjectValue is float value && value > 0f) field = value;
        else Set(variable, field);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 pos = transform.position;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(pos, _attackRange);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(pos, _searchRadius);

        Gizmos.color = Color.yellow;
        Vector3 eye = pos + Vector3.up * NPCConditionUtils.EyeHeight;
        Vector3 left = Quaternion.Euler(0f, -_viewAngle * 0.5f, 0f) * transform.forward;
        Vector3 right = Quaternion.Euler(0f, _viewAngle * 0.5f, 0f) * transform.forward;
        Gizmos.DrawLine(eye, eye + left * _viewDistance);
        Gizmos.DrawLine(eye, eye + right * _viewDistance);

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
