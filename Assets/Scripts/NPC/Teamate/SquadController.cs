using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using VRSwat.Doors;
using VRSwat.Tutorial;

/// <summary>Lệnh người chơi ra cho cả đội (menu lệnh VR, phím test hoặc checkbox test trên Teammate).</summary>
public enum SquadCommand
{
    FallIn,
    StackUp,
    /// <summary>Xông vào: mở cửa (khóa thì phá khóa nếu AutoPickLock, không thì chờ người chơi) -> (flash) -> clear phòng.</summary>
    OpenAndClear,
    /// <summary>Phá khóa + xông vào: luôn phá khóa nếu cửa khóa -> mở -> (flash) -> clear phòng.</summary>
    BreachAndClear,
    /// <summary>Chỉ mở cửa rồi chĩa súng vào phòng (cửa khóa thì báo).</summary>
    OpenDoor,
    /// <summary>Chỉ phá khóa rồi về chỗ stack giữ súng.</summary>
    PickLock,
    /// <summary>Mở cửa (nếu đang đóng, không khóa) rồi ném lựu đạn choáng vào phòng, không vào.</summary>
    ThrowFlash,
    // Chỉ thêm vào cuối.
    /// <summary>Còng người đã đầu hàng gần điểm chỉ (người được chỉ định / gần nhất còng, người kia canh).</summary>
    Restrain,
    /// <summary>Cả đội đi lần lượt tới điểm chỉ.</summary>
    MoveTo,
    /// <summary>Cả đội đứng tại chỗ chĩa súng vào điểm chỉ.</summary>
    Cover,
    /// <summary>Stack up rồi soi gương dưới khe cửa, báo trong phòng có ai (SquadController.LastMirror).</summary>
    Mirror,
    /// <summary>Báo cáo: số nghi phạm còn / đã còng, con tin còn / đã cứu, máu đội.</summary>
    ReportStatus,
}

/// <summary>Kết quả lần soi gương dưới cửa gần nhất.</summary>
public struct MirrorReport
{
    public DoorController Door;
    public int Suspects;
    public int Civilians;
    public float Time;
}

/// <summary>
/// Điều phối 2 đồng đội: lo KHI NÀO làm gì. Chia lệnh thành việc đơn lẻ, giao bằng Teammate.GiveOrder và chờ
/// Teammate.ReportDone. Graph của từng đồng đội lo LÀM THẾ NÀO.
///
/// - Fall in        : cả hai đi theo người chơi (việc mặc định khi vào màn).
/// - Stack up       : A -> door.stackLeft, B -> door.stackRight, đi lần lượt (MoveOneByOne); tới nơi thì chĩa súng vào
///                    giữa cửa. Tín hiệu SquadStacked.
/// - Lệnh test trên một Teammate: đồng đội đó (Lead) là người mở cửa / phá khóa / ném flash / đi và vào trước.
/// - Open door      : stack up -> A mở cửa -> cả hai chĩa súng vào phòng. Cửa khóa: "Cửa khóa!", SquadDoorLocked.
/// - Pick lock      : stack up -> A tới ổ khóa, ngồi phá khóa, xong về chỗ stack. B yểm trợ (chĩa súng vào cửa).
/// - Throw flash    : stack up -> (A mở cửa) -> B ném lựu đạn choáng vào giữa phòng -> cả hai chĩa súng vào phòng.
/// - Open and clear : stack up -> mở cửa (khóa: phá khóa nếu AutoPickLock, không thì chờ người chơi mở / phá) ->
///                    (B ném flash) -> Entry.
/// - Breach and clear: như Open and clear nhưng luôn phá khóa.
/// - Slicing the pie: trước khi vào, đứng ở chỗ stack quét súng dần qua khe cửa (SliceTime giây).
/// - Entry          : người phía tay nắm vào trước, 0.4 s sau người phía bản lề. Cánh cửa mở vào phòng: Cross (cắt
///                    chéo); mở ra phía đội: Button-hook (móc vào phía mình). Mỗi người lần lượt chĩa súng vào các góc
///                    phòng phía mình (TeammateRoomCorners hoặc tự dò). Cả hai quét xong và 2 s liền không ai thấy địch
///                    -> "Phòng an toàn!", SquadRoomClear, chĩa súng vào trong phòng.
/// - Flash của đội  : ném xong cả đội quay mặt khỏi cửa tới khi flash nổ (tối đa FlashTurnAwayMax giây).
/// - Restrain       : người chỉ định / gần nhất tới sau lưng người đã đầu hàng gần điểm chỉ, còng 2-3 s; người kia canh.
///                    AutoRestrain: canh nghi phạm an toàn AutoRestrainDelay giây thì tự còng.
/// - Move to        : đi lần lượt tới điểm chỉ. Cover: đứng tại chỗ chĩa súng vào điểm chỉ.
/// - Mirror         : stack up, quỳ ở chân cửa soi gương dưới khe, báo trong phòng có ai (LastMirror).
/// - Report status  : báo số nghi phạm / con tin còn lại / đã còng, máu đội (LastStatus).
/// - Mỗi bước có timeout (không tính lúc đồng đội đang giao chiến / canh nghi phạm).
/// - NPC địch đầu hàng: giao canh cho đồng đội gần nhất trong 10 m; bị còng / chết thì xóa.
///
/// Menu lệnh VR: TeammateCommandMenu (tự thêm nếu scene chưa có). API: Issue(cmd, door, flashFirst) và CanIssue(...).
/// Phím test: 1 Stack up, 2 Open and clear, 3 Breach and clear, 4 Fall in, 5 Open door, 6 Pick lock, 7 Throw flash,
/// G bật/tắt flash (cửa = cửa gần người chơi nhất có điểm stack, người chơi đứng đúng phía stack).
/// </summary>
[DisallowMultipleComponent]
public class SquadController : MonoBehaviour
{
    [Header("Đồng đội (trống = tự tìm Teammate trong scene theo Slot)")]
    [Tooltip("A: đi bên trái, người mở cửa / phá khóa.")]
    [SerializeField] private Teammate _alpha;
    [Tooltip("B: đi bên phải, người ném flashbang.")]
    [SerializeField] private Teammate _bravo;

    [Header("Thời gian (giây)")]
    [Tooltip("Mỗi bước di chuyển tối đa ngần này giây.")]
    [SerializeField] private float _stepTimeout = 10f;
    [Tooltip("Stack up: đồng đội di chuyển lần lượt (người sau chờ người trước tới chỗ rồi mới đi). Tắt: đi cùng lúc.")]
    [SerializeField] private bool _moveOneByOne = true;
    [Tooltip("B vào phòng sau A ngần này giây.")]
    [SerializeField] private float _entryDelay = 0.4f;
    [Tooltip("Ném flash xong chờ ngần này giây mới vào.")]
    [SerializeField] private float _flashWait = 1.5f;
    [Tooltip("Quét góc xong, ngần này giây liền không ai thấy địch thì báo phòng an toàn.")]
    [SerializeField] private float _roomClearQuiet = 2f;

    [Header("Cửa khóa")]
    [Tooltip("Xông vào (Open and clear) gặp cửa khóa: đồng đội tự phá khóa. Tắt: báo cửa khóa và chờ người chơi mở / phá.")]
    [SerializeField] private bool _autoPickLock = true;

    [Header("Canh nghi phạm / còng")]
    [Tooltip("NPC đầu hàng trong bán kính này thì đồng đội gần nhất tới canh.")]
    [SerializeField] private float _guardRadius = 10f;
    [Tooltip("Lệnh Còng: tìm người đã đầu hàng trong khoảng này quanh điểm chỉ (m).")]
    [SerializeField] private float _restrainRange = 12f;
    [Tooltip("Canh một nghi phạm đã đầu hàng an toàn (không còn địch) AutoRestrainDelay giây thì tự còng.")]
    [SerializeField] private bool _autoRestrain;
    [SerializeField] private float _autoRestrainDelay = 5f;

    [Header("Xông vào")]
    [Tooltip("Slicing the pie: trước khi vào, ngần này giây quét súng dần qua khe cửa. 0 = tắt.")]
    [SerializeField] private float _sliceTime = 1.5f;
    [Tooltip("Ném flash của đội: quay mặt khỏi cửa tới khi flash nổ (tối đa ngần này giây).")]
    [SerializeField] private float _flashTurnAwayMax = 4f;

    [Header("Soi gương dưới cửa")]
    [Tooltip("Không có TeammateRoomCorners.roomVolume: đếm người trong khoảng này phía bên kia cửa (m).")]
    [SerializeField] private float _mirrorRange = 8f;

    [Header("Menu lệnh VR")]
    [Tooltip("Scene chưa có TeammateCommandMenu thì tự thêm vào object này.")]
    [SerializeField] private bool _autoAddCommandMenu = true;
    [Tooltip("Chọn cửa trong khoảng này (m) quanh người chơi.")]
    [SerializeField] private float _doorRange = 6f;

    [Header("Test")]
    [Tooltip("Phím 1 Stack up, 2 Open and clear, 3 Breach and clear, 4 Fall in, 5 Open door, 6 Pick lock, 7 Throw flash, G bật/tắt flash.")]
    [SerializeField] private bool _testKeys = true;
    [Tooltip("+ Flashbang first cho Open / Breach and clear.")]
    [SerializeField] private bool _flashFirst;
    [Tooltip("Đồng đội bất tử (bật cho phòng 8-9 của tutorial).")]
    [SerializeField] private bool _invulnerable;

    [Header("Events")]
    [Tooltip("Một đồng đội chết: thua màn.")]
    public UnityEvent<Teammate> OnTeammateDown;
    public UnityEvent<DoorController> OnStacked;
    public UnityEvent<DoorController> OnDoorLocked;
    public UnityEvent<DoorController> OnDoorUnlocked;
    public UnityEvent<DoorController> OnRoomClear;

    public static SquadController Instance { get; private set; }

    /// <summary>Kết quả soi gương gần nhất (Door null = chưa soi lần nào).</summary>
    public MirrorReport LastMirror { get; private set; }

    /// <summary>Câu báo cáo tình hình gần nhất (lệnh ReportStatus).</summary>
    public string LastStatus { get; private set; }

    public bool AutoRestrain
    {
        get => _autoRestrain;
        set => _autoRestrain = value;
    }

    /// <summary>Lệnh cần chọn cửa (các lệnh khác dùng điểm chỉ hoặc không cần gì).</summary>
    public static bool NeedsDoor(SquadCommand cmd) => cmd switch
    {
        SquadCommand.FallIn or SquadCommand.Restrain or SquadCommand.MoveTo or SquadCommand.Cover or SquadCommand.ReportStatus => false,
        _ => true,
    };

    /// <summary>Lệnh cần một điểm chỉ (tia tay cầm / hướng nhìn).</summary>
    public static bool NeedsPoint(SquadCommand cmd) => cmd == SquadCommand.MoveTo || cmd == SquadCommand.Cover;

    private readonly Dictionary<GameObject, float> _guardSince = new(); // nghi phạm -> lúc bắt đầu được canh
    private Coroutine _routine;
    private Teammate _lead; // người thực hiện lệnh do test chỉ định (null = mặc định: A mở cửa, B ném flash)
    private int _runningSerial = -1; // CommandSerial của lệnh đang chạy, -1 = rảnh

    /// <summary>Tăng mỗi lần ra lệnh / hủy lệnh.</summary>
    public int CommandSerial { get; private set; }

    /// <summary>Đang chạy lệnh nào đó.</summary>
    public bool IsBusy => _runningSerial >= 0;

    /// <summary>Lệnh serial còn đang chạy (chưa xong, chưa bị hủy / thay bằng lệnh khác).</summary>
    public bool IsRunning(int serial) => serial >= 0 && serial == _runningSerial;

    /// <summary>Lý do lần Issue gần nhất bị từ chối (null = lần gần nhất ra được lệnh).</summary>
    public string LastRejectReason { get; private set; }

    public bool FlashFirst
    {
        get => _flashFirst;
        set => _flashFirst = value;
    }

    public bool AutoPickLock
    {
        get => _autoPickLock;
        set => _autoPickLock = value;
    }

    public bool Invulnerable
    {
        get => _invulnerable;
        set
        {
            _invulnerable = value;
            foreach (Teammate t in Members()) t.Invulnerable = value;
        }
    }

    public float DoorRange => _doorRange;

    /// <summary>Lệnh đang chạy (null = chưa ra lệnh nào).</summary>
    public SquadCommand? CurrentCommand { get; private set; }
    public DoorController CurrentDoor { get; private set; }

    /// <summary>Đang chờ người chơi mở / phá cửa khóa.</summary>
    public bool WaitingForPlayer { get; private set; }

    /// <summary>Người mở cửa / phá khóa: người được chỉ định trong lệnh, không có thì A (A chết thì B).</summary>
    public Teammate Opener => Alive(_lead) ?? Alive(A) ?? Alive(B);

    /// <summary>Người ném flash: người được chỉ định trong lệnh, không có thì B (B chết thì A).</summary>
    public Teammate Flasher => Alive(_lead) ?? Alive(B) ?? Alive(A);

    /// <summary>Người được chỉ định làm lệnh hiện tại (test trên một đồng đội), null = phân công mặc định.</summary>
    public Teammate Lead => Alive(_lead);

    /// <summary>Còn ít nhất một đồng đội sống.</summary>
    public bool HasMembers => Opener != null;

    private Teammate A => _alpha != null ? _alpha : FindSlot(0);
    private Teammate B => _bravo != null ? _bravo : FindSlot(1);

    /// <summary>SquadController trong scene; chưa có thì tạo object "Squad".</summary>
    public static SquadController GetOrCreate()
    {
        if (Instance != null) return Instance;
        SquadController squad = FindAnyObjectByType<SquadController>();
        return squad != null ? squad : new GameObject("Squad").AddComponent<SquadController>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) Debug.LogWarning("[Squad] Có nhiều SquadController trong scene.", this);
        Instance = this;
        if (_autoAddCommandMenu && FindAnyObjectByType<TeammateCommandMenu>() == null)
            gameObject.AddComponent<TeammateCommandMenu>();
    }

    private void OnEnable()
    {
        NPC.AnySurrendered += OnSuspectSurrendered;
        NPC.AnyRestrained += OnSuspectDone;
        NPC.AnyDied += OnSuspectDone;
        Teammate.Died += OnTeammateDied;
    }

    private void OnDisable()
    {
        NPC.AnySurrendered -= OnSuspectSurrendered;
        NPC.AnyRestrained -= OnSuspectDone;
        NPC.AnyDied -= OnSuspectDone;
        Teammate.Died -= OnTeammateDied;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (_invulnerable) Invulnerable = true;
    }

    private void OnValidate()
    {
        if (Application.isPlaying) Invulnerable = _invulnerable;
    }

    private void Update()
    {
        if (_testKeys) UpdateTestKeys();
        if (_autoRestrain && !IsBusy) UpdateAutoRestrain();
    }

    /// <summary>AutoRestrain: đồng đội canh nghi phạm đã đầu hàng đủ lâu, không còn địch quanh đó thì tự còng.</summary>
    private void UpdateAutoRestrain()
    {
        foreach (Teammate t in Members())
            if (t.HasEnemyTarget) return; // còn giao chiến: chưa an toàn
        foreach (Teammate t in Members())
        {
            if (!t.HasGuardTarget) continue;
            GameObject suspect = t.GuardTarget;
            if (!_guardSince.TryGetValue(suspect, out float since)) { _guardSince[suspect] = Time.time; continue; }
            if (Time.time - since < _autoRestrainDelay) continue;
            Debug.Log($"[Squad] AutoRestrain: {t.CallSign} canh {suspect.name} an toàn {_autoRestrainDelay:0} s, tự còng.", t);
            Issue(SquadCommand.Restrain, null, null, suspect.transform.position, t, suspect.transform.position);
            return;
        }
    }

    // ---------- API cho menu lệnh ----------

    public bool CanIssue(SquadCommand cmd, DoorController door, Vector3 playerPos) => CanIssue(cmd, door, playerPos, out _);

    public bool CanIssue(SquadCommand cmd, DoorController door, Vector3 playerPos, out string reason) =>
        CanIssue(cmd, door, playerPos, null, out reason);

    /// <summary>
    /// Lệnh có ra được không: còn đồng đội; lệnh theo cửa: cửa có đủ điểm, người chơi đứng đúng phía đặt điểm stack,
    /// trạng thái cửa hợp với lệnh (phá khóa: cửa phải khóa; mở cửa / soi gương: cửa chưa mở...); lệnh theo điểm
    /// (Di chuyển / Yểm trợ): có điểm (đi tới được); Còng: có người đã đầu hàng gần điểm chỉ (point, không có thì
    /// quanh người chơi).
    /// </summary>
    public bool CanIssue(SquadCommand cmd, DoorController door, Vector3 playerPos, Vector3? point, out string reason)
    {
        reason = null;
        if (Opener == null) { reason = "không còn đồng đội nào"; return false; }
        switch (cmd)
        {
            case SquadCommand.FallIn:
            case SquadCommand.ReportStatus:
                return true;
            case SquadCommand.Restrain:
                if (FindRestrainTarget(point ?? playerPos) != null) return true;
                reason = "không có ai đầu hàng chờ còng gần đó";
                return false;
            case SquadCommand.MoveTo:
            case SquadCommand.Cover:
                if (point == null) { reason = "chưa chỉ điểm"; return false; }
                if (cmd == SquadCommand.MoveTo && !NavMesh.SamplePosition(point.Value, out _, 1.5f, NavMesh.AllAreas))
                {
                    reason = "điểm chỉ không đi tới được";
                    return false;
                }
                return true;
        }
        if (door == null) { reason = "không có cửa gần"; return false; }
        if (door.stackLeft == null || door.stackRight == null || door.clearLeft == null || door.clearRight == null)
        {
            reason = $"cửa {door.name} chưa đặt đủ stackLeft/Right, clearLeft/Right";
            return false;
        }
        // Điểm stack/clear chỉ đặt một phía: chặn lệnh từ phía kia của cửa.
        if (SideOf(door, playerPos) != SideOf(door, door.stackLeft.position))
        {
            reason = "đứng ở phía kia của cửa";
            return false;
        }

        switch (cmd)
        {
            case SquadCommand.OpenDoor when IsOpen(door):
                reason = "cửa đã mở";
                return false;
            case SquadCommand.PickLock when door.State != DoorState.Locked:
                reason = "cửa không khóa";
                return false;
            case SquadCommand.ThrowFlash when door.State == DoorState.Locked:
                reason = "cửa khóa, phá khóa trước";
                return false;
            case SquadCommand.ThrowFlash when !CanThrowFlash(Flasher):
                reason = "không có lựu đạn choáng";
                return false;
            case SquadCommand.Mirror when IsOpen(door):
                reason = "cửa đang mở, nhìn thẳng vào được";
                return false;
        }
        return true;
    }

    /// <summary>
    /// Ra lệnh cho cả đội. flashFirst null = dùng FlashFirst hiện tại. issuerPos null = vị trí đầu người chơi
    /// (dùng để kiểm tra đứng đúng phía cửa). lead: đồng đội làm việc chính (mở cửa, phá khóa, ném flash, đi / vào
    /// phòng trước), null = A mở / phá khóa, B ném flash. point: điểm chỉ cho Di chuyển / Yểm trợ / Còng.
    /// Trả false nếu lệnh không hợp lệ.
    /// </summary>
    public bool Issue(SquadCommand cmd, DoorController door = null, bool? flashFirst = null, Vector3? issuerPos = null,
        Teammate lead = null, Vector3? point = null)
    {
        Transform head = Teammate.PlayerHead;
        Vector3 playerPos = issuerPos ?? (head != null ? head.position : transform.position);
        Teammate prevLead = _lead;
        _lead = lead; // đặt trước để CanIssue kiểm tra lựu đạn của đúng người ném
        if (!NeedsDoor(cmd)) door = null;
        if (!CanIssue(cmd, door, playerPos, point, out string reason))
        {
            _lead = prevLead;
            LastRejectReason = reason;
            Debug.LogWarning($"[Squad] Không ra được lệnh {cmd}: {reason}.", this);
            return false;
        }

        StopRoutine();
        LastRejectReason = null;
        int serial = ++CommandSerial;
        _runningSerial = serial;
        CurrentCommand = cmd;
        CurrentDoor = door;
        bool flash = flashFirst ?? _flashFirst;
        bool clears = cmd == SquadCommand.OpenAndClear || cmd == SquadCommand.BreachAndClear;
        Debug.Log($"[Squad] Lệnh {cmd}{(CurrentDoor != null ? $" cửa {door.name}" : "")}{(point.HasValue && NeedsPoint(cmd) ? $" tới {point.Value}" : "")}" +
                  $"{(flash && clears ? " + flashbang" : "")}{(Lead != null ? $", {Lead.CallSign} thực hiện" : "")}.", this);
        if (cmd != SquadCommand.ReportStatus) (Lead ?? Opener).SayAck();

        if (cmd == SquadCommand.FallIn) TutorialSignals.Raise(Sig.CmdFallIn, this);
        else if (door != null) TutorialSignals.Raise(Sig.CmdDoor, door.gameObject);

        IEnumerator body = cmd switch
        {
            SquadCommand.FallIn => FallIn(),
            SquadCommand.StackUp => StackUp(door),
            SquadCommand.OpenDoor => OpenOnly(door),
            SquadCommand.PickLock => PickLockOnly(door),
            SquadCommand.ThrowFlash => FlashOnly(door),
            SquadCommand.BreachAndClear => Clear(door, flash, true),
            SquadCommand.Restrain => RestrainCmd(FindRestrainTarget(point ?? playerPos)),
            SquadCommand.MoveTo => MoveToCmd(point.Value),
            SquadCommand.Cover => CoverCmd(point.Value),
            SquadCommand.Mirror => MirrorCmd(door),
            SquadCommand.ReportStatus => ReportStatusCmd(),
            _ => Clear(door, flash, _autoPickLock),
        };
        Coroutine routine = StartCoroutine(Run(serial, cmd, body));
        // Lệnh xong ngay trong frame này (vd. đã đứng đúng chỗ) thì Run đã dọn, không giữ coroutine đã chết.
        if (_runningSerial == serial) _routine = routine;
        return true;
    }

    /// <summary>
    /// Dừng lệnh đang chạy, cả đội đứng tại chỗ chĩa súng về hướng đang canh (dùng khi bỏ tick ô test lệnh).
    /// </summary>
    public void Cancel()
    {
        if (_runningSerial < 0) return;
        StopRoutine();
        CommandSerial++;
        foreach (Teammate t in Members())
        {
            Vector3 look = t.OrderLook != Vector3.zero
                ? t.OrderLook
                : t.transform.position + t.transform.forward * 3f + Vector3.up * 1.3f;
            t.GiveOrder(TeammateOrder.HoldAim, t.transform.position, look, t.OrderDoor);
        }
        Debug.Log($"[Squad] Hủy lệnh {CurrentCommand}: cả đội đứng tại chỗ.", this);
    }

    private void StopRoutine()
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = null;
        _runningSerial = -1;
        WaitingForPlayer = false;
    }

    /// <summary>Chạy một lệnh tới hết rồi đánh dấu xong (IsRunning(serial) = false).</summary>
    private IEnumerator Run(int serial, SquadCommand cmd, IEnumerator body)
    {
        yield return body;
        if (_runningSerial != serial) yield break;
        _runningSerial = -1;
        _routine = null;
        WaitingForPlayer = false;
        Debug.Log($"[Squad] Xong lệnh {cmd}.", this);
    }

    /// <summary>Đi theo người chơi; xong khi mọi người đã tới chỗ đứng sau lưng người chơi (tối đa StepTimeout).</summary>
    private IEnumerator FallIn()
    {
        foreach (Teammate t in Members()) t.GiveOrder(TeammateOrder.Follow);
        float elapsed = 0f;
        while (elapsed < _stepTimeout)
        {
            bool all = true;
            foreach (Teammate t in Members())
                if (t.TryGetFollowSlot(out Vector3 slot) && Vector3.Distance(Flat(t.transform.position), Flat(slot)) > 1f)
                    all = false;
            if (all) yield break;
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    // ---------- Các lệnh ----------

    private IEnumerator StackUp(DoorController door)
    {
        Vector3 look = DoorAimPoint(door);
        Teammate a = Alive(A), b = Alive(B);
        // Chỉ còn 1 người: đứng chỗ stackLeft.
        Teammate left = a ?? b, right = a != null ? b : null;

        if (_moveOneByOne && left != null && right != null)
        {
            // Lần lượt: người được chỉ định (không có thì A) đi trước, tới chỗ thì chĩa súng vào cửa, rồi người sau mới đi.
            bool rightFirst = Lead == right;
            Teammate first = rightFirst ? right : left, second = rightFirst ? left : right;
            Transform firstSpot = rightFirst ? door.stackRight : door.stackLeft;
            Transform secondSpot = rightFirst ? door.stackLeft : door.stackRight;

            int i1 = first.GiveOrder(TeammateOrder.MoveTo, firstSpot.position, look, door);
            second.GiveOrder(TeammateOrder.HoldAim, second.transform.position, look, door); // đứng chờ tới lượt
            yield return WaitDone(first, i1);
            if (Alive(first) != null) first.GiveOrder(TeammateOrder.HoldAim, first.transform.position, look, door);

            if (Alive(second) != null)
            {
                int i2 = second.GiveOrder(TeammateOrder.MoveTo, secondSpot.position, look, door);
                yield return WaitDone(second, i2);
            }
        }
        else
        {
            int ia = left != null ? left.GiveOrder(TeammateOrder.MoveTo, door.stackLeft.position, look, door) : -1;
            int ib = right != null ? right.GiveOrder(TeammateOrder.MoveTo, door.stackRight.position, look, door) : -1;
            yield return WaitDone(left, ia, right, ib);
        }

        HoldAll(look, door);
        TutorialSignals.Raise(Sig.SquadStacked, door.gameObject);
        OnStacked?.Invoke(door);
        if (Opener != null) Opener.SayStacked();
    }

    private IEnumerator OpenOnly(DoorController door)
    {
        if (!IsStackedAt(door)) yield return StackUp(door);
        yield return OpenSequence(door, false, false);
        HoldAll(IsOpen(door) ? RoomLookPoint(door) : DoorAimPoint(door), door);
    }

    private IEnumerator PickLockOnly(DoorController door)
    {
        if (!IsStackedAt(door)) yield return StackUp(door);
        if (door.State == DoorState.Locked) yield return PickLock(door);
        HoldAll(DoorAimPoint(door), door);
    }

    private IEnumerator FlashOnly(DoorController door)
    {
        if (!IsStackedAt(door)) yield return StackUp(door);
        yield return OpenSequence(door, false, false);
        if (IsOpen(door)) yield return ThrowFlash(door);
        HoldAll(IsOpen(door) ? RoomLookPoint(door) : DoorAimPoint(door), door);
    }

    private IEnumerator Clear(DoorController door, bool flash, bool pickIfLocked)
    {
        if (!IsStackedAt(door)) yield return StackUp(door);
        yield return OpenSequence(door, pickIfLocked, true);
        if (door == null || !IsOpen(door)) yield break;
        if (flash) yield return ThrowFlash(door);
        yield return SliceThePie(door);
        yield return Entry(door);
    }

    /// <summary>
    /// Slicing the pie: đứng ở chỗ stack, mỗi người quét súng dần qua khe cửa: bắt đầu từ giữa phòng gần cửa, xoay dần
    /// sang phía đối diện và sâu vào trong (lộ thêm từng "lát" phòng) trước khi vào.
    /// </summary>
    private IEnumerator SliceThePie(DoorController door)
    {
        if (_sliceTime <= 0f || door == null) yield break;
        HoldAll(DoorAimPoint(door), door);
        Vector3 mid = DoorMid(door), center = RoomCenter(door);
        Vector3 into = Flat(center - mid);
        if (into.sqrMagnitude < 0.01f) yield break;
        into.Normalize();
        Vector3 lateral = Vector3.Cross(Vector3.up, into);
        for (float t = 0f; t < _sliceTime; t += Time.deltaTime)
        {
            float k = t / _sliceTime;
            foreach (Teammate m in Members())
            {
                if (m.Order != TeammateOrder.HoldAim) continue;
                float side = Vector3.Dot(m.transform.position - mid, lateral) >= 0f ? 1f : -1f;
                Vector3 look = center + into * Mathf.Lerp(0.5f, 3f, k) - lateral * (side * Mathf.Lerp(0.3f, 2.5f, k)) + Vector3.up * 1.3f;
                m.SetOrderLook(look);
            }
            yield return null;
        }
    }

    /// <summary>
    /// Mở cửa: khóa mà pickIfLocked thì A phá khóa trước. Vẫn không mở được thì báo "Cửa khóa!" và
    /// (waitForPlayer) chờ người chơi mở / phá cửa, không timeout, ra lệnh khác thì hủy chờ.
    /// </summary>
    private IEnumerator OpenSequence(DoorController door, bool pickIfLocked, bool waitForPlayer)
    {
        if (IsOpen(door)) yield break;
        Vector3 look = DoorAimPoint(door);

        if (pickIfLocked && door.State == DoorState.Locked) yield return PickLock(door);

        Teammate opener = Opener;
        if (opener != null && !IsOpen(door) && door.State != DoorState.Locked)
        {
            int id = opener.GiveOrder(TeammateOrder.OpenDoor, opener.transform.position, look, door);
            // Đi tới tay nắm + mở + quay về chỗ stack (+ chờ người chơi buông cửa): cho thêm thời gian.
            yield return WaitDone(opener, id, timeout: _stepTimeout * 2f);
        }
        if (IsOpen(door)) yield break;

        // Cửa khóa (hoặc người chơi đang giữ cửa).
        if (Opener != null)
        {
            if (door.State == DoorState.Locked) Opener.SayDoorLocked();
            else Opener.Say("Can't open the door!", NPCVoiceLine.DoorLocked);
        }
        TutorialSignals.Raise(Sig.SquadDoorLocked, door.gameObject);
        OnDoorLocked?.Invoke(door);
        HoldAll(look, door);
        if (!waitForPlayer) yield break;

        WaitingForPlayer = true;
        yield return new WaitUntil(() => door == null || IsOpen(door));
        WaitingForPlayer = false;
    }

    /// <summary>A tới ổ khóa phá khóa rồi về chỗ đang đứng; B chĩa súng vào cửa yểm trợ.</summary>
    private IEnumerator PickLock(DoorController door)
    {
        Teammate picker = Opener;
        if (picker == null) yield break;
        Vector3 look = DoorAimPoint(door);
        foreach (Teammate t in Members())
            if (t != picker) t.GiveOrder(TeammateOrder.HoldAim, t.transform.position, look, door);

        int id = picker.GiveOrder(TeammateOrder.PickLock, picker.transform.position, look, door);
        yield return WaitDone(picker, id, timeout: _stepTimeout * 2f + picker.PickLockTime);
        if (door != null && door.State != DoorState.Locked) OnDoorUnlocked?.Invoke(door);
    }

    private IEnumerator ThrowFlash(DoorController door)
    {
        Teammate f = Flasher;
        if (f == null || !CanThrowFlash(f)) yield break;
        Vector3 center = RoomCenter(door);
        int id = f.GiveOrder(TeammateOrder.ThrowFlash, center, center, door);
        yield return WaitDone(f, id);

        // Flash của chính đội: quay mặt khỏi cửa tới khi flash nổ (giảm choáng, NPCStun tính theo hướng mặt).
        if (_flashTurnAwayMax > 0f)
        {
            Vector3 mid = DoorMid(door);
            foreach (Teammate m in Members())
            {
                Vector3 away = Flat(m.transform.position - mid);
                if (away.sqrMagnitude < 0.01f) away = -Flat(RoomCenter(door) - mid);
                m.GiveOrder(TeammateOrder.HoldAim, m.transform.position, m.transform.position + away.normalized * 3f + Vector3.up * 1.3f, door);
            }
            float until = Time.time + _flashTurnAwayMax;
            yield return new WaitUntil(() => Time.time > until || !FlashbangNear(center, 20f));
            HoldAll(DoorAimPoint(door), door);
            yield return new WaitForSeconds(Mathf.Min(_flashWait, 0.5f)); // quay lại cửa
        }
        else yield return new WaitForSeconds(_flashWait);
    }

    /// <summary>Còn quả flashbang (chưa nổ) trong range quanh point.</summary>
    private static bool FlashbangNear(Vector3 point, float range)
    {
        foreach (VRSwat.Grenades.Flashbang f in FindObjectsByType<VRSwat.Grenades.Flashbang>(FindObjectsSortMode.None))
            if (f != null && f.isActiveAndEnabled && Vector3.Distance(f.transform.position, point) <= range) return true;
        return false;
    }

    /// <summary>Kiểu vào phòng: Cross = mỗi người cắt chéo sang phía đối diện; ButtonHook = mỗi người móc vào phía mình.</summary>
    public enum EntryStyle { Cross, ButtonHook }

    /// <summary>Kiểu vào phòng lần Entry gần nhất (để xem / test).</summary>
    public EntryStyle LastEntryStyle { get; private set; }

    /// <summary>Người vào trước trong lần Entry gần nhất.</summary>
    public Teammate LastEntryFirst { get; private set; }

    /// <summary>
    /// Xông vào theo bản lề cửa (DoorController.HingeLocalPosition / LeafSide, chỉ đọc; xem IsHandleSide):
    /// - Người đứng phía TAY NẮM vào trước (người được chỉ định trong lệnh test thì người đó vào trước).
    /// - Cánh cửa mở vào trong phòng (nằm dọc tường phía bản lề, che góc đó): Cross: mỗi người cắt chéo sang phía đối
    ///   diện, người phía tay nắm lo luôn góc sau cánh cửa. Cánh cửa mở ra phía đội: Button-hook: mỗi người móc vào
    ///   phía mình, ôm sát tường.
    /// </summary>
    private IEnumerator Entry(DoorController door)
    {
        Vector3 inside = RoomLookPoint(door);
        Teammate a = Alive(A), b = Alive(B);
        // Ai đang đứng chỗ stack nào.
        Teammate atLeft = a, atRight = b;
        if (a != null && b != null && Dist(a, door.stackRight) + Dist(b, door.stackLeft) < Dist(a, door.stackLeft) + Dist(b, door.stackRight))
            (atLeft, atRight) = (b, a);
        bool leftIsHandle = IsHandleSide(door, door.stackLeft.position);
        Teammate handleSide = leftIsHandle ? atLeft : atRight, hingeSide = leftIsHandle ? atRight : atLeft;
        Teammate first = handleSide ?? hingeSide, second = handleSide != null ? hingeSide : null;
        if (Lead != null && Lead == second) (first, second) = (second, first);

        int teamSide = SideOf(door, DoorMid(door));
        EntryStyle style = door.LeafSide != 0 && door.LeafSide == teamSide ? EntryStyle.ButtonHook : EntryStyle.Cross;
        LastEntryStyle = style;
        LastEntryFirst = first;
        Transform ClearFor(Teammate t)
        {
            bool onLeft = t == atLeft;
            bool goLeft = style == EntryStyle.ButtonHook ? onLeft : !onLeft;
            bool clearLeftOnLeft = SameLateralSide(door, door.clearLeft.position, door.stackLeft.position);
            return goLeft == clearLeftOnLeft ? door.clearLeft : door.clearRight;
        }
        Debug.Log($"[Squad] Vào phòng {door.name} kiểu {style}: {(first != null ? first.CallSign : "-")} (phía tay nắm) vào trước.", this);

        int ia = first != null ? first.GiveOrder(TeammateOrder.ClearRoom, ClearFor(first).position, inside, door) : -1;
        yield return new WaitForSeconds(_entryDelay);
        int ib = second != null ? second.GiveOrder(TeammateOrder.ClearRoom, ClearFor(second).position, inside, door) : -1;
        // Chạy vào + quét từng góc: cho thêm thời gian.
        yield return WaitDone(first, ia, second, ib, _stepTimeout * 2f);

        // 2 s liền không ai thấy địch (và không còn ai phải canh nghi phạm) thì phòng an toàn.
        float quiet = 0f;
        while (quiet < _roomClearQuiet)
        {
            bool threat = false;
            foreach (Teammate t in Members()) threat |= t.HasEnemyTarget || t.HasGuardTarget;
            quiet = threat ? 0f : quiet + Time.deltaTime;
            yield return null;
        }

        Teammate speaker = Opener;
        if (speaker != null) speaker.SayRoomClear();
        TutorialSignals.Raise(Sig.SquadRoomClear, door.gameObject);
        OnRoomClear?.Invoke(door);
        HoldAll(inside, door);
    }

    // ---------- Lệnh mới: Còng / Di chuyển / Yểm trợ / Soi gương / Báo cáo ----------

    /// <summary>
    /// Còng: người được chỉ định (không có thì người gần nhất) tới sau lưng người đã đầu hàng, hô "Restraining", còng
    /// 2-3 s, báo "Suspect / Civilian secured". Người kia canh (nghi phạm) / chĩa súng yểm trợ.
    /// </summary>
    private IEnumerator RestrainCmd(GameObject target)
    {
        if (target == null) yield break;
        Teammate cuffer = Lead ?? Nearest(target.transform.position);
        if (cuffer == null) yield break;
        bool suspect = target.TryGetComponent(out NPC _);
        Vector3 chest = NPCConditionUtils.CenterOf(target);

        // Người còng thôi canh (việc canh giao cho người kia nếu rảnh), người kia canh / yểm trợ.
        GameObject handOver = cuffer.GuardTarget;
        if (handOver != null) cuffer.SetGuardTarget(null);
        foreach (Teammate other in Members())
        {
            if (other == cuffer) continue;
            if (suspect && !other.HasGuardTarget) other.SetGuardTarget(target);
            else if (handOver != null && handOver != target && !other.HasGuardTarget) other.SetGuardTarget(handOver);
            other.GiveOrder(TeammateOrder.HoldAim, other.transform.position, chest);
        }

        int id = cuffer.GiveOrder(TeammateOrder.Restrain, target.transform.position, chest, null, target);
        yield return WaitDone(cuffer, id, timeout: _stepTimeout * 2f + 4f);
        if (Alive(cuffer) != null) cuffer.GiveOrder(TeammateOrder.HoldAim, cuffer.transform.position, chest);
    }

    /// <summary>Di chuyển: người đầu (chỉ định / A) đi tới điểm, người sau đi theo lệch sau 1.2 m; tới nơi chĩa súng về phía trước.</summary>
    private IEnumerator MoveToCmd(Vector3 point)
    {
        if (!NavMesh.SamplePosition(point, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) yield break;
        point = hit.position;
        Teammate first = Lead ?? Alive(A) ?? Alive(B);
        Teammate second = null;
        foreach (Teammate m in Members()) if (m != first) second = m;
        if (first == null) yield break;

        Vector3 dir = Flat(point - first.transform.position);
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : first.transform.forward;
        Vector3 look = point + dir * 4f + Vector3.up * 1.3f;
        Vector3 secondSpot = point - dir * 1.2f + Vector3.Cross(Vector3.up, dir) * (second != null && second.Slot == 1 ? 0.6f : -0.6f);
        if (NavMesh.SamplePosition(secondSpot, out NavMeshHit sh, 1f, NavMesh.AllAreas)) secondSpot = sh.position;
        else secondSpot = point;

        first.SayMoving();
        int i1 = first.GiveOrder(TeammateOrder.MoveTo, point, look);
        if (second != null) second.GiveOrder(TeammateOrder.HoldAim, second.transform.position, look);
        yield return new WaitForSeconds(0.8f); // đi lần lượt
        int i2 = second != null ? second.GiveOrder(TeammateOrder.MoveTo, secondSpot, look) : -1;
        yield return WaitDone(first, i1, second, i2, _stepTimeout * 2f);
        HoldAll(look, null);
    }

    /// <summary>Yểm trợ: cả đội đứng tại chỗ chĩa súng vào điểm chỉ (tới khi có lệnh mới).</summary>
    private IEnumerator CoverCmd(Vector3 point)
    {
        HoldAll(point, null);
        yield break;
    }

    /// <summary>Soi gương: stack up, người soi (chỉ định / A) quỳ ở chân cửa soi gương dưới khe, báo, về chỗ stack.</summary>
    private IEnumerator MirrorCmd(DoorController door)
    {
        if (!IsStackedAt(door)) yield return StackUp(door);
        Teammate m = Opener;
        if (m == null) yield break;
        Vector3 look = DoorAimPoint(door);
        foreach (Teammate t in Members())
            if (t != m) t.GiveOrder(TeammateOrder.HoldAim, t.transform.position, look, door);
        int id = m.GiveOrder(TeammateOrder.Mirror, m.transform.position, look, door);
        yield return WaitDone(m, id, timeout: _stepTimeout * 2f + m.MirrorTime);
        HoldAll(look, door);
    }

    /// <summary>Báo cáo tình hình: nghi phạm còn / đã còng / gục, con tin chưa cứu / đã cứu, máu đội.</summary>
    private IEnumerator ReportStatusCmd()
    {
        int susFree = 0, susCuffed = 0, susDown = 0, civFree = 0, civSafe = 0, civDown = 0;
        foreach (NPC n in FindObjectsByType<NPC>(FindObjectsSortMode.None))
        {
            if (n.Team != Team.Suspect) continue;
            if (n.IsDead) susDown++;
            else if (n.IsRestrained) susCuffed++;
            else susFree++;
        }
        foreach (Hostage h in FindObjectsByType<Hostage>(FindObjectsSortMode.None))
        {
            if (h.IsDead) civDown++;
            else if (h.IsRestrained) civSafe++;
            else civFree++;
        }
        var team = new List<string>();
        foreach (Teammate t in Teammate.Active)
            team.Add(t.IsDead ? $"{t.CallSign} down" : $"{t.CallSign} {t.Npc.Health / Mathf.Max(1f, t.Npc.MaxHealth):P0}");
        LastStatus = $"Status: {susFree} suspect(s) unsecured, {susCuffed} restrained, {susDown} down. " +
                     $"{civFree} civilian(s) unsecured, {civSafe} secured{(civDown > 0 ? $", {civDown} down" : "")}. " +
                     $"Team: {string.Join(", ", team)}.";
        Teammate speaker = Lead ?? Opener;
        if (speaker != null) speaker.Say(LastStatus, NPCVoiceLine.ReportStatus);
        yield break;
    }

    /// <summary>
    /// Người đã đầu hàng chờ còng (nghi phạm / con tin: còn sống, chưa còng, không bị đồng đội khác đang còng) gần
    /// point nhất trong RestrainRange.
    /// </summary>
    public GameObject FindRestrainTarget(Vector3 point)
    {
        GameObject best = null;
        float bestDist = _restrainRange;
        foreach (NPC n in NPC.Active)
        {
            if (n == null || n.Team != Team.Suspect || n.IsDead || !n.IsSurrendered || n.IsRestrained) continue;
            if (n.TryGetComponent(out NPCCuffable c) && c.IsAICuffing && !BeingCuffedByUs(n.gameObject)) continue;
            Consider(n.gameObject);
        }
        foreach (Hostage h in Hostage.Active)
        {
            if (h == null || h.IsDead || !h.IsSurrendered || h.IsRestrained) continue;
            Consider(h.gameObject);
        }
        return best;

        void Consider(GameObject go)
        {
            float d = Vector3.Distance(Flat(go.transform.position), Flat(point));
            if (d < bestDist) { bestDist = d; best = go; }
        }
    }

    private bool BeingCuffedByUs(GameObject target)
    {
        foreach (Teammate t in Members()) if (t.Order == TeammateOrder.Restrain && t.OrderTarget == target) return true;
        return false;
    }

    /// <summary>
    /// Đếm người trong phòng phía sau cửa (soi gương): nghi phạm chưa còng, con tin chưa còng. Dùng
    /// TeammateRoomCorners.roomVolume nếu có; không thì người ở phía bên kia cửa trong MirrorRange m có đường NavMesh
    /// từ ngay sau cửa tới (không tính người ở phòng khác sau tường).
    /// </summary>
    public void ScanRoom(DoorController door, out int suspects, out int civilians)
    {
        suspects = civilians = 0;
        if (door == null) return;
        TeammateRoomCorners room = door.GetComponent<TeammateRoomCorners>();
        bool useVolume = room != null && room.roomVolume != null;
        int teamSide = SideOf(door, DoorMid(door));
        Vector3 mid = DoorMid(door);
        Vector3 beyond = mid + Flat(RoomCenter(door) - mid).normalized * 0.8f; // ngay sau khe cửa, phía phòng
        bool hasBeyond = NavMesh.SamplePosition(beyond, out NavMeshHit bh, 1.5f, NavMesh.AllAreas);
        var path = new NavMeshPath();

        bool InRoom(Vector3 p)
        {
            if (useVolume) return room.Contains(p);
            if (SideOf(door, p) == teamSide || Vector3.Distance(Flat(p), Flat(mid)) > _mirrorRange) return false;
            if (!hasBeyond) return true;
            if (!NavMesh.CalculatePath(bh.position, p, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return false;
            float len = 0f;
            for (int i = 1; i < path.corners.Length; i++) len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            return len <= _mirrorRange * 1.5f;
        }

        foreach (NPC n in NPC.Active)
            if (n != null && n.Team == Team.Suspect && !n.IsDead && !n.IsRestrained && InRoom(n.transform.position)) suspects++;
        foreach (Hostage h in Hostage.Active)
            if (h != null && !h.IsDead && !h.IsRestrained && InRoom(h.transform.position)) civilians++;
    }

    /// <summary>Node Mirror soi xong: đếm, lưu LastMirror, người soi báo.</summary>
    public MirrorReport ReportMirror(Teammate who, DoorController door)
    {
        ScanRoom(door, out int suspects, out int civilians);
        LastMirror = new MirrorReport { Door = door, Suspects = suspects, Civilians = civilians, Time = Time.time };
        if (who != null) who.SayMirror(suspects, civilians);
        Debug.Log($"[Squad] Soi gương {door.name}: {suspects} nghi phạm, {civilians} con tin.", this);
        return LastMirror;
    }

    private void HoldAll(Vector3 look, DoorController door)
    {
        foreach (Teammate t in Members()) t.GiveOrder(TeammateOrder.HoldAim, t.transform.position, look, door);
    }

    /// <summary>
    /// Chờ đồng đội báo xong việc. Mỗi người tối đa timeout (mặc định StepTimeout) giây, không tính lúc đang
    /// giao chiến / canh nghi phạm (xong thì đồng đội tự quay lại làm tiếp). Người chết hoặc đã nhận lệnh khác thì thôi chờ.
    /// </summary>
    private IEnumerator WaitDone(Teammate a, int ia, Teammate b = null, int ib = -1, float timeout = -1f)
    {
        float limit = timeout > 0f ? timeout : _stepTimeout;
        float ta = 0f, tb = 0f;
        while (Pending(a, ia, ref ta, limit) | Pending(b, ib, ref tb, limit)) yield return null;
    }

    private static bool Pending(Teammate t, int id, ref float elapsed, float limit)
    {
        if (t == null || t.IsDead || id < 0 || t.OrderId != id || t.IsOrderFinished) return false;
        if (!t.HasEnemyTarget && !t.HasGuardTarget) elapsed += Time.deltaTime;
        if (elapsed <= limit) return true;
        Debug.LogWarning($"[Squad] {t.CallSign} quá {limit:0} s chưa xong việc {t.Order}, đi bước tiếp.", t);
        return false;
    }

    // ---------- Canh nghi phạm ----------

    private void OnSuspectSurrendered(NPC suspect)
    {
        if (suspect == null || suspect.Team != Team.Suspect) return;
        Teammate best = null;
        float bestDist = _guardRadius;
        foreach (Teammate t in Members())
        {
            if (t.HasGuardTarget) continue;
            float d = Vector3.Distance(t.transform.position, suspect.transform.position);
            if (d <= bestDist) { bestDist = d; best = t; }
        }
        if (best == null) return;
        best.SetGuardTarget(suspect.gameObject);
        _guardSince[suspect.gameObject] = Time.time;
        Debug.Log($"[Squad] {best.CallSign} canh {suspect.name}.", best);
    }

    private void OnSuspectDone(NPC suspect)
    {
        if (suspect == null) return;
        _guardSince.Remove(suspect.gameObject);
        foreach (Teammate t in Members())
            if (t.GuardTarget == suspect.gameObject) t.SetGuardTarget(null);
    }

    private void OnTeammateDied(Teammate t)
    {
        Debug.LogWarning($"[Squad] Đồng đội {t.CallSign} đã chết: thua màn.", t);
        foreach (Teammate other in Members())
            if (other != t) other.Say("Officer down!", NPCVoiceLine.ManDown);
        OnTeammateDown?.Invoke(t);
    }

    // ---------- Phím test ----------

    private void UpdateTestKeys()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (kb.gKey.wasPressedThisFrame)
        {
            _flashFirst = !_flashFirst;
            Debug.Log($"[Squad] Flashbang first: {(_flashFirst ? "BẬT" : "TẮT")}", this);
        }
        if (kb.digit4Key.wasPressedThisFrame) Issue(SquadCommand.FallIn);

        SquadCommand? cmd = kb.digit1Key.wasPressedThisFrame ? SquadCommand.StackUp
            : kb.digit2Key.wasPressedThisFrame ? SquadCommand.OpenAndClear
            : kb.digit3Key.wasPressedThisFrame ? SquadCommand.BreachAndClear
            : kb.digit5Key.wasPressedThisFrame ? SquadCommand.OpenDoor
            : kb.digit6Key.wasPressedThisFrame ? SquadCommand.PickLock
            : kb.digit7Key.wasPressedThisFrame ? SquadCommand.ThrowFlash
            : null;
        if (cmd == null) return;

        DoorController door = FindDoorNearPlayer();
        if (door == null) Debug.LogWarning($"[Squad] Không có cửa nào (đủ điểm stack, đúng phía) trong {_doorRange} m quanh người chơi.", this);
        else Issue(cmd.Value, door);
    }

    /// <summary>Cửa gần người chơi nhất có đủ điểm AI và người chơi đứng đúng phía stack.</summary>
    public DoorController FindDoorNearPlayer()
    {
        Transform head = Teammate.PlayerHead;
        return head != null ? FindTargetDoor(head.position, head.forward) : null;
    }

    /// <summary>
    /// Cửa người chơi định ra lệnh: trong DoorRange, đủ điểm AI, đứng đúng phía stack; ưu tiên cửa gần và nằm
    /// gần hướng nhìn / hướng trỏ (forward). forward = zero thì chỉ xét khoảng cách.
    /// </summary>
    public DoorController FindTargetDoor(Vector3 origin, Vector3 forward)
    {
        DoorController best = null;
        float bestScore = float.PositiveInfinity;
        Vector3 fwd = Flat(forward);
        bool useAngle = fwd.sqrMagnitude > 0.0001f;
        foreach (DoorController door in DoorController.All)
        {
            if (door == null || !CanIssue(SquadCommand.StackUp, door, origin)) continue;
            Vector3 to = Flat(DoorMid(door) - origin);
            float d = to.magnitude;
            if (d > _doorRange) continue;
            float score = d + (useAngle && d > 0.01f ? Vector3.Angle(fwd, to) / 30f : 0f); // 30° lệch = 1 m xa hơn
            if (score < bestScore) { bestScore = score; best = door; }
        }
        return best;
    }

    /// <summary>Cửa gần point nhất (trong range) có đủ điểm stack/clear, không xét phía (dùng cho test).</summary>
    public DoorController FindDoorNear(Vector3 point, float range)
    {
        DoorController best = null;
        float bestDist = range;
        foreach (DoorController door in DoorController.All)
        {
            if (door == null || door.stackLeft == null || door.stackRight == null || door.clearLeft == null || door.clearRight == null) continue;
            float d = Vector3.Distance(Flat(DoorMid(door)), Flat(point));
            if (d < bestDist) { bestDist = d; best = door; }
        }
        return best;
    }

    // ---------- Tiện ích ----------

    private IEnumerable<Teammate> Members()
    {
        Teammate a = Alive(A), b = Alive(B);
        if (a != null) yield return a;
        if (b != null && b != a) yield return b;
    }

    private static Teammate Alive(Teammate t) => t != null && !t.IsDead ? t : null;

    private Teammate Nearest(Vector3 point)
    {
        Teammate best = null;
        float bestDist = float.PositiveInfinity;
        foreach (Teammate t in Members())
        {
            float d = Vector3.Distance(t.transform.position, point);
            if (d < bestDist) { bestDist = d; best = t; }
        }
        return best;
    }

    private static float Dist(Teammate t, Transform spot) =>
        t == null || spot == null ? 0f : Vector3.Distance(Flat(t.transform.position), Flat(spot.position));

    /// <summary>
    /// point (chỗ stack) nằm phía tay nắm cửa theo trục X local của cửa. Bản lề nằm ở một mép khung, nên phía tay nắm =
    /// hướng từ bản lề về giữa ô cửa (giữa 2 điểm stack). Không dùng LeafAxisSign vì đó là chiều quay, không phải phía
    /// của cánh cửa (vd. Door_Room_1: LeafAxisSign +1 nhưng tay nắm ở phía -X). Bản lề trùng giữa ô cửa thì dùng tay nắm
    /// (DoorHandleGrab / LockPoint).
    /// </summary>
    public static bool IsHandleSide(DoorController door, Vector3 point)
    {
        float hingeX = door.HingeLocalPosition.x;
        float centerX = door.transform.InverseTransformPoint(DoorMid(door)).x;
        float toHandle = centerX - hingeX;
        if (Mathf.Abs(toHandle) < 0.1f)
        {
            Transform handle = TeammateNodeUtils.FindDoorHandle(door, 1f) ?? TeammateNodeUtils.FindDoorHandle(door, -1f);
            toHandle = handle != null ? door.transform.InverseTransformPoint(handle.position).x - hingeX : door.LeafAxisSign;
        }
        float x = door.transform.InverseTransformPoint(point).x;
        return Mathf.Sign(x - centerX) == Mathf.Sign(toHandle);
    }

    /// <summary>a và b cùng nằm một bên trục giữa cửa (theo X local của cửa, so với giữa 2 điểm stack).</summary>
    private static bool SameLateralSide(DoorController door, Vector3 a, Vector3 b)
    {
        float mid = door.transform.InverseTransformPoint(DoorMid(door)).x;
        return Mathf.Sign(door.transform.InverseTransformPoint(a).x - mid) == Mathf.Sign(door.transform.InverseTransformPoint(b).x - mid);
    }

    private static bool CanThrowFlash(Teammate t)
    {
        if (t == null) return false;
        NPCGrenadeThrower thrower = t.GetComponentInChildren<NPCGrenadeThrower>();
        return thrower != null && thrower.FlashbangPrefabAssigned && thrower.Flashbangs != 0;
    }

    private Teammate FindSlot(int slot)
    {
        foreach (Teammate t in Teammate.Active)
            if (t.Slot == slot && t != _alpha && t != _bravo) return t;
        // Hai người cùng Slot: người thứ hai làm B.
        if (slot == 1)
            foreach (Teammate t in Teammate.Active)
                if (t != A) return t;
        return null;
    }

    private bool IsStackedAt(DoorController door)
    {
        Teammate a = Alive(A), b = Alive(B);
        bool ok = a == null || Vector3.Distance(Flat(a.transform.position), Flat(door.stackLeft.position)) < 1f;
        Transform bSpot = a != null ? door.stackRight : door.stackLeft;
        return ok && (b == null || Vector3.Distance(Flat(b.transform.position), Flat(bSpot.position)) < 1f);
    }

    public static bool IsOpen(DoorController door) => door.State == DoorState.Open || door.State == DoorState.Breached;

    private static int SideOf(DoorController door, Vector3 point) => door.transform.InverseTransformPoint(point).z >= 0f ? 1 : -1;

    private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

    private static Vector3 DoorMid(DoorController door) => (door.stackLeft.position + door.stackRight.position) * 0.5f;

    /// <summary>Giữa 2 điểm clear (trên sàn): nơi ném flash.</summary>
    private static Vector3 RoomCenter(DoorController door) => (door.clearLeft.position + door.clearRight.position) * 0.5f;

    /// <summary>Chĩa súng vào giữa cửa (ngang ngực), nhìn từ chỗ stack vào phòng.</summary>
    private static Vector3 DoorAimPoint(DoorController door) => RoomCenter(door) + Vector3.up * 1.3f;

    /// <summary>Điểm sâu trong phòng để chĩa súng sau khi vào.</summary>
    private static Vector3 RoomLookPoint(DoorController door)
    {
        Vector3 center = RoomCenter(door);
        Vector3 dir = Flat(center - DoorMid(door));
        return center + (dir.sqrMagnitude > 0.01f ? dir.normalized * 3f : Vector3.zero) + Vector3.up * 1.3f;
    }
}
