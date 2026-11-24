using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRSwat.Doors
{
    /// <summary>
    /// Ổ dán bom trên cánh cửa: người chơi thả <see cref="BreachCharge"/> lại gần là nó tự hít
    /// vào đúng vị trí, đúng hướng, và dính luôn theo cánh cửa nếu cửa xoay.
    ///
    /// Kế thừa <see cref="XRSocketInteractor"/> vì đây là cơ chế snap đã dùng khắp project
    /// (khoang đạn của súng, túi băng đạn, giá treo đồ): socket tự lo pose và tự giữ Rigidbody,
    /// và PhysicsHands đã cố tình bỏ qua socket khi nó đăng ký interactor nên không đụng độ bàn
    /// tay vật lý.
    ///
    /// Phải chặn theo type chứ không dùng Interaction Layer: project mới chỉ khai báo đúng một
    /// layer "Default", nên để nguyên thì ổ này hút cả súng, băng đạn lẫn kit phá khóa vào cửa.
    /// Thêm layer mới thì phải sửa project settings rồi nhớ set cho từng món; chặn bằng
    /// <see cref="CanSelect"/> gọn hơn và không quên được.
    /// </summary>
    [AddComponentMenu("VR SWAT/Charge Mount")]
    public class ChargeMount : XRSocketInteractor
    {
        [Header("Cửa")]
        [Tooltip("Cửa mà ổ này thuộc về. Để trống sẽ tự tìm ngược lên cha.")]
        [SerializeField] private DoorController door;

        /// <summary>Cửa sẽ bị phá khi quả bom trong ổ này nổ.</summary>
        public DoorController Door => door;

        protected override void Awake()
        {
            base.Awake();

            if (door == null)
                door = GetComponentInParent<DoorController>();

            if (door == null)
                Debug.LogWarning(
                    $"{name}: không tìm thấy DoorController ở cấp cha, bom dán vào đây sẽ nổ mà " +
                    "cửa không việc gì. Gán ô Door hoặc đặt ổ này làm con của cửa.", this);
        }

        public override bool CanHover(IXRHoverInteractable interactable)
        {
            return base.CanHover(interactable) && IsCharge(interactable);
        }

        public override bool CanSelect(IXRSelectInteractable interactable)
        {
            return base.CanSelect(interactable) && IsCharge(interactable);
        }

        private static bool IsCharge(IXRInteractable interactable)
        {
            return interactable.transform != null &&
                   interactable.transform.GetComponent<BreachCharge>() != null;
        }
    }
}
