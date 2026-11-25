using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// Còng tay con tin đã đầu hàng bằng tay VR (cùng cách với NPCCuffable của NPC).
///
/// - Con tin quỳ giơ tay. Người chơi lại gần (PresentDistance): con tin quay về phía người chơi và đưa 2 tay ra trước
///   (IK đè lên anim Comply, dùng chung NPCHandsIK).
/// - Người chơi đưa tay tới cổ tay con tin, bấm nút còng (chọn ở PlayerCuffInput > Hostage Cuff Button, mặc định Grip)
///   và GIỮ CuffTime giây -> Hostage.Restrain(): anim Cuffed.
/// - Test không cần kính: đứng gần, giữ phím R. Hoặc tick "Test Cuffed Anim" để con tin đầu hàng rồi bị còng ngay.
/// </summary>
[RequireComponent(typeof(Hostage))]
public class HostageCuffable : MonoBehaviour
{
    [Header("Đưa tay ra")]
    [SerializeField] private float _presentDistance = 2.2f;
    [SerializeField] private float _blendTime = 0.5f;
    [SerializeField] private float _handsForward = 0.36f;
    [SerializeField] private float _handsHeight = 0.8f;
    [SerializeField] private float _wristGap = 0.06f;
    [SerializeField] private float _turnSpeed = 90f;

    [Header("Còng")]
    [SerializeField] private float _cuffTime = 1f;
    [Tooltip("Tay người chơi phải gần cổ tay con tin trong khoảng này mới còng được (m).")]
    [SerializeField] private float _maxHandDistance = 0.45f;
    [SerializeField] private GameObject _cuffsPrefab;
    [SerializeField] private AudioClip _cuffClip;
    [SerializeField] private bool _keyboardR = true;

    [Header("Test")]
    [Tooltip("Tick (kể cả lúc đang Play): con tin đầu hàng ngay rồi sau TestRestrainDelay giây bị còng (anim Cuffed).")]
    [SerializeField] private bool _testCuffedAnim;
    [Tooltip("Chờ bao lâu sau khi đầu hàng mới còng (giây), để xem hết anim quỳ giơ tay.")]
    [SerializeField] private float _testRestrainDelay = 2f;

    private Hostage _hostage;
    private NPCHandsIK _ik;
    private Transform _cuffPoint;
    private Transform _leftHand, _rightHand;

    private float _weight;
    private bool _vrCuffing;
    private XRNode _cuffHand;
    private float _cuffProgress;
    private bool _keyboardCuffing;
    private float _testRestrainAt = -1f;

    public float PresentWeight => _weight;
    public float CuffProgress => _cuffTime <= 0f ? 1f : Mathf.Clamp01(_cuffProgress / _cuffTime);
    public bool CanBeCuffed => _hostage != null && Cuffable && _weight > 0.7f;

    private bool Cuffable => _hostage.IsSurrendered && !_hostage.IsRestrained && !_hostage.IsDead;

    private void Awake()
    {
        _hostage = GetComponent<Hostage>();
        Animator animator = GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman)
        {
            _leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            _ik = animator.GetComponent<NPCHandsIK>();
            if (_ik == null) _ik = animator.gameObject.AddComponent<NPCHandsIK>();
        }
        var go = new GameObject("CuffPoint");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, _handsHeight, _handsForward);
        _cuffPoint = go.transform;
    }

    private void Start() => _ = PlayerCuffInput.Instance; // tạo sớm để đọc nút từ đầu

    private void Update()
    {
        if (UpdateTest()) return;

        Transform player = PlayerHead();
        bool want = Cuffable && player != null && FlatDistance(transform.position, player.position) <= _presentDistance;
        if (_vrCuffing || _keyboardCuffing) want |= Cuffable; // đang còng dở thì giữ tay ra

        float step = _blendTime <= 0f ? 1f : Time.deltaTime / _blendTime;
        _weight = Mathf.MoveTowards(_weight, want ? 1f : 0f, step);

        if (want && player != null) FacePlayer(player.position);
        UpdateIK(player);

        if (!CanBeCuffed) { CancelCuffing(); return; }

        UpdateVRInput();
        UpdateKeyboard();
        UpdateCuffing();
    }

    private void LateUpdate()
    {
        if (_cuffPoint != null && _leftHand != null && _rightHand != null)
            _cuffPoint.position = (_leftHand.position + _rightHand.position) * 0.5f;
    }

    // ---------- Test ----------

    /// <summary>Tick Test Cuffed Anim: đầu hàng rồi còng. Trả true khi đang chạy test (bỏ qua IK / input).</summary>
    private bool UpdateTest()
    {
        if (!_testCuffedAnim || _hostage.IsDead || _hostage.IsRestrained)
        {
            _testRestrainAt = -1f;
            return false;
        }

        if (!_hostage.IsSurrendered) _hostage.Surrender();
        if (_testRestrainAt < 0f) _testRestrainAt = Time.time + _testRestrainDelay;

        _weight = 0f;
        if (_ik != null) _ik.Weight = 0f;
        CancelCuffing();

        if (_hostage.IsSurrendered && Time.time >= _testRestrainAt) CompleteCuff(null);
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
        if (Quaternion.Angle(transform.rotation, goal) < 20f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, goal, _turnSpeed * Time.deltaTime);
    }

    // ---------- Còng ----------

    private void UpdateVRInput()
    {
        PlayerCuffInput input = PlayerCuffInput.Instance;
        if (_vrCuffing)
        {
            if (!input.IsHolding(PlayerCuffInput.Target.Hostage, _cuffHand, _cuffPoint.position, _maxHandDistance))
            {
                _vrCuffing = false;
                if (!_keyboardCuffing) _cuffProgress = 0f;
            }
        }
        else if (input.TryBeginCuff(PlayerCuffInput.Target.Hostage, _cuffPoint.position, _maxHandDistance, out XRNode hand))
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

    /// <summary>Giữ R chỉ còng người gần nhất (xét cả con tin lẫn NPC đang đưa tay).</summary>
    private bool IsNearestCuffable()
    {
        Transform player = PlayerHead();
        if (player == null) return false;
        float mine = FlatDistance(transform.position, player.position);
        foreach (Hostage other in Hostage.Active)
        {
            if (other == _hostage || !other.TryGetComponent(out HostageCuffable c) || !c.CanBeCuffed) continue;
            if (FlatDistance(other.transform.position, player.position) < mine) return false;
        }
        foreach (NPC npc in NPC.Active)
        {
            if (!npc.TryGetComponent(out NPCCuffable c) || !c.CanBeCuffed) continue;
            if (FlatDistance(npc.transform.position, player.position) < mine) return false;
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
        if (!_hostage.Restrain()) return;

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
    }
}
