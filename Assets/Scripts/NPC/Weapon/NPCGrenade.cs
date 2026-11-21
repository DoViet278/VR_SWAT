using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Quả bom NPC ném ra. Khi còn trong tay: kinematic, tắt collider, bám theo tay.
/// Launch(): rời tay, cần gạt (Spoon) bật ra, ngòi bắt đầu cháy; chạm đất phát tiếng nảy.
/// Hết FuseTime: sinh hiệu ứng nổ (ExplosionPrefab: lửa, khói, chớp sáng, vết cháy, tiếng nổ),
/// gây sát thương giảm dần theo khoảng cách cho mọi thứ trong Radius không bị tường chắn,
/// và đẩy văng các Rigidbody xung quanh.
///
/// Sát thương: NPC nhận qua NPC.TakeDamageFrom (để chọn anim trúng đòn / chết đúng hướng),
/// object khác (Player) nhận qua SendMessage("TakeDamage", float) giống phát bắn của AttackAction.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class NPCGrenade : MonoBehaviour
{
    [Header("Fuse")]
    [SerializeField] private float _fuseTime = 2.5f;

    [Header("Damage")]
    [SerializeField] private float _damage = 120f;
    [Tooltip("Trong bán kính này nhận đủ sát thương, xa hơn thì giảm dần tới 0 ở Radius.")]
    [SerializeField] private float _innerRadius = 1.5f;
    [SerializeField] private float _radius = 6f;
    [SerializeField] private float _force = 700f;
    [SerializeField] private LayerMask _damageMask = Physics.DefaultRaycastLayers;
    [Tooltip("Lớp vật cản chắn sát thương (tường, vật che).")]
    [SerializeField] private LayerMask _occlusionMask = Physics.DefaultRaycastLayers;

    [Header("Effects")]
    [SerializeField] private GameObject _explosionPrefab;
    [Tooltip("Cần gạt của bom, bật ra khi bom rời tay.")]
    [SerializeField] private Rigidbody _spoon;
    [SerializeField] private AudioSource _audio;
    [SerializeField] private AudioClip[] _bounceClips;
    [SerializeField, Range(0f, 1f)] private float _bounceVolume = 0.6f;

    private Rigidbody _body;
    private Collider[] _colliders;
    private bool _armed;
    private float _explodeAt;
    private float _nextBounceSound;
    private GameObject _target;
    private static readonly Collider[] s_Hits = new Collider[64];
    private static readonly RaycastHit[] s_Rays = new RaycastHit[16];

    private void Awake()
    {
        _body = GetComponent<Rigidbody>();
        _colliders = GetComponentsInChildren<Collider>(true);
        if (_audio == null) _audio = GetComponent<AudioSource>();
    }

    /// <summary>Cầm trong tay: bám theo điểm grip, không va chạm.</summary>
    public void Hold(Transform grip)
    {
        transform.SetParent(grip, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        _body.isKinematic = true;
        SetColliders(false);
        if (_spoon != null) _spoon.isKinematic = true;
    }

    /// <summary>Ném đi với vận tốc velocity. owner: NPC ném (không va chạm với chính NPC). target: luôn được tính sát thương nếu trong tầm.</summary>
    public void Launch(Vector3 velocity, Transform owner, GameObject target)
    {
        _target = target;
        transform.SetParent(null, true);
        SetColliders(true);
        if (owner != null)
            foreach (Collider own in owner.GetComponentsInChildren<Collider>())
                foreach (Collider c in _colliders)
                    if (c != null) Physics.IgnoreCollision(own, c);

        _body.isKinematic = false;
        _body.interpolation = RigidbodyInterpolation.Interpolate;
        _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _body.linearVelocity = velocity;
        _body.angularVelocity = Random.onUnitSphere * Random.Range(4f, 9f);

        if (_spoon != null)
        {
            _spoon.transform.SetParent(null, true);
            _spoon.isKinematic = false;
            _spoon.linearVelocity = velocity * 0.6f + _spoon.transform.up * 2f;
            _spoon.angularVelocity = Random.onUnitSphere * 20f;
            foreach (Collider c in _colliders)
                if (c != null && _spoon.TryGetComponent(out Collider sc) && sc != c) Physics.IgnoreCollision(sc, c);
            Destroy(_spoon.gameObject, 8f);
        }

        _armed = true;
        _explodeAt = Time.time + _fuseTime;
        if (!s_Live.Contains(this)) s_Live.Add(this);
    }

    // ---------- Bom đang cháy ngòi (để NPC không chạy xuyên qua vùng nổ) ----------

    private static readonly List<NPCGrenade> s_Live = new();

    /// <summary>Các quả bom đã ném, đang cháy ngòi, chưa nổ.</summary>
    public static IReadOnlyList<NPCGrenade> Live => s_Live;

    public bool IsArmed => _armed;

    private bool _hasPredictedImpact;
    private Vector3 _predictedImpact;

    /// <summary>Người ném báo trước điểm bom sẽ rơi (để NPC né vùng nổ ngay khi bom còn đang bay).</summary>
    public void SetPredictedImpact(Vector3 point)
    {
        _predictedImpact = point;
        _hasPredictedImpact = true;
    }

    /// <summary>Tâm vùng nguy hiểm: đang bay thì lấy điểm sẽ rơi, nằm yên / lăn chậm thì lấy vị trí thật.</summary>
    public Vector3 BlastCenter =>
        _hasPredictedImpact && _body != null && !_body.isKinematic && _body.linearVelocity.sqrMagnitude > 4f
            ? _predictedImpact : transform.position;

    private void OnDisable() => s_Live.Remove(this);

    /// <summary>Điểm point nằm trong vùng nổ (bán kính + margin) của một quả bom đang cháy ngòi.</summary>
    public static bool InAnyBlast(Vector3 point, float margin) => TryGetBlastAt(point, margin, out _);

    /// <summary>Quả bom đang cháy ngòi mà point nằm trong vùng nổ (bán kính + margin) của nó, gần nhất.</summary>
    public static bool TryGetBlastAt(Vector3 point, float margin, out NPCGrenade grenade)
    {
        grenade = null;
        float best = float.MaxValue;
        foreach (NPCGrenade g in s_Live)
        {
            if (g == null || !g._armed) continue;
            float d = Vector3.Distance(point, g.BlastCenter);
            if (d <= g._radius + margin && d < best) { best = d; grenade = g; }
        }
        return grenade != null;
    }

    /// <summary>Đường đi (các góc NavMeshPath) đi xuyên qua vùng nổ của một quả bom đang cháy ngòi.</summary>
    public static bool PathCrossesBlast(Vector3[] corners, float margin)
    {
        if (corners == null || corners.Length == 0) return false;
        foreach (NPCGrenade g in s_Live)
        {
            if (g == null || !g._armed) continue;
            Vector3 c = g.BlastCenter;
            float r = g._radius + margin;
            if (Vector3.Distance(corners[0], c) <= r) return true;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector3 a = corners[i - 1], b = corners[i];
                Vector3 ab = b - a;
                float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector3.Dot(c - a, ab) / ab.sqrMagnitude) : 0f;
                if (Vector3.Distance(a + ab * t, c) <= r) return true;
            }
        }
        return false;
    }

    private void Update()
    {
        if (_armed && Time.time >= _explodeAt) Explode();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!_armed || _audio == null || _bounceClips == null || _bounceClips.Length == 0) return;
        if (Time.time < _nextBounceSound || collision.relativeVelocity.magnitude < 1.2f) return;
        _nextBounceSound = Time.time + 0.15f;
        AudioClip clip = _bounceClips[Random.Range(0, _bounceClips.Length)];
        float volume = _bounceVolume * Mathf.Clamp01(collision.relativeVelocity.magnitude / 6f);
        _audio.pitch = Random.Range(0.92f, 1.08f);
        _audio.PlayOneShot(clip, volume);
    }

    public void Explode()
    {
        if (!_armed) return;
        _armed = false;
        s_Live.Remove(this);
        Vector3 center = transform.position;

        if (_explosionPrefab != null) Instantiate(_explosionPrefab, center, Quaternion.identity);

        ApplyDamage(center);
        ApplyForce(center);
        Destroy(gameObject);
    }

    private void ApplyDamage(Vector3 center)
    {
        var damaged = new HashSet<Object>();
        int count = Physics.OverlapSphereNonAlloc(center, _radius, s_Hits, _damageMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            Collider col = s_Hits[i];
            if (col == null || col.transform.IsChildOf(transform)) continue;

            NPC npc = col.GetComponentInParent<NPC>();
            Object key = npc != null ? npc : col.transform.root;
            if (damaged.Contains(key)) continue;

            Vector3 point = col.bounds.center;
            if (!InSight(center, point, col.transform.root)) continue;

            float amount = DamageAt(Vector3.Distance(center, col.bounds.ClosestPoint(center)));
            if (amount <= 0f) continue;
            damaged.Add(key);

            if (npc != null) npc.TakeDamageFrom(amount, center);
            else col.SendMessageUpwards("TakeDamage", amount, SendMessageOptions.DontRequireReceiver);
        }

        // NPC có thể không có collider: tính theo danh sách NPC, tâm thân cao 1m.
        foreach (NPC npc in NPC.Active.ToArray())
        {
            if (npc == null || npc.IsDead || damaged.Contains(npc)) continue;
            Vector3 body = npc.transform.position + Vector3.up;
            float amount = PredictDamage(center, body, npc.transform);
            if (amount <= 0f) continue;
            damaged.Add(npc);
            npc.TakeDamageFrom(amount, center);
        }

        // Target (Player) có thể không có collider trong bán kính (vd. XR Origin): tính riêng theo tâm thân.
        if (_target != null && !damaged.Contains(_target.transform.root))
        {
            Vector3 body = NPCConditionUtils.CenterOf(_target);
            float distance = Vector3.Distance(center, body);
            if (distance <= _radius && InSight(center, body, _target.transform.root))
            {
                float amount = DamageAt(distance);
                if (amount > 0f) _target.SendMessage("TakeDamage", amount, SendMessageOptions.DontRequireReceiver);
            }
        }
    }

    private void ApplyForce(Vector3 center)
    {
        int count = Physics.OverlapSphereNonAlloc(center, _radius, s_Hits, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        var pushed = new HashSet<Rigidbody>();
        for (int i = 0; i < count; i++)
        {
            Rigidbody rb = s_Hits[i] != null ? s_Hits[i].attachedRigidbody : null;
            if (rb == null || rb.isKinematic || !pushed.Add(rb)) continue;
            rb.AddExplosionForce(_force, center, _radius, 0.5f, ForceMode.Impulse);
        }
    }

    // ---------- Thông số vùng nổ (NPCGrenadeThrower dùng để lập kế hoạch ném trên prefab) ----------

    public float FuseTime => _fuseTime;
    public float Damage => _damage;
    public float Radius => _radius;
    public float InnerRadius => _innerRadius;

    /// <summary>Bán kính thân bom (để mô phỏng va chạm của đường bay).</summary>
    public float BodyRadius
    {
        get
        {
            Collider c = GetComponent<Collider>();
            return c is CapsuleCollider cap ? cap.radius : c is SphereCollider s ? s.radius : 0.035f;
        }
    }

    /// <summary>Sát thương ở khoảng cách distance tính từ tâm nổ.</summary>
    public float DamageAt(float distance)
    {
        if (distance > _radius) return 0f;
        if (distance <= _innerRadius) return _damage;
        return _damage * Mathf.Clamp01(1f - (distance - _innerRadius) / Mathf.Max(0.01f, _radius - _innerRadius));
    }

    /// <summary>
    /// Sát thương mà target sẽ nhận nếu bom nổ tại center: trong Radius và không bị vật cản chắn
    /// (đúng logic sát thương lúc nổ thật). 0 = vụ nổ không với tới target.
    /// </summary>
    public float PredictDamage(Vector3 center, GameObject target)
    {
        if (target == null) return 0f;
        return PredictDamage(center, NPCConditionUtils.CenterOf(target), target.transform.root);
    }

    /// <summary>Như trên nhưng với vị trí thân giả định (vị trí NPC biết / dự đoán, không phải vị trí thật).</summary>
    public float PredictDamage(Vector3 center, Vector3 body, Transform receiverRoot)
    {
        float amount = DamageAt(Vector3.Distance(center, body));
        return amount > 0f && InSight(center, body, receiverRoot) ? amount : 0f;
    }

    /// <summary>Không có vật cản giữa tâm nổ và điểm (bỏ qua collider của chính object nhận sát thương).</summary>
    /// <remarks>
    /// Chỉ tường / vật cố định mới chắn. Người (Player, NPC khác) và vật nhỏ có vật lý không chắn được vụ nổ,
    /// nên đứng sau lưng người khác vẫn trúng bom.
    /// </remarks>
    public bool InSight(Vector3 center, Vector3 point, Transform receiverRoot)
    {
        Vector3 from = center + Vector3.up * 0.15f;
        Vector3 delta = point - from;
        float length = delta.magnitude;
        if (length < 0.001f) return true;

        int count = Physics.RaycastNonAlloc(from, delta / length, s_Rays, length, _occlusionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = s_Rays[i];
            if (receiverRoot != null && hit.transform.IsChildOf(receiverRoot)) continue;
            if (hit.rigidbody != null && !hit.rigidbody.isKinematic) continue;
            if (IsCharacter(hit.transform)) continue;
            return false;
        }
        return true;
    }

    private static bool IsCharacter(Transform t) =>
        t.GetComponentInParent<NPC>() != null || t.root.CompareTag("Player") || t.GetComponentInParent<CharacterController>() != null;

    private void SetColliders(bool enabled)
    {
        foreach (Collider c in _colliders)
            if (c != null) c.enabled = enabled;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0f);
        Gizmos.DrawWireSphere(transform.position, _innerRadius);
        Gizmos.DrawWireSphere(transform.position, _radius);
    }
}
