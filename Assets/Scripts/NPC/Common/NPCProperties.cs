using System.Collections;
using System.Collections.Generic;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.Serialization;
using VRSwat.Tutorial;

/// <summary>Phe của NPC. Suspect = địch, Police = đồng đội của người chơi.</summary>
public enum Team
{
    Suspect,
    Police,
}

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
///
/// Phe (Team):
/// - Suspect: Target là người chơi hoặc đồng đội (phe Police) đang nhìn thấy, ưu tiên người chơi
///   (điểm = khoảng cách, người chơi trừ PlayerPriority m; ai vừa bắn / chĩa súng vào nó trong 2 s được chọn trước).
///   Giữ mục tiêu ít nhất TargetHoldTime giây rồi mới đổi.
/// - Police: Target là NPC phe Suspect gần nhất đang nhìn thấy, còn sống, chưa đầu hàng (không tìm tag Player).
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

    [Tooltip("Hệ số nhân sát thương nhận vào (độ khó). 0 = bất tử.")]
    [SerializeField, Min(0f)] private float _damageTakenScale = 1f;
    [Tooltip("Áo giáp: chặn ngần này phần sát thương đạn trúng thân (nổ: một nửa). Đồng đội đặt qua Teammate.")]
    [SerializeField, Range(0f, 1f)] private float _bodyArmor;
    [Tooltip("Mũ: chặn ngần này phần sát thương đạn trúng đầu (trúng đầu x3).")]
    [SerializeField, Range(0f, 1f)] private float _helmet;
    [Tooltip("Chết xong giữ xác ngần này giây rồi mới xóa (SWAT 4: xác nằm lại).")]
    [SerializeField] private float _corpseLifetime = 60f;

    [Header("Target")]
    [Tooltip("Suspect = địch (bắn người chơi và đồng đội). Police = đồng đội (bắn NPC phe Suspect).")]
    [SerializeField] private Team _team = Team.Suspect;
    [Tooltip("Để trống: tự tìm object có tag TargetTag (vd. XR Origin của người chơi). Phe Police bỏ qua.")]
    [SerializeField] private GameObject _target;
    [SerializeField] private string _targetTag = "Player";
    [Tooltip("Phe Suspect: khi chọn giữa người chơi và đồng đội đang thấy, người chơi được trừ ngần này mét.")]
    [SerializeField] private float _playerPriority = 4f;
    [Tooltip("Đã có mục tiêu thì giữ ít nhất ngần này giây rồi mới đổi (không quay qua quay lại).")]
    [SerializeField] private float _targetHoldTime = 1.5f;

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
    private NPCVoice _voice;
    private bool _wasAlerted;
    private CapsuleCollider _body;
    private bool _surrenderPending;
    private float _health;
    private float _lastHitTime = float.NegativeInfinity;
    private float _lastSeenTime = float.NegativeInfinity;
    private float _nextPerception;
    private CoverSpot _cover;
    private bool _crouching;
    private bool _crouchInPlace;
    private GameObject _player;
    private float _targetChosenTime = float.NegativeInfinity;
    private GameObject _lastAttacker;
    private float _lastAttackedTime = float.NegativeInfinity;

    private const float AttackerMemory = 2f;

    /// <summary>Một NPC bất kỳ vừa đầu hàng / bị còng / chết (SquadController nghe để giao việc canh).</summary>
    public static event System.Action<NPC> AnySurrendered, AnyRestrained, AnyDied;

    /// <summary>(NPC, sát thương thực nhận, thông tin) mỗi lần trúng.</summary>
    public static event System.Action<NPC, float, DamageInfo> AnyDamaged;

    private bool _deathHandled;

    public float BodyArmor
    {
        get => _bodyArmor;
        set => _bodyArmor = Mathf.Clamp01(value);
    }

    public float Helmet
    {
        get => _helmet;
        set => _helmet = Mathf.Clamp01(value);
    }

    /// <summary>Lần trúng gần nhất (loại, vùng, hướng).</summary>
    public DamageInfo LastDamage { get; private set; }

    /// <summary>
    /// Đặt máu tối đa (và hồi đầy) lúc khởi tạo, vd. Teammate. Gọi được cả trước NPC.Awake (lúc _health còn 0): không
    /// dựa vào IsDead, chỉ bỏ qua khi NPC đã chết thật; không bao giờ ghi Health = 0 lên Blackboard (graph sẽ chạy Die).
    /// </summary>
    /// <summary>Ghi lại máu hiện tại lên Blackboard (sửa lệch, vd. graph đọc Health cũ).</summary>
    public void RefreshHealthVar() => Set(HealthVar, _health);

    public void SetMaxHealth(float max)
    {
        _maxHealth = Mathf.Max(1f, max);
        if (!_deathHandled) _health = _maxHealth;
        if (_health > 0f) Set(HealthVar, _health);
    }

    public Team Team => _team;
    public bool IsPolice => _team == Team.Police;

    /// <summary>Hệ số nhân sát thương nhận vào. 0 = bất tử.</summary>
    public float DamageTakenScale
    {
        get => _damageTakenScale;
        set => _damageTakenScale = Mathf.Max(0f, value);
    }

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

    /// <summary>NPCStun báo vừa bị choáng: kêu lên ("Ahh, my eyes!"), vẫn nhớ hướng mối đe dọa.</summary>
    public void NotifyStunned()
    {
        if (IsDead || IsSurrendered) return;
        Alert();
        Say(NPCVoiceLine.Stunned);
    }

    /// <summary>Nói một câu qua NPCVoice (không có NPCVoice / chưa có clip thì bỏ qua).</summary>
    public bool Say(NPCVoiceLine line)
    {
        if (_voice == null && !TryGetComponent(out _voice)) return false;
        return _voice.Play(line);
    }

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
        _voice = GetComponent<NPCVoice>();
        _body = GetComponent<CapsuleCollider>();
        _health = _maxHealth;
        NPCWorldListener.Ensure(); // tiếng súng người chơi / tiếng cửa -> NPCHearing
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

        ChooseTarget();
        CanSeeTarget = _target != null && NPCConditionUtils.CanSee(gameObject, _target, _viewDistance, _viewAngle);
        if (CanSeeTarget) Alert();

        // Nghi phạm vừa phát hiện cảnh sát: "Cops!" (đồng đội hô "Contact!" qua Teammate).
        bool alerted = IsAlerted;
        if (alerted && !_wasAlerted && CanSeeTarget && _team == Team.Suspect) Say(NPCVoiceLine.Spotted);
        _wasAlerted = alerted;
    }

    // ---------- Chọn mục tiêu theo phe ----------

    /// <summary>Kẻ vừa bắn / đang chĩa súng vào NPC này (đồng đội gọi mỗi frame khi ngắm, súng người chơi khi bắn trúng).</summary>
    public void NotifyAttackedBy(GameObject attacker)
    {
        if (attacker == null || IsDead) return;
        _lastAttacker = attacker;
        _lastAttackedTime = Time.time;
    }

    private bool IsValidTarget(GameObject go)
    {
        if (go == null || !go.activeInHierarchy) return false;
        if (!go.TryGetComponent(out NPC npc)) return _team == Team.Suspect && !SwatPlayerHealth.IsPlayerDead(go); // người chơi: chỉ địch nhắm, còn sống
        return !npc.IsDead && !npc.IsSurrendered && npc._team != _team;
    }

    private void ChooseTarget()
    {
        bool currentValid = IsValidTarget(_target);
        // Phe Police không có mục tiêu mặc định: mục tiêu chết / đầu hàng thì bỏ.
        if (!currentValid && _team == Team.Police && _target != null) SwitchTarget(null);
        if (currentValid && Time.time - _targetChosenTime < _targetHoldTime) return;

        GameObject best = null;
        float bestScore = float.PositiveInfinity;
        if (_team == Team.Suspect)
        {
            if (_player == null && !string.IsNullOrEmpty(_targetTag)) _player = GameObject.FindWithTag(_targetTag);
            if (_player != null) Consider(_player, -_playerPriority, ref best, ref bestScore);
        }
        foreach (NPC npc in Active)
        {
            if (npc == this || npc._team == _team || npc.IsDead || npc.IsSurrendered) continue;
            Consider(npc.gameObject, 0f, ref best, ref bestScore);
        }

        if (best != null && best != _target) SwitchTarget(best);
        // Phe Suspect: mục tiêu cũ (đồng đội) đã chết mà không thấy ai thì quay về người chơi như trước.
        else if (best == null && !currentValid && _team == Team.Suspect && _player != null && _target != _player) SwitchTarget(_player);
    }

    private void Consider(GameObject candidate, float bonus, ref GameObject best, ref float bestScore)
    {
        if (!NPCConditionUtils.CanSee(gameObject, candidate, _viewDistance, _viewAngle)) return;
        float score = FlatDistance(transform.position, candidate.transform.position) + bonus;
        if (candidate == _lastAttacker && Time.time - _lastAttackedTime <= AttackerMemory) score -= 1000f;
        if (score < bestScore)
        {
            bestScore = score;
            best = candidate;
        }
    }

    /// <summary>Đổi mục tiêu do tự chọn (khác SetTarget: không quên vị trí đã thấy, mục tiêu mới đang được nhìn thấy).</summary>
    private void SwitchTarget(GameObject target)
    {
        _target = target;
        _targetChosenTime = Time.time;
        if (target == null) ForgetTarget();
        Set(TargetVar, _target);
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

    /// <summary>Hệ thống cũ: SendMessage("TakeDamage", float) khi không biết hướng bắn (coi như đạn, vùng random).</summary>
    public void TakeDamage(float amount) => ApplyDamage(new DamageInfo { Amount = amount, Type = DamageType.Bullet });

    /// <summary>Đạn bắn từ hitFrom (vùng trúng random theo thống kê nhắm thân).</summary>
    /// <remarks>Đặt tên khác TakeDamage để SendMessage("TakeDamage") không bị nhầm overload.</remarks>
    public void TakeDamageFrom(float amount, Vector3 hitFrom) => ApplyDamage(DamageInfo.Bullet(amount, hitFrom));

    /// <summary>
    /// Súng / lựu đạn của người chơi (IDamageable, Gun.cs / VRGrenade.cs không sửa). Phân biệt: có quả VRGrenade vừa nổ
    /// gần đó = nổ; không thì đạn, vùng trúng tính từ điểm trúng thật (headshot của người chơi x3).
    /// </summary>
    void IDamageable.ApplyDamage(float damage, RaycastHit hit)
    {
        if (_player == null && !string.IsNullOrEmpty(_targetTag)) _player = GameObject.FindWithTag(_targetTag);
        NotifyAttackedBy(_player);
        if (DamageSources.TryFindExplodingGrenade(hit.point, out Vector3 center))
        {
            ApplyDamage(DamageInfo.Explosion(damage, center, _player));
            return;
        }
        Transform head = Teammate.PlayerHead;
        Vector3 from = head != null ? head.position : _player != null ? _player.transform.position : hit.point + hit.normal;
        ApplyDamage(DamageInfo.Bullet(damage, from, _player).WithPoint(hit.point));
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

        Say(NPCVoiceLine.Comply); // "Don't shoot! I give up!"
        OnSurrendered?.Invoke();
        AnySurrendered?.Invoke(this);
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
        AnyRestrained?.Invoke(this);
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

    /// <summary>
    /// Nhận sát thương (hệ thống mới): vùng trúng (từ điểm trúng, không có thì random theo thống kê) -> hệ số vùng +
    /// giáp (DamageModel.Resolve) -> DamageTakenScale. Hết máu: chết NGAY (Die), không chờ Behavior Graph.
    /// </summary>
    public void ApplyDamage(DamageInfo info)
    {
        if (IsDead || info.Amount <= 0f) return;
        if (!info.ZoneKnown)
            info = info.WithZone(info.HasPoint && info.Type == DamageType.Bullet ? DamageModel.ZoneOf(gameObject, info.Point)
                : info.Type == DamageType.Bullet ? DamageModel.RollAimedZone(0.5f) : HitZone.Torso);
        float amount = DamageModel.Resolve(info, _bodyArmor, _helmet) * _damageTakenScale;
        LastDamage = info;
        if (amount <= 0f) return;

        _health = Mathf.Max(0f, _health - amount);
        _lastHitTime = Time.time;
        Alert(); // bị bắn thì biết Target ở đâu, kể cả khi đang không nhìn thấy
        LastHitSide = info.HasFrom ? SideOf(info.From) : "Front";
        Set(HealthVar, _health);
        OnDamaged?.Invoke(amount);
        AnyDamaged?.Invoke(this, amount, info);

        if (IsDead)
        {
            Die();
            return;
        }

        // Đang thay đạn / rút, ném bom thì không phát anim trúng đạn (cùng layer UpperBody) để anim và model không bị ngắt.
        // Đã đầu hàng: trigger Hit* chạy anim complyHit* / cuffedHit* ở Base Layer (UpperBody bỏ qua khi Surrendered).
        if (_team == Team.Suspect) Say(NPCVoiceLine.Hurt); // đồng đội nói "I'm hit!" qua Teammate
        bool reloading = !IsSurrendered && _weaponHolder != null && (_weaponHolder.IsReloading || _weaponHolder.IsThrowing);
        if (_animator != null && !reloading) _animator.SetTrigger(HitTrigger(LastHitSide));
    }

    /// <summary>
    /// Chết: dừng Behavior Graph ngay (không chờ action đang chạy kết thúc), hạ súng / thả súng, tắt các layer thân trên
    /// (UpperBody không đè lên anim chết), phát anim chết theo hướng trúng đòn (đang quỳ: complyDie*), tắt collider /
    /// agent, giữ xác CorpseLifetime giây. Chỉ chạy một lần.
    /// </summary>
    private void Die()
    {
        if (_deathHandled) return;
        _deathHandled = true;
        Say(NPCVoiceLine.Death); // ưu tiên cao nhất, sau đó NPCVoice im hẳn
        Debug.Log($"[NPC] {name} chết ({LastDamage}, hướng {LastHitSide}).", this);

        if (_behaviorAgent != null && _behaviorAgent.enabled)
        {
            _behaviorAgent.End(); // node đang chạy dọn dẹp (thả cửa, hạ tay...)
            _behaviorAgent.enabled = false;
        }
        if (_weaponHolder != null)
        {
            _weaponHolder.StopAiming();
            _weaponHolder.ReleaseLeftHand();
            if (!IsSurrendered) _weaponHolder.DropWeapon(); // súng rơi xuống đất
        }
        if (_animator != null)
        {
            for (int i = 1; i < _animator.layerCount; i++) _animator.SetLayerWeight(i, 0f);
            _animator.SetFloat(MoveXHash, 0f);
            _animator.SetFloat(MoveYHash, 0f);
        }
        _cover = default;
        _crouchInPlace = false;

        OnDied?.Invoke();
        AnyDied?.Invoke(this);
        NPCMorale.ReportAllyDown(this, true);
        // Animator: Surrendered = true thì trigger Die* chuyển sang anim complyDie* (chết khi đang quỳ).
        DieAction.Kill(gameObject, _corpseLifetime);
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
        // Phe Police không bắn người chơi: Target chỉ là NPC phe Suspect, tự chọn lúc chạy.
        if (_team == Team.Police && !IsValidTarget(_target)) _target = null;
        if (_target == null && _team == Team.Suspect && !string.IsNullOrEmpty(_targetTag)) _target = GameObject.FindWithTag(_targetTag);
        if (target != null) Set(TargetVar, _target);

        // Trạng thái lúc chạy: NPC là nguồn.
        if (!Set(HealthVar, _health)) missing.Add(HealthVar);

        if (missing.Count > 0)
            Debug.LogWarning($"[NPC] {name}: Blackboard thiếu biến (hoặc sai kiểu): {string.Join(", ", missing)}", this);
        if (_target == null && _team == Team.Suspect)
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
