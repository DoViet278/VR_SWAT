using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Còng tay NPC đã đầu hàng bằng tay VR.
///
/// - NPC quỳ giơ tay (Comply). Người chơi lại gần (PresentDistance): NPC quay về phía người chơi và
///   đưa 2 tay ra trước ngực, cổ tay sát nhau (IK đè lên anim Comply), đầu nhìn người chơi.
/// - Người chơi đưa tay tới cổ tay NPC, bóp grip (select) và GIỮ CuffTime giây (giống giữ phím trong SWAT 4)
///   -> NPC.Restrain(): anim Cuffed (tay ra sau lưng), hiện còng (nếu có prefab). Buông sớm thì hủy.
/// - Người chơi lùi xa thì NPC rút tay về tư thế giơ tay.
///
/// Gắn lên object gốc của NPC (cùng NPC). Script tự tạo điểm còng (XRSimpleInteractable + trigger) ở giữa
/// 2 cổ tay và gắn NPCHandsIK lên object có Animator (Animator Controller cần bật IK Pass ở Base Layer).
/// Test không cần kính: đứng gần NPC, giữ phím R.
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
    [Tooltip("Giữ grip trên cổ tay NPC bao lâu thì còng xong (giây).")]
    [SerializeField] private float _cuffTime = 1f;
    [Tooltip("Bán kính vùng cổ tay để chạm tay còng (m).")]
    [SerializeField] private float _cuffRadius = 0.15f;
    [Tooltip("Tay người chơi (interactor) phải gần cổ tay NPC trong khoảng này mới còng được (chặn còng bằng tia từ xa).")]
    [SerializeField] private float _maxHandDistance = 0.45f;
    [Tooltip("Prefab còng hiện trên cổ tay trái khi còng xong (tùy chọn).")]
    [SerializeField] private GameObject _cuffsPrefab;
    [SerializeField] private AudioClip _cuffClip;
    [Tooltip("Phím test trong Editor: giữ để còng NPC gần nhất đang đưa tay.")]
    [SerializeField] private bool _keyboardR = true;

    private NPC _npc;
    private Animator _animator;
    private NPCHandsIK _ik;
    private Transform _cuffPoint;
    private XRSimpleInteractable _interactable;
    private Transform _leftHand, _rightHand;

    private float _weight;
    private IXRSelectInteractor _cuffer;
    private float _cuffProgress;
    private bool _keyboardCuffing;

    /// <summary>0-1: NPC đang đưa tay ra tới đâu.</summary>
    public float PresentWeight => _weight;

    /// <summary>0-1: tiến độ còng (đang giữ grip).</summary>
    public float CuffProgress => _cuffTime <= 0f ? 1f : Mathf.Clamp01(_cuffProgress / _cuffTime);

    /// <summary>Đang đưa tay ra đủ để còng.</summary>
    public bool CanBeCuffed => _npc != null && _npc.IsSurrendered && !_npc.IsRestrained && !_npc.IsDead && _weight > 0.7f;

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

    private void CreateCuffPoint()
    {
        var go = new GameObject("CuffPoint");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, _handsHeight, _handsForward);
        // Cùng layer với NPC để hand/ray interactor dò được như các vật khác.
        go.layer = gameObject.layer;
        var sphere = go.AddComponent<SphereCollider>();
        sphere.isTrigger = true;
        sphere.radius = _cuffRadius;

        _interactable = go.AddComponent<XRSimpleInteractable>();
        _interactable.colliders.Clear();
        _interactable.colliders.Add(sphere);
        _interactable.selectEntered.AddListener(OnSelectEntered);
        _interactable.selectExited.AddListener(OnSelectExited);
        _cuffPoint = go.transform;
        go.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_interactable == null) return;
        _interactable.selectEntered.RemoveListener(OnSelectEntered);
        _interactable.selectExited.RemoveListener(OnSelectExited);
    }

    private void Update()
    {
        Transform player = PlayerHead();
        bool want = _npc.IsSurrendered && !_npc.IsRestrained && !_npc.IsDead && player != null
                    && FlatDistance(transform.position, player.position) <= _presentDistance;

        // Đang còng dở thì giữ tay ra dù người chơi hơi lùi.
        if (_cuffer != null || _keyboardCuffing) want = want || (_npc.IsSurrendered && !_npc.IsRestrained && !_npc.IsDead);

        float step = _blendTime <= 0f ? 1f : Time.deltaTime / _blendTime;
        _weight = Mathf.MoveTowards(_weight, want ? 1f : 0f, step);

        if (want && player != null) FacePlayer(player.position);
        UpdateIK(player);

        bool active = CanBeCuffed;
        if (_cuffPoint.gameObject.activeSelf != active)
        {
            if (!active) CancelCuffing();
            _cuffPoint.gameObject.SetActive(active);
        }

        UpdateKeyboard();
        UpdateCuffing();
    }

    private void LateUpdate()
    {
        // Điểm còng bám theo 2 cổ tay thật (sau khi anim + IK đã chạy).
        if (_cuffPoint != null && _cuffPoint.gameObject.activeSelf && _leftHand != null && _rightHand != null)
            _cuffPoint.position = (_leftHand.position + _rightHand.position) * 0.5f;
    }

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

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (!CanBeCuffed) return;
        // Chỉ còng bằng tay đặt ngay ở cổ tay NPC, không còng bằng tia từ xa.
        Transform hand = args.interactorObject.transform;
        if (hand != null && Vector3.Distance(hand.position, _cuffPoint.position) > _maxHandDistance) return;
        _cuffer = args.interactorObject;
        _cuffProgress = 0f;
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        if (args.interactorObject == _cuffer) CancelCuffing();
    }

    private void UpdateKeyboard()
    {
        if (!_keyboardR || Keyboard.current == null) return;
        bool held = Keyboard.current.rKey.isPressed && CanBeCuffed && IsNearestCuffable();
        if (held && !_keyboardCuffing) { _keyboardCuffing = true; _cuffProgress = 0f; }
        else if (!held && _keyboardCuffing) { _keyboardCuffing = false; if (_cuffer == null) _cuffProgress = 0f; }
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
        if (_cuffer == null && !_keyboardCuffing) return;
        if (!CanBeCuffed) { CancelCuffing(); return; }

        _cuffProgress += Time.deltaTime;
        if (_cuffProgress < _cuffTime) return;

        IXRSelectInteractor cuffer = _cuffer;
        CancelCuffing();
        if (!_npc.Restrain()) return;

        if (_cuffClip != null) AudioSource.PlayClipAtPoint(_cuffClip, _cuffPoint.position);
        if (cuffer is XRBaseInputInteractor input) input.SendHapticImpulse(0.6f, 0.15f);
        if (_cuffsPrefab != null && _leftHand != null) Instantiate(_cuffsPrefab, _leftHand, false);
        // Buông interactable để tay người chơi không còn "nắm" NPC.
        _cuffPoint.gameObject.SetActive(false);
    }

    private void CancelCuffing()
    {
        _cuffer = null;
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
        Gizmos.DrawWireSphere(transform.position + transform.up * _handsHeight + transform.forward * _handsForward, _cuffRadius);
    }
}
