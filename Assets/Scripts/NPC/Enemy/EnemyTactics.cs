using UnityEngine;
using VRSwat.Doors;

/// <summary>
/// Chiến thuật kiểu SWAT 4 của nghi phạm (gắn cùng NPC phe Suspect). Graph Enemy đọc component này qua các node:
/// - Investigate : nghe tiếng động (NPCHearing) mà chưa thấy ai -> đi bộ tới xem, nhìn quanh, rồi về chỗ cũ.
/// - Barricade   : cố thủ: không tuần tra, đứng chĩa súng vào cửa gần nhất (hoặc BarricadeDoor) chờ cảnh sát.
/// - Flee        : morale Panicked + cảnh sát ở gần -> chạy tới chỗ xa cảnh sát, hô "Run!". Mỗi FleeCooldown giây
///                 tối đa một lần (bị dồn vào góc thì thôi, quay lại đánh / đầu hàng). Chạy tới nơi thì cố thủ ở
///                 chỗ mới (HoldAfterFlee), không tuần tra / đi xem tiếng động quay về chỗ cảnh sát.
/// - Take hostage: (tắt mặc định) có con tin trong HostageGrabDistance + thấy cảnh sát + morale Aggressive ->
///                 khống chế con tin, chĩa súng vào con tin, hô "Back off or he dies!". Morale tụt dưới Aggressive
///                 hoặc bị hô ReleaseAfterShouts lần thì buông.
/// Không có component này: Investigate và Flee dùng thông số mặc định, Barricade / Take hostage tắt.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NPC))]
public class EnemyTactics : MonoBehaviour
{
    [Header("Nghe tiếng động -> đi xem")]
    [SerializeField] private bool _investigate = true;
    [Tooltip("Chỉ đi xem tiếng động nghe trong ngần này giây trở lại.")]
    [SerializeField] private float _noiseMemory = 8f;
    [Tooltip("Tới chỗ nghe thấy thì nhìn quanh ngần này giây (random trong khoảng).")]
    [SerializeField] private Vector2 _lookAroundTime = new(3f, 5f);
    [Tooltip("Quá ngần này giây chưa tới chỗ thì thôi.")]
    [SerializeField] private float _investigateTimeout = 15f;

    [Header("Cố thủ (Barricade)")]
    [SerializeField] private bool _barricaded;
    [Tooltip("Cửa canh. Trống = cửa gần nhất trong BarricadeDoorRange.")]
    [SerializeField] private DoorController _barricadeDoor;
    [SerializeField] private float _barricadeDoorRange = 12f;
    [Tooltip("Ngồi cố thủ (sau vật che) thay vì đứng.")]
    [SerializeField] private bool _barricadeCrouch;

    [Header("Bỏ chạy (morale Panicked)")]
    [SerializeField] private bool _canFlee = true;
    [Tooltip("Cảnh sát / người chơi trong khoảng này thì mới chạy.")]
    [SerializeField] private float _fleeTriggerDistance = 10f;
    [Tooltip("Chạy tới điểm cách vị trí cảnh sát ít nhất ngần này mét.")]
    [SerializeField] private float _fleeDistance = 12f;
    [SerializeField] private float _fleeCooldown = 15f;
    [Tooltip("Chạy tới nơi thì cố thủ ở chỗ mới (Barricade, chỗ mới thành Home) thay vì tuần tra / đi xem tiếng động quay về chỗ cảnh sát.")]
    [SerializeField] private bool _holdAfterFlee = true;

    [Header("Khống chế con tin")]
    [SerializeField] private bool _canTakeHostage;
    [SerializeField] private float _hostageGrabDistance = 3f;
    [Tooltip("Bị hô ngần này lần trong lúc giữ con tin thì buông.")]
    [SerializeField, Min(1)] private int _releaseAfterShouts = 2;
    [Tooltip("Hô đe dọa lại sau mỗi ngần này giây.")]
    [SerializeField] private float _threatenInterval = 4f;
    [Tooltip("Buông con tin rồi thì ngần này giây sau mới được bắt lại.")]
    [SerializeField] private float _regrabCooldown = 30f;

    private NPC _npc;
    private float _lastRelease = float.NegativeInfinity;

    public bool Investigate => _investigate;
    public float NoiseMemory => _noiseMemory;
    public Vector2 LookAroundTime => _lookAroundTime;
    public float InvestigateTimeout => _investigateTimeout;

    public bool Barricaded
    {
        get => _barricaded;
        set => _barricaded = value;
    }
    public bool BarricadeCrouch => _barricadeCrouch;

    public bool CanFlee
    {
        get => _canFlee;
        set => _canFlee = value;
    }
    public float FleeTriggerDistance => _fleeTriggerDistance;
    public float FleeDistance => _fleeDistance;

    public bool CanTakeHostage
    {
        get => _canTakeHostage;
        set => _canTakeHostage = value;
    }
    public float HostageGrabDistance => _hostageGrabDistance;
    public float ThreatenInterval => _threatenInterval;

    /// <summary>Vị trí lúc bắt đầu màn (đi xem tiếng động xong thì về đây).</summary>
    public Vector3 Home { get; private set; }

    /// <summary>Lần bỏ chạy gần nhất (kể cả lần bị dồn vào góc không chạy được).</summary>
    public float LastFleeTime { get; set; } = float.NegativeInfinity;
    public bool FleeOnCooldown => Time.time - LastFleeTime < _fleeCooldown;

    /// <summary>FleeAction báo đã chạy tới nơi: cố thủ ở chỗ mới (nếu HoldAfterFlee), quên tiếng động cũ.</summary>
    public void OnFleeArrived(Vector3 point)
    {
        LastFleeTime = Time.time;
        if (!_holdAfterFlee) return;
        Home = point;
        _barricaded = true;
        _barricadeDoor = null; // canh cửa gần chỗ mới
        if (TryGetComponent(out NPCHearing hearing)) hearing.Consume();
    }

    /// <summary>Con tin đang bị khống chế (null = không).</summary>
    public Hostage HeldHostage { get; private set; }

    /// <summary>Số lần bị hô kể từ lúc bắt con tin.</summary>
    public int ShoutsWhileHolding { get; private set; }
    public bool ShouldReleaseHostage => ShoutsWhileHolding >= _releaseAfterShouts;

    private void Awake()
    {
        _npc = GetComponent<NPC>();
        Home = transform.position;
    }

    private void OnDisable() => ReleaseHostage();

    /// <summary>Cửa cố thủ: BarricadeDoor nếu đã gán, không thì cửa gần nhất trong BarricadeDoorRange.</summary>
    public DoorController BarricadeDoor()
    {
        if (_barricadeDoor != null) return _barricadeDoor;
        DoorController best = null;
        float bestDist = _barricadeDoorRange;
        foreach (DoorController door in DoorController.All)
        {
            if (door == null) continue;
            float d = Vector3.Distance(door.transform.position, transform.position);
            if (d < bestDist) { bestDist = d; best = door; }
        }
        return best;
    }

    /// <summary>Con tin gần nhất có thể bắt (còn sống, chưa giơ tay / bị còng / bị người khác giữ).</summary>
    public Hostage FindHostageToGrab()
    {
        Hostage best = null;
        float bestDist = _hostageGrabDistance;
        foreach (Hostage h in Hostage.Active)
        {
            if (h == null || h.IsDead || h.IsSurrendered || h.IsRestrained || (h.IsHeld && h.HeldBy != _npc)) continue;
            float d = Vector3.Distance(h.transform.position, transform.position);
            if (d <= bestDist) { bestDist = d; best = h; }
        }
        return best;
    }

    public bool GrabHostage(Hostage hostage)
    {
        if (hostage == null || HeldHostage == hostage) return hostage != null;
        ReleaseHostage();
        if (!hostage.SetHeld(true, _npc)) return false;
        HeldHostage = hostage;
        ShoutsWhileHolding = 0;
        return true;
    }

    public void ReleaseHostage()
    {
        if (HeldHostage != null)
        {
            HeldHostage.SetHeld(false, _npc);
            _lastRelease = Time.time;
        }
        HeldHostage = null;
        ShoutsWhileHolding = 0;
    }

    /// <summary>Vừa buông con tin trong RegrabCooldown giây thì chưa được bắt lại.</summary>
    public bool CanGrabAgain => Time.time - _lastRelease >= _regrabCooldown;

    /// <summary>NPCMorale báo bị hô hàng (đếm khi đang giữ con tin).</summary>
    public void NotifyComplianceOrder()
    {
        if (HeldHostage != null) ShoutsWhileHolding++;
    }
}
