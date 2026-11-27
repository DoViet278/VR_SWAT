using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Giữ một vật phẩm vừa được nhét vào tay người chơi, và quyết định lúc nào thì nhả ra.
///
/// Component này do <see cref="HandItemSpawner"/> gắn lúc runtime lên chính vật vừa spawn, không
/// kéo tay vào prefab.
///
/// Vì sao phải có: cái "đang cầm" của XRI chỉ là quan hệ interactor ↔ interactable, mà mỗi frame
/// <c>XRInteractionManager</c> lại xét lại qua <c>interactor.isSelectActive</c> — tức là người
/// chơi phải đang bóp nút grip. Vật tự hiện ra trong tay thì không ai bóp grip cả, nên nếu chỉ
/// gọi <c>SelectEnter</c> thì frame sau nó rơi ngay. Phải dùng <c>StartManualInteraction</c>,
/// cái này bật <c>isSelectActive</c> cho tới khi <c>EndManualInteraction</c>.
///
/// Hai kiểu cầm, xem <see cref="HoldMode"/>.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("")]   // chỉ gắn bằng code, không cho thêm tay trong Inspector
public class HandItemGrip : MonoBehaviour
{
    public enum HoldMode
    {
        /// <summary>
        /// Dính tay cho tới khi gọi <see cref="Release"/>. Bóp grip hay xòe tay đều không rơi.
        /// Dùng cho thứ không được phép đánh rơi, ví dụ kíp nổ.
        /// </summary>
        StickUntilReleased,

        /// <summary>
        /// Dính tay tạm thời. Người chơi bóp grip một cái là chuyển sang nắm bình thường của
        /// XRI, từ đó nhả grip là rơi như mọi vật khác. Dùng cho đồ phải thả ra mới xài được:
        /// kit phá khóa (đưa vào ổ khóa), bom C4 (dán lên cửa).
        /// </summary>
        HandOverOnGrip,
    }

    private XRBaseInteractor hand;
    private XRBaseInteractable interactable;
    private HandPoseDriver posedHand;
    private HoldMode mode;

    /// <summary>
    /// Người chơi đang bóp sẵn grip lúc vật hiện ra thì phải chờ họ nhả ra đã, không thì cú bóp
    /// đang giữ bị tính luôn là "nhận đồ" ngay frame đầu.
    /// </summary>
    private bool waitForGripRelease;

    /// <summary>Tay đang cầm vật này. Null nghĩa là đã nhả.</summary>
    public XRBaseInteractor Hand => hand;

    public HoldMode Mode => mode;

    /// <summary>Lấy component này trên một vật, hoặc null nếu vật đó không do spawner đưa vào tay.</summary>
    public static HandItemGrip Of(GameObject instance)
    {
        return instance != null ? instance.GetComponent<HandItemGrip>() : null;
    }

    /// <summary>
    /// Nhét <paramref name="interactable"/> vào <paramref name="hand"/>. Trả về component vừa
    /// gắn, hoặc null nếu tay không nhận (thường do Interaction Layer Mask không khớp).
    /// </summary>
    public static HandItemGrip Attach(XRBaseInteractor hand, XRBaseInteractable interactable,
        HoldMode mode, bool closeHand)
    {
        if (hand == null || interactable == null)
            return null;

        if (!(interactable is IXRSelectInteractable select))
            return null;

        hand.StartManualInteraction(select);

        if (!hand.IsSelecting(select))
            return null;     // StartManualInteraction đã log lý do

        HandItemGrip grip = interactable.gameObject.AddComponent<HandItemGrip>();
        grip.hand = hand;
        grip.interactable = interactable;
        grip.mode = mode;
        grip.waitForGripRelease = ReadGrip(hand);

        if (closeHand)
            grip.CloseHand();

        return grip;
    }

    /// <summary>
    /// Nhả vật khỏi tay nhưng KHÔNG hủy nó. Gọi cái này trước khi Destroy, đừng Destroy thẳng:
    /// <c>EndManualInteraction</c> chạy <c>SelectExit</c> lên interactable mà XRI không kiểm tra
    /// gì, nên gọi lúc vật đã bị hủy là đụng vào object chết.
    /// </summary>
    public void Release()
    {
        XRBaseInteractor releasing = hand;
        XRBaseInteractable held = interactable;

        OpenHand();
        hand = null;
        interactable = null;

        if (releasing == null || !releasing.isPerformingManualInteraction)
            return;

        if (held != null && releasing.IsSelecting((IXRSelectInteractable)held))
            releasing.EndManualInteraction();
        else
            Debug.LogWarning(
                $"{name}: vật bị hủy trước khi nhả tay nên không gọi được EndManualInteraction. " +
                "Gọi HandItemGrip.Release() trước rồi mới Destroy.", this);
    }

    private void Update()
    {
        // Vật hoặc tay biến mất giữa chừng (đổi chế độ nhập liệu là model tay bị hủy và dựng lại).
        if (hand == null || interactable == null)
        {
            enabled = false;
            return;
        }

        // Ai đó đã kết thúc manual interaction từ bên ngoài (vd. ChargeMount hút quả bom vào ổ).
        if (!hand.isPerformingManualInteraction)
        {
            OpenHand();
            hand = null;
            interactable = null;
            Destroy(this);
            return;
        }

        if (mode != HoldMode.HandOverOnGrip)
            return;

        bool grip = ReadGrip(hand);

        if (waitForGripRelease)
        {
            if (!grip)
                waitForGripRelease = false;

            return;
        }

        if (grip)
            HandOverToPlayer();
    }

    private void OnDestroy()
    {
        // Không gọi Release() ở đây: OnDestroy chạy khi vật đã đang bị hủy, đúng cái trường hợp
        // EndManualInteraction không được phép chạm vào. Chỉ trả bàn tay về bình thường.
        OpenHand();
    }

    /// <summary>
    /// Chuyển từ "dính tay" sang nắm bình thường: kết thúc manual interaction rồi chủ động
    /// SelectEnter lại. Người chơi đang bóp grip nên <c>isSelectActive</c> đã true, XRI sẽ giữ
    /// vật đó ở các frame sau, và nhả grip là rơi đúng như mọi vật cầm tay khác.
    /// </summary>
    private void HandOverToPlayer()
    {
        XRBaseInteractor releasing = hand;
        XRBaseInteractable held = interactable;
        var manager = held.interactionManager;

        Release();

        if (manager != null &&
            releasing is IXRSelectInteractor selector &&
            held is IXRSelectInteractable select &&
            !held.isSelected)
        {
            manager.SelectEnter(selector, select);
        }

        Destroy(this);
    }

    /// <summary>
    /// Quặp bàn tay lại quanh vật.
    ///
    /// Ngón tay của model chạy theo nút grip qua <see cref="AnimateHandOnInput"/>, mà vật này tự
    /// hiện ra chứ không ai bóp grip, nên không ép thì vật nằm trong một bàn tay xòe.
    ///
    /// Prefab có <see cref="HandPoseProvider"/> kèm dáng tạo sẵn thì dáng đó vẫn ăn và đè lên
    /// cái này — đẹp hơn, nên ép grip chỉ là mức tối thiểu cho prefab chưa có dáng.
    /// </summary>
    private void CloseHand()
    {
        HandSide side;

        switch (hand.handedness)
        {
            case InteractorHandedness.Left:
                side = HandSide.Left;
                break;

            case InteractorHandedness.Right:
                side = HandSide.Right;
                break;

            default:
                return;     // interactor không khai bên tay nào thì không biết quặp tay nào
        }

        posedHand = HandPoseDriver.Find(side);

        if (posedHand != null)
            posedHand.SetGripOverride(1f);
    }

    private void OpenHand()
    {
        if (posedHand == null)
            return;

        posedHand.ClearGripOverride();
        posedHand = null;
    }

    /// <summary>Người chơi có đang bóp nút grip của tay này không.</summary>
    private static bool ReadGrip(XRBaseInteractor hand)
    {
        // isSelectActive không dùng được: trong lúc manual interaction nó luôn trả true. Phải đọc
        // thẳng nút.
        return hand is XRBaseInputInteractor input && input.selectInput.ReadIsPerformed();
    }
}
