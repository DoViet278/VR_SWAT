using System;
using UnityEngine;
using UnityEngine.Events;
using static CONST;

/// <summary>Tâm lý chiến đấu của NPC, suy ra từ Morale.</summary>
public enum MoraleState
{
    /// <summary>Tinh thần cao: tấn công như bình thường (bắn ngoài trống, đuổi theo).</summary>
    Aggressive,
    /// <summary>Tinh thần lung lay: ưu tiên nấp, ngồi nấp lâu hơn, bắn ít hơn, kém chính xác hơn.</summary>
    Defensive,
    /// <summary>Hoảng loạn: gần như chỉ nấp, bắn rất ít. Bị hô hàng là đầu hàng.</summary>
    Panicked,
}

/// <summary>
/// Chỉ số tinh thần (Morale, 0-1) của NPC, mô phỏng cơ chế của SWAT 4:
///
/// - Mỗi NPC bắt đầu với morale ngẫu nhiên trong StartMorale (giống MinMorale/MaxMorale của từng loại nghi phạm).
/// - Mọi áp lực từ người chơi làm morale tụt: bị hô "Police! Drop your weapon!", bị bắn trúng, đạn sượt qua người,
///   lựu đạn / flashbang nổ gần, đồng đội gục hoặc đầu hàng, hết đạn, máu thấp.
///   Bị hô bất ngờ (NPC chưa phát hiện người chơi) thì tụt mạnh hơn.
/// - Khi người chơi hô hàng (ReceiveComplianceOrder): morale dưới ComplyBelow thì NPC đầu hàng,
///   còn cao hơn thì phớt lờ và giao chiến (lần hô sau dễ khuất phục hơn vì morale đã tụt).
/// - Morale quyết định cách đánh (State): Aggressive -> tấn công, Defensive -> ưu tiên nấp, Panicked -> chỉ chực nấp.
/// - Morale rất thấp (dưới AutoSurrenderBelow) và đang thấy người chơi ở gần thì tự đầu hàng không cần hô.
/// - Yên ổn một lúc (không bị áp lực trong CalmDelay giây) thì morale hồi dần về mức ban đầu.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NPC))]
public class NPCMorale : MonoBehaviour
{
    [Header("Morale")]
    [Tooltip("Morale ban đầu, random trong khoảng (0-1). Thấp = nghi phạm nhút nhát, cao = liều lĩnh.")]
    [SerializeField] private Vector2 _startMorale = new(0.5f, 0.9f);

    [Header("Ngưỡng")]
    [Tooltip("Morale dưới mức này: chuyển sang phòng thủ (ưu tiên nấp).")]
    [SerializeField, Range(0f, 1f)] private float _defensiveBelow = 0.6f;
    [Tooltip("Morale dưới mức này: hoảng loạn, và bị hô hàng là đầu hàng.")]
    [SerializeField, Range(0f, 1f)] private float _complyBelow = 0.35f;
    [Tooltip("Độ ngẫu nhiên (+/-) khi xét đầu hàng, để không đoán trước được hoàn toàn.")]
    [SerializeField, Range(0f, 0.2f)] private float _complyJitter = 0.05f;
    [Tooltip("Morale dưới mức này và đang thấy người chơi trong AutoSurrenderDistance: tự đầu hàng. 0 = tắt.")]
    [SerializeField, Range(0f, 1f)] private float _autoSurrenderBelow = 0.1f;
    [SerializeField] private float _autoSurrenderDistance = 8f;

    [Header("Áp lực (lượng morale bị trừ)")]
    [Tooltip("Mỗi lần nghe người chơi hô hàng.")]
    [SerializeField] private float _shout = 0.1f;
    [Tooltip("Hệ số nhân thêm khi bị hô lúc chưa phát hiện người chơi (bị bất ngờ).")]
    [SerializeField] private float _surpriseMultiplier = 2f;
    [Tooltip("Mỗi lần bị bắn trúng.")]
    [SerializeField] private float _wounded = 0.12f;
    [Tooltip("Cộng thêm theo % máu mất của phát bắn (vd. 0.4 x 25% máu = 0.1).")]
    [SerializeField] private float _woundedPerHealth = 0.4f;
    [Tooltip("Đạn của người chơi bay sượt qua trong NearMissRadius (bị áp chế).")]
    [SerializeField] private float _nearMiss = 0.04f;
    [SerializeField] private float _nearMissRadius = 1.5f;
    [Tooltip("Lựu đạn / flashbang nổ trong bán kính ảnh hưởng.")]
    [SerializeField] private float _explosion = 0.3f;
    [Tooltip("Thấy / ở gần đồng đội bị hạ.")]
    [SerializeField] private float _allyKilled = 0.2f;
    [Tooltip("Ở gần đồng đội đầu hàng.")]
    [SerializeField] private float _allySurrendered = 0.12f;
    [SerializeField] private float _allyRadius = 20f;
    [Tooltip("Hết sạch đạn (một lần).")]
    [SerializeField] private float _outOfAmmo = 0.3f;
    [Tooltip("Máu tụt dưới LowHealthRatio (một lần).")]
    [SerializeField] private float _lowHealth = 0.15f;
    [SerializeField, Range(0f, 1f)] private float _lowHealthRatio = 0.35f;

    [Header("Tuân lệnh (SWAT 4)")]
    [Tooltip("Máu dưới LowHealthRatio: cộng ngần này vào ngưỡng đầu hàng khi bị hô.")]
    [SerializeField, Range(0f, 0.5f)] private float _woundedComplyBonus = 0.15f;
    [Tooltip("Vừa nổ súng trong ngần này giây (và không bị choáng) thì phớt lờ lệnh hô.")]
    [SerializeField] private float _firingIgnoreTime = 1f;

    [Header("Củng cố / hồi phục")]
    [Tooltip("Mỗi phát bắn trúng người chơi.")]
    [SerializeField] private float _hitPlayer = 0.02f;
    [Tooltip("Hồi morale mỗi giây khi yên ổn, tối đa về mức ban đầu.")]
    [SerializeField] private float _recoverPerSecond = 0.01f;
    [SerializeField] private float _calmDelay = 12f;

    [Header("Debug")]
    [SerializeField] private bool _logChanges;

    [Header("Events")]
    public UnityEvent<float> OnMoraleChanged;

    private NPC _npc;
    private NPCWeaponHolder _holder;
    private float _morale;
    private float _baseMorale;
    private float _lastPressure = float.NegativeInfinity;
    private float _nextNearMiss;
    private bool _outOfAmmoApplied;
    private bool _lowHealthApplied;

    public float Morale => _morale;
    public float BaseMorale => _baseMorale;
    public float ComplyBelow => _complyBelow;

    public MoraleState State =>
        _morale < _complyBelow ? MoraleState.Panicked :
        _morale < _defensiveBelow ? MoraleState.Defensive :
        MoraleState.Aggressive;

    /// <summary>Hệ số độ chính xác khi bắn: 1 lúc tinh thần vững, ~0.5 lúc hoảng loạn.</summary>
    public float AccuracyScale => Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(_morale / Mathf.Max(0.01f, _defensiveBelow)));

    private void Awake()
    {
        _npc = GetComponent<NPC>();
        _holder = GetComponentInChildren<NPCWeaponHolder>();
        float min = Mathf.Clamp01(Mathf.Min(_startMorale.x, _startMorale.y));
        float max = Mathf.Clamp01(Mathf.Max(_startMorale.x, _startMorale.y));
        _baseMorale = _morale = UnityEngine.Random.Range(min, max);
    }

    private void OnEnable()
    {
        ObserverManager.Register(PLAYER_SHOOT_TARGET, (Action<Gun, GameObject, RaycastHit>)OnPlayerShoot);
        _npc.OnDamaged.AddListener(OnDamaged);
    }

    private void OnDisable()
    {
        ObserverManager.Unregister(PLAYER_SHOOT_TARGET, (Action<Gun, GameObject, RaycastHit>)OnPlayerShoot);
        _npc.OnDamaged.RemoveListener(OnDamaged);
    }

    private void Update()
    {
        if (_npc.IsDead || _npc.IsSurrendered) return;

        if (!_outOfAmmoApplied && _holder != null && _holder.IsOutOfAmmo)
        {
            _outOfAmmoApplied = true;
            Pressure(_outOfAmmo, "hết đạn");
        }

        if (_autoSurrenderBelow > 0f && _morale < _autoSurrenderBelow && _npc.CanSeeTarget && _npc.Target != null
            && NPCConditionUtils.IsWithin(transform.position, _npc.Target.transform.position, _autoSurrenderDistance))
        {
            if (_logChanges) Debug.Log($"[Morale] {name}: suy sụp, tự đầu hàng ({_morale:0.00}).", this);
            _npc.Surrender();
            return;
        }

        if (_recoverPerSecond > 0f && _morale < _baseMorale && Time.time - _lastPressure >= _calmDelay)
            SetMorale(Mathf.Min(_baseMorale, _morale + _recoverPerSecond * Time.deltaTime), null);
    }

    // ---------- API ----------

    /// <summary>Trừ morale vì một áp lực (reason để log).</summary>
    public void Pressure(float amount, string reason)
    {
        if (amount <= 0f || _npc.IsDead) return;
        _lastPressure = Time.time;
        SetMorale(_morale - amount, reason);
    }

    /// <summary>Cộng morale (vd. bắn trúng người chơi).</summary>
    public void Boost(float amount, string reason)
    {
        if (amount <= 0f || _npc.IsDead) return;
        SetMorale(Mathf.Min(1f, _morale + amount), reason);
    }

    /// <summary>NPC vừa bắn trúng người chơi.</summary>
    public void OnHitPlayer() => Boost(_hitPlayer, "bắn trúng người chơi");

    /// <summary>
    /// Người chơi hô hàng ("Police! Drop your weapon!"). Trả true nếu NPC đầu hàng.
    /// Không đầu hàng thì NPC phát hiện người chơi và tiếp tục giao chiến.
    /// </summary>
    public bool ReceiveComplianceOrder(Vector3 issuerPosition)
    {
        if (_npc.IsDead || _npc.IsSurrendered) return false;

        bool surprised = !_npc.IsAlerted;
        Pressure(_shout * (surprised ? _surpriseMultiplier : 1f), surprised ? "bị hô bất ngờ" : "bị hô hàng");

        if (TryGetComponent(out EnemyTactics tactics)) tactics.NotifyComplianceOrder(); // đếm lần bị hô khi giữ con tin

        // Như SWAT 4: bị choáng hoặc bị thương nặng thì dễ đầu hàng hơn nhiều;
        // đang nổ súng (vừa bắn trong FiringIgnoreTime giây) mà không bị choáng thì phớt lờ lệnh.
        bool stunned = NPCStun.IsStunnedObject(gameObject);
        float bonus = NPCStun.ComplyBonusOf(gameObject);
        if (_npc.MaxHealth > 0f && _npc.Health < _npc.MaxHealth * _lowHealthRatio) bonus += _woundedComplyBonus;
        bool firing = !stunned && _holder != null && Time.time - _holder.LastFireTime <= _firingIgnoreTime;

        float roll = _morale + UnityEngine.Random.Range(-_complyJitter, _complyJitter);
        if (!firing && roll < _complyBelow + bonus && _npc.Surrender())
        {
            if (_logChanges) Debug.Log($"[Morale] {name}: đầu hàng ({_morale:0.00}, cộng {bonus:0.00}).", this);
            return true;
        }

        _npc.Say(NPCVoiceLine.Taunt); // "Get out of here!"
        _npc.NotifyThreat();
        if (_logChanges) Debug.Log($"[Morale] {name}: phớt lờ lệnh hô ({_morale:0.00}).", this);
        return false;
    }

    // ---------- Sự kiện chung (gọi từ súng, lựu đạn, NPC khác) ----------

    /// <summary>Người chơi hô hàng: mọi NPC nghe được (trong radius, thấy hoặc rất gần) xét đầu hàng. Trả số NPC đầu hàng.</summary>
    public static int IssueComplianceOrder(Vector3 origin, float radius, float hearThroughWallsRadius, GameObject exclude = null)
    {
        int complied = 0;
        foreach (NPC npc in NPC.Active.ToArray())
        {
            if (npc == null || npc.IsDead || npc.IsSurrendered || npc.gameObject == exclude) continue;
            if (!npc.TryGetComponent(out NPCMorale morale)) continue;
            Vector3 ear = NPCConditionUtils.EyeOf(npc.gameObject);
            float sqr = (ear - origin).sqrMagnitude;
            if (sqr > radius * radius) continue;
            if (sqr > hearThroughWallsRadius * hearThroughWallsRadius && !ClearPath(origin, ear, npc.transform)) continue;
            if (morale.ReceiveComplianceOrder(origin)) complied++;
        }
        return complied;
    }

    /// <summary>
    /// Vụ nổ (lựu đạn, flashbang...) tại center: NPC trong radius và không bị chắn sẽ tụt morale.
    /// VRGrenade (lựu đạn của người chơi / đồng đội) gọi hàm này lúc nổ. Nếu đó là flashbang (có component Flashbang
    /// ngay tại tâm nổ) thì NPC / con tin còn bị choáng (NPCStun). Mọi vụ nổ đều phát tiếng động (NPCHearing).
    /// </summary>
    public static void ReportExplosion(Vector3 center, float radius, float strength = 1f)
    {
        NPCHearing.Report(center, 40f, NoiseType.Explosion);
        if (IsFlashbangAt(center)) NPCStun.ReportFlash(center, NPCStun.FlashRadius);

        foreach (NPC npc in NPC.Active.ToArray())
        {
            if (npc == null || npc.IsDead || !npc.TryGetComponent(out NPCMorale morale)) continue;
            Vector3 chest = NPCConditionUtils.CenterOf(npc.gameObject);
            float dist = Vector3.Distance(center, chest);
            if (dist > radius || !ClearPath(center, chest, npc.transform)) continue;
            float falloff = Mathf.Lerp(1f, 0.4f, dist / Mathf.Max(0.01f, radius));
            if (npc.IsSurrendered) npc.Flinch();
            else
            {
                morale.Pressure(morale._explosion * strength * falloff, "nổ gần");
                npc.NotifyThreat();
            }
        }
    }

    /// <summary>Một NPC bị hạ / đầu hàng: đồng đội ở gần tụt morale.</summary>
    public static void ReportAllyDown(NPC who, bool killed)
    {
        if (who == null) return;
        foreach (NPC npc in NPC.Active.ToArray())
        {
            if (npc == null || npc == who || npc.Team != who.Team || npc.IsDead || npc.IsSurrendered || !npc.TryGetComponent(out NPCMorale morale)) continue;
            if (!NPCConditionUtils.IsWithin(npc.transform.position, who.transform.position, morale._allyRadius)) continue;
            morale.Pressure(killed ? morale._allyKilled : morale._allySurrendered, killed ? "đồng đội bị hạ" : "đồng đội đầu hàng");
        }
    }

    // ---------- Nội bộ ----------

    private void OnDamaged(float amount)
    {
        if (_npc.IsDead) return;
        Pressure(_wounded + _woundedPerHealth * amount / Mathf.Max(1f, _npc.MaxHealth), "trúng đạn");
        if (!_lowHealthApplied && _npc.Health < _npc.MaxHealth * _lowHealthRatio)
        {
            _lowHealthApplied = true;
            Pressure(_lowHealth, "máu thấp");
        }
    }

    /// <summary>Đạn người chơi: trúng chỗ khác nhưng bay sượt qua NPC thì tính là bị áp chế.</summary>
    private void OnPlayerShoot(Gun gun, GameObject hitObject, RaycastHit hit)
    {
        if (_npc.IsDead || _npc.IsSurrendered || gun == null || Time.time < _nextNearMiss) return;
        if (hitObject != null && hitObject.transform.IsChildOf(transform)) return; // trúng thật: OnDamaged lo

        Vector3 chest = NPCConditionUtils.CenterOf(gameObject);
        Vector3 from = gun.transform.position;
        if (DistanceToSegment(chest, from, hit.point) > _nearMissRadius) return;

        _nextNearMiss = Time.time + 0.2f;
        Pressure(_nearMiss, "đạn sượt qua");
        _npc.NotifyThreat();
    }

    private void SetMorale(float value, string reason)
    {
        float old = _morale;
        _morale = Mathf.Clamp01(value);
        if (Mathf.Approximately(old, _morale)) return;
        OnMoraleChanged?.Invoke(_morale);
        if (_logChanges && reason != null)
            Debug.Log($"[Morale] {name}: {old:0.00} -> {_morale:0.00} ({reason}) => {State}", this);
    }

    /// <summary>
    /// Có quả flashbang (component VRSwat.Grenades.Flashbang) nằm ngay tại center không. Lúc VRGrenade báo nổ thì
    /// object quả bom chưa bị hủy, nên tìm theo vị trí. Chỉ chạy lúc có vụ nổ.
    /// </summary>
    private static bool IsFlashbangAt(Vector3 center)
    {
        const float maxOffset = 0.6f;
        foreach (VRSwat.Grenades.Flashbang flash in FindObjectsByType<VRSwat.Grenades.Flashbang>(FindObjectsSortMode.None))
            if (flash != null && (flash.transform.position - center).sqrMagnitude <= maxOffset * maxOffset) return true;
        return false;
    }

    /// <summary>Không có vật cản giữa from và to (vật chắn thuộc ignore thì bỏ qua). NPCStun / NPCHearing dùng chung.</summary>
    public static bool ClearPath(Vector3 from, Vector3 to, Transform ignore)
    {
        if (!Physics.Linecast(from, to, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return true;
        return hit.transform.IsChildOf(ignore);
    }

    private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector3.Distance(p, a + ab * t);
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        Gizmos.color = State switch
        {
            MoraleState.Aggressive => Color.red,
            MoraleState.Defensive => Color.yellow,
            _ => Color.white,
        };
        Gizmos.DrawSphere(transform.position + Vector3.up * 2.2f, 0.1f + 0.15f * _morale);
    }
}
