using UnityEngine;
using UnityEngine.InputSystem;
using VRSwat.Tutorial;

/// <summary>
/// Người chơi hô hàng ("Police! Drop your weapon!") giống phím F trong SWAT 4.
/// Gắn lên XR Origin (hoặc Main Camera). Mỗi lần hô: NPC nghe được sẽ tụt morale và xét đầu hàng
/// (xem NPCMorale.ReceiveComplianceOrder). NPC chưa đủ sợ thì phớt lờ và giao chiến.
///
/// Đầu vào: action VR (vd. nút A / X trên tay cầm) và/hoặc phím F khi test trong Editor.
/// Có thể gọi Shout() từ script khác (vd. nhận diện giọng nói).
/// </summary>
public class PlayerShout : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Action nút bấm để hô (vd. XRI Right/Primary Button).")]
    [SerializeField] private InputActionProperty _shoutAction;
    [Tooltip("Cho phép bấm phím F để hô (test không cần kính).")]
    [SerializeField] private bool _keyboardF = true;

    [Header("Shout")]
    [Tooltip("Điểm phát tiếng hô (đầu người chơi). Trống: Main Camera, không có thì transform này.")]
    [SerializeField] private Transform _mouth;
    [Tooltip("NPC trong bán kính này và không bị chắn thì nghe thấy.")]
    [SerializeField] private float _radius = 15f;
    [Tooltip("Trong bán kính này nghe thấy kể cả qua tường / cửa.")]
    [SerializeField] private float _throughWallsRadius = 5f;
    [SerializeField] private float _cooldown = 1f;

    [Header("Audio")]
    [SerializeField] private AudioSource _audio;
    [SerializeField] private AudioClip[] _clips;

    private float _nextShout;

    private void OnEnable() => _shoutAction.action?.Enable();

    private void Update()
    {
        bool pressed = _shoutAction.action != null && _shoutAction.action.WasPressedThisFrame();
        if (_keyboardF && Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame) pressed = true;
        if (pressed) Shout();
    }

    /// <summary>Hô hàng. Trả số NPC đầu hàng (-1 nếu đang cooldown).</summary>
    public int Shout()
    {
        if (Time.time < _nextShout) return -1;
        _nextShout = Time.time + _cooldown;

        Transform mouth = _mouth != null ? _mouth : Camera.main != null ? Camera.main.transform : transform;
        if (_audio != null && _clips != null && _clips.Length > 0)
            _audio.PlayOneShot(_clips[Random.Range(0, _clips.Length)]);

        TutorialSignals.Raise(Sig.PlayerShout, this, mouth.position);
        return NPCMorale.IssueComplianceOrder(mouth.position, _radius, _throughWallsRadius);
    }
}
