using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

/// <summary>
/// Đưa một prefab vào tay người chơi lúc runtime.
///
/// Gom vào một chỗ vì đã có ba nơi cần: kíp nổ tự hiện ra sau khi dán bom
/// (<c>DetonatorSpawner</c>), kit phá khóa và bom C4 do panel chọn cách mở cửa phát ra
/// (<c>DoorSpawnItemOption</c>). Phần khó nằm ở chỗ XRI đòi người chơi phải đang bóp grip thì vật
/// mới ở lại trong tay — xem <see cref="HandItemGrip"/>.
///
/// Class tĩnh, không cần đặt object nào trong scene. Danh sách bàn tay được cache lại và tự quét
/// lại khi model tay bị hủy (đổi giữa tay cầm và hand tracking).
/// </summary>
public static class HandItemSpawner
{
    private static readonly List<XRBaseInteractor> hands = new List<XRBaseInteractor>();
    private static Transform cachedRoot;

    /// <summary>
    /// Sinh <paramref name="prefab"/> ngay tại điểm cầm của <paramref name="hand"/> rồi nhét vào
    /// tay đó. Trả về instance vừa tạo, hoặc null nếu không đưa vào tay được (prefab thiếu
    /// XR Grab Interactable, hoặc Interaction Layer Mask không khớp với bàn tay).
    /// </summary>
    public static GameObject SpawnIntoHand(GameObject prefab, XRBaseInteractor hand,
        HandItemGrip.HoldMode mode, AudioClip spawnClip = null,
        float hapticAmplitude = 0.3f, float hapticDuration = 0.08f, bool closeHand = true)
    {
        if (prefab == null || hand == null)
            return null;

        Transform attach = hand.GetAttachTransform(null);
        Vector3 position = attach != null ? attach.position : hand.transform.position;
        Quaternion rotation = attach != null ? attach.rotation : hand.transform.rotation;

        GameObject instance = Object.Instantiate(prefab, position, rotation);
        instance.SetActive(true);

        XRBaseInteractable interactable = instance.GetComponent<XRBaseInteractable>();

        if (interactable == null)
        {
            Debug.LogWarning(
                $"HandItemSpawner: {prefab.name} thiếu XR Grab Interactable ở object gốc nên " +
                "không nhét vào tay được. Kiểm tra lại prefab.", prefab);
            Object.Destroy(instance);
            return null;
        }

        if (HandItemGrip.Attach(hand, interactable, mode, closeHand) == null)
        {
            Debug.LogWarning(
                $"HandItemSpawner: tay không nhận được {instance.name}. Kiểm tra Interaction " +
                "Layer Mask của XR Grab Interactable trên prefab.", prefab);
            Object.Destroy(instance);
            return null;
        }

        if (spawnClip != null)
            AudioSource.PlayClipAtPoint(spawnClip, position);

        SendHaptic(hand, hapticAmplitude, hapticDuration);
        return instance;
    }

    /// <summary>
    /// Nhả vật khỏi tay rồi hủy nó. Dùng thay cho <c>Destroy</c> với vật do
    /// <see cref="SpawnIntoHand"/> sinh ra, kẻo bàn tay mắc cờ manual và từ đó hít mọi thứ đi
    /// ngang qua.
    /// </summary>
    public static void DespawnFromHand(GameObject instance)
    {
        if (instance == null)
            return;

        HandItemGrip grip = HandItemGrip.Of(instance);

        if (grip != null)
            grip.Release();

        Object.Destroy(instance);
    }

    /// <summary>
    /// Lưới an toàn: tay còn mắc cờ manual interaction mà vật đã biến mất thì gỡ cờ ra. Không gỡ
    /// thì <c>isSelectActive</c> của tay đó cứ true và nó sẽ hít mọi vật đi ngang qua.
    ///
    /// Chỉ gọi được khi tay vẫn đang cầm một vật còn sống — <c>EndManualInteraction</c> chạy
    /// <c>SelectExit</c> mà XRI không kiểm tra null, nên gọi lúc vật đã bị hủy là đụng object
    /// chết. Rơi vào trường hợp đó thì chỉ còn cách báo để sửa chỗ gọi Destroy.
    /// </summary>
    public static void ForceEndManual(XRBaseInteractor hand)
    {
        if (hand == null || !hand.isPerformingManualInteraction)
            return;

        if (hand.hasSelection)
        {
            hand.EndManualInteraction();
            return;
        }

        Debug.LogWarning(
            $"HandItemSpawner: {hand.name} còn mắc cờ manual interaction mà vật đang cầm đã bị " +
            "hủy, nên tay này sẽ hít mọi vật đi ngang qua. Dọn vật bằng " +
            "HandItemSpawner.DespawnFromHand() thay vì Destroy().", hand);
    }

    /// <summary>
    /// Tay rảnh để nhận đồ: ưu tiên <paramref name="preferred"/>, không rảnh thì lấy tay rảnh
    /// còn lại. Null = hai tay đang đầy.
    /// </summary>
    public static XRBaseInteractor PickFreeHand(Transform handsRoot, XRBaseInteractor preferred = null)
    {
        if (IsFree(preferred))
            return preferred;

        IReadOnlyList<XRBaseInteractor> all = Hands(handsRoot);

        for (int i = 0; i < all.Count; i++)
        {
            if (IsFree(all[i]))
                return all[i];
        }

        return null;
    }

    /// <summary>Tay này đang không cầm gì.</summary>
    public static bool IsFree(XRBaseInteractor hand)
    {
        return hand != null && hand.isActiveAndEnabled && !hand.hasSelection;
    }

    /// <summary>
    /// Interactor của hai bàn tay dưới <paramref name="handsRoot"/>. Để trống thì tự tìm
    /// <see cref="XROrigin"/> trong scene.
    /// </summary>
    public static IReadOnlyList<XRBaseInteractor> Hands(Transform handsRoot)
    {
        // Lọc rụng cái đã bị hủy trước, rồi mới quyết định có phải quét lại scene không.
        for (int i = hands.Count - 1; i >= 0; i--)
        {
            if (hands[i] == null)
                hands.RemoveAt(i);
        }

        if (hands.Count > 0 && (handsRoot == null || handsRoot == cachedRoot))
            return hands;

        Transform root = handsRoot;

        if (root == null)
        {
            XROrigin origin = Object.FindFirstObjectByType<XROrigin>();
            root = origin != null ? origin.transform : null;
        }

        if (root == null)
            return hands;

        hands.Clear();
        cachedRoot = root;

        foreach (XRBaseInteractor interactor in root.GetComponentsInChildren<XRBaseInteractor>(true))
        {
            if (!(interactor is IXRSelectInteractor))
                continue;

            // Socket (túi đồ, ổ dán bom, giá treo) cũng select vật nhưng không phải bàn tay.
            if (interactor is XRSocketInteractor)
                continue;

            hands.Add(interactor);
        }

        return hands;
    }

    /// <summary>Rung tay, để người chơi biết tay mình vừa có đồ.</summary>
    public static void SendHaptic(XRBaseInteractor hand, float amplitude, float duration)
    {
        if (amplitude <= 0f || hand == null)
            return;

        HapticImpulsePlayer player = hand.transform.GetComponentInParent<HapticImpulsePlayer>();

        if (player != null)
            player.SendHapticImpulse(amplitude, duration);
    }
}
