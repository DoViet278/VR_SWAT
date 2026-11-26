using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Rút bom từ hông phải và ném bom cho NPC (đặt cùng GameObject với Animator và NPCWeaponHolder).
///
/// Trình tự anim (layer UpperBody, chân vẫn chạy anim đứng / ngồi nấp bình thường):
/// 1. Trigger DrawGrenade -> state DrawGrenade (clip equipFlashbang): súng được đeo lên ngực (RifleSling)
///    để tay phải rảnh, tay phải với ra hông phải. Lúc tay chạm hông (GrabAt) model bom trên hông biến mất
///    và quả bom thật xuất hiện trong lòng bàn tay phải (HandGrip).
/// 2. DrawGrenade tự chuyển sang state Throw (clip sGrenadeThrowRight). Lúc tay vung nhanh nhất (ReleaseAt)
///    bom rời tay theo đường bay đã lập kế hoạch.
/// 3. Gần cuối anim (RifleBackAt) súng trở lại tay phải, IK tay trái bật lại.
///
/// QUYẾT ĐỊNH NÉM (WantsToThrow):
/// - Bom là vũ khí cuối cùng: CHỈ ném khi súng đã hết sạch đạn (băng trên súng rỗng và đã bắn hết băng dự phòng).
///   Còn đạn thì NPC bắn, không ném.
/// - Hết đạn: mỗi ReplanInterval giây LẬP KẾ HOẠCH NÉM: thử nhiều điểm rơi (quanh vị trí Player, có đón đầu khi
///   Player di chuyển) x nhiều độ cao đường bay. Mỗi phương án được mô phỏng từng đoạn (SphereCast theo đúng kích thước bom):
///   * đường bay đâm vào tường / trần / vật cản (mặt dốc đứng) -> bỏ, vì bom nảy ngược khó đoán;
///   * điểm bom chạm đất: dùng đúng logic sát thương của NPCGrenade (bán kính nổ + vật cản chắn) để tính
///     sát thương Player sẽ nhận. Vùng nổ không với tới Player (quá xa, hoặc bị tường / vật che chắn) -> bỏ;
///   * điểm nổ quá gần chính NPC hoặc trúng đồng đội NPC -> bỏ.
///   Chọn phương án có sát thương dự đoán cao nhất (ưu tiên đường bay thấp, bay nhanh).
/// - ThrowImmediatelyWhenOutOfAmmo (mặc định bật): hết đạn là ném NGAY, bỏ qua ThrowRange; không có phương án nào
///   đạt MinDamageFraction thì vẫn ném phương án an toàn tốt nhất (điểm nổ gần Player nhất). Chỉ không ném khi
///   mọi phương án đều đâm tường / nổ trúng mình hoặc đồng đội -> NPC núp, xét lại mỗi ReplanInterval giây.
///   Tắt: không đạt MinDamageFraction thì để dành bom, núp chờ Player bước vào chỗ ném tới được.
/// - Ném xong: trong Cooldown giây không ném nữa nên IsOutOfAmmo đúng -> NPC đi nấp (HideNoAmmo).
/// - Lúc bom rời tay: lập kế hoạch lại từ vị trí tay thật và vị trí Player mới nhất; không được thì dùng
///   điểm rơi đã chọn lúc quyết định.
/// - Ném hết bom: IsOutOfAmmo đúng hẳn, NPC núp vào Cover gần nhất để trốn Player.
/// </summary>
[RequireComponent(typeof(Animator))]
public class NPCGrenadeThrower : MonoBehaviour
{
    private static readonly int DrawGrenadeHash = Animator.StringToHash("DrawGrenade");
    private static readonly int ThrowHash = Animator.StringToHash("Throw");
    private static readonly int FireHash = Animator.StringToHash("Fire");
    private static readonly int DrawGrenadeStateHash = Animator.StringToHash("DrawGrenade");
    private static readonly int ThrowStateHash = Animator.StringToHash("Throw");
    private static readonly int EmptyStateHash = Animator.StringToHash("Empty");

    private enum Phase { None, Drawing, Throwing }

    /// <summary>Một phương án ném đã mô phỏng.</summary>
    public struct ThrowPlan
    {
        public bool Valid;
        public Vector3 Aim;          // điểm nhắm (trên mặt đất)
        public Vector3 Impact;       // điểm bom chạm đất theo mô phỏng (= tâm nổ dự đoán)
        public Vector3 Velocity;     // vận tốc ném
        public float Apex;           // độ cao đỉnh so với tay
        public float FlightTime;
        public float ExpectedDamage; // sát thương Player sẽ nhận theo dự đoán
    }

    [Header("Grenade")]
    [SerializeField, Min(0)] private int _grenades = 1;
    [SerializeField] private NPCGrenade _grenadePrefab;
    [Tooltip("Model bom gắn trên hông phải. Ẩn khi tay rút bom ra (và khi hết bom).")]
    [SerializeField] private GameObject _hipGrenade;
    [Tooltip("Điểm trong lòng bàn tay phải (con của xương RightHand) để cầm bom.")]
    [SerializeField] private Transform _handGrip;
    [Tooltip("Điểm đeo súng trên ngực (con của xương Chest/Spine) khi tay phải bận rút / ném bom.")]
    [SerializeField] private Transform _rifleSling;
    [SerializeField] private float _rifleBlendTime = 0.25f;
    [SerializeField] private int _upperBodyLayer = 1;

    [Header("Flashbang (đồng đội ném vào phòng)")]
    [Tooltip("Prefab flashbang của người chơi (FlashGrenade, có VRGrenade + Flashbang). Dùng cho BeginThrowFlashbangAt.")]
    [SerializeField] private VRGrenade _flashbangPrefab;
    [Tooltip("Số flashbang mang theo. -1 = vô hạn.")]
    [SerializeField] private int _flashbangs = -1;
    [Tooltip("Độ cao đỉnh đường bay thêm (m) khi ném flashbang qua cửa.")]
    [SerializeField] private float _flashbangApex = 0.4f;

    [Header("Decision (chỉ ném khi hết sạch đạn)")]
    [Tooltip("Khoảng cách (m) tới Target được phép ném: gần quá thì tự nổ trúng mình, xa quá thì không tới.")]
    [SerializeField] private Vector2 _throwRange = new(5f, 18f);
    [Tooltip("Số giây giữa 2 lần lập kế hoạch ném khi đang chờ thời cơ (Player chưa ở chỗ ném tới được).")]
    [SerializeField] private float _replanInterval = 0.5f;
    [Tooltip("Có nhiều bom: số giây sau một lần ném mới được ném quả tiếp theo (trong lúc chờ NPC đi nấp).")]
    [SerializeField] private float _cooldown = 4f;
    [Tooltip("Bật: hết đạn là ném ngay, không cần đợi thời cơ. Bỏ qua ThrowRange và MinDamageFraction, " +
             "chỉ còn giữ an toàn (không đâm tường, không nổ trúng mình / đồng đội). Có phương án đủ sát thương thì vẫn ưu tiên.")]
    [SerializeField] private bool _throwImmediatelyWhenOutOfAmmo = true;
    [Tooltip("In ra Console quyết định ném bom: lý do hết đạn mà chưa ném, điểm rơi dự kiến, điểm nổ thật (để debug).")]
    [SerializeField] private bool _logDecisions = false;

    [Header("Bước ra để ném (vật che chắn đường ném / Player đứng quá gần)")]
    [Tooltip("Từ chỗ đứng hiện tại không ném được: thử bước ngang ra khỏi vật che hoặc lùi xa Player, đứng dậy ném từ đó.")]
    [SerializeField] private bool _repositionToThrow = true;
    [Tooltip("Các khoảng bước ngang (m), sang trái / phải so với hướng tới Player.")]
    [SerializeField] private float[] _sideStepDistances = { 1.2f, 2f };
    [Tooltip("Các khoảng lùi xa Player (m), khi Player đứng quá gần (bom nổ trúng mình).")]
    [SerializeField] private float[] _backStepDistances = { 2.5f, 4f };
    [Tooltip("Độ cao xương hông khi đứng (m), để ước lượng vị trí tay lúc ném ở chỗ mới.")]
    [SerializeField] private float _standingHipHeight = 1f;

    [Header("Planning (tránh ném vào vật cản)")]
    [Tooltip("Chỉ ném khi sát thương dự đoán lên Player >= tỉ lệ này của sát thương tối đa của bom.")]
    [SerializeField, Range(0.05f, 1f)] private float _minDamageFraction = 0.35f;
    [Tooltip("Khoảng cách tối thiểu từ điểm nổ tới NPC = bán kính nổ + giá trị này.")]
    [SerializeField] private float _selfSafetyMargin = 0.5f;
    [Tooltip("Không ném nếu đồng đội NPC (còn sống, không bị che) nằm trong vùng nổ.")]
    [SerializeField] private bool _avoidAllies = true;
    [Tooltip("Bán kính vòng điểm nhắm phụ quanh Player (ném vòng qua mép vật che).")]
    [SerializeField] private float _aimRingRadius = 1.5f;
    [Tooltip("Các độ cao đỉnh (m, cộng thêm vào độ cao tối thiểu) được thử: thấp = bay nhanh, cao = vượt vật che.")]
    [SerializeField] private float[] _apexOptions = { 0f, 1.5f, 3f };
    [Tooltip("Đỉnh đường bay tối thiểu (m) so với tay.")]
    [SerializeField] private float _minApexHeight = 1f;
    [Tooltip("Vận tốc ném tối đa (m/s) mà tay người ném được.")]
    [SerializeField] private float _maxThrowSpeed = 18f;
    [Tooltip("Mặt chạm có pháp tuyến hướng lên ít nhất mức này mới coi là mặt đất (bom nằm lại). Thấp hơn = tường, bom nảy đi.")]
    [SerializeField, Range(0f, 1f)] private float _groundNormalY = 0.6f;
    [Tooltip("Đón đầu Player đang di chuyển: tỉ lệ thời gian bay dùng để dự đoán vị trí.")]
    [SerializeField, Range(0f, 1.5f)] private float _leadFactor = 0.8f;
    [Tooltip("Độ lệch ngẫu nhiên nhỏ (m) của điểm nhắm lúc ném, chỉ dùng nếu vẫn hiệu quả.")]
    [SerializeField] private float _scatter = 0.4f;
    [SerializeField] private LayerMask _trajectoryMask = Physics.DefaultRaycastLayers;
    [SerializeField] private bool _drawPlanGizmos = true;

    [Header("Timing (normalizedTime của state)")]
    [Tooltip("Độ dài clip DrawGrenade (equipFlashbang), dùng khi Animator chưa vào state.")]
    [SerializeField] private float _drawDuration = 1.25f;
    [Tooltip("Tay phải chạm hông phải: lấy bom ra khỏi túi.")]
    [SerializeField, Range(0f, 1f)] private float _grabAt = 0.4f;
    [Tooltip("Độ dài clip Throw (sGrenadeThrowRight).")]
    [SerializeField] private float _throwDuration = 2.5f;
    [Tooltip("Tay vung nhanh nhất: bom rời tay.")]
    [SerializeField, Range(0f, 1f)] private float _releaseAt = 0.6f;
    [Tooltip("Tay phải trở về trước ngực: cầm súng lại.")]
    [SerializeField, Range(0f, 1f)] private float _rifleBackAt = 0.86f;
    [Tooltip("Vị trí tay lúc bom rời tay, so với xương hông (đo từ clip sGrenadeThrowRight): phải, lên, trước.")]
    [SerializeField] private Vector3 _releaseOffsetFromHips = new(0.1f, 0.4f, 0.5f);

    private const float SimStep = 0.04f;
    private const float SimMaxTime = 4f;

    private Animator _animator;
    private NPC _npc;
    private NPCWeaponHolder _holder;
    private Transform _hips;

    private Phase _phase;
    private float _phaseStart;
    private bool _enteredState;
    private NPCGrenade _inHand;
    private VRGrenade _flashInHand;
    private bool _throwingFlash;
    private Vector3 _flashPoint;
    private bool _released;

    private float _nextAllowed;
    private float _nextPlan;
    private ThrowPlan _plan;
    private Vector3 _planTargetPos;

    // Ước lượng vận tốc của Target để đón đầu.
    private GameObject _trackedTarget;
    private Vector3 _lastTargetPos;
    private Vector3 _targetVelocity;

    // Gizmo: đường bay của kế hoạch gần nhất.
    private readonly List<Vector3> _debugPath = new();
    private Vector3 _debugImpact;
    private bool _debugValid;

    // Súng: chỗ gốc trên tay phải và hiệu ứng chuyển mượt sang / về chỗ đeo.
    private Transform _rifle;
    private Transform _rifleHome;
    private Vector3 _rifleHomePos;
    private Quaternion _rifleHomeRot;
    private bool _rifleSlung;
    private float _rifleBlend = 1f;
    private Vector3 _blendFromPos;
    private Quaternion _blendFromRot;

    private static readonly RaycastHit[] s_Hits = new RaycastHit[16];

    public int Grenades => _grenades;
    public bool HasGrenade => _grenades > 0 && _grenadePrefab != null;
    public bool IsBusy => _phase != Phase.None || _rifleSlung || _rifleBlend < 1f;
    public Vector2 ThrowRange => _throwRange;
    public ThrowPlan CurrentPlan => _plan;

    /// <summary>Tóm tắt lần lập kế hoạch gần nhất (vì sao ném / không ném), để debug.</summary>
    public string LastPlanReport { get; private set; } = "";

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _npc = GetComponent<NPC>();
        _holder = GetComponent<NPCWeaponHolder>();
        if (_animator.isHuman)
        {
            _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            if (_handGrip == null) _handGrip = _animator.GetBoneTransform(HumanBodyBones.RightHand);
        }

        if (_holder != null && _holder.Weapon != null)
        {
            _rifle = _holder.Weapon.transform;
            _rifleHome = _rifle.parent;
            _rifleHomePos = _rifle.localPosition;
            _rifleHomeRot = _rifle.localRotation;
        }
        UpdateHipGrenade();
        WarnIfCannotReachSafeDistance();
    }

    /// <summary>
    /// Tầm ném xa nhất (mặt phẳng, góc 45°) là v²/g. Nếu tầm này không vượt nổi khoảng cách an toàn
    /// (bán kính nổ + SelfSafetyMargin) thì MỌI phương án đều bị loại ("vượt sức ném" / "nguy hiểm cho mình")
    /// và NPC hết đạn sẽ chỉ núp mãi mà không bao giờ ném. Báo lỗi cấu hình rõ ràng thay vì im lặng.
    /// </summary>
    private void WarnIfCannotReachSafeDistance()
    {
        if (_grenadePrefab == null) return;
        float g = Mathf.Max(0.1f, -Physics.gravity.y);
        float maxRange = _maxThrowSpeed * _maxThrowSpeed / g;
        float safeDistance = _grenadePrefab.Radius + _selfSafetyMargin;
        if (maxRange < safeDistance + 1f)
            Debug.LogWarning($"[{name}] NPCGrenadeThrower: Max Throw Speed = {_maxThrowSpeed} m/s chỉ ném xa tối đa ~{maxRange:F1}m, " +
                             $"không vượt được khoảng cách an toàn {safeDistance:F1}m (bán kính nổ + Self Safety Margin) -> NPC sẽ KHÔNG BAO GIỜ ném bom. " +
                             $"Cần Max Throw Speed >= {Mathf.Sqrt((safeDistance + 3f) * g):F0} m/s.", this);
    }

    // ---------- Quyết định ----------

    /// <summary>Đủ điều kiện kỹ thuật để bắt đầu ném (còn bom, rảnh tay, còn sống).</summary>
    public bool CanThrow =>
        HasGrenade && !IsBusy && (_npc == null || !_npc.IsDead) && (_holder == null || !_holder.IsReloading);

    /// <summary>Súng đã hết sạch đạn: băng trên súng rỗng và đã bắn hết băng dự phòng (không có súng cũng tính là hết).</summary>
    public bool GunOutOfAmmo => _holder == null || !_holder.HasWeapon || _holder.IsOutOfAmmo;

    /// <summary>
    /// Điều kiện ném: súng hết sạch đạn, còn bom, đang giao chiến, Player trong ThrowRange
    /// VÀ có phương án ném mà vùng nổ với tới Player. Kế hoạch được lập lại mỗi ReplanInterval giây
    /// (hoặc ngay khi Player chạy xa chỗ cũ), nên Condition trong graph đọc nhiều lần vẫn cho cùng kết quả.
    /// </summary>
    public bool WantsToThrow()
    {
        if (!GunOutOfAmmo) return false; // còn đạn thì bắn, bom để dành tới khi hết đạn
        if (!HasGrenade) return Why(_grenadePrefab == null ? "chưa gán Grenade Prefab" : "đã hết bom");
        if (!CanThrow) return Why(IsBusy ? "đang bận tay (rút / ném / súng chưa về tay)" : "đang thay đạn hoặc đã chết");
        if (_npc == null) return Why("không có component NPC");
        if (_npc.Target == null) return Why("NPC chưa có Target (chưa từng thấy Player)");
        // Ném ngay: núp lâu mất dấu Player vẫn ném vào vị trí thấy lần cuối.
        if (!_throwImmediatelyWhenOutOfAmmo && !_npc.IsAlerted) return Why("NPC hết cảnh giác (mất dấu Player quá lâu)");
        if (Time.time < _nextAllowed) return Why($"đang Cooldown sau lần ném trước ({_nextAllowed - Time.time:F1}s)");

        Vector3 targetPos = _npc.ThreatPosition;
        float distance = FlatDistance(transform.position, targetPos);
        // Ném ngay: không xét tầm ném, sức tay (MaxThrowSpeed) và IsSafe tự loại phương án quá xa / quá gần.
        if (!_throwImmediatelyWhenOutOfAmmo && (distance < _throwRange.x || distance > _throwRange.y))
            return Why($"Player cách {distance:F1}m, ngoài ThrowRange {_throwRange.x}-{_throwRange.y}m");

        if (Time.time >= _nextPlan || FlatDistance(targetPos, _planTargetPos) > 1.5f)
        {
            _nextPlan = Time.time + _replanInterval;
            _planTargetPos = targetPos;
            NeedsReposition = false;
            _plan = PlanThrow(EstimatedReleasePoint(), _npc.Target, targetPos);
            if (!_plan.Valid && _repositionToThrow)
            {
                string hereReport = LastPlanReport;
                if (!TryFindThrowPosition(targetPos)) LastPlanReport = hereReport + " | không có chỗ bước ra nào ném được";
            }
        }
        return _plan.Valid || Why($"không có đường ném (Player cách {distance:F1}m): {LastPlanReport}");
    }

    /// <summary>Kế hoạch ném hiện tại cần NPC bước tới ThrowPosition trước (ThrowGrenadeAction lo việc di chuyển).</summary>
    public bool NeedsReposition { get; private set; }
    public Vector3 ThrowPosition { get; private set; }

    /// <summary>
    /// Thử các chỗ đứng quanh NPC: bước ngang trái / phải (ra khỏi vật che), lùi xa Player (khi Player quá gần),
    /// gần trước xa sau. Chỗ nào trên NavMesh, đi tới được và có đường ném (tay ở tư thế đứng) thì chọn.
    /// </summary>
    private bool TryFindThrowPosition(Vector3 targetPos)
    {
        Vector3 self = transform.position;
        Vector3 toTarget = targetPos - self;
        toTarget.y = 0f;
        toTarget = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : transform.forward;
        Vector3 side = Vector3.Cross(Vector3.up, toTarget);

        var candidates = new List<Vector3>();
        foreach (float d in _sideStepDistances) { candidates.Add(self + side * d); candidates.Add(self - side * d); }
        // Player đứng trong vùng nổ: thêm một khoảng lùi vừa đủ để điểm nổ cạnh Player không trúng mình.
        var backSteps = new List<float>(_backStepDistances);
        if (_grenadePrefab != null)
        {
            float need = _grenadePrefab.Radius + _selfSafetyMargin + 1f - FlatDistance(self, targetPos);
            if (need > 0f && !backSteps.Exists(d => d >= need)) backSteps.Add(need);
        }
        foreach (float d in backSteps)
        {
            candidates.Add(self - toTarget * d);
            candidates.Add(self - toTarget * d + side * d * 0.5f);
            candidates.Add(self - toTarget * d - side * d * 0.5f);
        }

        NavMeshAgent agent = GetComponent<NavMeshAgent>();
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return false; // không đi được thì thôi
        var path = new NavMeshPath();
        Vector3 o = _releaseOffsetFromHips;

        foreach (Vector3 c in candidates)
        {
            if (!NavMesh.SamplePosition(c, out NavMeshHit hit, 0.75f, agent.areaMask)) continue;
            if (!NavMesh.CalculatePath(agent.nextPosition, hit.position, agent.areaMask, path) || path.status != NavMeshPathStatus.PathComplete) continue;
            float length = 0f;
            for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            if (length > Vector3.Distance(self, c) * 2.5f + 1f) continue; // phải đi vòng xa: bỏ

            Vector3 fwd = targetPos - hit.position;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : toTarget;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 from = hit.position + Vector3.up * (_standingHipHeight + o.y) + right * o.x + fwd * o.z;

            ThrowPlan plan = PlanThrow(from, _npc.Target, targetPos, hit.position);
            if (!plan.Valid) continue;
            _plan = plan;
            NeedsReposition = true;
            ThrowPosition = hit.position;
            LastPlanReport = $"BƯỚC RA {Vector3.Distance(self, hit.position):F1}m rồi ném | {LastPlanReport}";
            return true;
        }
        return false;
    }

    /// <summary>Lý do hết đạn mà chưa ném, gần nhất. Bật LogDecisions để in ra Console mỗi khi lý do đổi.</summary>
    public string LastNoThrowReason { get; private set; } = "";

    private bool Why(string reason)
    {
        if (_logDecisions && reason != LastNoThrowReason) Debug.Log($"[{name}] Hết đạn nhưng chưa ném bom: {reason}", this);
        LastNoThrowReason = reason;
        return false;
    }

    // ---------- Lập kế hoạch ném ----------

    /// <summary>Vị trí tay lúc bom rời tay, ước lượng theo xương hông hiện tại (đứng hay ngồi nấp đều đúng).</summary>
    private Vector3 EstimatedReleasePoint()
    {
        Vector3 basePos = _hips != null ? _hips.position : transform.position + Vector3.up;
        Vector3 o = _releaseOffsetFromHips;
        return basePos + transform.right * o.x + Vector3.up * o.y + transform.forward * o.z;
    }

    /// <summary>Vị trí Target dự đoán khi bom chạm đất (đón đầu theo vận tốc ước lượng).</summary>
    private Vector3 PredictTarget(Vector3 targetPos, float flightTime)
    {
        if (_npc == null || !_npc.CanSeeTarget) return targetPos; // không thấy thì không biết hướng chạy
        Vector3 v = Vector3.ClampMagnitude(new Vector3(_targetVelocity.x, 0f, _targetVelocity.z), 5f);
        return targetPos + v * flightTime * _leadFactor;
    }

    /// <summary>
    /// Thử các điểm nhắm (quanh vị trí Player, có đón đầu) x các độ cao đường bay, mô phỏng từng phương án
    /// và chọn phương án có sát thương dự đoán lên Player cao nhất. Không đạt MinDamageFraction thì Valid = false.
    /// </summary>
    public ThrowPlan PlanThrow(Vector3 from, GameObject target, Vector3 targetPos) =>
        PlanThrow(from, target, targetPos, transform.position);

    /// <summary>selfPos: chỗ NPC sẽ đứng lúc ném (để tránh bom nổ trúng mình), khác vị trí hiện tại khi bước ra ném.</summary>
    public ThrowPlan PlanThrow(Vector3 from, GameObject target, Vector3 targetPos, Vector3 selfPos)
    {
        ThrowPlan best = default;
        if (_grenadePrefab == null || target == null) return best;

        float minDamage = _grenadePrefab.Damage * _minDamageFraction;
        float bestScore = float.NegativeInfinity;
        // Phương án an toàn nhưng chưa đủ sát thương: dùng khi ném ngay lúc hết đạn mà không có phương án tốt hơn.
        ThrowPlan fallback = default;
        float fallbackScore = float.NegativeInfinity;
        float firstGuessTime = Mathf.Clamp(FlatDistance(from, targetPos) / 9f, 0.6f, 2.2f);
        Vector3 predicted = PredictTarget(targetPos, firstGuessTime);
        // Thân Player tại vị trí NPC biết / dự đoán (không dùng vị trí thật khi Player đang khuất).
        Vector3 bodyOffset = NPCConditionUtils.CenterOf(target) - target.transform.position;
        Vector3 expectedBody = predicted + bodyOffset;
        expectedBody.y = Mathf.Max(expectedBody.y, SnapToGround(predicted).y + 0.5f); // thân không nằm dưới đất
        Transform targetRoot = target.transform.root;

        // Điểm nhắm: Player (dự đoán), Player hiện tại, và vòng quanh Player (ném vòng qua mép vật che).
        int ringCount = 6;
        int count = 2 + ringCount;
        bool anyPath = false;
        int nSolve = 0, nNoHit = 0, nWall = 0, nLedge = 0, nUnsafe = 0, nWeak = 0, nOk = 0;
        float bestRejected = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector3 aim;
            if (i == 0) aim = predicted;
            else if (i == 1) aim = targetPos;
            else
            {
                float angle = (i - 2) * Mathf.PI * 2f / ringCount;
                aim = predicted + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _aimRingRadius;
            }
            if (i == 1 && FlatDistance(predicted, targetPos) < 0.3f) continue;
            aim = SnapToGround(aim);

            foreach (float extra in _apexOptions)
            {
                if (!TrySolve(from, aim, extra, out Vector3 velocity, out float apex, out float time)) { nSolve++; continue; }
                if (!Simulate(from, velocity, target, out Vector3 impact, out bool hitWall)) { nNoHit++; continue; }
                anyPath = true;
                if (hitWall) { nWall++; continue; }
                if (!IsStableLanding(impact)) { nLedge++; continue; }
                if (!IsSafe(impact, selfPos)) { nUnsafe++; continue; }

                float damage = _grenadePrefab.PredictDamage(impact + Vector3.up * 0.1f, expectedBody, targetRoot);
                // Ưu tiên sát thương cao, sau đó đường bay thấp (bay nhanh, Player khó né).
                // Sát thương bằng nhau (vd. đều 0 vì Player khuất): ưu tiên điểm nổ gần Player.
                float score = damage - apex * 2f - time * 3f - FlatDistance(impact, predicted);
                if (damage < minDamage)
                {
                    nWeak++;
                    bestRejected = Mathf.Max(bestRejected, damage);
                    if (score > fallbackScore)
                    {
                        fallbackScore = score;
                        fallback = new ThrowPlan
                        {
                            Valid = true, Aim = aim, Impact = impact, Velocity = velocity,
                            Apex = apex, FlightTime = time, ExpectedDamage = damage,
                        };
                    }
                    continue;
                }
                nOk++;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = new ThrowPlan
                    {
                        Valid = true, Aim = aim, Impact = impact, Velocity = velocity,
                        Apex = apex, FlightTime = time, ExpectedDamage = damage,
                    };
                }
            }
        }

        bool usedFallback = false;
        if (!best.Valid && fallback.Valid && _throwImmediatelyWhenOutOfAmmo && GunOutOfAmmo)
        {
            best = fallback;
            usedFallback = true;
        }

        if (_drawPlanGizmos)
        {
            _debugValid = best.Valid;
            _debugImpact = best.Valid ? best.Impact : targetPos;
            _debugPath.Clear();
            if (best.Valid) Simulate(from, best.Velocity, target, out _, out _, _debugPath);
        }
        LastPlanReport = best.Valid
            ? usedFallback
                ? $"NÉM NGAY (hết đạn): sát thương dự đoán {best.ExpectedDamage:F0}/{minDamage:F0}, chưa có phương án đủ mạnh"
                : $"NÉM: sát thương dự đoán {best.ExpectedDamage:F0}, {nOk} phương án đạt"
            : $"KHÔNG NÉM: vượt sức ném {nSolve}, không chạm gì {nNoHit}, đâm tường/trần {nWall}, rơi mép hẹp {nLedge}, "
              + $"nguy hiểm cho mình/đồng đội {nUnsafe}, vùng nổ không tới Player {nWeak} (cao nhất {bestRejected:F0}/{minDamage:F0})";
        if (!best.Valid && !anyPath) best.Aim = targetPos;
        return best;
    }

    /// <summary>Vận tốc ném để bom rơi đúng điểm to với đỉnh cao (độ cao tối thiểu + extra) so với tay.</summary>
    private bool TrySolve(Vector3 from, Vector3 to, float extraApex, out Vector3 velocity, out float apex, out float time)
    {
        float g = Mathf.Max(0.1f, -Physics.gravity.y);
        Vector3 flat = to - from;
        flat.y = 0f;
        float dy = to.y - from.y;

        apex = Mathf.Max(_minApexHeight, dy + 0.5f) + extraApex;
        float vy = Mathf.Sqrt(2f * g * apex);
        float disc = vy * vy - 2f * g * dy;
        time = disc >= 0f ? (vy + Mathf.Sqrt(disc)) / g : 0f;
        velocity = time > 0.05f ? flat / time + Vector3.up * vy : Vector3.zero;
        return time > 0.05f && velocity.magnitude <= _maxThrowSpeed;
    }

    /// <summary>
    /// Mô phỏng đường bay theo từng đoạn ngắn (SphereCast bằng đúng kích thước bom).
    /// impact: điểm chạm đầu tiên. hitWall: chạm mặt dốc đứng / trần (bom sẽ nảy đi, không nằm lại đó).
    /// Chạm trúng người Target cũng coi là điểm rơi tốt.
    /// </summary>
    private bool Simulate(Vector3 from, Vector3 velocity, GameObject target, out Vector3 impact, out bool hitWall, List<Vector3> path = null)
    {
        impact = from;
        hitWall = false;
        float radius = _grenadePrefab != null ? _grenadePrefab.BodyRadius : 0.035f;
        Vector3 g = Physics.gravity;
        Vector3 pos = from;
        Vector3 vel = velocity;
        Transform targetRoot = target != null ? target.transform.root : null;
        path?.Add(pos);

        for (float t = 0f; t < SimMaxTime; t += SimStep)
        {
            Vector3 next = pos + vel * SimStep + 0.5f * g * SimStep * SimStep;
            vel += g * SimStep;
            Vector3 delta = next - pos;
            float length = delta.magnitude;

            if (length > 0.0001f && TryCast(pos, delta / length, radius, length, targetRoot, out RaycastHit hit, out bool hitTarget))
            {
                impact = hit.point + hit.normal * radius;
                hitWall = !hitTarget && hit.normal.y < _groundNormalY;
                // Đập trúng người Player: bom rơi xuống ngay dưới chân.
                if (hitTarget) impact = SnapToGround(new Vector3(impact.x, hit.point.y, impact.z), targetRoot) + Vector3.up * radius;
                path?.Add(impact);
                return true;
            }
            pos = next;
            path?.Add(pos);
        }
        return false; // bay mãi không chạm gì (rơi khỏi map)
    }

    /// <summary>SphereCast bỏ qua collider của chính NPC, trigger và vật nhỏ có vật lý (vỏ đạn, băng rơi).</summary>
    private bool TryCast(Vector3 origin, Vector3 dir, float radius, float distance, Transform targetRoot, out RaycastHit nearest, out bool hitTarget)
    {
        nearest = default;
        hitTarget = false;
        int count = Physics.SphereCastNonAlloc(origin, radius, dir, s_Hits, distance, _trajectoryMask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = s_Hits[i];
            if (h.transform.IsChildOf(transform)) continue;
            if (h.rigidbody != null && !h.rigidbody.isKinematic && h.rigidbody.mass < 5f) continue;
            if (h.distance <= 0f && h.point == Vector3.zero) continue; // bắt đầu nằm trong collider
            if (h.distance < best)
            {
                best = h.distance;
                nearest = h;
                found = true;
            }
        }
        if (found && targetRoot != null) hitTarget = nearest.transform.IsChildOf(targetRoot);
        return found;
    }

    /// <summary>Điểm nổ không làm hại chính NPC và đồng đội (đồng đội bị vật cản che thì không sao).</summary>
    private bool IsSafe(Vector3 impact, Vector3 selfPos)
    {
        float radius = _grenadePrefab.Radius;
        if (Vector3.Distance(impact, selfPos) < radius + _selfSafetyMargin) return false;
        if (!_avoidAllies) return true;

        // Duyệt danh sách NPC (NPC có thể không có collider nên không dùng OverlapSphere).
        foreach (NPC ally in NPC.Active)
        {
            if (ally == null || ally == _npc || ally.IsDead || (_npc != null && ally.Team != _npc.Team)) continue;
            if (Vector3.Distance(impact, ally.transform.position) > radius + 1f) continue;
            Vector3 body = ally.transform.position + Vector3.up * 1f;
            if (_grenadePrefab.PredictDamage(impact + Vector3.up * 0.1f, body, ally.transform) > 0f) return false;
        }
        return true;
    }

    /// <summary>
    /// Điểm rơi đủ rộng để bom nằm yên: quanh điểm rơi (4 hướng, LedgeCheckRadius) đều có mặt đỡ cùng độ cao.
    /// Rơi trên mép tường mỏng, lan can, đầu thùng nhỏ... thì bom sẽ lăn đi chỗ khác -> không tin được.
    /// </summary>
    private bool IsStableLanding(Vector3 impact)
    {
        const float ledgeCheckRadius = 0.3f;
        const float maxDrop = 0.25f;
        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
        foreach (Vector3 d in dirs)
        {
            Vector3 origin = impact + d * ledgeCheckRadius + Vector3.up * 0.2f;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 0.2f + maxDrop + 0.1f, _trajectoryMask, QueryTriggerInteraction.Ignore))
                return false;
            if (hit.rigidbody != null && !hit.rigidbody.isKinematic) return false;
        }
        return true;
    }

    /// <summary>Mặt đất ngay dưới point, bỏ qua người (Player, NPC) và vật nhỏ có vật lý.</summary>
    private Vector3 SnapToGround(Vector3 point, Transform ignoreRoot = null)
    {
        int count = Physics.RaycastNonAlloc(point + Vector3.up * 2f, Vector3.down, s_Hits, 6f, _trajectoryMask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Vector3 ground = point;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = s_Hits[i];
            if (ignoreRoot != null && h.transform.IsChildOf(ignoreRoot)) continue;
            if (h.transform.IsChildOf(transform) || h.transform.GetComponentInParent<NPC>() != null || h.transform.root.CompareTag("Player")) continue;
            if (h.rigidbody != null && !h.rigidbody.isKinematic) continue;
            if (h.distance < best) { best = h.distance; ground = h.point; }
        }
        return ground;
    }

    // ---------- Ném ----------

    /// <summary>Còn flashbang, rảnh tay, còn sống.</summary>
    public bool CanThrowFlashbang =>
        _flashbangPrefab != null && _flashbangs != 0 && !IsBusy && (_npc == null || !_npc.IsDead) && (_holder == null || !_holder.IsReloading);

    /// <summary>
    /// Rút flashbang (cùng anim rút / ném bom) và ném vào điểm cho trước (vd. giữa phòng).
    /// Không lập kế hoạch như bom: đường bay tính thẳng từ tay tới point. Trả false nếu không ném được lúc này.
    /// </summary>
    public bool BeginThrowFlashbangAt(Vector3 point)
    {
        if (!CanThrowFlashbang) return false;
        _throwingFlash = true;
        _flashPoint = point;
        StartThrowAnim();
        return true;
    }

    /// <summary>Bắt đầu rút bom và ném. Trả false nếu không ném được lúc này.</summary>
    public bool BeginThrow()
    {
        if (!CanThrow) return false;
        _throwingFlash = false;

        // Gọi thẳng (không qua WantsToThrow, vd. từ code test): lập kế hoạch ngay.
        if (!_plan.Valid && _npc != null && _npc.Target != null)
            _plan = PlanThrow(EstimatedReleasePoint(), _npc.Target, _npc.ThreatPosition);

        _nextAllowed = Time.time + _cooldown;
        if (_logDecisions) Debug.Log($"[{name}] Rút bom ném: {LastPlanReport}", this);
        StartThrowAnim();
        return true;
    }

    private void StartThrowAnim()
    {
        _phase = Phase.Drawing;
        _phaseStart = Time.time;
        _enteredState = false;
        _released = false;
        NeedsReposition = false;
        LastNoThrowReason = "";

        _animator.ResetTrigger(FireHash);
        _animator.ResetTrigger(ThrowHash);
        _animator.SetTrigger(DrawGrenadeHash);
        SlingRifle(true);
    }

    private void Update()
    {
        bool dead = _npc != null && _npc.IsDead;

        TrackTarget();
        TrackLastThrown();

        if (dead)
        {
            if (_phase != Phase.None) Cancel();
            return;
        }

        switch (_phase)
        {
            case Phase.Drawing: TickDraw(); break;
            case Phase.Throwing: TickThrow(); break;
        }
        TickRifleBlend();
    }

    /// <summary>Ước lượng vận tốc Target (làm mượt) khi đang nhìn thấy, để đón đầu và biết Player có đứng yên không.</summary>
    private void TrackTarget()
    {
        GameObject target = _npc != null ? _npc.Target : null;
        if (target == null || Time.deltaTime <= 0f) return;
        Vector3 pos = target.transform.position;
        if (target != _trackedTarget)
        {
            _trackedTarget = target;
            _lastTargetPos = pos;
            _targetVelocity = Vector3.zero;
            return;
        }
        Vector3 v = (pos - _lastTargetPos) / Time.deltaTime;
        _lastTargetPos = pos;
        _targetVelocity = Vector3.Lerp(_targetVelocity, v, 1f - Mathf.Exp(-Time.deltaTime * 4f));
    }

    // Debug: theo dõi quả bom vừa ném, nổ thì in ra điểm nổ thật và khoảng cách tới NPC.
    private NPCGrenade _lastThrown;
    private Vector3 _lastThrownPos;

    private void TrackLastThrown()
    {
        if (_lastThrown == null && _lastThrownPos == Vector3.zero) return;
        if (_lastThrown != null && _lastThrown.IsArmed) { _lastThrownPos = _lastThrown.transform.position; return; }
        Debug.Log($"[{name}] Bom nổ tại {_lastThrownPos}, cách NPC {Vector3.Distance(_lastThrownPos, transform.position):F1}m" +
                  (_npc != null && _npc.Target != null ? $", cách Player {Vector3.Distance(_lastThrownPos, _npc.Target.transform.position):F1}m" : ""), this);
        _lastThrown = null;
        _lastThrownPos = Vector3.zero;
    }

    private void TickDraw()
    {
        float t = Progress(DrawGrenadeStateHash, _drawDuration);
        if (t >= _grabAt) Grab();

        // Anim đã sang Throw (hoặc hết anim rút bom): chuyển giai đoạn.
        if (IsInUpperState(ThrowStateHash) || t >= 1f)
        {
            Grab();
            if (!IsInUpperState(ThrowStateHash)) _animator.SetTrigger(ThrowHash); // phòng khi controller thiếu transition
            _phase = Phase.Throwing;
            _phaseStart = Time.time;
            _enteredState = false;
        }
    }

    private void TickThrow()
    {
        float t = Progress(ThrowStateHash, _throwDuration);
        if (!_released && t >= _releaseAt) Release();
        if (_rifleSlung && t >= _rifleBackAt) SlingRifle(false);
        if (t >= 1f)
        {
            if (!_released) Release();
            if (_rifleSlung) SlingRifle(false);
            _phase = Phase.None;
        }
    }

    /// <summary>Tay phải chạm túi trên hông: bom trên hông biến mất, xuất hiện trong tay.</summary>
    private void Grab()
    {
        if (_throwingFlash)
        {
            GrabFlashbang();
            return;
        }
        if (_inHand != null || _grenadePrefab == null) return;
        _grenades = Mathf.Max(0, _grenades - 1);
        UpdateHipGrenade();

        Transform grip = _handGrip != null ? _handGrip : transform;
        _inHand = Instantiate(_grenadePrefab, grip.position, grip.rotation);
        _inHand.Hold(grip);
    }

    /// <summary>
    /// Bom rời tay. Lập kế hoạch lại từ vị trí tay thật tới vị trí Player mới nhất (có lệch nhỏ nếu vẫn hiệu quả);
    /// không được thì ném vào điểm rơi đã chọn lúc quyết định.
    /// </summary>
    private void Release()
    {
        _released = true;
        if (_throwingFlash)
        {
            ReleaseFlashbang();
            return;
        }
        if (_inHand == null) return;

        Vector3 from = _inHand.transform.position;
        GameObject target = _npc != null ? _npc.Target : null;
        Vector3 targetPos = _npc != null ? _npc.ThreatPosition : transform.position + transform.forward * 10f;

        ThrowPlan plan = default;
        if (target != null)
        {
            Vector2 offset = Random.insideUnitCircle * _scatter;
            plan = PlanThrow(from, target, targetPos + new Vector3(offset.x, 0f, offset.y));
            if (!plan.Valid) plan = PlanThrow(from, target, targetPos);
        }

        Vector3 velocity;
        if (plan.Valid) velocity = plan.Velocity;
        else if (_plan.Valid && TrySolve(from, _plan.Impact, Mathf.Max(0f, _plan.Apex - _minApexHeight), out velocity, out _, out _)) { }
        else if (!TrySolve(from, SnapToGround(targetPos), _apexOptions.Length > 1 ? _apexOptions[1] : 1f, out velocity, out _, out _))
            velocity = transform.forward * 6f + Vector3.up * 4f;

        _inHand.Launch(velocity, transform, target);
        if (Simulate(from, velocity, target, out Vector3 landing, out _)) _inHand.SetPredictedImpact(landing);
        if (_logDecisions)
        {
            Vector3 planned = plan.Valid ? plan.Impact : _plan.Valid ? _plan.Impact : targetPos;
            Debug.Log($"[{name}] Bom rời tay: kế hoạch {(plan.Valid ? "mới" : _plan.Valid ? "cũ" : "KHÔNG CÓ")}, điểm rơi dự kiến {planned} " +
                      $"(cách NPC {Vector3.Distance(planned, transform.position):F1}m), v={velocity}, tay={from} | {LastPlanReport}", this);
            _lastThrown = _inHand;
        }
        _inHand = null;
        _plan = default;
    }

    /// <summary>Chết giữa chừng: bom đã rút ra thì rơi xuống chân (vẫn nổ), anim tay trở về Empty.</summary>
    private void Cancel()
    {
        if (_inHand != null)
        {
            _inHand.Launch(Random.insideUnitSphere * 0.5f, transform, null);
            _inHand = null;
        }
        if (_flashInHand != null) LaunchFlashbang(Random.insideUnitSphere * 0.5f);
        _throwingFlash = false;
        _phase = Phase.None;
        _animator.ResetTrigger(DrawGrenadeHash);
        _animator.ResetTrigger(ThrowHash);
        if (_upperBodyLayer < _animator.layerCount && _animator.HasState(_upperBodyLayer, EmptyStateHash))
            _animator.CrossFade(EmptyStateHash, 0.1f, _upperBodyLayer);
    }

    private void UpdateHipGrenade()
    {
        if (_hipGrenade != null) _hipGrenade.SetActive(_grenades > 0 || (_flashbangPrefab != null && _flashbangs != 0));
    }

    // ---------- Flashbang ----------

    /// <summary>Tay chạm hông: flashbang xuất hiện trong tay (kinematic, tắt collider để không vướng thân NPC).</summary>
    private void GrabFlashbang()
    {
        if (_flashInHand != null || _flashbangPrefab == null) return;
        if (_flashbangs > 0) _flashbangs--;
        UpdateHipGrenade();

        Transform grip = _handGrip != null ? _handGrip : transform;
        _flashInHand = Instantiate(_flashbangPrefab, grip.position, grip.rotation, grip);
        if (_flashInHand.TryGetComponent(out Rigidbody body)) body.isKinematic = true;
        foreach (Collider c in _flashInHand.GetComponentsInChildren<Collider>()) c.enabled = false;
    }

    private void ReleaseFlashbang()
    {
        _throwingFlash = false;
        if (_flashInHand == null) return;
        Vector3 from = _flashInHand.transform.position;
        if (!TrySolve(from, _flashPoint, _flashbangApex, out Vector3 velocity, out _, out _))
        {
            Vector3 dir = _flashPoint - from;
            dir.y = 0f;
            velocity = dir.normalized * 6f + Vector3.up * 3f;
        }
        LaunchFlashbang(velocity);
    }

    /// <summary>Rời tay, rút chốt, ngòi bắt đầu cháy (VRGrenade.Arm).</summary>
    private void LaunchFlashbang(Vector3 velocity)
    {
        VRGrenade flash = _flashInHand;
        _flashInHand = null;
        flash.transform.SetParent(null, true);
        Collider[] colliders = flash.GetComponentsInChildren<Collider>(true);
        foreach (Collider c in colliders) c.enabled = true;
        foreach (Collider own in GetComponentsInChildren<Collider>())
            foreach (Collider c in colliders)
                Physics.IgnoreCollision(own, c);

        if (flash.TryGetComponent(out Rigidbody body))
        {
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = velocity;
            body.angularVelocity = Random.onUnitSphere * Random.Range(4f, 9f);
        }
        flash.Arm();
    }

    // ---------- Súng: đeo lên ngực / cầm lại ----------

    private void SlingRifle(bool sling)
    {
        if (_rifle == null || _rifleSling == null || _rifleHome == null || _rifleSlung == sling) return;
        _rifleSlung = sling;
        _rifle.SetParent(sling ? _rifleSling : _rifleHome, true);
        _blendFromPos = _rifle.localPosition;
        _blendFromRot = _rifle.localRotation;
        _rifleBlend = 0f;
    }

    private void TickRifleBlend()
    {
        if (_rifle == null || _rifleBlend >= 1f) return;
        _rifleBlend = Mathf.MoveTowards(_rifleBlend, 1f, Time.deltaTime / Mathf.Max(0.01f, _rifleBlendTime));
        float k = Mathf.SmoothStep(0f, 1f, _rifleBlend);
        Vector3 pos = _rifleSlung ? Vector3.zero : _rifleHomePos;
        Quaternion rot = _rifleSlung ? Quaternion.identity : _rifleHomeRot;
        _rifle.localPosition = Vector3.Lerp(_blendFromPos, pos, k);
        _rifle.localRotation = Quaternion.Slerp(_blendFromRot, rot, k);
    }

    // ---------- Tiến độ anim ----------

    /// <summary>Tiến độ 0-1 của state: đọc từ Animator khi đã vào state, không thì tính theo thời gian.</summary>
    private float Progress(int stateHash, float duration)
    {
        if (_upperBodyLayer < _animator.layerCount)
        {
            if (_animator.IsInTransition(_upperBodyLayer))
            {
                AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(_upperBodyLayer);
                if (next.shortNameHash == stateHash)
                {
                    _enteredState = true;
                    return Mathf.Clamp01(next.normalizedTime);
                }
            }
            AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(_upperBodyLayer);
            if (current.shortNameHash == stateHash)
            {
                _enteredState = true;
                return Mathf.Clamp01(current.normalizedTime);
            }
            if (_enteredState) return 1f; // đã rời state
        }
        return Mathf.Clamp01((Time.time - _phaseStart) / Mathf.Max(0.1f, duration));
    }

    private bool IsInUpperState(int hash)
    {
        if (_upperBodyLayer >= _animator.layerCount) return false;
        if (_animator.IsInTransition(_upperBodyLayer) && _animator.GetNextAnimatorStateInfo(_upperBodyLayer).shortNameHash == hash) return true;
        return _animator.GetCurrentAnimatorStateInfo(_upperBodyLayer).shortNameHash == hash;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0f);
        Gizmos.DrawWireSphere(transform.position, _throwRange.x);
        Gizmos.DrawWireSphere(transform.position, _throwRange.y);
        if (_handGrip != null) Gizmos.DrawWireSphere(_handGrip.position, 0.04f);

        if (!_drawPlanGizmos || !Application.isPlaying) return;
        // Đường bay của kế hoạch gần nhất (xanh = ném được, đỏ = không có cách ném hiệu quả) và vùng nổ dự đoán.
        Gizmos.color = _debugValid ? Color.green : Color.red;
        for (int i = 1; i < _debugPath.Count; i++) Gizmos.DrawLine(_debugPath[i - 1], _debugPath[i]);
        if (_grenadePrefab != null) Gizmos.DrawWireSphere(_debugImpact, _grenadePrefab.Radius);
    }
}
