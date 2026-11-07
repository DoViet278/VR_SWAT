using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Mọi thứ trong môi trường mà người chơi, AI hoặc script nhiệm vụ có thể "dùng": cửa, ngăn kéo,
/// nút bấm, van... Nhờ interface này mà logic nhiệm vụ không cần biết đó cụ thể là loại gì:
/// <c>foreach (var i in room.GetComponentsInChildren&lt;IEnvironmentInteractable&gt;()) i.Lock();</c>
/// </summary>
public interface IEnvironmentInteractable
{
    /// <summary>Tên để hiện lên UI gợi ý ("Cửa phòng tin"). Rỗng thì lấy tên GameObject.</summary>
    string DisplayName { get; }

    bool IsLocked { get; }

    /// <summary>Hiện tại có dùng được không (chưa khoá, chưa bị phá hỏng).</summary>
    bool CanInteract { get; }

    void Lock();
    void Unlock();

    /// <summary>
    /// "Dùng" nó một lần: mở cửa, kéo ngăn kéo, bấm nút. Trả về false nếu đang khoá hoặc
    /// không dùng được - khi đó vẫn có phản hồi (tiếng lắc ổ khoá, rung tay).
    /// </summary>
    /// <param name="interactorObject">Bàn tay đang tác động, để gửi rung. Có thể null (AI gọi).</param>
    bool TryInteract(object interactorObject);
}

/// <summary>Thứ có trạng thái đóng - mở liên tục: cửa bản lề, cửa lùa, ngăn kéo, nắp hầm.</summary>
public interface IOpenable : IEnvironmentInteractable
{
    /// <summary>0 = đóng hẳn, 1 = mở hết.</summary>
    float OpenAmount { get; }

    bool IsOpen { get; }
    bool IsClosed { get; }

    /// <summary>Tự mở ra (AI mở cửa, cửa tự động, script nhiệm vụ).</summary>
    void Open();

    void Close();
    void Toggle();
}

/// <summary>
/// Gửi rung cho bàn tay đang tác động lên một vật trong môi trường. Tách riêng khỏi
/// GunHapticFeedback vì cái đó gắn chặt với súng (có field riêng cho từng kiểu rung của súng).
/// </summary>
public static class EnvironmentHaptics
{
    [System.Serializable]
    public struct Pulse
    {
        [Range(0f, 1f)] public float amplitude;
        public float duration;
        [Tooltip("Hz. 0 = tần số mặc định của thiết bị. Chỉ có tác dụng trên Touch Plus (Quest 3).")]
        public float frequency;
    }

    public static void Play(object interactorObject, Pulse pulse)
    {
        if (pulse.amplitude <= 0f || pulse.duration <= 0f)
            return;

        if (!(interactorObject is Component interactor))
            return;

        float amplitude = Mathf.Clamp01(pulse.amplitude);

        HapticImpulsePlayer player = interactor.GetComponentInParent<HapticImpulsePlayer>();

        if (player != null)
        {
            player.SendHapticImpulse(amplitude, pulse.duration, pulse.frequency);
            return;
        }

        if (interactor is XRBaseInputInteractor inputInteractor)
            inputInteractor.SendHapticImpulse(amplitude, pulse.duration);
    }
}
