using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using InputDevice = UnityEngine.XR.InputDevice;
using CommonUsages = UnityEngine.XR.CommonUsages;

/// <summary>Nút trên tay cầm VR dùng để còng.</summary>
public enum CuffButton
{
    Grip,
    Trigger,
    [InspectorName("Primary Button (A / X)")] PrimaryButton,
    [InspectorName("Secondary Button (B / Y)")] SecondaryButton,
    [InspectorName("Thumbstick Click")] ThumbstickClick,
}

/// <summary>
/// Chọn nút trên tay cầm để còng Enemy (NPCCuffable) và Hostage (HostageCuffable).
/// Gắn lên Player / XR Origin (cùng chỗ PlayerShout). Không có trong scene thì tự tạo với mặc định Grip.
///
/// Còng: đưa tay (controller) tới cổ tay NPC, BẤM nút đã chọn rồi GIỮ CuffTime giây. Phải bấm khi tay đã ở
/// gần cổ tay (không tính nút đang giữ sẵn, vd. đang bóp grip cầm súng).
/// </summary>
public class PlayerCuffInput : MonoBehaviour
{
    public enum Target { Enemy, Hostage }

    [Header("Nút còng")]
    [Tooltip("Nút trên tay cầm để còng Enemy.")]
    [SerializeField] private CuffButton _enemyCuffButton = CuffButton.Grip;
    [Tooltip("Nút trên tay cầm để còng Hostage.")]
    [SerializeField] private CuffButton _hostageCuffButton = CuffButton.Grip;

    [Header("Tay (trống = tự tìm theo interactor trái / phải)")]
    [SerializeField] private Transform _leftHand;
    [SerializeField] private Transform _rightHand;

    private static PlayerCuffInput _instance;

    private static readonly CuffButton[] AllButtons = (CuffButton[])System.Enum.GetValues(typeof(CuffButton));
    private static readonly XRNode[] Hands = { XRNode.LeftHand, XRNode.RightHand };

    // [hand, button] -> đang giữ / vừa bấm frame này
    private readonly bool[,] _held = new bool[2, 5];
    private readonly bool[,] _down = new bool[2, 5];
    private int _lastPollFrame = -1;

    private readonly List<Transform> _leftAuto = new();
    private readonly List<Transform> _rightAuto = new();
    private float _nextHandSearch;

    public CuffButton EnemyCuffButton { get => _enemyCuffButton; set => _enemyCuffButton = value; }
    public CuffButton HostageCuffButton { get => _hostageCuffButton; set => _hostageCuffButton = value; }

    /// <summary>Instance trong scene; chưa có thì tạo mới (mặc định Grip).</summary>
    public static PlayerCuffInput Instance
    {
        get
        {
            if (_instance != null) return _instance;
            _instance = FindAnyObjectByType<PlayerCuffInput>();
            if (_instance == null) _instance = new GameObject(nameof(PlayerCuffInput)).AddComponent<PlayerCuffInput>();
            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance == null) _instance = this;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    // Đọc nút mỗi frame để biết chính xác lúc bấm xuống (nút đã giữ sẵn từ trước không tính là bấm).
    private void Update() => Poll();

    public CuffButton ButtonFor(Target target) => target == Target.Hostage ? _hostageCuffButton : _enemyCuffButton;

    /// <summary>
    /// Tay nào vừa BẤM nút còng (frame này) khi đang ở trong maxDistance quanh point. Trả false nếu không có.
    /// </summary>
    public bool TryBeginCuff(Target target, Vector3 point, float maxDistance, out XRNode hand)
    {
        Poll();
        int b = (int)ButtonFor(target);
        for (int h = 0; h < 2; h++)
        {
            if (_down[h, b] && IsHandNear(Hands[h], point, maxDistance))
            {
                hand = Hands[h];
                return true;
            }
        }
        hand = default;
        return false;
    }

    /// <summary>Tay đang còng vẫn giữ nút và vẫn ở gần point.</summary>
    public bool IsHolding(Target target, XRNode hand, Vector3 point, float maxDistance)
    {
        Poll();
        int h = hand == XRNode.LeftHand ? 0 : 1;
        return _held[h, (int)ButtonFor(target)] && IsHandNear(hand, point, maxDistance);
    }

    public static void Haptic(XRNode hand, float amplitude, float duration)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(hand);
        if (device.isValid) device.SendHapticImpulse(0u, amplitude, duration);
    }

    // ---------- Đọc nút ----------

    private void Poll()
    {
        if (_lastPollFrame == Time.frameCount) return;
        bool first = _lastPollFrame < 0;
        _lastPollFrame = Time.frameCount;

        for (int h = 0; h < 2; h++)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(Hands[h]);
            foreach (CuffButton button in AllButtons)
            {
                int b = (int)button;
                bool pressed = device.isValid && device.TryGetFeatureValue(Usage(button), out bool v) && v;
                _down[h, b] = pressed && !_held[h, b] && !first;
                _held[h, b] = pressed;
            }
        }
    }

    private static InputFeatureUsage<bool> Usage(CuffButton button) => button switch
    {
        CuffButton.Trigger => CommonUsages.triggerButton,
        CuffButton.PrimaryButton => CommonUsages.primaryButton,
        CuffButton.SecondaryButton => CommonUsages.secondaryButton,
        CuffButton.ThumbstickClick => CommonUsages.primary2DAxisClick,
        _ => CommonUsages.gripButton,
    };

    // ---------- Vị trí tay ----------

    private bool IsHandNear(XRNode hand, Vector3 point, float maxDistance)
    {
        Transform manual = hand == XRNode.LeftHand ? _leftHand : _rightHand;
        if (manual != null) return manual.gameObject.activeInHierarchy && Vector3.Distance(manual.position, point) <= maxDistance;

        FindHandsIfNeeded();
        foreach (Transform t in hand == XRNode.LeftHand ? _leftAuto : _rightAuto)
        {
            if (t != null && t.gameObject.activeInHierarchy && Vector3.Distance(t.position, point) <= maxDistance)
                return true;
        }
        return false;
    }

    /// <summary>Lấy transform của mọi interactor tay trái / phải (near-far, direct, poke...).</summary>
    private void FindHandsIfNeeded()
    {
        if (Time.unscaledTime < _nextHandSearch && (_leftAuto.Count > 0 || _rightAuto.Count > 0)) return;
        _nextHandSearch = Time.unscaledTime + 2f;
        _leftAuto.Clear();
        _rightAuto.Clear();
        foreach (XRBaseInputInteractor interactor in FindObjectsByType<XRBaseInputInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (interactor.handedness == InteractorHandedness.Left) _leftAuto.Add(interactor.transform);
            else if (interactor.handedness == InteractorHandedness.Right) _rightAuto.Add(interactor.transform);
        }
    }
}
