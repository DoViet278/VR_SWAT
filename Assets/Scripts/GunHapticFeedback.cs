using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class GunHapticFeedback : MonoBehaviour
{
    public enum HapticQuality
    {
        Auto,
        Basic,
        Advanced
    }

    [System.Serializable]
    public struct HapticPulse
    {
        [Range(0f, 1f)] public float amplitude;
        public float duration;
        [Tooltip("Hz. 0 = tần số mặc định của thiết bị. Chỉ có tác dụng trên Quest 3 (Touch Plus).")]
        public float frequency;
        [Tooltip("Thời gian chờ sau xung trước đó.")]
        public float delay;
    }

    [Header("Mode")]
    [Tooltip("Auto: Touch Plus (Quest 3/3S) dùng Advanced, còn lại (Quest 2) dùng Basic.")]
    [SerializeField] private HapticQuality quality = HapticQuality.Auto;
    [Tooltip("Chuỗi tên thiết bị cho là Advanced (so khớp không phân biệt hoa thường).")]
    [SerializeField] private string[] advancedDeviceKeywords = { "Plus", "Pro" };

    [Header("Basic (Quest 2): một kiểu rung duy nhất")]
    [SerializeField] private HapticPulse basicShot = new HapticPulse { amplitude = 0.6f, duration = 0.08f };

    [Header("Advanced (Quest 3): nhiều xung, tần số tùy biến")]
    [SerializeField] private HapticPulse[] advancedShot =
    {
        new HapticPulse { amplitude = 1f, duration = 0.03f, frequency = 320f, delay = 0f },
        new HapticPulse { amplitude = 0.45f, duration = 0.07f, frequency = 140f, delay = 0.03f }
    };

    [Header("Dry Fire")]
    [SerializeField] private HapticPulse dryFire = new HapticPulse { amplitude = 0.25f, duration = 0.03f, frequency = 200f };

    [Header("Reload (shotgun)")]
    [Tooltip("Rung khi nhồi được 1 viên vào ống đạn.")]
    [SerializeField] private HapticPulse shellInsert = new HapticPulse { amplitude = 0.4f, duration = 0.04f, frequency = 180f };
    [Tooltip("Rung khi ống đạn đã đầy, không nhồi thêm được.")]
    [SerializeField] private HapticPulse shellRejected = new HapticPulse { amplitude = 0.15f, duration = 0.03f, frequency = 90f };
    [Tooltip("Rung khi kéo pump về sau (nhả vỏ).")]
    [SerializeField] private HapticPulse pumpPull = new HapticPulse { amplitude = 0.5f, duration = 0.05f, frequency = 150f };
    [Tooltip("Rung khi đẩy pump lên (lên nòng).")]
    [SerializeField] private HapticPulse pumpPush = new HapticPulse { amplitude = 0.6f, duration = 0.05f, frequency = 220f };

    [Header("Two-Handed")]
    [Tooltip("Hệ số nhân độ rung khi cầm hai tay.")]
    [SerializeField] [Range(0f, 1f)] private float twoHandedMultiplier = 0.7f;

    private GunRecoilController recoilController;
    private Coroutine playing;

    private void Awake()
    {
        recoilController = GetComponent<GunRecoilController>();
    }

    public void PlayShot(object interactorObject)
    {
        float multiplier = recoilController != null && recoilController.IsTwoHanded ? twoHandedMultiplier : 1f;
        Play(interactorObject, IsAdvanced(interactorObject) ? advancedShot : new[] { basicShot }, multiplier);
    }

    public void PlayDryFire(object interactorObject)
    {
        Play(interactorObject, new[] { dryFire }, 1f);
    }

    public void PlayShellInsert(object interactorObject)
    {
        Play(interactorObject, new[] { shellInsert }, 1f);
    }

    public void PlayShellRejected(object interactorObject)
    {
        Play(interactorObject, new[] { shellRejected }, 1f);
    }

    public void PlayPumpPull(object interactorObject)
    {
        Play(interactorObject, new[] { pumpPull }, 1f);
    }

    public void PlayPumpPush(object interactorObject)
    {
        Play(interactorObject, new[] { pumpPush }, 1f);
    }

    /// <summary>Phát một xung rung tuỳ ý vào bàn tay đang tác động.</summary>
    public void PlayPulse(object interactorObject, HapticPulse pulse)
    {
        Play(interactorObject, new[] { pulse }, 1f);
    }

    private void Play(object interactorObject, HapticPulse[] pulses, float multiplier)
    {
        if (!(interactorObject is Component interactor) || pulses == null || pulses.Length == 0)
            return;

        if (playing != null)
            StopCoroutine(playing);

        playing = StartCoroutine(PlaySequence(interactor, pulses, multiplier));
    }

    private IEnumerator PlaySequence(Component interactor, HapticPulse[] pulses, float multiplier)
    {
        var player = interactor.GetComponentInParent<HapticImpulsePlayer>();
        var inputInteractor = interactor as XRBaseInputInteractor;

        foreach (HapticPulse pulse in pulses)
        {
            if (pulse.delay > 0f)
                yield return new WaitForSeconds(pulse.delay);

            if (interactor == null)
                yield break;

            float amplitude = Mathf.Clamp01(pulse.amplitude * multiplier);

            if (player != null)
                player.SendHapticImpulse(amplitude, pulse.duration, pulse.frequency);
            else if (inputInteractor != null)
                inputInteractor.SendHapticImpulse(amplitude, pulse.duration);
        }

        playing = null;
    }

    private bool IsAdvanced(object interactorObject)
    {
        if (quality != HapticQuality.Auto)
            return quality == HapticQuality.Advanced;

        XRNode node = XRNode.RightHand;
        if (interactorObject is IXRInteractor interactor && interactor.handedness == UnityEngine.XR.Interaction.Toolkit.Interactors.InteractorHandedness.Left)
            node = XRNode.LeftHand;

        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid)
            return false;

        foreach (string keyword in advancedDeviceKeywords)
        {
            if (!string.IsNullOrEmpty(keyword) && device.name.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }
}
