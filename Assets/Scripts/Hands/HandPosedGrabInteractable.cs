using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// <see cref="XRGrabInteractable"/> chọn điểm bám theo BÀN TAY thay vì theo thứ tự grab.
///
/// <see cref="XRGrabInteractable"/> gốc chỉ có một <c>Attach Transform</c> dùng chung cho mọi
/// tay. Với dáng tay tạo sẵn thì không được: tay trái và tay phải ôm báng ở hai chỗ khác nhau,
/// mỗi bên cần một điểm bám riêng.
///
/// Cách làm là xếp lại hai ô điểm bám của XRI (<c>attachTransform</c> cho tay cầm chính,
/// <c>secondaryAttachTransform</c> cho tay thứ hai) mỗi lần danh sách tay đang cầm thay đổi,
/// thay vì override <c>GetAttachTransform</c>: hàm đó còn phải trả về visual attach transform
/// trong lúc dựng hình, mà mấy transform nội bộ đó là private nên override là mất tính năng.
///
/// Có một cái bẫy phải xử lý: XRI bật dynamic attach cho tay thứ hai BẤT KỂ Use Dynamic Attach
/// có tắt hay không, miễn là <c>secondaryAttachTransform</c> còn trống
/// (xem <c>XRGrabInteractable.GetAttachTransform</c>). Nên ô đó luôn được điền sẵn bằng điểm bám
/// của bên tay còn trống, kể cả khi mới có một tay đang cầm.
/// </summary>
[AddComponentMenu("VR SWAT/Hand Posed Grab Interactable")]
public class HandPosedGrabInteractable : XRGrabInteractable
{
    [Header("Điểm bám theo tay")]
    [Tooltip("Điểm bám khi cầm bằng tay trái. Chính chỗ này là chỗ bàn tay trái sẽ snap vào.")]
    [SerializeField] private Transform leftHandAttachTransform;

    [Tooltip("Điểm bám khi cầm bằng tay phải.")]
    [SerializeField] private Transform rightHandAttachTransform;

    private readonly List<IXRSelectInteractor> ordered = new List<IXRSelectInteractor>();

    private Transform fallbackAttachTransform;
    private bool capturedFallback;

    public Transform LeftHandAttachTransform
    {
        get => leftHandAttachTransform;
        set
        {
            leftHandAttachTransform = value;
            Arrange(null, null);
        }
    }

    public Transform RightHandAttachTransform
    {
        get => rightHandAttachTransform;
        set
        {
            rightHandAttachTransform = value;
            Arrange(null, null);
        }
    }

    public Transform GetHandAttachTransform(HandSide side)
    {
        return side == HandSide.Left ? leftHandAttachTransform : rightHandAttachTransform;
    }

    public void SetHandAttachTransform(HandSide side, Transform attach)
    {
        if (side == HandSide.Left)
            leftHandAttachTransform = attach;
        else
            rightHandAttachTransform = attach;
    }

    protected override void Awake()
    {
        CaptureFallback();
        base.Awake();
        Arrange(null, null);
    }

    protected override void OnSelectEntering(SelectEnterEventArgs args)
    {
        // Phải xếp TRƯỚC base: XRGrabInteractable dựng dynamic attach rồi chụp lại attach pose
        // ngay trong base.OnSelectEntering, nên lúc đó hai ô đã phải đúng. Interactor mới chưa
        // nằm trong interactorsSelecting ở thời điểm này nên phải truyền vào.
        Arrange(args.interactorObject, null);
        base.OnSelectEntering(args);
    }

    protected override void OnSelectEntered(SelectEnterEventArgs args)
    {
        base.OnSelectEntered(args);
        Arrange(null, null);
    }

    protected override void OnSelectExited(SelectExitEventArgs args)
    {
        base.OnSelectExited(args);

        // Tay chính vừa nhả: tay còn lại lên làm tay chính, nên hai ô phải đổi chỗ.
        Arrange(null, null);
    }

    private void CaptureFallback()
    {
        if (capturedFallback)
            return;

        fallbackAttachTransform = attachTransform;
        capturedFallback = true;
    }

    /// <summary>
    /// Xếp lại <c>attachTransform</c> / <c>secondaryAttachTransform</c> theo danh sách tay đang
    /// cầm, có tính thêm tay sắp vào (<paramref name="incoming"/>) hoặc sắp rời
    /// (<paramref name="leaving"/>).
    /// </summary>
    private void Arrange(IXRSelectInteractor incoming, IXRSelectInteractor leaving)
    {
        CaptureFallback();

        ordered.Clear();

        List<IXRSelectInteractor> selecting = interactorsSelecting;

        for (int i = 0; i < selecting.Count; i++)
        {
            IXRSelectInteractor interactor = selecting[i];

            if (interactor != null && interactor != leaving)
                ordered.Add(interactor);
        }

        if (incoming != null && !ordered.Contains(incoming))
            ordered.Add(incoming);

        IXRSelectInteractor primary = ordered.Count > 0 ? ordered[0] : null;
        IXRSelectInteractor secondary = ordered.Count > 1 ? ordered[1] : null;

        attachTransform = AttachFor(primary) ?? fallbackAttachTransform;

        Transform secondaryAttach = AttachFor(secondary) ?? FreeHandAttach(primary);
        secondaryAttachTransform = secondaryAttach != null ? secondaryAttach : attachTransform;
    }

    private Transform AttachFor(IXRSelectInteractor interactor)
    {
        if (interactor == null)
            return null;

        switch (interactor.handedness)
        {
            case InteractorHandedness.Left:
                return leftHandAttachTransform;

            case InteractorHandedness.Right:
                return rightHandAttachTransform;

            default:
                return null;
        }
    }

    /// <summary>Điểm bám của bên tay chưa cầm, để ô thứ hai không bao giờ trống.</summary>
    private Transform FreeHandAttach(IXRSelectInteractor primary)
    {
        if (primary != null)
        {
            switch (primary.handedness)
            {
                case InteractorHandedness.Left:
                    return rightHandAttachTransform != null ? rightHandAttachTransform : leftHandAttachTransform;

                case InteractorHandedness.Right:
                    return leftHandAttachTransform != null ? leftHandAttachTransform : rightHandAttachTransform;
            }
        }

        return rightHandAttachTransform != null ? rightHandAttachTransform : leftHandAttachTransform;
    }
}
