using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// Máu của người chơi (thiết kế lại sát thương theo SWAT 4). Tự gắn lên object tag "Player" (XR Origin) khi NPC đầu
/// tiên khởi động (NPCWorldListener.Ensure), hoặc gắn tay để chỉnh thông số.
///
/// Nhận sát thương:
/// - Đạn NPC / lựu đạn NPC: Damage.Apply -> ApplyDamage(DamageInfo) (vùng trúng + giáp thân / mũ).
/// - SendMessage("TakeDamage", float) (hệ thống cũ): coi như đạn trúng thân.
/// - IDamageable (lựu đạn của chính người chơi nổ gần, VRGrenade): coi như nổ.
/// Không hồi máu (SWAT 4). Trúng đạn: nháy đỏ màn hình + rung 2 tay cầm. Máu thấp: viền đỏ. Chết: màn hình tối dần,
/// chữ "BẠN ĐÃ BỊ HẠ", OnDied / Died; NPC thôi nhắm người chơi. Tùy chọn tải lại màn sau ReloadDelay giây.
/// </summary>
[DisallowMultipleComponent]
public class SwatPlayerHealth : MonoBehaviour, IDamageable
{
    [Header("Máu / giáp")]
    [SerializeField] private float _maxHealth = 100f;
    [Tooltip("Áo giáp: chặn ngần này phần sát thương đạn trúng thân (nổ: một nửa).")]
    [SerializeField, Range(0f, 1f)] private float _bodyArmor = 0.4f;
    [Tooltip("Mũ: chặn ngần này phần sát thương đạn trúng đầu.")]
    [SerializeField, Range(0f, 1f)] private float _helmet = 0.25f;
    [Tooltip("Nhân sát thương nhận vào (độ khó). 0 = bất tử.")]
    [SerializeField, Min(0f)] private float _damageScale = 1f;
    [SerializeField] private bool _invulnerable;

    [Header("Phản hồi")]
    [SerializeField] private Color _hitColor = new(0.75f, 0f, 0f, 0.45f);
    [SerializeField] private float _hitFlashTime = 0.35f;
    [Tooltip("Dưới tỉ lệ máu này thì viền đỏ liên tục.")]
    [SerializeField, Range(0f, 1f)] private float _lowHealthRatio = 0.35f;
    [SerializeField, Range(0f, 1f)] private float _hapticAmplitude = 0.7f;
    [SerializeField] private AudioClip[] _hurtClips;
    [SerializeField] private AudioClip _deathClip;

    [Header("Chết")]
    [SerializeField] private string _deathText = "BẠN ĐÃ BỊ HẠ";
    [Tooltip("Chết xong tải lại màn chơi sau ReloadDelay giây.")]
    [SerializeField] private bool _reloadSceneOnDeath;
    [SerializeField] private float _reloadDelay = 6f;

    [Header("Events")]
    public UnityEvent<float> OnDamaged;
    public UnityEvent OnDied;

    /// <summary>Người chơi vừa chết.</summary>
    public static event System.Action<SwatPlayerHealth> Died;

    /// <summary>(người chơi, sát thương thực nhận, thông tin) mỗi lần trúng.</summary>
    public static event System.Action<SwatPlayerHealth, float, DamageInfo> AnyDamaged;

    public static SwatPlayerHealth Instance { get; private set; }

    private float _health;
    private Image _overlay;
    private TextMeshProUGUI _text;
    private float _flashUntil;
    private float _deathFade;
    private AudioSource _audio;

    public float Health => _health;
    public float MaxHealth => _maxHealth;
    public bool IsDead => _health <= 0f;
    public DamageInfo LastDamage { get; private set; }

    public bool Invulnerable
    {
        get => _invulnerable;
        set => _invulnerable = value;
    }

    /// <summary>SwatPlayerHealth của người chơi chứa go (go là rig người chơi hoặc con của nó).</summary>
    public static SwatPlayerHealth Of(GameObject go)
    {
        if (go == null) return null;
        SwatPlayerHealth p = go.GetComponentInParent<SwatPlayerHealth>();
        if (p != null) return p;
        return Instance != null && go.transform.root == Instance.transform.root ? Instance : null;
    }

    /// <summary>go là người chơi đã chết (NPC thôi nhắm).</summary>
    public static bool IsPlayerDead(GameObject go)
    {
        SwatPlayerHealth p = Of(go);
        return p != null && p.IsDead;
    }

    /// <summary>Gắn SwatPlayerHealth lên object tag "Player" nếu chưa có.</summary>
    public static SwatPlayerHealth EnsureOnPlayer(string tag = "Player")
    {
        if (Instance != null || !Application.isPlaying) return Instance;
        SwatPlayerHealth existing = FindAnyObjectByType<SwatPlayerHealth>();
        if (existing != null) return existing;
        GameObject player = GameObject.FindWithTag(tag);
        return player != null ? player.AddComponent<SwatPlayerHealth>() : null;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        _health = _maxHealth;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_overlay != null) Destroy(_overlay.canvas.gameObject);
    }

    // ---------- Nhận sát thương ----------

    /// <summary>Hệ thống cũ: SendMessage("TakeDamage", float) = đạn trúng thân.</summary>
    public void TakeDamage(float amount) => ApplyDamage(new DamageInfo { Amount = amount, Type = DamageType.Bullet, Zone = HitZone.Torso, ZoneKnown = true });

    /// <summary>Lựu đạn của chính người chơi (VRGrenade) nổ gần.</summary>
    void IDamageable.ApplyDamage(float damage, RaycastHit hit) => ApplyDamage(DamageInfo.Explosion(damage, hit.point));

    public void ApplyDamage(DamageInfo info)
    {
        if (IsDead || info.Amount <= 0f) return;
        if (!info.ZoneKnown) info = info.WithZone(info.Type == DamageType.Bullet ? DamageModel.RollAimedZone(0.5f) : HitZone.Torso);
        float amount = DamageModel.Resolve(info, _bodyArmor, _helmet) * (_invulnerable ? 0f : _damageScale);
        LastDamage = info;
        if (amount <= 0f) return;

        _health = Mathf.Max(0f, _health - amount);
        Debug.Log($"[Player] Trúng {info} -> mất {amount:0.#} máu, còn {_health:0}/{_maxHealth:0}.", this);
        _flashUntil = Time.time + _hitFlashTime;
        Haptic(XRNode.LeftHand, info.Type == DamageType.Explosion ? 1f : _hapticAmplitude);
        Haptic(XRNode.RightHand, info.Type == DamageType.Explosion ? 1f : _hapticAmplitude);
        PlayClip(_hurtClips != null && _hurtClips.Length > 0 ? _hurtClips[Random.Range(0, _hurtClips.Length)] : null);
        OnDamaged?.Invoke(amount);
        AnyDamaged?.Invoke(this, amount, info);
        if (IsDead) Die();
    }

    /// <summary>Hồi máu (bộ sơ cứu, kịch bản...).</summary>
    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f) return;
        _health = Mathf.Min(_maxHealth, _health + amount);
    }

    private void Die()
    {
        Debug.LogWarning("[Player] Người chơi đã bị hạ.", this);
        PlayClip(_deathClip);
        OnDied?.Invoke();
        Died?.Invoke(this);
        if (_reloadSceneOnDeath) StartCoroutine(ReloadAfter(_reloadDelay));
    }

    private IEnumerator ReloadAfter(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ---------- Màn hình ----------

    private void LateUpdate()
    {
        float ratio = _maxHealth > 0f ? _health / _maxHealth : 0f;
        float alpha = 0f;
        if (Time.time < _flashUntil) alpha = Mathf.Max(alpha, _hitColor.a * ((_flashUntil - Time.time) / Mathf.Max(0.01f, _hitFlashTime)));
        if (!IsDead && ratio < _lowHealthRatio) alpha = Mathf.Max(alpha, 0.12f + 0.08f * Mathf.Sin(Time.time * 4f)); // nhịp tim
        if (IsDead) _deathFade = Mathf.MoveTowards(_deathFade, 1f, Time.deltaTime / 2.5f);
        if (alpha <= 0.001f && !IsDead)
        {
            if (_overlay != null) _overlay.enabled = false;
            return;
        }
        if (!EnsureOverlay()) return;
        _overlay.enabled = true;
        Color c = IsDead ? Color.Lerp(_hitColor, new Color(0.05f, 0f, 0f, 0.92f), _deathFade) : new Color(_hitColor.r, _hitColor.g, _hitColor.b, alpha);
        _overlay.color = c;
        _text.enabled = IsDead && _deathFade > 0.5f;
        if (_text.enabled) _text.alpha = Mathf.InverseLerp(0.5f, 1f, _deathFade);
    }

    /// <summary>Lớp phủ màu trước camera (Canvas Screen Space - Camera, hiện ở cả 2 mắt VR).</summary>
    private bool EnsureOverlay()
    {
        if (_overlay != null) return true;
        Camera cam = Camera.main;
        if (cam == null) return false;
        var go = new GameObject("SwatPlayerHealth Overlay", typeof(RectTransform));
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = Mathf.Max(cam.nearClipPlane + 0.02f, 0.12f);
        canvas.sortingOrder = 1000;
        _overlay = new GameObject("Tint", typeof(RectTransform)).AddComponent<Image>();
        _overlay.transform.SetParent(go.transform, false);
        var rt = _overlay.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        _overlay.raycastTarget = false;
        _text = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        _text.transform.SetParent(go.transform, false);
        var tr = _text.rectTransform;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = tr.offsetMax = Vector2.zero;
        _text.text = _deathText;
        _text.fontSize = 48f;
        _text.alignment = TextAlignmentOptions.Center;
        _text.color = new Color(1f, 0.85f, 0.85f);
        _text.raycastTarget = false;
        _text.enabled = false;
        return true;
    }

    private void PlayClip(AudioClip clip)
    {
        if (clip == null) return;
        if (_audio == null)
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
        }
        _audio.PlayOneShot(clip);
    }

    private static void Haptic(XRNode node, float amplitude)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (device.isValid) device.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), 0.15f);
    }
}
