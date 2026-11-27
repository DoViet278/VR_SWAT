using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRSwat.Doors
{
    /// <summary>Lọc option theo trạng thái cửa. Dùng mặt nạ để một option hợp lệ ở nhiều trạng thái.</summary>
    [System.Flags]
    public enum DoorStateFilter
    {
        None = 0,
        Open = 1 << 0,
        Closed = 1 << 1,
        Locked = 1 << 2,
        Breached = 1 << 3,
        Any = Open | Closed | Locked | Breached,
    }

    /// <summary>Mọi thứ một option cần biết lúc được chọn. Struct để khỏi sinh rác mỗi lần mở panel.</summary>
    public readonly struct DoorActionContext
    {
        public readonly DoorActionMenu Menu;
        public readonly DoorController Door;
        public readonly XRBaseInteractor Hand;
        public readonly Transform HandsRoot;

        public DoorActionContext(DoorActionMenu menu, XRBaseInteractor hand, Transform handsRoot)
        {
            Menu = menu;
            Door = menu != null ? menu.Door : null;
            Hand = hand;
            HandsRoot = handsRoot;
        }

        /// <summary>Điểm đại diện cho người chơi, dùng để biết cửa phải bật về phía nào.</summary>
        public Vector3 PlayerPoint
        {
            get
            {
                if (Hand != null)
                    return Hand.transform.position;

                Camera cam = Camera.main;
                return cam != null ? cam.transform.position : Vector3.zero;
            }
        }
    }

    /// <summary>
    /// Một dòng trong panel chọn cách mở cửa (xem <see cref="DoorActionMenu"/>).
    ///
    /// Mỗi option là một asset riêng chứ không phải một nhánh trong switch: thêm cách mở cửa mới
    /// (bang, gas, soi gương, ra lệnh cho đội) chỉ là tạo thêm asset và kéo vào danh sách, còn
    /// thứ tự và chữ hiển thị thì chỉnh thẳng trong Inspector.
    ///
    /// Option không dùng được ở trạng thái hiện tại thì mặc định vẫn hiện nhưng xám lại, để người
    /// chơi biết cửa này CÓ cách đó, chỉ là chưa dùng được. Muốn giấu hẳn thì bật
    /// <c>hideWhenUnusable</c>.
    /// </summary>
    public abstract class DoorActionOption : ScriptableObject
    {
        [Header("Hiển thị")]
        [Tooltip("Chữ hiện trên panel. Viết hoa cho giống SWAT 4.")]
        [SerializeField] private string label = "HÀNH ĐỘNG";

        [Tooltip("Icon ô vuông bên trái dòng. Không bắt buộc.")]
        [SerializeField] private Sprite icon;

        [Tooltip("Màu của ô vuông bên trái, để phân nhóm option cho dễ nhìn.")]
        [SerializeField] private Color tint = new Color(0.95f, 0.78f, 0.17f);

        [Header("Khi nào dùng được")]
        [Tooltip("Trạng thái cửa mà option này dùng được.")]
        [SerializeField] private DoorStateFilter usableStates = DoorStateFilter.Any;

        [Tooltip("Bật: không dùng được thì giấu hẳn dòng này. Tắt: vẫn hiện nhưng xám và bấm " +
                 "không ăn, để người chơi biết cửa này có cách đó.")]
        [SerializeField] private bool hideWhenUnusable;

        public string Label => label;

        public Sprite Icon => icon;

        public Color Tint => tint;

        public bool HideWhenUnusable => hideWhenUnusable;

        /// <summary>Option này có dùng được ngay bây giờ không. Lớp con nhớ gọi <c>base</c>.</summary>
        public virtual bool CanUse(in DoorActionContext ctx)
        {
            return ctx.Door != null && (usableStates & ToFilter(ctx.Door.State)) != 0;
        }

        /// <summary>Người chơi nhả nút trên dòng này. Chỉ gọi khi <see cref="CanUse"/> trả true.</summary>
        public abstract void Execute(in DoorActionContext ctx);

        public static DoorStateFilter ToFilter(DoorState state)
        {
            switch (state)
            {
                case DoorState.Open: return DoorStateFilter.Open;
                case DoorState.Closed: return DoorStateFilter.Closed;
                case DoorState.Locked: return DoorStateFilter.Locked;
                case DoorState.Breached: return DoorStateFilter.Breached;
                default: return DoorStateFilter.None;
            }
        }
    }
}
