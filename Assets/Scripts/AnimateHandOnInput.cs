using UnityEngine;
using UnityEngine.InputSystem;

public class AnimateHandOnInput : MonoBehaviour
{
    public InputActionProperty triggerValue;
    public InputActionProperty gripValue;

    public Animator handAnimator;

    /// <summary>Giá trị grip bị ép từ ngoài. Âm = không ép, đọc nút như bình thường.</summary>
    private float gripOverride = -1f;

    /// <summary>Đang bị ép quặp tay hay không.</summary>
    public bool HasGripOverride => gripOverride >= 0f;

    /// <summary>
    /// Ép bàn tay quặp lại như đang bóp grip dù người chơi không bóp nút nào.
    ///
    /// Dùng cho vật tự hiện ra trong tay (xem <c>DetonatorSpawner</c>): vật thì nằm trong tay mà
    /// bàn tay vẫn xòe ra vì nút grip đang nhả, trông như vật dính lơ lửng.
    ///
    /// Người chơi bóp grip thật thì vẫn tính giá trị lớn hơn, nên ép rồi không chặn gì cả.
    /// </summary>
    public void SetGripOverride(float value)
    {
        gripOverride = Mathf.Clamp01(value);
    }

    /// <summary>Bỏ ép, trả bàn tay về đọc nút grip.</summary>
    public void ClearGripOverride()
    {
        gripOverride = -1f;
    }

    void Update()
    {
        float trigger = triggerValue.action.ReadValue<float>();
        float grip = gripValue.action.ReadValue<float>();

        if (gripOverride >= 0f)
            grip = Mathf.Max(grip, gripOverride);

        handAnimator.SetFloat("Trigger", trigger);
        handAnimator.SetFloat("Grip", grip);
    }
}
