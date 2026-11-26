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
    BreachDoor,
    ThrowFlash,
}

/// <summary>
/// Đồng đội AI (phe Police). Gắn cùng NPC, NPCLocomotion, NPCWeaponHolder, NPCGrenadeThrower và TeammateGraph.
///
/// - SquadController quyết định KHI NÀO làm gì và gọi GiveOrder(): ghi Order* (cả lên Blackboard), tăng OrderId.
/// - TeammateGraph quyết định LÀM THẾ NÀO: node đang chạy thấy OrderId đổi thì thoát trong vòng 1 frame,
///   làm xong việc thì gọi ReportDone(id, thành công?) để Squad đi bước tiếp.
/// - Giao chiến (theo luật SWAT 4): hô trước, chỉ bắn khi nghi phạm không đầu hàng, không bắn qua người chơi / đồng đội.
/// - Canh nghi phạm: Squad giao GuardTarget khi một NPC đầu hàng, xóa khi bị còng / chết.
/// - Khó chết: NPC.MaxHealth 300 (đặt bởi Setup Prefab), chỉ nhận DamageTakenScale (50%) sát thương, Invulnerable = bất tử.
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

    [Header("Đội")]
    [Tooltip("0 = A (đi bên trái, người mở cửa). 1 = B (bên phải, người ném flashbang).")]
    [SerializeField, Range(0, 1)] private int _slot;
    [SerializeField] private string _callSign = "Alpha";

    [Header("Máu")]
    [Tooltip("Chỉ nhận ngần này phần sát thương (0.5 = 50%).")]
    [SerializeField, Range(0f, 1f)] private float _damageTakenScale = 0.5f;
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

    [Header("Canh nghi phạm")]
    [Tooltip("Đứng cách nghi phạm đã đầu hàng trong khoảng này (m).")]
    [SerializeField] private Vector2 _guardDistance = new(2f, 3f);

    [Header("Giọng nói (Audios/NPC/Teamate, để trống thì chỉ log)")]
    [SerializeField] private AudioSource _voice;
    [SerializeField] private AudioClip[] _shoutClips;
    [SerializeField] private AudioClip[] _doorLockedClips;
    [SerializeField] private AudioClip[] _roomClearClips;
    [SerializeField] private AudioClip[] _hitClips;
    [SerializeField] private AudioClip[] _ackClips;
    [SerializeField] private float _hitVoiceCooldown = 3f;

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
    private readonly Dictionary<NPC, float> _shouted = new(); // nghi phạm -> lúc hô
    private float _lastShout = float.NegativeInfinity;
    private float _nextHitVoice;

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

    /// <summary>Tăng mỗi lệnh mới. Node đang chạy thấy đổi thì thoát.</summary>
    public int OrderId { get; private set; }

    /// <summary>Graph đã báo xong lệnh hiện tại (thành công hay không).</summary>
    public bool IsOrderFinished => _finishedId == OrderId;

    /// <summary>Kết quả lần ReportDone của lệnh hiện tại.</summary>
    public bool OrderSucceeded => IsOrderFinished && _lastSucceeded;

    /// <summary>Nghi phạm đã đầu hàng cần canh (Squad giao).</summary>
    public GameObject GuardTarget { get; private set; }

    /// <summary>Giao việc mới. Trả OrderId của việc đó.</summary>
    public int GiveOrder(TeammateOrder order, Vector3 pos = default, Vector3 look = default, DoorController door = null)
    {
        Order = order;
        OrderPos = pos;
        OrderLook = look;
        OrderDoor = door;
        OrderId++;
        Set(OrderTypeVar, order);
        Set(OrderPosVar, pos);
        Set(OrderLookVar, look);
        Set(OrderDoorVar, door != null ? door.gameObject : null);
        Set(OrderIdVar, OrderId);
        return OrderId;
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

    // ---------- Vòng đời ----------

    private void Awake()
    {
        _npc = GetComponent<NPC>();
        _locomotion = GetComponent<NPCLocomotion>();
        _agent = GetComponent<BehaviorGraphAgent>();
        if (_voice == null) _voice = GetComponent<AudioSource>();
        _lookPoint = new GameObject($"{name}_LookPoint").transform;
        ApplyDamageScale();
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
    }

    private void ApplyDamageScale()
    {
        if (_npc != null) _npc.DamageTakenScale = _invulnerable ? 0f : _damageTakenScale;
    }

    private void OnDamaged(float amount)
    {
        if (IsDead || Time.time < _nextHitVoice) return;
        _nextHitVoice = Time.time + _hitVoiceCooldown;
        Say("Tôi trúng đạn!", _hitClips);
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

    /// <summary>Chỗ đứng sau lưng người chơi: A lệch trái, B lệch phải, đã kẹp vào NavMesh.</summary>
    public bool TryGetFollowSlot(out Vector3 slot)
    {
        return TryGetBehindPlayer(_followDistance, _slot == 0 ? -_followSideOffset : _followSideOffset, out slot);
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

    /// <summary>Hô vào một nghi phạm (1 lần mỗi nghi phạm, cách nhau ShoutCooldown). Trả true nếu vừa hô.</summary>
    public bool TryShoutAt(NPC suspect)
    {
        if (suspect == null || _shouted.ContainsKey(suspect) || Time.time < _lastShout + _shoutCooldown) return false;
        _shouted[suspect] = Time.time;
        _lastShout = Time.time;
        Say("Cảnh sát! Bỏ súng xuống!", _shoutClips);
        if (suspect.TryGetComponent(out NPCMorale morale)) morale.ReceiveComplianceOrder(transform.position);
        else suspect.NotifyThreat();
        return true;
    }

    /// <summary>Lúc đã hô vào nghi phạm này; chưa hô thì false.</summary>
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
        if (_voice != null && clips != null && clips.Length > 0)
        {
            AudioClip clip = clips[Random.Range(0, clips.Length)];
            if (clip != null) _voice.PlayOneShot(clip);
        }
    }

    public void SayDoorLocked() => Say("Cửa khóa!", _doorLockedClips);
    public void SayRoomClear() => Say("Phòng an toàn!", _roomClearClips);
    public void SayAck() => Say("Rõ!", _ackClips);

    private void Set<T>(string variable, T value)
    {
        if (_agent != null) _agent.SetVariableValue(variable, value);
    }
}
