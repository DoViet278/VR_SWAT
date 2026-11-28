using System;
using System.Collections.Generic;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using VRSwat.Doors;
using Random = UnityEngine.Random;

/// <summary>Việc đơn lẻ SquadController giao cho một đồng đội (biến OrderType trên Blackboard).</summary>
[BlackboardEnum]
public enum TeammateOrder
{
    Follow,
    MoveTo,
    HoldAim,
    OpenDoor,
    /// <summary>Phá khóa cửa OrderDoor (tới ổ khóa, ngồi phá, xong quay về OrderPos).</summary>
    PickLock,
    ThrowFlash,
    /// <summary>Vào OrderPos (điểm clear) rồi lần lượt chĩa súng vào từng góc phòng.</summary>
    ClearRoom,
    // Chỉ thêm vào cuối (giá trị enum đã lưu trên Blackboard).
    /// <summary>Tới sau lưng OrderTarget (nghi phạm / con tin đã đầu hàng) và còng.</summary>
    Restrain,
    /// <summary>Quỳ ở chân cửa OrderDoor, soi gương dưới khe cửa, báo trong phòng có ai, về OrderPos.</summary>
    Mirror,
}

/// <summary>Mức đe dọa của một nghi phạm (đồng đội chỉ bắn khi đạt Teammate.FireAtThreat).</summary>
public enum ThreatLevel
{
    /// <summary>Chết / đầu hàng / bị còng.</summary>
    None,
    /// <summary>Không có vũ khí.</summary>
    Low,
    /// <summary>Có súng nhưng không chĩa vào ai.</summary>
    Medium,
    /// <summary>Đang chĩa súng vào cảnh sát / người chơi, vừa nổ súng hoặc đang ném bom.</summary>
    High,
}

/// <summary>
/// Đồng đội AI (phe Police). Gắn cùng NPC, NPCLocomotion, NPCWeaponHolder, NPCGrenadeThrower và TeammateGraph.
///
/// - SquadController quyết định KHI NÀO làm gì và gọi GiveOrder(): ghi Order* (cả lên Blackboard), tăng OrderId.
/// - TeammateGraph quyết định LÀM THẾ NÀO: node đang chạy thấy OrderId đổi thì thoát trong vòng 1 frame,
///   làm xong việc thì gọi ReportDone(id, thành công?) để Squad đi bước tiếp.
/// - Giao chiến (theo luật SWAT 4): hô trước, chỉ bắn khi nghi phạm không đầu hàng VÀ có mức đe dọa >= FireAtThreat
///   (mặc định High: đang chĩa súng vào cảnh sát, vừa nổ súng, đang ném bom). Chưa đủ đe dọa thì chĩa súng, hô lại.
///   Không bắn qua người chơi / đồng đội.
/// - Clear phòng: đứng ở điểm clear, lần lượt chĩa súng vào các góc (TeammateRoomCorners trên cửa, không có thì tự dò).
/// - Phá khóa: tới ổ khóa, ngồi phá PickLockTime giây, xong quay về chỗ đứng.
/// - Test trong Inspector (Play mode): tick ô "Test lệnh" để ra lệnh cho cả đội qua SquadController. Ô giữ tick tới khi
///   làm xong mới tự bỏ; bỏ tick giữa chừng = hủy lệnh. Đồng đội được tick là người thực hiện (mở cửa, phá khóa,
///   ném flash), người kia yểm trợ.
/// - Canh nghi phạm: Squad giao GuardTarget khi một NPC đầu hàng, xóa khi bị còng / chết.
/// - Máu / giáp: MaxHealth 150, áo giáp chặn 50% đạn trúng thân, mũ chặn 35% đạn trúng đầu (trúng đầu x3, tay chân x0.6,
///   nổ: giáp chặn một nửa). Invulnerable = bất tử.
///   Đồng đội chết là thua màn (SquadController.OnTeammateDown).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NPC))]
public class Teammate : MonoBehaviour
{
    public const string OrderTypeVar = "OrderType";
    public const string OrderPosVar = "OrderPos";
    public const string OrderLookVar = "OrderLook";
    public const string OrderDoorVar = "OrderDoor";
    public const string OrderIdVar = "OrderId";
    public const string GuardTargetVar = "GuardTarget";
    public const string OrderTargetVar = "OrderTarget";

    [Header("Đội")]
    [Tooltip("0 = A (đi bên trái, người mở cửa). 1 = B (bên phải, người ném flashbang).")]
    [SerializeField, Range(0, 1)] private int _slot;
    [SerializeField] private string _callSign = "Alpha";

    [Header("Máu / giáp (hệ thống sát thương mới: đầu x3, tay chân x0.6, giáp chặn đạn ở thân / đầu)")]
    [Tooltip("Máu tối đa (ghi đè NPC.MaxHealth lúc vào màn).")]
    [SerializeField] private float _maxHealth = 150f;
    [Tooltip("Áo giáp SWAT: chặn ngần này phần sát thương đạn trúng thân (nổ: một nửa).")]
    [SerializeField, Range(0f, 1f)] private float _bodyArmor = 0.5f;
    [Tooltip("Mũ chống đạn: chặn ngần này phần sát thương đạn trúng đầu.")]
    [SerializeField, Range(0f, 1f)] private float _helmet = 0.35f;
    [Tooltip("Nhân sát thương nhận vào (độ khó, 1 = bình thường).")]
    [SerializeField, Min(0f)] private float _damageScale = 1f;
    [Tooltip("Bất tử (bật cho phòng 8-9 của tutorial).")]
    [SerializeField] private bool _invulnerable;
    [Tooltip("Máu dưới tỉ lệ này: lùi về sau lưng người chơi thay vì đứng bắn.")]
    [SerializeField, Range(0f, 1f)] private float _lowHealthRatio = 0.25f;

    [Header("Đi theo người chơi")]
    [Tooltip("Khoảng cách sau lưng người chơi (m).")]
    [SerializeField] private float _followDistance = 1.5f;
    [Tooltip("Lệch trái (A) / phải (B) so với người chơi (m), để 2 người không đứng đè nhau.")]
    [SerializeField] private float _followSideOffset = 0.8f;
    [Tooltip("Xa chỗ đứng hơn mức này thì chạy.")]
    [SerializeField] private float _runDistance = 5f;

    [Header("Giao chiến")]
    [Tooltip("Hô xong chờ bao lâu (giây) rồi mới bắn nếu nghi phạm chưa đầu hàng.")]
    [SerializeField] private float _shoutWait = 0.5f;
    [Tooltip("Khoảng nghỉ tối thiểu giữa 2 lần hô (giây). Mỗi nghi phạm chỉ được hô 1 lần.")]
    [SerializeField] private float _shoutCooldown = 2f;
    [Tooltip("Số viên mỗi loạt (gồm cả 2 đầu).")]
    [SerializeField] private Vector2Int _burst = new(2, 3);
    [SerializeField] private float _fireInterval = 0.12f;
    [Tooltip("Nghỉ giữa 2 loạt (giây).")]
    [SerializeField] private Vector2 _burstPause = new(0.4f, 0.8f);
    [SerializeField, Range(0f, 1f)] private float _accuracy = 0.85f;
    [SerializeField] private float _damage = 35f;
    [Tooltip("Bán kính 'ống' kiểm tra đường đạn (m): người chơi / đồng đội trong ống thì không bắn.")]
    [SerializeField] private float _safetyRadius = 0.3f;
    [Tooltip("Đường đạn bị chắn bởi người chơi / đồng đội: bước ngang ngần này mét.")]
    [SerializeField] private float _sideStep = 1f;

    [Header("Luật nổ súng")]
    [Tooltip("Chỉ bắn nghi phạm có mức đe dọa >= mức này. High = đang chĩa súng vào cảnh sát / vừa nổ súng / đang ném bom.")]
    [SerializeField] private ThreatLevel _fireAtThreat = ThreatLevel.High;
    [Tooltip("Nghi phạm nổ súng trong ngần này giây trước thì vẫn coi là đe dọa cao.")]
    [SerializeField] private float _recentFireMemory = 3f;
    [Tooltip("Nghi phạm chưa đủ đe dọa mà chưa đầu hàng: hô lại sau ngần này giây.")]
    [SerializeField] private float _reShoutInterval = 3f;

    [Header("Clear phòng")]
    [Tooltip("Chĩa súng vào mỗi góc ngần này giây (sau khi đã quay tới).")]
    [SerializeField] private float _cornerDwell = 0.6f;
    [Tooltip("Tối đa bao nhiêu góc mỗi người (ngoài điểm cuối nhìn vào giữa phòng).")]
    [SerializeField, Range(1, 5)] private int _maxCorners = 3;

    [Header("Phá khóa")]
    [Tooltip("Ngồi phá khóa ngần này giây thì cửa hết khóa.")]
    [SerializeField] private float _pickLockTime = 5f;
    [Tooltip("Hiện thanh tiến trình (UnlockProgressBar trong scene) khi phá khóa.")]
    [SerializeField] private bool _showPickProgress = true;

    [Header("Canh nghi phạm")]
    [Tooltip("Đứng cách nghi phạm đã đầu hàng trong khoảng này (m).")]
    [SerializeField] private Vector2 _guardDistance = new(2f, 3f);

    [Header("Còng / soi gương")]
    [Tooltip("Còng một người mất ngần này giây (random trong khoảng).")]
    [SerializeField] private Vector2 _restrainTime = new(2f, 3f);
    [Tooltip("Soi gương dưới khe cửa ngần này giây rồi báo.")]
    [SerializeField] private float _mirrorTime = 2f;

    [Header("Đi theo: hàng một ở chỗ hẹp")]
    [Tooltip("Lối đi hẹp hơn ngần này (m) thì đi hàng một: A sau người chơi, B sau A.")]
    [SerializeField] private float _singleFileWidth = 1.8f;

    [Header("Hô hàng / choáng")]
    [Tooltip("Người khác (nghi phạm, con tin) trong bán kính này nghe đồng đội hô hàng (m).")]
    [SerializeField] private float _shoutRadius = 12f;
    [Tooltip("Trong khoảng này nghe được cả qua tường (m).")]
    [SerializeField] private float _shoutThroughWallsRadius = 4f;
    [Tooltip("Giảm cường độ choáng của flashbang (0 = như dân thường, 1 = miễn nhiễm).")]
    [SerializeField, Range(0f, 1f)] private float _flashResistance = 0.6f;

    [Header("Giọng nói: NPCVoice (bộ giọng Officer). Chưa có clip cho câu nào thì dùng các mảng clip cũ bên dưới")]
    [SerializeField] private NPCVoice _npcVoice;
    [SerializeField] private AudioSource _voice;
    [SerializeField] private AudioClip[] _shoutClips;
    [SerializeField] private AudioClip[] _doorLockedClips;
    [SerializeField] private AudioClip[] _roomClearClips;
    [SerializeField] private AudioClip[] _hitClips;
    [SerializeField] private AudioClip[] _ackClips;
    [SerializeField] private float _hitVoiceCooldown = 3f;

    [Header("Test lệnh (Play mode: tick để ra lệnh, đồng đội NÀY thực hiện; làm xong ô mới tự bỏ tick, bỏ tick = hủy)")]
    [Tooltip("Cửa dùng cho lệnh test. Trống = cửa gần đồng đội này nhất (đủ điểm stack).")]
    [SerializeField] private DoorController _testDoor;
    [SerializeField] private bool _testFollow;
    [SerializeField] private bool _testStackUp;
    [SerializeField] private bool _testOpenDoor;
    [SerializeField] private bool _testPickLock;
    [SerializeField] private bool _testThrowFlash;
    [Tooltip("Xông vào: (phá khóa nếu khóa) -> mở cửa -> vào phòng, clear từng góc.")]
    [SerializeField] private bool _testClear;
    [Tooltip("Như Xông vào nhưng ném lựu đạn choáng trước khi vào.")]
    [SerializeField] private bool _testFlashClear;
    [Tooltip("Điểm cho Move To / Cover / Restrain. Trống = 4 m trước mặt đồng đội này (Restrain: người đầu hàng gần nhất).")]
    [SerializeField] private Transform _testPoint;
    [Tooltip("Còng người đã đầu hàng gần Test Point nhất (đồng đội này còng, người kia canh).")]
    [SerializeField] private bool _testRestrain;
    [Tooltip("Cả đội đi tới Test Point.")]
    [SerializeField] private bool _testMoveTo;
    [Tooltip("Cả đội đứng tại chỗ chĩa súng vào Test Point.")]
    [SerializeField] private bool _testCover;
    [Tooltip("Soi gương dưới cửa Test Door, báo trong phòng có ai.")]
    [SerializeField] private bool _testMirror;
    [Tooltip("Báo cáo tình hình (số nghi phạm / con tin, máu đội).")]
    [SerializeField] private bool _testReport;
    [Tooltip("Trạng thái lệnh test (chỉ để xem).")]
    [SerializeField] private string _testStatus;

    [Header("Events")]
    public UnityEvent<string> OnSay;
    public UnityEvent OnDied;

    /// <summary>Mọi đồng đội đang bật trong scene.</summary>
    public static readonly List<Teammate> Active = new();

    /// <summary>(đồng đội, OrderId, thành công?) khi graph báo xong một việc.</summary>
    public static event Action<Teammate, int, bool> OrderFinished;

    /// <summary>Một đồng đội vừa chết (thua màn).</summary>
    public static event Action<Teammate> Died;

    private NPC _npc;
    private NPCLocomotion _locomotion;
    private BehaviorGraphAgent _agent;
    private Transform _lookPoint;
    private int _finishedId = -1;
    private bool _lastSucceeded;
    private readonly Dictionary<NPC, float> _shouted = new(); // nghi phạm -> lúc hô lần đầu
    private readonly Dictionary<NPC, float> _lastShoutAt = new(); // nghi phạm -> lúc hô gần nhất
    private float _lastShout = float.NegativeInfinity;
    private float _nextHitVoice;
    private TestCmd _activeTest = TestCmd.None; // ô test đang chạy
    private int _activeSerial = -1;             // SquadController.CommandSerial của lệnh test đó

    public NPC Npc => _npc;
    public int Slot => _slot;
    public string CallSign => _callSign;
    public bool IsDead => _npc == null || _npc.IsDead;
    public bool IsLowHealth => _npc != null && _npc.Health < _npc.MaxHealth * _lowHealthRatio;

    public float ShoutWait => _shoutWait;
    public Vector2Int Burst => _burst;
    public float FireInterval => _fireInterval;
    public Vector2 BurstPause => _burstPause;
    public float Accuracy => _accuracy;
    public float Damage => _damage;
    public float SideStep => _sideStep;
    public Vector2 GuardDistance => _guardDistance;
    public float RunDistance => _runDistance;
    public ThreatLevel FireAtThreat => _fireAtThreat;
    public float CornerDwell => _cornerDwell;
    public int MaxCorners => _maxCorners;
    public float PickLockTime => _pickLockTime;
    public bool ShowPickProgress => _showPickProgress;
    public float MirrorTime => _mirrorTime;

    /// <summary>Thời gian còng một người (random trong RestrainTime).</summary>
    public float RollRestrainTime() => Random.Range(_restrainTime.x, Mathf.Max(_restrainTime.x, _restrainTime.y));

    // ---------- Tiến độ việc (giữ qua lúc bị ngắt vì giao chiến, node chạy lại thì làm tiếp) ----------

    /// <summary>Các điểm (góc phòng) cần chĩa súng của lệnh ClearRoom ClearOrderId.</summary>
    public readonly List<Vector3> ClearPoints = new();
    /// <summary>Lệnh ClearRoom mà ClearPoints / ClearStep thuộc về (-1 = chưa tính).</summary>
    public int ClearOrderId { get; set; } = -1;
    /// <summary>Đã chĩa xong bao nhiêu điểm trong ClearPoints.</summary>
    public int ClearStep { get; set; }

    /// <summary>Cửa đang phá khóa dở và tiến trình 0..1 (đổi cửa thì làm lại từ đầu).</summary>
    public DoorController PickDoor { get; set; }
    public float PickProgress { get; set; }

    public bool Invulnerable
    {
        get => _invulnerable;
        set
        {
            _invulnerable = value;
            ApplyDamageScale();
        }
    }

    // ---------- Lệnh ----------

    public TeammateOrder Order { get; private set; } = TeammateOrder.Follow;
    public Vector3 OrderPos { get; private set; }
    public Vector3 OrderLook { get; private set; }
    public DoorController OrderDoor { get; private set; }

    /// <summary>Người cần còng (lệnh Restrain): nghi phạm / con tin đã đầu hàng.</summary>
    public GameObject OrderTarget { get; private set; }

    /// <summary>Tăng mỗi lệnh mới. Node đang chạy thấy đổi thì thoát.</summary>
    public int OrderId { get; private set; }

    /// <summary>Graph đã báo xong lệnh hiện tại (thành công hay không).</summary>
    public bool IsOrderFinished => _finishedId == OrderId;

    /// <summary>Kết quả lần ReportDone của lệnh hiện tại.</summary>
    public bool OrderSucceeded => IsOrderFinished && _lastSucceeded;

    /// <summary>Nghi phạm đã đầu hàng cần canh (Squad giao).</summary>
    public GameObject GuardTarget { get; private set; }

    /// <summary>Giao việc mới. Trả OrderId của việc đó.</summary>
    public int GiveOrder(TeammateOrder order, Vector3 pos = default, Vector3 look = default, DoorController door = null,
        GameObject target = null)
    {
        Order = order;
        OrderPos = pos;
        OrderLook = look;
        OrderDoor = door;
        OrderTarget = target;
        OrderId++;
        Set(OrderTypeVar, order);
        Set(OrderPosVar, pos);
        Set(OrderLookVar, look);
        Set(OrderDoorVar, door != null ? door.gameObject : null);
        Set(OrderTargetVar, target);
        Set(OrderIdVar, OrderId);
        return OrderId;
    }

    /// <summary>Đổi hướng chĩa súng của lệnh hiện tại (HoldAim...) mà không tạo lệnh mới (slicing the pie, quay tránh flash).</summary>
    public void SetOrderLook(Vector3 look)
    {
        OrderLook = look;
        Set(OrderLookVar, look);
    }

    /// <summary>Graph làm xong việc id. Bỏ qua nếu đã có lệnh mới hoặc đã báo rồi.</summary>
    public void ReportDone(int id, bool success)
    {
        if (id != OrderId || _finishedId == id) return;
        _finishedId = id;
        _lastSucceeded = success;
        OrderFinished?.Invoke(this, id, success);
    }

    public void SetGuardTarget(GameObject suspect)
    {
        GuardTarget = suspect;
        Set(GuardTargetVar, suspect);
    }

    // ---------- Trạng thái cho Condition ----------

    /// <summary>Nghi phạm mà NPC đang nhắm (phe Suspect, còn sống, chưa đầu hàng), không có thì null.</summary>
    public NPC EnemyTarget
    {
        get
        {
            if (_npc == null || _npc.Target == null || !_npc.Target.TryGetComponent(out NPC enemy)) return null;
            return enemy.IsDead || enemy.IsSurrendered || enemy.Team == _npc.Team ? null : enemy;
        }
    }

    /// <summary>Đang thấy (hoặc vừa thấy trong AlertMemory giây) một nghi phạm chưa đầu hàng.</summary>
    public bool HasEnemyTarget => !IsDead && EnemyTarget != null && (_npc.CanSeeTarget || _npc.IsAlerted);

    public bool HasGuardTarget
    {
        get
        {
            if (IsDead || GuardTarget == null) return false;
            return GuardTarget.TryGetComponent(out NPC suspect) && !suspect.IsDead && !suspect.IsRestrained;
        }
    }

    /// <summary>Có việc gấp hơn việc theo lệnh (lệnh mới, địch, canh nghi phạm): node làm việc tự thoát.</summary>
    public bool NeedsAttention(int orderId) => IsDead || orderId != OrderId || HasEnemyTarget || HasGuardTarget;

    // ---------- Mức đe dọa ----------

    /// <summary>
    /// Đánh giá nghi phạm: High khi đang chĩa súng vào người nó thấy (mục tiêu của nghi phạm luôn là cảnh sát / người chơi),
    /// vừa nổ súng trong RecentFireMemory giây hoặc đang rút / ném bom. Có súng mà không chĩa: Medium. Không súng: Low.
    /// </summary>
    public ThreatLevel AssessThreat(NPC suspect)
    {
        if (suspect == null || suspect.IsDead || suspect.IsSurrendered || suspect.IsRestrained) return ThreatLevel.None;
        if (suspect.Team == (_npc != null ? _npc.Team : Team.Police)) return ThreatLevel.None;

        NPCGrenadeThrower thrower = suspect.GetComponentInChildren<NPCGrenadeThrower>();
        if (thrower != null && thrower.IsBusy) return ThreatLevel.High;

        NPCWeaponHolder holder = suspect.GetComponentInChildren<NPCWeaponHolder>();
        if (holder == null || !holder.HasWeapon) return ThreatLevel.Low;
        if (FiredRecently(holder)) return ThreatLevel.High;
        if (holder.IsAiming && suspect.Target != null && suspect.CanSeeTarget) return ThreatLevel.High;
        return ThreatLevel.Medium;
    }

    /// <summary>Đủ đe dọa để nổ súng theo luật (AssessThreat >= FireAtThreat).</summary>
    public bool MayFireAt(NPC suspect) => AssessThreat(suspect) >= _fireAtThreat;

    /// <summary>Nghi phạm vừa nổ súng: bắn trả ngay, không chờ hô xong.</summary>
    public bool IsUnderFireFrom(NPC suspect)
    {
        if (suspect == null) return false;
        NPCWeaponHolder holder = suspect.GetComponentInChildren<NPCWeaponHolder>();
        return holder != null && FiredRecently(holder);
    }

    private bool FiredRecently(NPCWeaponHolder holder) => Time.time - holder.LastFireTime <= _recentFireMemory;

    // ---------- Vòng đời ----------

    private void Awake()
    {
        _npc = GetComponent<NPC>();
        _locomotion = GetComponent<NPCLocomotion>();
        _agent = GetComponent<BehaviorGraphAgent>();
        if (_voice == null) _voice = GetComponent<AudioSource>();
        if (_npcVoice == null) _npcVoice = GetComponent<NPCVoice>();
        _lookPoint = new GameObject($"{name}_LookPoint").transform;
        if (_npc != null) _npc.SetMaxHealth(_maxHealth);
        ApplyDamageScale();
        // Được huấn luyện: flash (kể cả của chính đội) chỉ làm choáng nhẹ.
        NPCStun.For(gameObject).Resistance = _flashResistance;
    }

    private void OnEnable()
    {
        Active.Add(this);
        Active.Sort((a, b) => a._slot.CompareTo(b._slot));
        _npc.OnDamaged.AddListener(OnDamaged);
        _npc.OnDied.AddListener(OnNpcDied);
    }

    private void OnDisable()
    {
        Active.Remove(this);
        _npc.OnDamaged.RemoveListener(OnDamaged);
        _npc.OnDied.RemoveListener(OnNpcDied);
    }

    private void Start() => GiveOrder(TeammateOrder.Follow); // vào màn là đi theo người chơi

    private void OnDestroy()
    {
        if (_lookPoint != null) Destroy(_lookPoint.gameObject);
    }

    private void OnValidate()
    {
        if (Application.isPlaying && _npc != null) ApplyDamageScale();
    }

    private void Update()
    {
        if (GuardTarget != null && !HasGuardTarget) SetGuardTarget(null);
        UpdateInspectorTests();
    }

    // ---------- Test lệnh trong Inspector ----------

    private enum TestCmd { None, Follow, StackUp, OpenDoor, PickLock, ThrowFlash, Clear, FlashClear, Restrain, MoveTo, Cover, Mirror, Report }

    private static readonly TestCmd[] TestOrder =
    {
        TestCmd.Follow, TestCmd.StackUp, TestCmd.OpenDoor, TestCmd.PickLock, TestCmd.ThrowFlash, TestCmd.Clear, TestCmd.FlashClear,
        TestCmd.Restrain, TestCmd.MoveTo, TestCmd.Cover, TestCmd.Mirror, TestCmd.Report,
    };

    /// <summary>
    /// Ô test đang tick = lệnh đang chạy: giữ tick tới khi đội làm xong mới tự bỏ. Bỏ tick giữa chừng = hủy lệnh
    /// (cả đội đứng tại chỗ). Tick ô khác khi đang chạy = thay lệnh. Lệnh bị lệnh khác (menu, phím, đồng đội kia)
    /// thay thì ô cũng tự bỏ tick. Trạng thái hiện ở ô "Test Status".
    /// </summary>
    private void UpdateInspectorTests()
    {
        SquadController squad = SquadController.Instance;

        // 1. Theo dõi lệnh test đang chạy.
        if (_activeTest != TestCmd.None)
        {
            bool running = squad != null && squad.IsRunning(_activeSerial);
            if (!GetTest(_activeTest))
            {
                if (running) squad.Cancel();
                _testStatus = $"Đã hủy {_activeTest}";
                Debug.Log($"[{_callSign}] Test {_activeTest}: bỏ tick, hủy lệnh.", this);
                _activeTest = TestCmd.None;
            }
            else if (!running)
            {
                bool replaced = squad == null || squad.CommandSerial != _activeSerial;
                _testStatus = replaced ? $"{_activeTest}: bị lệnh khác thay" : $"Xong {_activeTest}";
                Debug.Log($"[{_callSign}] Test {_activeTest}: {(replaced ? "bị lệnh khác thay" : "xong")}, bỏ tick.", this);
                SetTest(_activeTest, false);
                _activeTest = TestCmd.None;
            }
            else
            {
                _testStatus = squad.WaitingForPlayer
                    ? $"Đang chạy {_activeTest}: chờ người chơi mở / phá cửa (bỏ tick để hủy)"
                    : $"Đang chạy {_activeTest}... (bỏ tick để hủy)";
            }
        }

        // 2. Ô mới được tick (khác ô đang chạy).
        TestCmd next = TestCmd.None;
        foreach (TestCmd c in TestOrder)
            if (c != _activeTest && GetTest(c)) { next = c; break; }
        if (next == TestCmd.None) return;

        // Chỉ giữ tick ô mới (lệnh mới thay lệnh cũ).
        foreach (TestCmd c in TestOrder) SetTest(c, c == next);
        _activeTest = TestCmd.None;
        if (!StartTest(next, out string error))
        {
            SetTest(next, false);
            _testStatus = $"{next}: không ra được lệnh ({error})";
            return;
        }
        _activeTest = next;
        _activeSerial = SquadController.Instance.CommandSerial;
        _testStatus = $"Đang chạy {next}... (bỏ tick để hủy)";
    }

    private bool StartTest(TestCmd test, out string error)
    {
        error = null;
        SquadController squad = SquadController.GetOrCreate();
        SquadCommand cmd = test switch
        {
            TestCmd.Follow => SquadCommand.FallIn,
            TestCmd.StackUp => SquadCommand.StackUp,
            TestCmd.OpenDoor => SquadCommand.OpenDoor,
            TestCmd.PickLock => SquadCommand.PickLock,
            TestCmd.ThrowFlash => SquadCommand.ThrowFlash,
            TestCmd.Restrain => SquadCommand.Restrain,
            TestCmd.MoveTo => SquadCommand.MoveTo,
            TestCmd.Cover => SquadCommand.Cover,
            TestCmd.Mirror => SquadCommand.Mirror,
            TestCmd.Report => SquadCommand.ReportStatus,
            _ => SquadCommand.OpenAndClear,
        };

        // Lệnh không cần cửa: Đi theo, Báo cáo, Còng / Di chuyển / Yểm trợ (theo Test Point).
        if (!SquadController.NeedsDoor(cmd))
        {
            Vector3 point = TestPoint();
            Vector3? issuer = cmd == SquadCommand.Restrain ? point : null;
            if (cmd == SquadCommand.Cover) point += Vector3.up * 1.3f;
            if (squad.Issue(cmd, null, null, issuer, this, point)) return true;
            error = squad.LastRejectReason;
            return false;
        }

        DoorController door = _testDoor != null ? _testDoor : squad.FindDoorNear(transform.position, 15f);
        if (door == null)
        {
            error = "không có cửa nào đủ điểm stack/clear trong 15 m, gán Test Door";
            Debug.LogWarning($"[{_callSign}] Test lệnh {cmd}: {error}.", this);
            return false;
        }
        // Test: bỏ qua kiểm tra người chơi đứng đúng phía cửa (coi như ra lệnh từ chỗ stack).
        // Đồng đội đang được test là người thực hiện (mở cửa, phá khóa, ném flash, đi / vào phòng trước).
        Vector3? from = door.stackLeft != null ? door.stackLeft.position : null;
        if (squad.Issue(cmd, door, test == TestCmd.FlashClear, from, this)) return true;
        error = squad.LastRejectReason;
        return false;
    }

    /// <summary>Test Point, không có thì 4 m trước mặt (trên NavMesh).</summary>
    private Vector3 TestPoint()
    {
        if (_testPoint != null) return _testPoint.position;
        Vector3 p = transform.position + transform.forward * 4f;
        return NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas) ? hit.position : transform.position;
    }

    private bool GetTest(TestCmd c) => c switch
    {
        TestCmd.Restrain => _testRestrain,
        TestCmd.MoveTo => _testMoveTo,
        TestCmd.Cover => _testCover,
        TestCmd.Mirror => _testMirror,
        TestCmd.Report => _testReport,
        TestCmd.Follow => _testFollow,
        TestCmd.StackUp => _testStackUp,
        TestCmd.OpenDoor => _testOpenDoor,
        TestCmd.PickLock => _testPickLock,
        TestCmd.ThrowFlash => _testThrowFlash,
        TestCmd.Clear => _testClear,
        TestCmd.FlashClear => _testFlashClear,
        _ => false,
    };

    private void SetTest(TestCmd c, bool value)
    {
        switch (c)
        {
            case TestCmd.Follow: _testFollow = value; break;
            case TestCmd.StackUp: _testStackUp = value; break;
            case TestCmd.OpenDoor: _testOpenDoor = value; break;
            case TestCmd.PickLock: _testPickLock = value; break;
            case TestCmd.ThrowFlash: _testThrowFlash = value; break;
            case TestCmd.Clear: _testClear = value; break;
            case TestCmd.FlashClear: _testFlashClear = value; break;
            case TestCmd.Restrain: _testRestrain = value; break;
            case TestCmd.MoveTo: _testMoveTo = value; break;
            case TestCmd.Cover: _testCover = value; break;
            case TestCmd.Mirror: _testMirror = value; break;
            case TestCmd.Report: _testReport = value; break;
        }
    }

    private void ApplyDamageScale()
    {
        if (_npc == null) return;
        _npc.DamageTakenScale = _invulnerable ? 0f : _damageScale;
        _npc.BodyArmor = _bodyArmor;
        _npc.Helmet = _helmet;
    }

    private void OnDamaged(float amount)
    {
        if (IsDead || Time.time < _nextHitVoice) return;
        _nextHitVoice = Time.time + _hitVoiceCooldown;
        Say("I'm hit!", NPCVoiceLine.Hit, _hitClips);
    }

    private void OnNpcDied()
    {
        ClearLook();
        OnDied?.Invoke();
        Died?.Invoke(this);
    }

    // ---------- Nhìn / người chơi ----------

    /// <summary>Đứng yên thì quay mặt về point (NPCLocomotion.LookTarget).</summary>
    public void LookAt(Vector3 point)
    {
        _lookPoint.position = point;
        if (_locomotion != null) _locomotion.LookTarget = _lookPoint;
    }

    public void ClearLook()
    {
        if (_locomotion != null) _locomotion.LookTarget = null;
    }

    public static Transform PlayerHead => Camera.main != null ? Camera.main.transform : null;

    /// <summary>
    /// Chỗ đứng sau lưng người chơi: chỗ rộng thì A lệch trái, B lệch phải. Lối đi hẹp (hành lang, khung cửa) thì đi
    /// hàng một: A ngay sau người chơi, B sau A. Đã kẹp vào NavMesh.
    /// </summary>
    public bool TryGetFollowSlot(out Vector3 slot)
    {
        if (IsNarrowAroundPlayer())
        {
            InSingleFile = true;
            Teammate lead = _slot == 1 ? Active.Find(t => t != this && t._slot == 0 && !t.IsDead) : null;
            if (lead == null) return TryGetBehindPlayer(_followDistance, 0f, out slot);
            // B theo A: đứng sau A trên đường từ người chơi tới A.
            slot = transform.position;
            Transform head = PlayerHead;
            if (head == null) return false;
            Vector3 a = lead.transform.position;
            Vector3 away = a - head.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -head.forward;
            away.y = 0f;
            Vector3 wanted = ReachableToward(a, a + away.normalized * 1.3f);
            slot = NavMesh.SamplePosition(wanted, out NavMeshHit hit, 0.5f, NavMesh.AllAreas) ? hit.position : a;
            return true;
        }
        InSingleFile = false;
        return TryGetBehindPlayer(_followDistance, _slot == 0 ? -_followSideOffset : _followSideOffset, out slot);
    }

    /// <summary>Đang đi hàng một (lần tính chỗ đứng gần nhất).</summary>
    public bool InSingleFile { get; private set; }

    /// <summary>
    /// Lối đi quanh chân người chơi hẹp hơn SingleFileWidth, đo ngang hướng nhìn bằng 2 cách, lấy nhỏ hơn:
    /// NavMesh (mép NavMesh) và vật lý (quả cầu nhỏ dò dần ra 2 bên ở tầm hông; bắt được cả MeshCollider tường không có
    /// mặt khung cửa, mà raycast thì xuyên qua). Bỏ qua người (NPC, con tin, người chơi) và cánh cửa.
    /// </summary>
    private bool IsNarrowAroundPlayer()
    {
        Transform head = PlayerHead;
        if (head == null || _singleFileWidth <= 0f) return false;
        if (!NavMesh.SamplePosition(head.position - Vector3.up * 1.2f, out NavMeshHit feet, 2f, NavMesh.AllAreas)) return false;
        Vector3 fwd = head.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) return false;
        Vector3 right = Vector3.Cross(Vector3.up, fwd.normalized);
        float reach = _singleFileWidth;
        float l = NavMesh.Raycast(feet.position, feet.position - right * reach, out NavMeshHit hl, NavMesh.AllAreas) ? hl.distance : reach;
        float r = NavMesh.Raycast(feet.position, feet.position + right * reach, out NavMeshHit hr, NavMesh.AllAreas) ? hr.distance : reach;
        // NavMesh đã co vào theo bán kính agent: cộng lại 2 lần bán kính (~0.6 m) để ra bề rộng thật.
        float navWidth = l + r + 0.6f;
        Vector3 hip = feet.position + Vector3.up * 1f;
        float physWidth = ProbeFree(hip, -right, reach, head.root) + ProbeFree(hip, right, reach, head.root);
        return Mathf.Min(navWidth, physWidth) < _singleFileWidth + (InSingleFile ? 0.3f : 0f); // trễ 0.3 m để không đổi qua lại liên tục
    }

    private static readonly Collider[] s_ProbeHits = new Collider[16];

    /// <summary>Khoảng trống từ origin theo dir (dò quả cầu 0.15 m mỗi 0.1 m), tối đa reach.</summary>
    private static float ProbeFree(Vector3 origin, Vector3 dir, float reach, Transform ignoreRoot)
    {
        const float radius = 0.15f;
        for (float o = 0.2f; o <= reach; o += 0.1f)
        {
            int n = Physics.OverlapSphereNonAlloc(origin + dir * o, radius, s_ProbeHits, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = s_ProbeHits[i];
                if (c.transform.root == ignoreRoot || c.GetComponentInParent<NPC>() != null || c.GetComponentInParent<Hostage>() != null
                    || c.GetComponentInParent<DoorController>() != null) continue;
                return o - radius;
            }
        }
        return reach;
    }

    /// <summary>Điểm sau lưng người chơi distance m, lệch ngang side m (dương = bên phải người chơi).</summary>
    public bool TryGetBehindPlayer(float distance, float side, out Vector3 point)
    {
        point = transform.position;
        Transform head = PlayerHead;
        if (head == null) return false;
        Vector3 fwd = head.forward;
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        // Chân người chơi trên NavMesh (đầu cao ~1.6 m nên dò rộng theo chiều dọc).
        Vector3 basePos = head.position - Vector3.up * 1.2f;
        if (!NavMesh.SamplePosition(basePos, out NavMeshHit feet, 2f, NavMesh.AllAreas)) return false;
        Vector3 wanted = ReachableToward(feet.position, feet.position - fwd * distance + right * side);
        // Sát tường sau lưng: đứng ngang bên cạnh người chơi.
        if (Vector3.Distance(wanted, feet.position) < 0.7f && Mathf.Abs(side) > 0.01f)
            wanted = ReachableToward(feet.position, feet.position + right * (Mathf.Sign(side) * Mathf.Max(1f, Mathf.Abs(side) * 1.5f)));
        point = NavMesh.SamplePosition(wanted, out NavMeshHit hit, 0.5f, NavMesh.AllAreas) ? hit.position : feet.position;
        return true;
    }

    /// <summary>Đi thẳng từ from tới to trên NavMesh; vướng tường / mép thì dừng trước chỗ vướng 0.3 m.</summary>
    private static Vector3 ReachableToward(Vector3 from, Vector3 to)
    {
        if (!NavMesh.Raycast(from, to, out NavMeshHit block, NavMesh.AllAreas)) return to;
        Vector3 back = from - block.position;
        return back.magnitude > 0.35f ? block.position + back.normalized * 0.3f : from;
    }

    // ---------- Hô hàng ----------

    /// <summary>
    /// Hô vào một nghi phạm. Mỗi nghi phạm hô lại được sau ReShoutInterval giây (nghi phạm chưa đủ đe dọa để bắn
    /// thì cứ hô tiếp tới khi đầu hàng), giữa 2 lần hô bất kỳ cách nhau ShoutCooldown. Trả true nếu vừa hô.
    /// </summary>
    public bool TryShoutAt(NPC suspect)
    {
        if (suspect == null || Time.time < _lastShout + _shoutCooldown) return false;
        if (_lastShoutAt.TryGetValue(suspect, out float last) && Time.time < last + _reShoutInterval) return false;
        if (!_shouted.ContainsKey(suspect)) _shouted[suspect] = Time.time;
        _lastShoutAt[suspect] = Time.time;
        _lastShout = Time.time;
        Say("Police! Drop your weapon!", NPCVoiceLine.ShoutCompliance, _shoutClips);
        if (suspect.TryGetComponent(out NPCMorale morale)) morale.ReceiveComplianceOrder(transform.position);
        else suspect.NotifyThreat();
        // Như SWAT 4: mọi người khác nghe được (nghi phạm khác, con tin) cũng xét tuân lệnh.
        Compliance.Order(NPCConditionUtils.EyeOf(gameObject), _shoutRadius, _shoutThroughWallsRadius, suspect.gameObject);
        NPCHearing.Report(transform.position, _shoutRadius, NoiseType.Shout, gameObject);
        return true;
    }

    /// <summary>Lúc hô LẦN ĐẦU vào nghi phạm này; chưa hô thì false.</summary>
    public bool TryGetShoutTime(NPC suspect, out float time)
    {
        time = 0f;
        return suspect != null && _shouted.TryGetValue(suspect, out time);
    }

    // ---------- An toàn đường đạn ----------

    /// <summary>
    /// Người chơi hoặc đồng đội khác nằm trong "ống" bán kính SafetyRadius từ from tới to thì không bắn.
    /// Dùng vị trí thân (không cần collider) nên đúng cả khi rig VR không có collider thân.
    /// blockerPos: vị trí người chắn để bước ngang tránh ra.
    /// </summary>
    public bool IsLineOfFireClear(Vector3 from, Vector3 to, out Vector3 blockerPos)
    {
        blockerPos = default;
        const float bodyRadius = 0.25f;
        float limit = _safetyRadius + bodyRadius;

        Transform head = PlayerHead;
        if (head != null)
        {
            foreach (Vector3 p in new[] { head.position, head.position - Vector3.up * 0.6f, head.position - Vector3.up * 1.1f })
                if (Blocks(from, to, p, limit)) { blockerPos = p; return false; }
        }
        foreach (Teammate other in Active)
        {
            if (other == this || other.IsDead) continue;
            Vector3 c = NPCConditionUtils.CenterOf(other.gameObject);
            foreach (Vector3 p in new[] { c, c + Vector3.up * 0.5f })
                if (Blocks(from, to, p, limit)) { blockerPos = p; return false; }
        }
        return true;
    }

    private static bool Blocks(Vector3 a, Vector3 b, Vector3 p, float limit)
    {
        Vector3 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-6f) return false;
        float t = Vector3.Dot(p - a, ab) / len2;
        if (t <= 0f || t >= 1f) return false; // sau lưng người bắn / sau mục tiêu
        return Vector3.Distance(p, a + ab * t) <= limit;
    }

    // ---------- Giọng nói ----------

    /// <summary>Nói một câu: phát clip (nếu có), log ra Console và gọi OnSay (gắn phụ đề / TTS sau).</summary>
    public void Say(string line, AudioClip[] clips = null)
    {
        Debug.Log($"[{_callSign}] {line}", this);
        OnSay?.Invoke(line);
        PlayLegacy(clips);
    }

    /// <summary>
    /// Nói câu voiceLine qua NPCVoice (bộ giọng Officer: ưu tiên, cooldown, không lặp clip). Không có NPCVoice / chưa
    /// có clip cho câu đó thì phát mảng clip cũ (legacy). Luôn log + OnSay(text) làm phụ đề.
    /// </summary>
    public void Say(string text, NPCVoiceLine voiceLine, AudioClip[] legacy = null)
    {
        Debug.Log($"[{_callSign}] {text}", this);
        OnSay?.Invoke(text);
        if (_npcVoice != null && _npcVoice.Play(voiceLine)) return;
        PlayLegacy(legacy);
    }

    private void PlayLegacy(AudioClip[] clips)
    {
        if (_voice == null || clips == null || clips.Length == 0) return;
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip != null) _voice.PlayOneShot(clip);
    }

    public void SayDoorLocked() => Say("Door's locked.", NPCVoiceLine.DoorLocked, _doorLockedClips);
    public void SayRoomClear() => Say("Room clear!", NPCVoiceLine.RoomClear, _roomClearClips);
    public void SayAck() => Say("Copy.", NPCVoiceLine.Ack, _ackClips);
    public void SayPicking() => Say("Picking the lock, cover me.", NPCVoiceLine.Moving, _ackClips);
    public void SayUnlocked() => Say("Lock's picked.", NPCVoiceLine.DoorOpened, _ackClips);
    public void SayFlashOut() => Say("Flashbang out!", NPCVoiceLine.FlashOut, _ackClips);
    public void SayContact() => Say("Contact!", NPCVoiceLine.Contact);
    public void SayStacked() => Say("Stacked up.", NPCVoiceLine.Stacked);
    public void SayMoving() => Say("Moving.", NPCVoiceLine.Moving, _ackClips);
    public void SayRestraining() => Say("Restraining.", NPCVoiceLine.Restraining);
    public void SaySecured(bool suspect) =>
        Say(suspect ? "Suspect secured." : "Civilian secured.", suspect ? NPCVoiceLine.SuspectSecured : NPCVoiceLine.CivilianSecured);

    /// <summary>Báo kết quả soi gương dưới cửa.</summary>
    public void SayMirror(int suspects, int civilians)
    {
        if (suspects > 0)
            Say(civilians > 0 ? $"I see suspects. {suspects} suspect(s), {civilians} civilian(s)." : $"I see suspects. {suspects} suspect(s).",
                NPCVoiceLine.MirrorSuspects);
        else if (civilians > 0) Say($"Civilians inside. {civilians} civilian(s).", NPCVoiceLine.MirrorCivilians);
        else Say("Looks clear.", NPCVoiceLine.MirrorClear);
    }

    private void Set<T>(string variable, T value)
    {
        if (_agent != null) _agent.SetVariableValue(variable, value);
    }
}
