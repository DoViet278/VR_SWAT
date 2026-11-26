using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using VRSwat.Doors;
using VRSwat.Tutorial;

/// <summary>Lệnh người chơi ra cho cả đội (menu lệnh VR hoặc phím test).</summary>
public enum SquadCommand
{
    FallIn,
    StackUp,
    OpenAndClear,
    BreachAndClear,
}

/// <summary>
/// Điều phối 2 đồng đội: lo KHI NÀO làm gì. Chia lệnh thành việc đơn lẻ, giao bằng Teammate.GiveOrder và chờ
/// Teammate.ReportDone. Graph của từng đồng đội lo LÀM THẾ NÀO.
///
/// - Fall in        : cả hai đi theo người chơi (việc mặc định khi vào màn).
/// - Stack up       : A -> door.stackLeft, B -> door.stackRight; tới nơi thì chĩa súng vào giữa cửa. Tín hiệu SquadStacked.
/// - Open and clear : stack up nếu chưa đứng đó -> A mở cửa. Mở được -> (B ném flash) -> Entry.
///                    Cửa khóa: "Cửa khóa!", SquadDoorLocked, cả hai chĩa súng ở chỗ stack, chờ người chơi mở / phá cửa
///                    (không timeout, ra lệnh khác thì hủy chờ) -> (flash) -> Entry.
/// - Breach and clear: TẠM chạy giống Open and clear (gọi node BreachDoor đang để trống trước khi mở).
/// - Entry          : A chạy tới clearLeft, 0.4 s sau B tới clearRight. Cả hai tới nơi và 2 s liền không ai thấy địch
///                    -> "Phòng an toàn!", SquadRoomClear, chĩa súng vào trong phòng.
/// - Mỗi bước di chuyển có timeout 10 s (không tính lúc đồng đội đang giao chiến / canh nghi phạm).
/// - NPC địch đầu hàng: giao canh cho đồng đội gần nhất trong 10 m; bị còng / chết thì xóa.
///
/// Điểm nối cho menu lệnh VR: Issue(cmd, door, flashFirst) và CanIssue(cmd, door, playerPos).
/// Trước khi có menu: phím test 1 Stack up, 2 Open and clear, 3 Breach and clear, 4 Fall in, G bật/tắt flash
/// (cửa = cửa gần người chơi nhất có điểm stack, người chơi đứng đúng phía stack).
/// </summary>
[DisallowMultipleComponent]
public class SquadController : MonoBehaviour
{
    [Header("Đồng đội (trống = tự tìm Teammate trong scene theo Slot)")]
    [Tooltip("A: đi bên trái, người mở cửa.")]
    [SerializeField] private Teammate _alpha;
    [Tooltip("B: đi bên phải, người ném flashbang.")]
    [SerializeField] private Teammate _bravo;

    [Header("Thời gian (giây)")]
    [Tooltip("Mỗi bước di chuyển tối đa ngần này giây.")]
    [SerializeField] private float _stepTimeout = 10f;
    [Tooltip("B vào phòng sau A ngần này giây.")]
    [SerializeField] private float _entryDelay = 0.4f;
    [Tooltip("Ném flash xong chờ ngần này giây mới vào.")]
    [SerializeField] private float _flashWait = 1.5f;
    [Tooltip("Vào phòng rồi, ngần này giây liền không ai thấy địch thì báo phòng an toàn.")]
    [SerializeField] private float _roomClearQuiet = 2f;

    [Header("Canh nghi phạm")]
    [Tooltip("NPC đầu hàng trong bán kính này thì đồng đội gần nhất tới canh.")]
    [SerializeField] private float _guardRadius = 10f;

    [Header("Test (thay menu lệnh VR)")]
    [Tooltip("Phím 1 Stack up, 2 Open and clear, 3 Breach and clear, 4 Fall in, G bật/tắt flash.")]
    [SerializeField] private bool _testKeys = true;
    [Tooltip("Phím test chọn cửa gần người chơi nhất trong khoảng này (m).")]
    [SerializeField] private float _testDoorRange = 6f;
    [Tooltip("+ Flashbang first cho Open / Breach and clear.")]
    [SerializeField] private bool _flashFirst;
    [Tooltip("Đồng đội bất tử (bật cho phòng 8-9 của tutorial).")]
    [SerializeField] private bool _invulnerable;

    [Header("Events")]
    [Tooltip("Một đồng đội chết: thua màn.")]
    public UnityEvent<Teammate> OnTeammateDown;
    public UnityEvent<DoorController> OnStacked;
    public UnityEvent<DoorController> OnDoorLocked;
    public UnityEvent<DoorController> OnRoomClear;

    public static SquadController Instance { get; private set; }

    private Coroutine _routine;

    public bool FlashFirst
    {
        get => _flashFirst;
        set => _flashFirst = value;
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

    /// <summary>Lệnh đang chạy (null = chưa ra lệnh nào).</summary>
    public SquadCommand? CurrentCommand { get; private set; }
    public DoorController CurrentDoor { get; private set; }

    /// <summary>Đang chờ người chơi mở / phá cửa khóa.</summary>
    public bool WaitingForPlayer { get; private set; }

    /// <summary>A: người mở cửa (A chết thì B làm).</summary>
    public Teammate Opener => Alive(A) ?? Alive(B);

    /// <summary>B: người ném flash (B chết thì A làm).</summary>
    public Teammate Flasher => Alive(B) ?? Alive(A);

    private Teammate A => _alpha != null ? _alpha : FindSlot(0);
    private Teammate B => _bravo != null ? _bravo : FindSlot(1);

    private void Awake()
    {
        if (Instance != null && Instance != this) Debug.LogWarning("[Squad] Có nhiều SquadController trong scene.", this);
        Instance = this;
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
    }

    // ---------- API cho menu lệnh ----------

    public bool CanIssue(SquadCommand cmd, DoorController door, Vector3 playerPos) => CanIssue(cmd, door, playerPos, out _);

    /// <summary>Lệnh có ra được không (còn đồng đội, cửa có đủ điểm, người chơi đứng đúng phía đặt điểm stack).</summary>
    public bool CanIssue(SquadCommand cmd, DoorController door, Vector3 playerPos, out string reason)
    {
        reason = null;
        if (Opener == null) { reason = "không còn đồng đội nào"; return false; }
        if (cmd == SquadCommand.FallIn) return true;
        if (door == null) { reason = "chưa chọn cửa"; return false; }
        if (door.stackLeft == null || door.stackRight == null || door.clearLeft == null || door.clearRight == null)
        {
            reason = $"cửa {door.name} chưa đặt đủ stackLeft/Right, clearLeft/Right";
            return false;
        }
        // Điểm stack/clear chỉ đặt một phía: chặn lệnh từ phía kia của cửa.
        if (SideOf(door, playerPos) != SideOf(door, door.stackLeft.position))
        {
            reason = $"người chơi đứng ở phía kia của cửa {door.name}";
            return false;
        }
        return true;
    }

    /// <summary>Ra lệnh cho cả đội. flashFirst null = dùng FlashFirst hiện tại. Trả false nếu lệnh không hợp lệ.</summary>
    public bool Issue(SquadCommand cmd, DoorController door = null, bool? flashFirst = null)
    {
        Transform head = Teammate.PlayerHead;
        Vector3 playerPos = head != null ? head.position : transform.position;
        if (!CanIssue(cmd, door, playerPos, out string reason))
        {
            Debug.LogWarning($"[Squad] Không ra được lệnh {cmd}: {reason}.", this);
            return false;
        }

        if (_routine != null) StopCoroutine(_routine);
        _routine = null;
        WaitingForPlayer = false;
        CurrentCommand = cmd;
        CurrentDoor = cmd == SquadCommand.FallIn ? null : door;
        bool flash = flashFirst ?? _flashFirst;
        Debug.Log($"[Squad] Lệnh {cmd}{(CurrentDoor != null ? $" cửa {door.name}" : "")}{(flash && cmd >= SquadCommand.OpenAndClear ? " + flashbang" : "")}.", this);
        Opener.SayAck();

        switch (cmd)
        {
            case SquadCommand.FallIn:
                foreach (Teammate t in Members()) t.GiveOrder(TeammateOrder.Follow);
                TutorialSignals.Raise(Sig.CmdFallIn, this);
                break;
            case SquadCommand.StackUp:
                TutorialSignals.Raise(Sig.CmdDoor, door.gameObject);
                _routine = StartCoroutine(StackUp(door));
                break;
            case SquadCommand.OpenAndClear:
            case SquadCommand.BreachAndClear:
                TutorialSignals.Raise(Sig.CmdDoor, door.gameObject);
                _routine = StartCoroutine(Clear(door, flash, cmd == SquadCommand.BreachAndClear));
                break;
        }
        return true;
    }

    // ---------- Các lệnh ----------

    private IEnumerator StackUp(DoorController door)
    {
        Vector3 look = DoorAimPoint(door);
        Teammate a = Alive(A), b = Alive(B);
        // Chỉ còn 1 người: đứng chỗ stackLeft.
        Teammate left = a ?? b, right = a != null ? b : null;
        int ia = left != null ? left.GiveOrder(TeammateOrder.MoveTo, door.stackLeft.position, look, door) : -1;
        int ib = right != null ? right.GiveOrder(TeammateOrder.MoveTo, door.stackRight.position, look, door) : -1;
        yield return WaitDone(left, ia, right, ib);

        foreach (Teammate t in Members()) t.GiveOrder(TeammateOrder.HoldAim, t.transform.position, look, door);
        TutorialSignals.Raise(Sig.SquadStacked, door.gameObject);
        OnStacked?.Invoke(door);
    }

    private IEnumerator Clear(DoorController door, bool flash, bool breach)
    {
        if (!IsStackedAt(door)) yield return StackUp(door);
        Vector3 look = DoorAimPoint(door);

        if (!IsOpen(door))
        {
            Teammate opener = Opener;
            if (opener == null) yield break;

            // Breach and clear: node BreachDoor đang để trống (trả Success ngay), giữ chỗ để thêm phá cửa sau.
            if (breach && door.State == DoorState.Locked)
            {
                int bid = opener.GiveOrder(TeammateOrder.BreachDoor, opener.transform.position, look, door);
                yield return WaitDone(opener, bid);
            }

            if (!IsOpen(door))
            {
                int id = opener.GiveOrder(TeammateOrder.OpenDoor, opener.transform.position, look, door);
                yield return WaitDone(opener, id);
            }

            if (!IsOpen(door))
            {
                // Cửa khóa (hoặc người chơi đang giữ cửa): báo và chờ người chơi mở / phá cửa, không timeout.
                if (Opener != null) Opener.SayDoorLocked();
                TutorialSignals.Raise(Sig.SquadDoorLocked, door.gameObject);
                OnDoorLocked?.Invoke(door);
                foreach (Teammate t in Members()) t.GiveOrder(TeammateOrder.HoldAim, t.transform.position, look, door);

                WaitingForPlayer = true;
                yield return new WaitUntil(() => door == null || IsOpen(door));
                WaitingForPlayer = false;
                if (door == null) yield break;
            }
        }

        if (flash) yield return ThrowFlash(door);
        yield return Entry(door);
    }

    private IEnumerator ThrowFlash(DoorController door)
    {
        Teammate f = Flasher;
        if (f == null) yield break;
        Vector3 center = RoomCenter(door);
        int id = f.GiveOrder(TeammateOrder.ThrowFlash, center, center, door);
        yield return WaitDone(f, id);
        yield return new WaitForSeconds(_flashWait);
    }

    private IEnumerator Entry(DoorController door)
    {
        Vector3 inside = RoomLookPoint(door);
        Teammate a = Alive(A), b = Alive(B);
        Teammate first = a ?? b, second = a != null ? b : null;

        int ia = first != null ? first.GiveOrder(TeammateOrder.MoveTo, door.clearLeft.position, inside, door) : -1;
        yield return new WaitForSeconds(_entryDelay);
        int ib = second != null ? second.GiveOrder(TeammateOrder.MoveTo, door.clearRight.position, inside, door) : -1;
        yield return WaitDone(first, ia, second, ib);

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
        foreach (Teammate t in Members()) t.GiveOrder(TeammateOrder.HoldAim, t.transform.position, inside, door);
        _routine = null;
    }

    /// <summary>
    /// Chờ đồng đội báo xong việc. Mỗi người tối đa StepTimeout giây, không tính lúc đang giao chiến / canh nghi phạm
    /// (xong thì đồng đội tự quay lại làm tiếp). Người chết hoặc đã nhận lệnh khác thì thôi chờ.
    /// </summary>
    private IEnumerator WaitDone(Teammate a, int ia, Teammate b = null, int ib = -1)
    {
        float ta = 0f, tb = 0f;
        while (Pending(a, ia, ref ta) | Pending(b, ib, ref tb)) yield return null;
    }

    private bool Pending(Teammate t, int id, ref float elapsed)
    {
        if (t == null || t.IsDead || id < 0 || t.OrderId != id || t.IsOrderFinished) return false;
        if (!t.HasEnemyTarget && !t.HasGuardTarget) elapsed += Time.deltaTime;
        if (elapsed <= _stepTimeout) return true;
        Debug.LogWarning($"[Squad] {t.CallSign} quá {_stepTimeout} s chưa xong việc {t.Order}, đi bước tiếp.", t);
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
        Debug.Log($"[Squad] {best.CallSign} canh {suspect.name}.", best);
    }

    private void OnSuspectDone(NPC suspect)
    {
        if (suspect == null) return;
        foreach (Teammate t in Members())
            if (t.GuardTarget == suspect.gameObject) t.SetGuardTarget(null);
    }

    private void OnTeammateDied(Teammate t)
    {
        Debug.LogWarning($"[Squad] Đồng đội {t.CallSign} đã chết: thua màn.", t);
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
            : null;
        if (cmd == null) return;

        DoorController door = FindDoorNearPlayer();
        if (door == null) Debug.LogWarning($"[Squad] Không có cửa nào (đủ điểm stack, đúng phía) trong {_testDoorRange} m quanh người chơi.", this);
        else Issue(cmd.Value, door);
    }

    /// <summary>Cửa gần người chơi nhất có đủ điểm AI và người chơi đứng đúng phía stack.</summary>
    public DoorController FindDoorNearPlayer()
    {
        Transform head = Teammate.PlayerHead;
        if (head == null) return null;
        DoorController best = null;
        float bestDist = _testDoorRange;
        foreach (DoorController door in DoorController.All)
        {
            if (door == null || !CanIssue(SquadCommand.StackUp, door, head.position)) continue;
            Vector3 mid = (door.stackLeft.position + door.stackRight.position) * 0.5f;
            float d = Vector3.Distance(Flat(mid), Flat(head.position));
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

    private static bool IsOpen(DoorController door) => door.State == DoorState.Open || door.State == DoorState.Breached;

    private static int SideOf(DoorController door, Vector3 point) => door.transform.InverseTransformPoint(point).z >= 0f ? 1 : -1;

    private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

    /// <summary>Giữa 2 điểm clear (trên sàn): nơi ném flash.</summary>
    private static Vector3 RoomCenter(DoorController door) => (door.clearLeft.position + door.clearRight.position) * 0.5f;

    /// <summary>Chĩa súng vào giữa cửa (ngang ngực), nhìn từ chỗ stack vào phòng.</summary>
    private static Vector3 DoorAimPoint(DoorController door) => RoomCenter(door) + Vector3.up * 1.3f;

    /// <summary>Điểm sâu trong phòng để chĩa súng sau khi vào.</summary>
    private static Vector3 RoomLookPoint(DoorController door)
    {
        Vector3 center = RoomCenter(door);
        Vector3 stack = (door.stackLeft.position + door.stackRight.position) * 0.5f;
        Vector3 dir = Flat(center - stack);
        return center + (dir.sqrMagnitude > 0.01f ? dir.normalized * 3f : Vector3.zero) + Vector3.up * 1.3f;
    }
}
