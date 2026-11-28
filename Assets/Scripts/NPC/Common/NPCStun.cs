using System;
using System.Collections.Generic;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Choáng do flashbang (như SWAT 4) cho NPC (nghi phạm, đồng đội) và con tin.
///
/// - Không sửa script lựu đạn: VRGrenade đã gọi NPCMorale.ReportExplosion khi nổ; ở đó nếu thấy component Flashbang
///   ngay tại tâm nổ thì gọi NPCStun.ReportFlash(center, FlashRadius).
/// - Mọi NPC / con tin trong bán kính, có đường nhìn tới tâm nổ, bị choáng theo cường độ: gần + nhìn thẳng vào thì
///   nặng. Rất gần (CloseRadius) thì choáng cả khi bị khuất.
/// - Choáng (3-8 s theo cường độ): dừng Behavior Graph (End, các node dọn dẹp như bình thường), đứng yên, hạ súng,
///   thả tay IK, che mắt (bool Animator "Stunned" nếu controller có; chưa có thì ngồi thụp tại chỗ). Không bắn, không chạy.
/// - Nghi phạm: morale tụt mạnh; bị hô hàng lúc choáng thì gần như chắc chắn đầu hàng (ComplyBonus cộng vào ngưỡng
///   đầu hàng của NPCMorale). Con tin: bị hô là giơ tay ngay.
/// - Đã đầu hàng / bị còng: chỉ giật mình (Flinch).
/// - Hết choáng: bật lại graph và Restart (Selector chọn lại nhánh từ đầu).
/// Component tự được thêm vào NPC / con tin lần đầu bị flash; gắn sẵn để chỉnh thông số (đồng đội: Resistance cao).
/// </summary>
[DisallowMultipleComponent]
public class NPCStun : MonoBehaviour
{
    /// <summary>Bán kính ảnh hưởng của một quả flashbang (m), dùng khi không đọc được thông số của quả bom.</summary>
    public const float FlashRadius = 10f;

    /// <summary>Trong bán kính này flash vẫn làm choáng dù bị khuất (tiếng nổ + ánh sáng dội tường).</summary>
    private const float CloseRadius = 1.5f;

    private static readonly int StunnedHash = Animator.StringToHash("Stunned");

    [Header("Thời gian choáng (giây) theo cường độ 0 -> 1")]
    [SerializeField] private Vector2 _duration = new(3f, 8f);
    [Tooltip("Cường độ dưới mức này thì không choáng (chỉ giật mình / cảnh giác).")]
    [SerializeField, Range(0f, 1f)] private float _minIntensity = 0.15f;
    [Tooltip("Giảm cường độ (0 = không, 1 = miễn nhiễm). Đồng đội được huấn luyện: 0.6.")]
    [SerializeField, Range(0f, 1f)] private float _resistance;

    [Header("Ảnh hưởng")]
    [Tooltip("Morale bị trừ (nhân cường độ) khi bị choáng.")]
    [SerializeField] private float _moralePressure = 0.35f;
    [Tooltip("Cộng vào ngưỡng đầu hàng khi bị hô lúc đang choáng (0.5 = rất dễ đầu hàng).")]
    [SerializeField, Range(0f, 1f)] private float _complyBonus = 0.5f;
    [Tooltip("Flash nổ sát người (trong CloseDamageRadius) gây sát thương nhẹ (bỏng, mảnh vỏ), như SWAT 4. 0 = tắt.")]
    [SerializeField] private float _closeDamage = 15f;
    [SerializeField] private float _closeDamageRadius = 1.2f;

    [Header("Debug")]
    [SerializeField] private bool _log = true;

    /// <summary>(ai, cường độ) mỗi khi có ai bị choáng.</summary>
    public static event Action<NPCStun, float> AnyStunned;

    private NPC _npc;
    private Hostage _hostage;
    private BehaviorGraphAgent _graph;
    private NavMeshAgent _agent;
    private Animator _animator;
    private NPCWeaponHolder _holder;
    private bool _hasStunnedParam;
    private bool _stunned;
    private bool _crouchFallback;
    private float _stunUntil;

    public bool IsStunned => _stunned;
    public float StunRemaining => _stunned ? Mathf.Max(0f, _stunUntil - Time.time) : 0f;
    public float Intensity { get; private set; }

    /// <summary>Cộng vào ngưỡng đầu hàng lúc đang choáng (0 nếu không choáng).</summary>
    public float ComplyBonus => _stunned ? _complyBonus * Mathf.Max(0.5f, Intensity) : 0f;

    public float Resistance
    {
        get => _resistance;
        set => _resistance = Mathf.Clamp01(value);
    }

    // ---------- API tĩnh ----------

    /// <summary>Flashbang nổ tại center: choáng mọi NPC / con tin trong radius.</summary>
    public static void ReportFlash(Vector3 center, float radius)
    {
        if (radius <= 0f) return;
        var targets = new List<GameObject>();
        foreach (NPC npc in NPC.Active) if (npc != null && !npc.IsDead) targets.Add(npc.gameObject);
        foreach (Hostage h in Hostage.Active) if (h != null && !h.IsDead) targets.Add(h.gameObject);
        foreach (GameObject go in targets) For(go).ReceiveFlash(center, radius);
    }

    /// <summary>NPCStun của go (chưa có thì thêm).</summary>
    public static NPCStun For(GameObject go) => go.TryGetComponent(out NPCStun s) ? s : go.AddComponent<NPCStun>();

    /// <summary>Choáng đang cộng thêm bao nhiêu vào ngưỡng đầu hàng của go (0 nếu không choáng).</summary>
    public static float ComplyBonusOf(GameObject go) => go != null && go.TryGetComponent(out NPCStun s) ? s.ComplyBonus : 0f;

    public static bool IsStunnedObject(GameObject go) => go != null && go.TryGetComponent(out NPCStun s) && s.IsStunned;

    // ---------- Vòng đời ----------

    private void Awake()
    {
        _npc = GetComponent<NPC>();
        _hostage = GetComponent<Hostage>();
        _graph = GetComponent<BehaviorGraphAgent>();
        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponentInChildren<Animator>();
        _holder = GetComponentInChildren<NPCWeaponHolder>();
        if (_animator != null && _animator.runtimeAnimatorController != null)
            foreach (AnimatorControllerParameter p in _animator.parameters)
                if (p.nameHash == StunnedHash && p.type == AnimatorControllerParameterType.Bool) _hasStunnedParam = true;
    }

    private void OnDisable()
    {
        if (_stunned) EndStun();
    }

    private void Update()
    {
        if (!_stunned) return;
        if (IsDead || Surrendered)
        {
            EndStun(); // đầu hàng / chết trong lúc choáng: NPC tự lo phần còn lại
            return;
        }
        HoldStill();
        if (Time.time >= _stunUntil) EndStun();
    }

    // ---------- Choáng ----------

    /// <summary>Tính cường độ theo khoảng cách, đường nhìn và hướng mặt rồi choáng. Trả true nếu bị choáng.</summary>
    public bool ReceiveFlash(Vector3 center, float radius)
    {
        if (IsDead) return false;
        Vector3 eye = NPCConditionUtils.EyeOf(gameObject);
        float dist = Vector3.Distance(eye, center);
        if (dist > radius) return false;

        // Nổ sát người: sát thương nhẹ, giảm dần tới 0 ở CloseDamageRadius (không xuyên tường).
        Vector3 body = NPCConditionUtils.CenterOf(gameObject);
        float bodyDist = Vector3.Distance(body, center);
        if (_closeDamage > 0f && bodyDist < _closeDamageRadius && NPCMorale.ClearPath(center, body, transform))
        {
            float amount = _closeDamage * (1f - bodyDist / _closeDamageRadius);
            Damage.Apply(gameObject, new DamageInfo { Amount = amount, Type = DamageType.Flash, From = center, HasFrom = true });
            if (IsDead) return false;
        }

        bool visible = NPCMorale.ClearPath(center, eye, transform);
        if (!visible && dist > CloseRadius) return false;

        float distFactor = 1f - Mathf.Clamp01(dist / radius);
        Vector3 toBlast = center - eye;
        toBlast.y = 0f;
        float facing = toBlast.sqrMagnitude > 0.01f ? Vector3.Dot(transform.forward, toBlast.normalized) : 1f;
        float faceFactor = Mathf.Lerp(0.55f, 1f, (facing + 1f) * 0.5f); // quay lưng vẫn bị (tiếng nổ chát chúa)
        float intensity = Mathf.Clamp01(Mathf.Lerp(0.35f, 1f, distFactor) * faceFactor * (visible ? 1f : 0.6f));
        return Stun(intensity);
    }

    /// <summary>Choáng với cường độ 0..1 (trước khi tính Resistance). Trả true nếu bị choáng.</summary>
    public bool Stun(float intensity)
    {
        if (IsDead) return false;
        intensity = Mathf.Clamp01(intensity * (1f - _resistance));

        if (Surrendered)
        {
            if (intensity > 0.05f) Flinch();
            return false;
        }
        if (intensity < _minIntensity)
        {
            NotifyThreat();
            return false;
        }

        float duration = Mathf.Lerp(_duration.x, Mathf.Max(_duration.x, _duration.y), intensity);
        _stunUntil = Mathf.Max(_stunUntil, Time.time + duration);
        Intensity = Mathf.Max(_stunned ? Intensity : 0f, intensity);

        if (_npc != null && _npc.TryGetComponent(out NPCMorale morale))
            morale.Pressure(_moralePressure * intensity, "bị choáng");

        if (!_stunned) BeginStun();
        if (_log) Debug.Log($"[Stun] {name}: choáng {intensity:0.00} trong {StunRemaining:0.0} s.", this);
        AnyStunned?.Invoke(this, intensity);
        return true;
    }

    private void BeginStun()
    {
        _stunned = true;
        if (_graph != null)
        {
            _graph.End();          // node đang chạy dọn dẹp (thả cửa, hạ tay, đứng dậy...)
            _graph.enabled = false;
        }
        if (_holder != null)
        {
            _holder.StopAiming();
            _holder.ReleaseLeftHand();
        }
        HoldStill();

        if (_hasStunnedParam) _animator.SetBool(StunnedHash, true);
        else if (_npc != null)
        {
            _crouchFallback = true; // controller chưa có anim che mắt: ngồi thụp tại chỗ
            _npc.CrouchInPlace();
        }
        if (_hostage != null) _hostage.SetScared(true);

        if (_npc != null) _npc.NotifyStunned();
        if (_hostage != null) _hostage.NotifyStunned();
    }

    private void EndStun()
    {
        _stunned = false;
        Intensity = 0f;
        if (_hasStunnedParam && _animator != null) _animator.SetBool(StunnedHash, false);
        if (_crouchFallback && _npc != null && !_npc.IsSurrendered && !_npc.IsDead) _npc.LeaveCover();
        _crouchFallback = false;
        if (_hostage != null && !_hostage.IsSurrendered) _hostage.SetScared(false);

        if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.isStopped = false;

        // NPC đầu hàng / chết trong lúc choáng thì graph đã tắt hẳn (NPC tự lo), không bật lại.
        // Con tin thì graph vẫn chạy sau khi đầu hàng (nhánh chờ còng / bị còng), chỉ không bật lại khi đã chết.
        bool npcDone = _npc != null && (_npc.IsDead || _npc.IsSurrendered);
        bool hostageDone = _hostage != null && _hostage.IsDead;
        if (_graph != null && !npcDone && !hostageDone && isActiveAndEnabled)
        {
            _graph.enabled = true;
            _graph.Restart();
        }
        if (_log) Debug.Log($"[Stun] {name}: hết choáng.", this);
    }

    private void HoldStill()
    {
        if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return;
        if (_agent.hasPath) _agent.ResetPath();
        _agent.isStopped = true;
    }

    // ---------- Tiện ích ----------

    private bool IsDead => (_npc != null && _npc.IsDead) || (_hostage != null && _hostage.IsDead);
    private bool Surrendered => (_npc != null && _npc.IsSurrendered) || (_hostage != null && _hostage.IsSurrendered);

    private void Flinch()
    {
        if (_npc != null) _npc.Flinch();
        if (_hostage != null) _hostage.Flinch();
    }

    private void NotifyThreat()
    {
        if (_npc != null) _npc.NotifyThreat();
        if (_hostage != null) _hostage.NotifyThreat();
    }
}
