using UnityEngine;

/// <summary>
/// Cầm súng cho NPC (đặt cùng GameObject với Animator).
/// - Súng là con của xương tay phải (đã căn sẵn trong prefab) nên luôn đi theo tay phải.
/// - Tay trái dùng IK 2 xương (chạy trong LateUpdate, sau Animator) bám vào NPCWeapon.LeftHandGrip,
///   nên ở mọi anim (đứng, đi, chạy, ngồi, quay, bắn) hai tay luôn khớp với súng.
///   Khi thay đạn, IK tay trái tắt dần để anim reload điều khiển tay trái, gần cuối anim thì bật lại.
/// - Thay đạn: bật trigger Reload / ReloadTactical trên layer UpperBody, phát âm thanh reload và
///   di chuyển model băng đạn (rút ra -> thả / cất -> lấy băng mới -> lắp vào) theo tiến độ của anim.
/// </summary>
[RequireComponent(typeof(Animator))]
public class NPCWeaponHolder : MonoBehaviour
{
    private static readonly int FireHash = Animator.StringToHash("Fire");
    private static readonly int ReloadHash = Animator.StringToHash("Reload");
    private static readonly int ReloadTacticalHash = Animator.StringToHash("ReloadTactical");
    private static readonly int ReloadStateHash = Animator.StringToHash("Reload");
    private static readonly int ReloadTacticalStateHash = Animator.StringToHash("ReloadTactical");
    private static readonly int ThrowStateHash = Animator.StringToHash("Throw");
    private static readonly int DrawGrenadeStateHash = Animator.StringToHash("DrawGrenade");

    /// <summary>Một đoạn của clip âm thanh reload, phát khi anim reload tới normalizedTime At.</summary>
    [System.Serializable]
    public struct SoundCue
    {
        [Range(0f, 1f)] public float At;
        [Tooltip("Giây bắt đầu trong clip âm thanh.")] public float ClipStart;
        [Tooltip("Giây kết thúc trong clip âm thanh.")] public float ClipEnd;

        public SoundCue(float at, float clipStart, float clipEnd)
        {
            At = at;
            ClipStart = clipStart;
            ClipEnd = clipEnd;
        }
    }

    [System.Serializable]
    public struct ReloadTiming
    {
        [Tooltip("Độ dài clip (giây), dùng khi Animator chưa vào state.")]
        public float Duration;
        [Tooltip("normalizedTime tay trái rút băng khỏi súng.")]
        [Range(0f, 1f)] public float MagOut;
        [Tooltip("normalizedTime thả băng cũ xuống đất (reload rỗng) / cất băng cũ vào túi (reload chiến thuật).")]
        [Range(0f, 1f)] public float MagDrop;
        [Tooltip("normalizedTime băng mới xuất hiện trên tay trái (lấy từ túi).")]
        [Range(0f, 1f)] public float MagNew;
        [Tooltip("normalizedTime băng mới được lắp vào súng (nạp đầy đạn).")]
        [Range(0f, 1f)] public float MagIn;
        [Tooltip("normalizedTime bắt đầu bật lại IK tay trái.")]
        [Range(0f, 1f)] public float IkBack;
        [Tooltip("Các đoạn âm thanh reload, sắp theo At tăng dần.")]
        public SoundCue[] Sounds;
    }

    [SerializeField] private NPCWeapon _weapon;
    [SerializeField] private int _upperBodyLayer = 1;

    [Header("Left hand IK")]
    [SerializeField] private bool _leftHandIK = true;
    [SerializeField, Range(0f, 1f)] private float _ikPositionWeight = 1f;
    [SerializeField, Range(0f, 1f)] private float _ikRotationWeight = 1f;
    [Tooltip("Thời gian (giây) bật/tắt IK tay trái.")]
    [SerializeField] private float _ikBlendTime = 0.2f;

    [Header("Reload")]
    [Tooltip("Đang nấp mà số đạn còn dưới tỉ lệ này thì thay đạn sớm (reload chiến thuật).")]
    [SerializeField, Range(0f, 1f)] private float _tacticalReloadRatio = 0.4f;
    // Mốc đo từ quỹ đạo tay trái của clip reloadM4A1empty / reloadM4A1full so với ổ băng đạn trên súng.
    // Đoạn âm thanh lấy từ "reload empty.wav" / "reload basic.wav" (Knife.PlayerController):
    // tiếng rút băng ~0.62s, lắp băng ~1.85s, gạt khóa nòng ~2.27s (reload empty) / vỗ băng ~2.4s (reload basic).
    [SerializeField] private ReloadTiming _emptyReload = new()
    {
        Duration = 4.083f, MagOut = 0.2f, MagDrop = 0.27f, MagNew = 0.4f, MagIn = 0.72f, IkBack = 0.92f,
        Sounds = new[] { new SoundCue(0.118f, 0.28f, 1.05f), new SoundCue(0.61f, 1.4f, 2.05f), new SoundCue(0.79f, 2.1f, 2.7f) },
    };
    [SerializeField] private ReloadTiming _tacticalReload = new()
    {
        Duration = 3.542f, MagOut = 0.25f, MagDrop = 0.44f, MagNew = 0.5f, MagIn = 0.81f, IkBack = 0.91f,
        Sounds = new[] { new SoundCue(0.155f, 0.28f, 1.15f), new SoundCue(0.683f, 1.4f, 2.15f), new SoundCue(0.82f, 2.25f, 2.8f) },
    };

    private Animator _animator;
    private NPC _npc;
    private NPCGrenadeThrower _thrower;
    private Transform _upperArm, _foreArm, _hand;
    private float _ikWeight = 1f;

    private bool _reloading;
    private bool _tactical;
    private float _reloadStart;
    private int _step;
    private int _soundIndex;
    private bool _enteredState;
    private Vector3 _magInHandPos;
    private Quaternion _magInHandRot;
    private bool _hasMagInHandPose;

    public NPCWeapon Weapon => _weapon;
    public bool HasWeapon => _weapon != null;
    public bool IsReloading => _reloading;
    /// <summary>Đang rút / ném bom: không bắn, không thay đạn, tay trái không bám súng.</summary>
    public bool IsThrowing => _thrower != null && _thrower.IsBusy;

    /// <summary>Hết sạch đạn: băng trên súng rỗng và túi trên hông hết băng.</summary>
    public bool IsOutOfAmmo => _weapon == null || _weapon.IsOutOfAmmo;

    public bool NeedsReload =>_weapon != null && _weapon.IsEmpty && _weapon.CanReload;

    /// <summary>Băng còn ít đạn, nên thay sớm khi đang an toàn (ngồi nấp).</summary>
    public bool WantsTacticalReload => _weapon != null && !_reloading && !_weapon.IsEmpty && _weapon.CanReload
                                       && _weapon.Ammo < _weapon.MagazineSize * _tacticalReloadRatio;

    /// <summary>Còn đạn và không đang thay đạn.</summary>
    public bool CanFire => _weapon != null && !_reloading && !IsThrowing &&!_weapon.IsEmpty && (_npc == null || !_npc.IsDead);

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _npc = GetComponent<NPC>();
        _thrower = GetComponent<NPCGrenadeThrower>();
        if (_weapon == null) _weapon = GetComponentInChildren<NPCWeapon>(true);
        if (_animator.isHuman)
        {
            _upperArm = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _foreArm = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _hand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
        }
        if (_weapon != null) _weapon.SetOwner(transform);
    }

    /// <summary>
    /// Bắn 1 phát vào target. Anim Fire, chớp lửa, tiếng súng, vỏ đạn chạy cùng một frame.
    /// Trả false nếu đang thay đạn / hết đạn.
    /// </summary>
    public bool Fire(GameObject target, bool hit)
    {
        if (!CanFire || target == null) return false;
        if (!_weapon.Fire(NPCConditionUtils.CenterOf(target), hit)) return false;
        _animator.SetTrigger(FireHash);
        return true;
    }

    /// <summary>
    /// Thay đạn. tactical = true: băng còn đạn, cất băng cũ vào túi (anim reloadM4A1full).
    /// false: băng rỗng, thả băng xuống đất (anim reloadM4A1empty).
    /// </summary>
    public bool Reload(bool tactical = false)
    {
        if (_weapon == null || _reloading || IsThrowing || !_weapon.CanReload ||(_npc != null && _npc.IsDead)) return false;
        tactical &= !_weapon.IsEmpty;

        _reloading = true;
        _tactical = tactical;
        _reloadStart = Time.time;
        _step = 0;
        _soundIndex = 0;
        _enteredState = false;
        _hasMagInHandPose = false;

        _animator.ResetTrigger(FireHash);
        _animator.SetTrigger(tactical ? ReloadTacticalHash : ReloadHash);
        return true;
    }

    /// <summary>Sắp đầu hàng: hủy thay đạn, bỏ lệnh bắn đang chờ.</summary>
    public void PrepareSurrender()
    {
        if (_reloading) CancelReload();
        _animator.ResetTrigger(FireHash);
        _ikWeight = 0f;
    }

    /// <summary>
    /// Thả súng xuống đất (đầu hàng): tách khỏi tay, thêm Rigidbody + collider để rơi.
    /// Sau đó NPC không còn súng (HasWeapon = false, IsOutOfAmmo = true).
    /// </summary>
    public void DropWeapon()
    {
        if (_weapon == null) return;
        if (_reloading) CancelReload();

        NPCWeapon weapon = _weapon;
        _weapon = null;
        weapon.StopReloadSound();
        weapon.transform.SetParent(null, true);

        if (weapon.GetComponentInChildren<Collider>() == null)
        {
            Bounds bounds = new(weapon.transform.position, Vector3.zero);
            foreach (Renderer r in weapon.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            BoxCollider box = weapon.gameObject.AddComponent<BoxCollider>();
            box.center = weapon.transform.InverseTransformPoint(bounds.center);
            Vector3 scale = weapon.transform.lossyScale;
            box.size = new Vector3(
                bounds.size.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                bounds.size.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                bounds.size.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
        }
        // Không va vào thân NPC đang quỳ.
        foreach (Collider own in GetComponentsInChildren<Collider>())
            foreach (Collider c in weapon.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(own, c);

        if (!weapon.TryGetComponent(out Rigidbody body)) body = weapon.gameObject.AddComponent<Rigidbody>();
        body.isKinematic = false;
        body.mass = 3.5f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.AddForce(transform.forward * 0.6f, ForceMode.VelocityChange);
        weapon.enabled = false;
    }

    private void Update()
    {
        bool dead = _npc != null && _npc.IsDead;
        if (dead && _reloading) CancelReload();
        if (_reloading) TickReload();

        float targetIk = !dead && _leftHandIK && WantsLeftHandIK() ? 1f : 0f;
        _ikWeight = Mathf.MoveTowards(_ikWeight, targetIk, Time.deltaTime / Mathf.Max(0.01f, _ikBlendTime));
    }

    // Chạy sau Animator: tay phải (và súng) đã ở tư thế của frame này.
    private void LateUpdate()
    {
        if (_ikWeight <= 0f || _weapon == null || _weapon.LeftHandGrip == null || _hand == null) return;
        Transform grip = _weapon.LeftHandGrip;
        Vector3 targetPos = Vector3.Lerp(_hand.position, grip.position, _ikWeight * _ikPositionWeight);
        Quaternion targetRot = Quaternion.Slerp(_hand.rotation, grip.rotation, _ikWeight * _ikRotationWeight);
        SolveTwoBone(_upperArm, _foreArm, _hand, targetPos);
        _hand.rotation = targetRot;
    }

    private bool WantsLeftHandIK()
    {
        if (_weapon == null || _weapon.LeftHandGrip == null) return false;
        if (_reloading)
        {
            ReloadTiming timing = _tactical ? _tacticalReload : _emptyReload;
            return ReloadProgress() >= timing.IkBack;
        }
        return !IsThrowing && !IsInUpperState(ThrowStateHash) && !IsInUpperState(DrawGrenadeStateHash);
    }

    private void TickReload()
    {
        ReloadTiming timing = _tactical ? _tacticalReload : _emptyReload;
        float t = ReloadProgress();

        // Âm thanh: mỗi đoạn phát đúng lúc tay làm động tác tương ứng.
        while (timing.Sounds != null && _soundIndex < timing.Sounds.Length && t >= timing.Sounds[_soundIndex].At)
        {
            SoundCue cue = timing.Sounds[_soundIndex++];
            _weapon.PlayReloadSegment(_tactical, cue.ClipStart, cue.ClipEnd);
        }

        if (_step == 0 && t >= timing.MagOut)
        {
            _weapon.TakeMagazineToHand(_hand);
            if (_weapon.Magazine != null && _hand != null)
            {
                // Ghi lại tư thế băng trên tay để băng mới được cầm giống hệt.
                _magInHandPos = _weapon.Magazine.localPosition;
                _magInHandRot = _weapon.Magazine.localRotation;
                _hasMagInHandPose = true;
            }
            _step = 1;
        }
        if (_step == 1 && t >= timing.MagDrop)
        {
            if (_tactical) _weapon.StashMagazine(); // cất băng cũ (còn đạn) vào túi trên hông
            else _weapon.DropMagazine(); // băng rỗng rơi xuống đất
            _step = 2;
        }
        if (_step == 2 && t >= timing.MagNew)
        {
            if (!_hasMagInHandPose && _hand != null)
                _weapon.GetSocketPoseIn(_hand, out _magInHandPos, out _magInHandRot);
            _weapon.SpawnMagazineInHand(_hand, _magInHandPos, _magInHandRot);
            _step = 3;
        }
        if (_step == 3 && t >= timing.MagIn)
        {
            _weapon.InsertMagazine();
            _step = 4;
        }

        if (t >= 1f || Time.time - _reloadStart > timing.Duration + 1f)
            FinishReload();
    }

    private void FinishReload()
    {
        if (_step < 4) _weapon.InsertMagazine();
        _reloading = false;
    }

    private void CancelReload()
    {
        _reloading = false;
        _weapon.StopReloadSound();
        if (_step >= 1 && _step < 4) _weapon.ResetMagazine();
        _animator.ResetTrigger(ReloadHash);
        _animator.ResetTrigger(ReloadTacticalHash);
    }

    /// <summary>Tiến độ 0-1 của anim reload: đọc từ Animator khi đã vào state, không thì tính theo thời gian.</summary>
    private float ReloadProgress()
    {
        ReloadTiming timing = _tactical ? _tacticalReload : _emptyReload;
        int stateHash = _tactical ? ReloadTacticalStateHash : ReloadStateHash;

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
            if (_enteredState) return 1f; // đã rời state reload
        }
        return Mathf.Clamp01((Time.time - _reloadStart) / Mathf.Max(0.1f, timing.Duration));
    }

    private bool IsInUpperState(int hash)
    {
        if (_upperBodyLayer >= _animator.layerCount) return false;
        if (_animator.IsInTransition(_upperBodyLayer) && _animator.GetNextAnimatorStateInfo(_upperBodyLayer).shortNameHash == hash) return true;
        return _animator.GetCurrentAnimatorStateInfo(_upperBodyLayer).shortNameHash == hash;
    }

    /// <summary>IK giải tích 2 xương (vai - khuỷu - cổ tay), giữ nguyên mặt phẳng khuỷu tay của anim.</summary>
    private static void SolveTwoBone(Transform a, Transform b, Transform c, Vector3 target)
    {
        if (a == null || b == null || c == null) return;
        Vector3 pa = a.position, pb = b.position, pc = c.position;
        float lab = Vector3.Distance(pa, pb);
        float lcb = Vector3.Distance(pb, pc);
        float lat = Mathf.Clamp(Vector3.Distance(pa, target), 0.001f, lab + lcb - 0.001f);

        float acAb0 = Angle(pc - pa, pb - pa);
        float baBc0 = Angle(pa - pb, pc - pb);
        float acAt0 = Angle(pc - pa, target - pa);

        float acAb1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
        float baBc1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));

        Vector3 axis0 = Vector3.Cross(pc - pa, pb - pa);
        if (axis0.sqrMagnitude < 1e-8f) axis0 = Vector3.Cross(pc - pa, a.up);
        axis0.Normalize();
        Vector3 axis1 = Vector3.Cross(pc - pa, target - pa);
        bool swing = axis1.sqrMagnitude > 1e-8f;
        if (swing) axis1.Normalize();

        Quaternion aRot = a.rotation, bRot = b.rotation;
        Quaternion bend = Quaternion.AngleAxis((acAb1 - acAb0) * Mathf.Rad2Deg, axis0);
        Quaternion turn = swing ? Quaternion.AngleAxis(acAt0 * Mathf.Rad2Deg, axis1) : Quaternion.identity;
        a.rotation = turn * bend * aRot;
        // Khuỷu: xoay quanh cùng trục bend, tính từ rotation gốc rồi áp lại cho xương con của a đã xoay.
        Quaternion bLocalBend = Quaternion.AngleAxis((baBc1 - baBc0) * Mathf.Rad2Deg, Quaternion.Inverse(bRot) * axis0);
        b.localRotation = b.localRotation * bLocalBend;
    }

    private static float Angle(Vector3 x, Vector3 y)
    {
        float d = x.magnitude * y.magnitude;
        return d < 1e-8f ? 0f : Mathf.Acos(Mathf.Clamp(Vector3.Dot(x, y) / d, -1f, 1f));
    }
}
