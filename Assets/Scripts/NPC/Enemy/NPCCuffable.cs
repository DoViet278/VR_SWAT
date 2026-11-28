using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// Còng tay NPC đã đầu hàng bằng tay VR.
///
/// - NPC quỳ giơ tay (Comply). Người chơi lại gần (PresentDistance): NPC quay về phía người chơi và
///   đưa 2 tay ra trước ngực, cổ tay sát nhau (IK đè lên anim Comply), đầu nhìn người chơi.
/// - Người chơi đưa tay tới cổ tay NPC, bấm nút còng (chọn ở PlayerCuffInput > Enemy Cuff Button, mặc định Grip)
///   và GIỮ CuffTime giây (giống giữ phím trong SWAT 4)
///   -> NPC.Restrain(): anim Cuffed (tay ra sau lưng), hiện còng (nếu có prefab). Buông sớm thì hủy.
/// - Người chơi lùi xa thì NPC rút tay về tư thế giơ tay.
///
/// Gắn lên object gốc của NPC (cùng NPC). Script tự tạo điểm còng ở giữa 2 cổ tay và gắn NPCHandsIK
/// lên object có Animator (Animator Controller cần bật IK Pass ở Base Layer).
/// Test không cần kính: đứng gần NPC, giữ phím R. Hoặc tick "Test Cuffed Anim" để NPC đầu hàng rồi bị còng ngay.
/// </summary>
[RequireComponent(typeof(NPC))]
public class NPCCuffable : MonoBehaviour
{
    [Header("Đưa tay ra")]
    [Tooltip("Người chơi (đầu) trong khoảng này thì NPC đưa tay ra cho còng.")]
    [SerializeField] private float _presentDistance = 2.2f;
    [Tooltip("Thời gian blend tay ra / về (giây).")]
    [SerializeField] private float _blendTime = 0.5f;
    [Tooltip("Khoảng cách 2 cổ tay ra trước so với gốc NPC (m).")]
    [SerializeField] private float _handsForward = 0.36f;
    [Tooltip("Độ cao 2 cổ tay so với gốc NPC khi quỳ (m).")]
    [SerializeField] private float _handsHeight = 0.8f;
    [Tooltip("Nửa khoảng cách giữa 2 cổ tay (m).")]
    [SerializeField] private float _wristGap = 0.06f;
    [Tooltip("Quay mặt về người chơi khi đưa tay (độ/giây). 0 = không quay.")]
    [SerializeField] private float _turnSpeed = 90f;

    [Header("Còng")]
    [Tooltip("Giữ nút còng trên cổ tay NPC bao lâu thì còng xong (giây).")]
    [SerializeField] private float _cuffTime = 1f;
    [Tooltip("Tay người chơi phải gần cổ tay NPC trong khoảng này mới còng được (m).")]
    [SerializeField] private float _maxHandDistance = 0.45f;
    [Tooltip("Prefab còng hiện trên cổ tay trái khi còng xong (tùy chọn).")]
    [SerializeField] private GameObject _cuffsPrefab;
    [SerializeField] private AudioClip _cuffClip;
    [Tooltip("Phím test trong Editor: giữ để còng NPC gần nhất đang đưa tay.")]
    [SerializeField] private bool _keyboardR = true;

    [Header("Test")]
    [Tooltip("Tick (kể cả lúc đang Play): NPC đầu hàng ngay rồi sau TestRestrainDelay giây bị còng (anim Cuffed).")]
    [SerializeField] private bool _testCuffedAnim;
    [Tooltip("Chờ bao lâu sau khi đầu hàng mới còng (giây), để xem hết anim quỳ giơ tay.")]
    [SerializeField] private float _testRestrainDelay = 2f;

    private NPC _npc;
    private Animator _animator;
    private NPCHandsIK _ik;
    private Transform _cuffPoint;
    private Transform _leftHand, _rightHand;

    private float _weight;
    private bool _vrCuffing;
    private XRNode _cuffHand;
    private float _cuffProgress;
    private bool _keyboardCuffing;
    private float _testRestrainAt = -1f;

    /// <summary>0-1: NPC đang đưa tay ra tới đâu.</summary>
    public float PresentWeight => _weight;

    /// <summary>0-1: tiến độ còng (đang giữ nút).</summary>
    public float CuffProgress => _cuffTime <= 0f ? 1f : Mathf.Clamp01(_cuffProgress / _cuffTime);

    /// <summary>Đang đưa tay ra đủ để còng.</summary>
    public bool CanBeCuffed => _npc != null && Cuffable && _weight > 0.7f;

    private bool Cuffable => _npc.IsSurrendered && !_npc.IsRestrained && !_npc.IsDead;

    // ---------- Đồng đội AI còng (lệnh Restrain) ----------

    private Object _aiCuffer;
    private float _aiProgress;

    /// <summary>Điểm giữa 2 cổ tay (đồng đội đưa tay tới đây khi còng).</summary>
    public Transform CuffPoint => _cuffPoint;

    /// <summary>Đã đầu hàng, chưa bị còng, còn sống.</summary>
    public bool IsCuffable => _npc != null && Cuffable;

    /// <summary>Đang bị một đồng đội AI còng.</summary>
    public bool IsAICuffing => _aiCuffer != null;

    /// <summary>Đồng đội bắt đầu còng: người chơi không còng chen được, NPC không đưa tay về phía người chơi.</summary>
    public bool BeginAICuff(Object cuffer)
    {
        if (!IsCuffable || (_aiCuffer != null && _aiCuffer != cuffer)) return false;
        if (_aiCuffer != cuffer) _aiProgress = 0f;
        _aiCuffer = cuffer;
        CancelCuffing();
        return true;
    }

    /// <summary>Tiếp tục còng; đủ duration giây thì còng xong (NPC.Restrain) và trả true.</summary>
    public bool TickAICuff(Object cuffer, float deltaTime, float duration)
    {
        if (_aiCuffer != cuffer || !IsCuffable) return false;
        _aiProgress += deltaTime;
        if (_aiProgress < duration) return false;
        _aiCuffer = null;
        _aiProgress = 0f;
        CompleteCuff(null);
        return _npc.IsRestrained;
    }

    public void CancelAICuff(Object cuffer)
    {
        if (_aiCuffer != cuffer) return;
        _aiCuffer = null;
        _aiProgress = 0f;
    }

    private void Awake()
    {
        _npc = GetComponent<NPC>();
        _animator = GetComponentInChildren<Animator>();
        if (_animator != null && _animator.isHuman)
        {
            _leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            _ik = _animator.GetComponent<NPCHandsIK>();
            if (_ik == null) _ik = _animator.gameObject.AddComponent<NPCHandsIK>();
        }
        CreateCuffPoint();
    }

    private void Start() => _ = PlayerCuffInput.Instance; // tạo sớm để đọc nút từ đầu

    private void CreateCuffPoint()
    {
        var go = new GameObject("CuffPoint");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, _handsHeight, _handsForward);
        _cuffPoint = go.transform;
    }

    private void Update()
    {
        if (UpdateTest()) return;

        Transform player = PlayerHead();
        bool want = Cuffable && _aiCuffer == null && player != null && FlatDistance(transform.position, player.position) <= _presentDistance;

        // Đang còng dở thì giữ tay ra dù người chơi hơi lùi.
        if (_vrCuffing || _keyboardCuffing) want |= Cuffable;

        float step = _blendTime <= 0f ? 1f : Time.deltaTime / _blendTime;
        _weight = Mathf.MoveTowards(_weight, want ? 1f : 0f, step);

        if (want && player != null) FacePlayer(player.position);
        UpdateIK(player);

        if (!CanBeCuffed || _aiCuffer != null) { CancelCuffing(); return; } // đồng đội đang còng: người chơi không chen

        UpdateVRInput();
        UpdateKeyboard();
        UpdateCuffing();
    }

    private void LateUpdate()
    {
        // Điểm còng bám theo 2 cổ tay thật (sau khi anim + IK đã chạy).
        if (_cuffPoint != null && _leftHand != null && _rightHand != null)
            _cuffPoint.position = (_leftHand.position + _rightHand.position) * 0.5f;
    }

    // ---------- Test ----------

    /// <summary>Tick Test Cuffed Anim: đầu hàng rồi còng. Trả true khi đang chạy test (bỏ qua IK / input).</summary>
    private bool UpdateTest()
    {
        if (!_testCuffedAnim || _npc.IsDead || _npc.IsRestrained)
        {
            _testRestrainAt = -1f;
            return false;
        }

        if (!_npc.IsSurrendered)
        {
            _npc.Surrender();
            _testRestrainAt = Time.time + _testRestrainDelay;
        }
        else if (_testRestrainAt < 0f)
        {
            _testRestrainAt = Time.time + _testRestrainDelay;
        }

        _weight = 0f;
        if (_ik != null) _ik.Weight = 0f;
        CancelCuffing();

        if (_npc.IsSurrendered && Time.time >= _testRestrainAt) CompleteCuff(null);
        return true;
    }

    [ContextMenu("Test: Surrender + Cuff")]
    private void TestCuffNow() => _testCuffedAnim = true;

    // ---------- Đưa tay ----------

    private void UpdateIK(Transform player)
    {
        if (_ik == null) return;
        _ik.Weight = _weight;
        Transform t = transform;
        Vector3 center = t.position + t.up * _handsHeight + t.forward * _handsForward;
        _ik.LeftHandPosition = center - t.right * _wristGap;
        _ik.RightHandPosition = center + t.right * _wristGap;
        // Lòng bàn tay úp vào nhau, ngón tay chỉ ra trước.
        _ik.LeftHandRotation = Quaternion.LookRotation(t.forward, t.right);
        _ik.RightHandRotation = Quaternion.LookRotation(t.forward, -t.right);
        _ik.LookAt = player != null ? player.position : center + t.forward;
    }

    private void FacePlayer(Vector3 playerPos)
    {
        if (_turnSpeed <= 0f) return;
        Vector3 dir = playerPos - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;
        Quaternion goal = Quaternion.LookRotation(dir);
        if (Quaternion.Angle(transform.rotation, goal) < 20f) return; // lệch ít thì thôi, tránh xoay lắc liên tục
        transform.rotation = Quaternion.RotateTowards(transform.rotation, goal, _turnSpeed * Time.deltaTime);
    }

    // ---------- Còng ----------

    private void UpdateVRInput()
    {
        PlayerCuffInput input = PlayerCuffInput.Instance;
        if (_vrCuffing)
        {
            if (!input.IsHolding(PlayerCuffInput.Target.Enemy, _cuffHand, _cuffPoint.position, _maxHandDistance))
            {
                _vrCuffing = false;
                if (!_keyboardCuffing) _cuffProgress = 0f;
            }
        }
        else if (input.TryBeginCuff(PlayerCuffInput.Target.Enemy, _cuffPoint.position, _maxHandDistance, out XRNode hand))
        {
            _vrCuffing = true;
            _cuffHand = hand;
            _cuffProgress = 0f;
        }
    }

    private void UpdateKeyboard()
    {
        if (!_keyboardR || Keyboard.current == null) return;
        bool held = Keyboard.current.rKey.isPressed && IsNearestCuffable();
        if (held && !_keyboardCuffing) { _keyboardCuffing = true; _cuffProgress = 0f; }
        else if (!held && _keyboardCuffing) { _keyboardCuffing = false; if (!_vrCuffing) _cuffProgress = 0f; }
    }

    private bool IsNearestCuffable()
    {
        Transform player = PlayerHead();
        if (player == null) return false;
        float mine = FlatDistance(transform.position, player.position);
        foreach (NPC other in NPC.Active)
        {
            if (other == _npc || !other.TryGetComponent(out NPCCuffable c) || !c.CanBeCuffed) continue;
            if (FlatDistance(other.transform.position, player.position) < mine) return false;
        }
        return true;
    }

    private void UpdateCuffing()
    {
        if (!_vrCuffing && !_keyboardCuffing) return;

        _cuffProgress += Time.deltaTime;
        if (_cuffProgress < _cuffTime) return;

        XRNode? hand = _vrCuffing ? _cuffHand : null;
        CancelCuffing();
        CompleteCuff(hand);
    }

    private void CompleteCuff(XRNode? hand)
    {
        if (!_npc.Restrain()) return;

        if (_cuffClip != null) AudioSource.PlayClipAtPoint(_cuffClip, _cuffPoint.position);
        if (hand.HasValue) PlayerCuffInput.Haptic(hand.Value, 0.6f, 0.15f);
        if (_cuffsPrefab != null && _leftHand != null) Instantiate(_cuffsPrefab, _leftHand, false);
    }

    private void CancelCuffing()
    {
        _vrCuffing = false;
        _keyboardCuffing = false;
        _cuffProgress = 0f;
    }

    private static Transform PlayerHead() => Camera.main != null ? Camera.main.transform : null;

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, _presentDistance);
        Gizmos.DrawWireSphere(transform.position + transform.up * _handsHeight + transform.forward * _handsForward, _maxHandDistance);
    }
}
