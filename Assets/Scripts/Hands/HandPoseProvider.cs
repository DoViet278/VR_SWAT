using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Gắn lên vật cầm được. Nghe select của vật rồi bảo <see cref="HandPoseDriver"/> của bàn tay
/// tương ứng áp dáng đã tạo sẵn cho vật này.
///
/// Mỗi vật có tới bốn dáng: tay cầm chính trái/phải, và tay đỡ trái/phải cho súng hai tay. Dáng
/// tay đỡ để trống thì bàn tay thứ hai dùng luôn dáng tay cầm chính.
///
/// Tay nào là tay đỡ thì quyết định theo THỨ TỰ trong <c>interactorsSelecting</c>: phần tử 0 là
/// tay cầm chính. Nên khi tay chính nhả ra và tay còn lại lên làm tay chính, dáng được tính lại.
///
/// Không tự tìm bàn tay theo cha của interactor được: <see cref="PhysicsHands"/> tháo model tay
/// khỏi Controller lúc chạy, nên bàn tay không còn nằm dưới interactor nữa. Driver tự ghi danh
/// vào một sổ tĩnh và tìm theo bên tay.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("VR SWAT/Hand Pose Provider")]
public class HandPoseProvider : MonoBehaviour
{
    [Header("Dáng tay cầm chính")]
    [SerializeField] private HandPose leftHandPose;
    [SerializeField] private HandPose rightHandPose;

    [Header("Dáng tay đỡ (bàn tay thứ hai)")]
    [Tooltip("Để trống thì bàn tay thứ hai dùng luôn dáng tay cầm chính.")]
    [SerializeField] private HandPose leftSupportPose;
    [SerializeField] private HandPose rightSupportPose;

    [Header("Blend")]
    [SerializeField] private float blendInTime = 0.08f;
    [SerializeField] private float blendOutTime = 0.08f;

    [Header("Liên kết")]
    [Tooltip("Để trống sẽ tự tìm trên cùng GameObject rồi tới cha.")]
    [SerializeField] private XRBaseInteractable interactable;

    private readonly List<HandPoseDriver> posedDrivers = new List<HandPoseDriver>();
    private readonly List<HandPoseDriver> scratchDrivers = new List<HandPoseDriver>();

    private bool subscribed;

    private void OnEnable()
    {
        if (interactable == null)
            interactable = GetComponent<XRBaseInteractable>();

        if (interactable == null)
            interactable = GetComponentInParent<XRBaseInteractable>();

        if (interactable == null)
        {
            Debug.LogWarning(nameof(HandPoseProvider) + ": không tìm thấy interactable nào, tự tắt.", this);
            enabled = false;
            return;
        }

        interactable.selectEntered.AddListener(OnSelectChanged);
        interactable.selectExited.AddListener(OnSelectChanged);
        subscribed = true;

        // Vật có thể đang được cầm sẵn (component vừa được bật lại).
        Refresh();
    }

    private void OnDisable()
    {
        if (subscribed && interactable != null)
        {
            interactable.selectEntered.RemoveListener(OnSelectChanged);
            interactable.selectExited.RemoveListener(OnSelectChanged);
        }

        subscribed = false;
        ReleaseAll();
    }

    private void OnSelectChanged(SelectEnterEventArgs args)
    {
        Refresh();
    }

    private void OnSelectChanged(SelectExitEventArgs args)
    {
        Refresh();
    }

    /// <summary>Tính lại dáng cho mọi bàn tay đang cầm vật này.</summary>
    public void Refresh()
    {
        if (interactable == null)
            return;

        scratchDrivers.Clear();

        List<IXRSelectInteractor> selecting = interactable.interactorsSelecting;

        for (int i = 0; i < selecting.Count; i++)
        {
            if (!TryGetSide(selecting[i], out HandSide side))
                continue;

            HandPoseDriver driver = HandPoseDriver.Find(side);

            if (driver == null || scratchDrivers.Contains(driver))
                continue;

            HandPose pose = GetPose(side, i > 0);

            if (pose == null)
                continue;

            if (driver.CurrentPose != pose)
                driver.ApplyPose(pose, blendInTime);

            scratchDrivers.Add(driver);
        }

        // Tay vừa rời khỏi vật (hoặc vật đổi sang dáng khác cho tay đó) thì trả tay về tự do.
        for (int i = posedDrivers.Count - 1; i >= 0; i--)
        {
            HandPoseDriver driver = posedDrivers[i];

            if (driver != null && !scratchDrivers.Contains(driver))
                driver.ClearPose(blendOutTime);
        }

        posedDrivers.Clear();
        posedDrivers.AddRange(scratchDrivers);
    }

    private void ReleaseAll()
    {
        foreach (HandPoseDriver driver in posedDrivers)
        {
            if (driver != null)
                driver.ClearPose(blendOutTime);
        }

        posedDrivers.Clear();
    }

    /// <summary>Dáng cho một bên tay. <paramref name="support"/> = bàn tay thứ hai.</summary>
    public HandPose GetPose(HandSide side, bool support)
    {
        if (support)
        {
            HandPose supportPose = side == HandSide.Left ? leftSupportPose : rightSupportPose;

            if (supportPose != null)
                return supportPose;
        }

        return side == HandSide.Left ? leftHandPose : rightHandPose;
    }

    /// <summary>Gán dáng vào đúng ô. Dùng bởi công cụ tạo pose.</summary>
    public void SetPose(HandSide side, bool support, HandPose pose)
    {
        if (support)
        {
            if (side == HandSide.Left)
                leftSupportPose = pose;
            else
                rightSupportPose = pose;

            return;
        }

        if (side == HandSide.Left)
            leftHandPose = pose;
        else
            rightHandPose = pose;
    }

    /// <summary>Có dáng nào được gán chưa.</summary>
    public bool HasAnyPose =>
        leftHandPose != null || rightHandPose != null ||
        leftSupportPose != null || rightSupportPose != null;

    private static bool TryGetSide(IXRSelectInteractor interactor, out HandSide side)
    {
        side = HandSide.Left;

        if (interactor == null)
            return false;

        switch (interactor.handedness)
        {
            case InteractorHandedness.Left:
                side = HandSide.Left;
                return true;

            case InteractorHandedness.Right:
                side = HandSide.Right;
                return true;

            default:
                return false;
        }
    }
}
