using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Súng trường của NPC: giữ số đạn, phát hiệu ứng / âm thanh khi bắn và quản lý model băng đạn khi thay đạn.
/// Gắn trên prefab súng (NPC_Rifle). NPCWeaponHolder trên NPC lo gắn súng vào tay và đồng bộ với Animator.
///
/// Băng dự phòng: mỗi băng trong túi giữ số đạn riêng (reload chiến thuật cất băng cũ còn đạn vào túi).
/// Model băng đạn trên hông trái (HipMagazine) hiện khi túi còn băng, ẩn khi tay rút băng cuối cùng ra.
///
/// Mọi thứ của một phát bắn (anim Fire, chớp lửa đầu nòng, tiếng súng, vỏ đạn, vết đạn) được gọi trong CÙNG một frame
/// từ Fire() nên âm thanh luôn khớp với phát bắn.
/// </summary>
[DisallowMultipleComponent]
public class NPCWeapon : MonoBehaviour
{
    [Header("Ammo")]
    [SerializeField, Min(1)] private int _magazineSize = 30;
    [Tooltip("Số băng dự phòng (đầy) trong túi trên hông. -1 = vô hạn.")]
    [SerializeField] private int _spareMagazines = 1;
    [Tooltip("Model băng dự phòng gắn trên hông trái NPC. Hiện khi túi còn băng.")]
    [SerializeField] private GameObject _hipMagazine;

    [Header("Points")]
    [Tooltip("Đầu nòng, trục Z hướng ra phía trước.")]
    [SerializeField] private Transform _muzzle;
    [Tooltip("Cửa nhả vỏ đạn, trục Z là hướng văng vỏ.")]
    [SerializeField] private Transform _shellEject;
    [Tooltip("Vị trí/hướng tay trái cầm súng (IK).")]
    [SerializeField] private Transform _leftHandGrip;
    [Tooltip("Ổ băng đạn trên súng. Băng đạn (Magazine) là con của transform này.")]
    [SerializeField] private Transform _magazineSocket;
    [SerializeField] private Transform _magazine;

    [Header("Effects")]
    [SerializeField] private GameObject _muzzleFlashPrefab;
    [SerializeField] private float _muzzleFlashLifeTime = 0.08f;
    [SerializeField] private Rigidbody _shellPrefab;
    [SerializeField] private Vector2 _shellForce = new(1.2f, 1.8f);
    [SerializeField] private float _shellLifeTime = 4f;
    [Tooltip("Vết đạn trên tường / vật cản khi bắn trượt.")]
    [SerializeField] private GameObject _hitEffectPrefab;
    [SerializeField] private float _hitEffectLifeTime = 10f;
    [Tooltip("Băng rỗng rơi xuống đất khi thay đạn (có Rigidbody).")]
    [SerializeField] private Rigidbody _droppedMagazinePrefab;
    [SerializeField] private float _droppedMagazineLifeTime = 15f;

    [Header("Ballistics")]
    [SerializeField] private float _range = 200f;
    [Tooltip("Độ lệch (độ) của phát trượt so với tâm mục tiêu.")]
    [SerializeField] private Vector2 _missAngle = new(1.5f, 4f);
    [SerializeField] private LayerMask _hitMask = Physics.DefaultRaycastLayers;

    [Header("Audio")]
    [SerializeField] private AudioSource _audio;
    [Tooltip("AudioSource riêng cho tiếng thay đạn (phát từng đoạn của clip reload, khớp với tay).")]
    [SerializeField] private AudioSource _reloadAudio;
    [SerializeField] private AudioClip[] _shotClips;
    [SerializeField] private AudioClip _lastShotClip;
    [SerializeField] private AudioClip _reloadEmptyClip;
    [SerializeField] private AudioClip _reloadTacticalClip;
    [SerializeField] private AudioClip[] _shellDropClips;
    [SerializeField, Range(0f, 0.3f)] private float _pitchJitter = 0.06f;
    [SerializeField, Range(0f, 1f)] private float _shellVolume = 0.35f;

    private int _ammo;
    private readonly List<int> _pouch = new(); // số đạn của từng băng trong túi
    private int _magInHandAmmo = -1;          // băng mới đang cầm trên tay (chưa lắp), -1 = không có
    private Transform _owner;
    private Transform _magazineParentAtStart;
    private Vector3 _magLocalPos;
    private Quaternion _magLocalRot;
    private readonly RaycastHit[] _hits = new RaycastHit[16];

    public int Ammo => _ammo;
    public int MagazineSize => _magazineSize;
    public bool IsEmpty => _ammo <= 0;
    public bool InfiniteMagazines => _spareMagazines < 0;
    public bool HasSpareMagazine => InfiniteMagazines || _pouch.Count > 0;

    /// <summary>Số đạn của băng đầy nhất trong túi.</summary>
    public int BestSpareAmmo
    {
        get
        {
            if (InfiniteMagazines) return _magazineSize;
            int best = 0;
            foreach (int ammo in _pouch) best = Mathf.Max(best, ammo);
            return best;
        }
    }

    /// <summary>Thay đạn có ích: băng trong túi nhiều đạn hơn băng đang lắp.</summary>
    public bool CanReload => _ammo < _magazineSize && HasSpareMagazine && BestSpareAmmo > _ammo;

    /// <summary>Hết sạch đạn: băng trên súng rỗng và túi không còn băng nào.</summary>
    public bool IsOutOfAmmo => _ammo <= 0 && !HasSpareMagazine && _magInHandAmmo < 0;
    public Transform Muzzle => _muzzle;
    public Transform LeftHandGrip => _leftHandGrip;
    public Transform Magazine => _magazine;
    public AudioClip ReloadEmptyClip => _reloadEmptyClip;
    public AudioClip ReloadTacticalClip => _reloadTacticalClip;

    private void Awake()
    {
        _ammo = _magazineSize;
        for (int i = 0; i < _spareMagazines; i++) _pouch.Add(_magazineSize);
        UpdateHipMagazine();
        if (_audio == null) _audio = GetComponent<AudioSource>();
        if (_magazine != null)
        {
            _magazineParentAtStart = _magazineSocket != null ? _magazineSocket : _magazine.parent;
            _magLocalPos = _magazine.localPosition;
            _magLocalRot = _magazine.localRotation;
        }
    }

    /// <summary>Gốc NPC đang cầm súng, dùng để raycast bỏ qua collider của chính NPC.</summary>
    public void SetOwner(Transform owner) => _owner = owner;

    /// <summary>
    /// Bắn 1 phát về phía aimPoint. hit = true: bắn thẳng vào aimPoint, false: lệch đi một góc để trượt.
    /// Trả về false nếu hết đạn (chỉ phát tiếng "tạch").
    /// </summary>
    public bool Fire(Vector3 aimPoint, bool hit)
    {
        if (_ammo <= 0) return false;
        _ammo--;

        Vector3 origin = _muzzle != null ? _muzzle.position : transform.position;
        Vector3 dir = aimPoint - origin;
        if (dir.sqrMagnitude < 0.0001f) dir = _muzzle != null ? _muzzle.forward : transform.forward;
        dir.Normalize();
        if (!hit) dir = Deviate(dir, Random.Range(_missAngle.x, _missAngle.y));

        PlayShotSound();
        SpawnMuzzleFlash();
        EjectShell();
        if (!hit) SpawnImpact(origin, dir);
        return true;
    }

    /// <summary>
    /// Phát đoạn [start, end] (giây) của clip reload. Anim reload dài hơn file âm thanh nên mỗi động tác
    /// (rút băng, lắp băng, gạt khóa nòng) phát đúng đoạn tiếng của nó vào đúng lúc tay làm động tác đó.
    /// </summary>
    public void PlayReloadSegment(bool tactical, float start, float end)
    {
        AudioSource src = _reloadAudio != null ? _reloadAudio : _audio;
        AudioClip clip = tactical && _reloadTacticalClip != null ? _reloadTacticalClip : _reloadEmptyClip;
        if (src == null || clip == null) return;

        start = Mathf.Clamp(start, 0f, clip.length);
        end = Mathf.Clamp(end, start, clip.length);
        if (end - start < 0.01f) return;

        src.pitch = 1f;
        src.clip = clip;
        src.time = start;
        src.Play();
        src.SetScheduledEndTime(AudioSettings.dspTime + (end - start));
    }

    public void StopReloadSound()
    {
        if (_reloadAudio != null) _reloadAudio.Stop();
    }

    // ---------- Băng đạn ----------

    /// <summary>Tay trái rút băng khỏi súng (băng bám theo tay).</summary>
    public void TakeMagazineToHand(Transform hand)
    {
        if (_magazine == null || hand == null) return;
        _magazine.SetParent(hand, true);
    }

    /// <summary>Thả băng đang cầm xuống đất (thay bằng bản copy có vật lý) và ẩn model băng trên tay.</summary>
    public void DropMagazine()
    {
        _ammo = 0; // đạn còn lại (nếu có) rơi theo băng
        if (_magazine == null) return;
        if (_droppedMagazinePrefab != null)
        {
            Rigidbody body = Instantiate(_droppedMagazinePrefab, _magazine.position, _magazine.rotation);
            IgnoreOwnerCollision(body);
            body.linearVelocity = Vector3.down * 0.5f;
            body.angularVelocity = Random.insideUnitSphere * 2f;
            Destroy(body.gameObject, _droppedMagazineLifeTime);
        }
        _magazine.gameObject.SetActive(false);
    }

    /// <summary>Reload chiến thuật: cất băng cũ (còn đạn) vào túi trên hông, ẩn model trên tay.</summary>
    public void StashMagazine()
    {
        if (!InfiniteMagazines && _ammo > 0) _pouch.Add(_ammo);
        _ammo = 0;
        UpdateHipMagazine();
        if (_magazine != null) _magazine.gameObject.SetActive(false);
    }

    /// <summary>Rút băng nhiều đạn nhất khỏi túi trên hông (túi hết băng thì ẩn model trên hông).</summary>
    public void TakeSpareMagazine()
    {
        if (_magInHandAmmo >= 0) return;
        if (InfiniteMagazines) _magInHandAmmo = _magazineSize;
        else if (_pouch.Count > 0)
        {
            int best = 0;
            for (int i = 1; i < _pouch.Count; i++)
                if (_pouch[i] > _pouch[best]) best = i;
            _magInHandAmmo = _pouch[best];
            _pouch.RemoveAt(best);
        }
        UpdateHipMagazine();
    }

    /// <summary>Lấy băng mới từ túi: model trên hông biến mất, hiện model băng trên tay trái.</summary>
    public void SpawnMagazineInHand(Transform hand, Vector3 localPos, Quaternion localRot)
    {
        TakeSpareMagazine();
        if (_magazine == null || hand == null) return;
        _magazine.SetParent(hand, false);
        _magazine.localPosition = localPos;
        _magazine.localRotation = localRot;
        _magazine.gameObject.SetActive(true);
    }

    /// <summary>Lắp băng đang cầm (lấy từ túi) vào súng.</summary>
    public void InsertMagazine()
    {
        TakeSpareMagazine(); // reload bị rút ngắn trước bước lấy băng mới
        ResetMagazine();
        if (_magInHandAmmo >= 0) _ammo = _magInHandAmmo;
        _magInHandAmmo = -1;
    }

    /// <summary>Test: bỏ hết đạn trên súng và băng dự phòng (chuột phải component > Debug: Empty All Ammo).</summary>
    [ContextMenu("Debug: Empty All Ammo")]
    public void EmptyAllAmmo()
    {
        _ammo = 0;
        _pouch.Clear();
        _magInHandAmmo = -1;
        if (_spareMagazines < 0) _spareMagazines = 0;
        UpdateHipMagazine();
    }

    private void UpdateHipMagazine()
    {
        if (_hipMagazine != null) _hipMagazine.SetActive(HasSpareMagazine);
    }

    /// <summary>Trả model băng về đúng chỗ trên súng (dùng khi lắp băng hoặc khi reload bị ngắt).</summary>
    public void ResetMagazine()
    {
        if (_magazine == null) return;
        _magazine.SetParent(_magazineParentAtStart, false);
        _magazine.localPosition = _magLocalPos;
        _magazine.localRotation = _magLocalRot;
        _magazine.gameObject.SetActive(true);
    }

    /// <summary>Vị trí/hướng của băng khi nằm trong súng, tính theo không gian của transform hand.</summary>
    public void GetSocketPoseIn(Transform hand, out Vector3 localPos, out Quaternion localRot)
    {
        Transform parent = _magazineParentAtStart != null ? _magazineParentAtStart : transform;
        Vector3 worldPos = parent.TransformPoint(_magLocalPos);
        Quaternion worldRot = parent.rotation * _magLocalRot;
        localPos = hand.InverseTransformPoint(worldPos);
        localRot = Quaternion.Inverse(hand.rotation) * worldRot;
    }

    // ---------- Hiệu ứng ----------

    private void PlayShotSound()
    {
        if (_audio == null) return;
        AudioClip clip = _ammo == 0 && _lastShotClip != null ? _lastShotClip
            : _shotClips != null && _shotClips.Length > 0 ? _shotClips[Random.Range(0, _shotClips.Length)] : null;
        if (clip == null) return;
        _audio.pitch = 1f + Random.Range(-_pitchJitter, _pitchJitter);
        _audio.PlayOneShot(clip);
    }

    private void SpawnMuzzleFlash()
    {
        if (_muzzleFlashPrefab == null || _muzzle == null) return;
        GameObject flash = Instantiate(_muzzleFlashPrefab, _muzzle.position, _muzzle.rotation * Quaternion.AngleAxis(Random.value * 360f, Vector3.forward), _muzzle);

        // Khói đầu nòng sống lâu hơn chớp lửa: tách ra khỏi súng rồi tự huỷ khi hết hạt.
        foreach (ParticleSystem ps in flash.GetComponentsInChildren<ParticleSystem>())
        {
            ps.transform.SetParent(null, true);
            ParticleSystem.MainModule main = ps.main;
            Destroy(ps.gameObject, main.duration + main.startLifetime.constantMax);
        }
        Destroy(flash, _muzzleFlashLifeTime);
    }

    /// <summary>Vỏ đạn / băng rơi không va vào chính NPC (capsule, hitbox).</summary>
    private void IgnoreOwnerCollision(Component spawned)
    {
        if (_owner == null) return;
        Collider[] mine = spawned.GetComponentsInChildren<Collider>();
        foreach (Collider own in _owner.GetComponentsInChildren<Collider>())
            foreach (Collider c in mine)
                Physics.IgnoreCollision(own, c);
    }

    private void EjectShell()
    {
        if (_shellPrefab == null || _shellEject == null) return;
        Rigidbody shell = Instantiate(_shellPrefab, _shellEject.position, _shellEject.rotation);
        IgnoreOwnerCollision(shell);
        Vector3 dir = _shellEject.rotation * Quaternion.Euler(Random.Range(-10f, 10f), Random.Range(-10f, 10f), 0f) * Vector3.forward;
        shell.linearVelocity = dir * Random.Range(_shellForce.x, _shellForce.y);
        shell.angularVelocity = _shellEject.up * Random.Range(10f, 25f);
        Destroy(shell.gameObject, _shellLifeTime);

        if (_shellDropClips != null && _shellDropClips.Length > 0)
            StartCoroutine(PlayShellDrop(shell.transform, Random.Range(0.35f, 0.6f)));
    }

    private System.Collections.IEnumerator PlayShellDrop(Transform shell, float delay)
    {
        yield return new WaitForSeconds(delay);
        Vector3 pos = shell != null ? shell.position : transform.position;
        AudioClip clip = _shellDropClips[Random.Range(0, _shellDropClips.Length)];
        if (clip != null) AudioSource.PlayClipAtPoint(clip, pos, _shellVolume);
    }

    private void SpawnImpact(Vector3 origin, Vector3 dir)
    {
        if (_hitEffectPrefab == null) return;
        int count = Physics.RaycastNonAlloc(origin, dir, _hits, _range, _hitMask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        RaycastHit nearest = default;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = _hits[i];
            if (_owner != null && h.transform.IsChildOf(_owner)) continue;
            if (h.rigidbody != null && !h.rigidbody.isKinematic) continue; // vỏ đạn, băng rơi...
            if (h.distance < best) { best = h.distance; nearest = h; found = true; }
        }
        if (!found) return;

        // HitEffect của Knife: decal nằm theo trục Z của prefab, nên quay Z ngược hướng pháp tuyến.
        GameObject fx = Instantiate(_hitEffectPrefab, nearest.point + nearest.normal * 0.005f,
            Quaternion.LookRotation(-nearest.normal) * Quaternion.AngleAxis(Random.value * 360f, Vector3.forward));
        if (!nearest.collider.gameObject.isStatic) fx.transform.SetParent(nearest.collider.transform, true);
        Destroy(fx, _hitEffectLifeTime);
    }

    private static Vector3 Deviate(Vector3 dir, float angle)
    {
        Vector3 axis = Vector3.Cross(dir, Random.onUnitSphere);
        if (axis.sqrMagnitude < 0.0001f) axis = Vector3.Cross(dir, Vector3.up);
        return Quaternion.AngleAxis(angle, axis.normalized) * dir;
    }

    private void OnDrawGizmosSelected()
    {
        if (_muzzle != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(_muzzle.position, _muzzle.position + _muzzle.forward * 0.5f);
        }
        if (_leftHandGrip != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(_leftHandGrip.position, 0.03f);
        }
        if (_shellEject != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(_shellEject.position, _shellEject.position + _shellEject.forward * 0.15f);
        }
    }
}
