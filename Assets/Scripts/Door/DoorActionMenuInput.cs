using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRSwat.Doors
{
    /// <summary>
    /// Điều khiển panel chọn cách mở cửa. Đặt MỘT cái trên XR Origin.
    ///
    /// Luồng chơi:
    ///
    /// 1. Lại gần cửa → ổ khóa sáng lên, báo "chỗ này tương tác được"
    ///    (<see cref="DoorActionMenu.SetPrompt"/>).
    /// 2. GIỮ nút phụ (B/Y) → panel hiện ra cạnh ổ khóa, đứng yên tại chỗ.
    /// 3. Kéo tay lên xuống → dòng đang sáng chạy theo, kèm một nhịp rung nhẹ.
    /// 4. NHẢ nút → thực thi dòng đang sáng. Dòng trên cùng luôn là THOÁT, và đó cũng là dòng
    ///    sáng sẵn lúc panel vừa hiện, nên giữ rồi nhả mà không kéo gì = hủy.
    ///
    /// Hai cách chọn, xem <see cref="SelectMode"/>. Mặc định là kéo tay vì nó không phụ thuộc
    /// vào tia của interactor — tắt Far Casting trên NearFarInteractor thì vẫn chọn được.
    ///
    /// Vì sao input nằm ở đây chứ không phải trên từng cửa: cả màn có thể có vài chục cửa, để
    /// mỗi cửa tự poll nút là vài chục <c>Update</c> làm cùng một việc. Cửa chỉ giữ phần cấu
    /// hình (xem <see cref="DoorActionMenu"/>), còn vòng lặp thì một chỗ lo hết.
    ///
    /// Nút phụ dùng mặc định là <c>secondaryButton</c> của tay cầm, không cần bind gì thêm. Muốn
    /// đổi nút thì gán action vào hai ô Left / Right Menu Button.
    /// </summary>
    [AddComponentMenu("VR SWAT/Door Action Menu Input")]
    [DisallowMultipleComponent]
    public class DoorActionMenuInput : MonoBehaviour
    {
        public enum SelectMode
        {
            /// <summary>
            /// Kéo bàn tay lên xuống, dòng sáng chạy theo độ cao của tay so với lúc bấm nút.
            /// Không cần tia nên tắt Far Casting vẫn dùng được, và không phải ngắm cho trúng.
            /// </summary>
            HandMovement,

            /// <summary>
            /// Chĩa tay vào panel, dòng nào bị tia xuyên qua thì sáng. Chỉ nên dùng khi
            /// NearFarInteractor còn BẬT Far Casting — tắt đi là mất đường tia nhìn thấy được,
            /// người chơi phải ngắm mù.
            /// </summary>
            HandRay,
        }

        public enum PanelPlacement
        {
            /// <summary>
            /// Panel hiện ngay giữa tầm nhìn, cách mắt một khoảng cố định. Luôn thấy trọn vẹn
            /// và luôn cùng một cỡ, bất kể người chơi đứng sát cửa hay đứng lùi.
            /// </summary>
            InFrontOfPlayer,

            /// <summary>
            /// Panel neo ở ổ khóa, kiểu nhãn dán lên cửa. Đẹp hơn nhưng đứng sát cửa là panel
            /// áp vào mặt và lệch ra khỏi tầm nhìn, nên có chặn khoảng cách tối thiểu bên dưới.
            /// </summary>
            AtDoor,
        }

        [Header("Rig")]
        [Tooltip("Gốc rig để tìm interactor hai bàn tay. Để trống sẽ tự tìm XR Origin trong scene.")]
        [SerializeField] private Transform handsRoot;

        [Tooltip("Đầu người chơi, để panel quay mặt về. Để trống thì lấy camera của XR Origin.")]
        [SerializeField] private Transform head;

        [Header("Nút mở menu")]
        [Tooltip("Action nút phụ tay trái. Để trống thì tự bind <XRController>{LeftHand}/secondaryButton.")]
        [SerializeField] private InputActionProperty leftMenuButton;

        [Tooltip("Action nút phụ tay phải. Để trống thì tự bind <XRController>{RightHand}/secondaryButton.")]
        [SerializeField] private InputActionProperty rightMenuButton;

        [Tooltip("Cho phép giữ phím Tab để mở menu (test trên màn hình, không cần đeo kính).")]
        [SerializeField] private bool keyboardTab = true;

        [Header("Cách chọn")]
        [Tooltip("Hand Movement: kéo tay lên xuống, không cần tia — dùng cái này nếu đã tắt Far " +
                 "Casting. Hand Ray: chĩa tay vào panel, chỉ hợp khi còn bật Far Casting.")]
        [SerializeField] private SelectMode selectMode = SelectMode.HandMovement;

        [Tooltip("Chỉ dùng cho Hand Movement: kéo tay xuống bao nhiêu mét thì nhảy một dòng. " +
                 "Nhỏ quá thì dòng sáng nhảy loạn vì tay rung, lớn quá thì phải vươn tay mỏi.")]
        [SerializeField] private float metersPerRow = 0.045f;

        [Header("Tầm với")]
        [Tooltip("Tay cách ổ khóa gần hơn mức này thì ổ khóa sáng lên.")]
        [SerializeField] private float promptRange = 1.1f;

        [Tooltip("Tay cách ổ khóa gần hơn mức này thì giữ nút mới mở được panel. Nên để bằng " +
                 "hoặc nhỏ hơn Prompt Range.")]
        [SerializeField] private float openRange = 1.1f;

        [Tooltip("Đang mở panel mà tay đi ra xa hơn mức này thì panel tự đóng, coi như hủy.")]
        [SerializeField] private float keepOpenRange = 2.5f;

        [Header("Panel")]
        [Tooltip("Panel dùng chung cho mọi cửa. Dựng bằng Tools > VR SWAT > Create Door Action Menu.")]
        [SerializeField] private DoorActionMenuView view;

        [Tooltip("In Front Of Player: panel hiện giữa tầm nhìn, luôn thấy trọn vẹn — nên dùng " +
                 "cái này. At Door: panel dán ở ổ khóa, đẹp hơn nhưng đứng sát cửa là bị áp mặt.")]
        [SerializeField] private PanelPlacement placement = PanelPlacement.InFrontOfPlayer;

        [Tooltip("Chỉ cho In Front Of Player: panel cách mắt bao nhiêu mét. Dưới 0.4 m là mắt " +
                 "phải hội tụ quá gần, đeo lâu sẽ mỏi — đừng hạ thấp hơn nữa. Lưu ý: panel tự " +
                 "co giãn theo Max Width Degrees bên View, nên kéo gần KHÔNG làm nó to lên, chỉ " +
                 "làm nó nằm gần tay hơn.")]
        [SerializeField] private float panelDistance = 0.4f;

        [Tooltip("Chỉ cho In Front Of Player: hạ panel xuống dưới tầm mắt bao nhiêu mét, để nó " +
                 "không che mất cánh cửa. Số âm là hạ xuống.")]
        [SerializeField] private float panelHeightOffset = -0.05f;

        [Tooltip("Chỉ cho At Door: panel không bao giờ được lại gần mắt hơn mức này. Đứng sát " +
                 "cửa thì panel tự lùi ra dọc hướng nhìn thay vì áp vào mặt.")]
        [SerializeField] private float minPanelDistance = 0.5f;

        [Tooltip("Danh sách option mặc định, dùng cho mọi cửa không tự khai danh sách riêng.")]
        [SerializeField] private List<DoorActionOption> defaultOptions = new List<DoorActionOption>();

        [Tooltip("Chữ của dòng thoát, luôn nằm trên cùng giống SWAT 4.")]
        [SerializeField] private string exitLabel = "THOÁT MENU";

        [Tooltip("Màu ô vuông của dòng thoát.")]
        [SerializeField] private Color exitTint = new Color(0.62f, 0.66f, 0.62f);

        [Tooltip("Hiện tên cửa / trạng thái ở đầu panel.")]
        [SerializeField] private bool showHeading = true;

        [Header("Phản hồi")]
        [Tooltip("Rung nhẹ mỗi khi dòng đang trỏ tới đổi. 0 = tắt.")]
        [SerializeField, Range(0f, 1f)] private float tickHaptic = 0.15f;

        [SerializeField] private float tickHapticDuration = 0.03f;

        [Tooltip("Tiếng lúc panel hiện ra. Không bắt buộc.")]
        [SerializeField] private AudioSource audioSource;

        [SerializeField] private AudioClip openClip, tickClip, confirmClip, cancelClip;

        private readonly List<DoorActionMenuView.Entry> entries = new List<DoorActionMenuView.Entry>();

        /// <summary>Option tương ứng từng dòng. Phần tử 0 là dòng thoát nên để null.</summary>
        private readonly List<DoorActionOption> rowOptions = new List<DoorActionOption>();

        private InputAction fallbackLeft, fallbackRight;
        private DoorActionMenu prompted;
        private DoorActionMenu openMenu;
        private XRBaseInteractor openHand;
        private int lastHighlight = -1;

        /// <summary>Độ cao bàn tay lúc bấm nút, làm mốc 0 cho kiểu chọn Hand Movement.</summary>
        private float openHandHeight;

        private void OnEnable()
        {
            leftMenuButton.action?.Enable();
            rightMenuButton.action?.Enable();

            if (!HasBinding(leftMenuButton))
                fallbackLeft = MakeFallback("DoorMenuLeft", "<XRController>{LeftHand}/secondaryButton");

            if (!HasBinding(rightMenuButton))
                fallbackRight = MakeFallback("DoorMenuRight", "<XRController>{RightHand}/secondaryButton");
        }

        private void OnDisable()
        {
            CloseMenu(false);
            SetPrompt(null);

            fallbackLeft?.Disable();
            fallbackLeft?.Dispose();
            fallbackLeft = null;

            fallbackRight?.Disable();
            fallbackRight?.Dispose();
            fallbackRight = null;
        }

        private void Update()
        {
            if (openMenu != null)
                TickOpen();
            else
                TickClosed();
        }

        // ===== chưa mở panel =====

        private void TickClosed()
        {
            IReadOnlyList<XRBaseInteractor> hands = HandItemSpawner.Hands(handsRoot);

            DoorActionMenu nearest = null;
            XRBaseInteractor nearestHand = null;
            float nearestDistance = float.MaxValue;

            for (int h = 0; h < hands.Count; h++)
            {
                XRBaseInteractor hand = hands[h];

                if (hand == null || !hand.isActiveAndEnabled)
                    continue;

                DoorActionMenu menu = NearestMenu(hand.transform.position, promptRange, out float distance);

                if (menu != null && distance < nearestDistance)
                {
                    nearest = menu;
                    nearestHand = hand;
                    nearestDistance = distance;
                }
            }

            SetPrompt(nearest);

            if (nearest == null || nearestDistance > openRange)
                return;

            if (WasPressedThisFrame(nearestHand))
                OpenMenu(nearest, nearestHand);
        }

        /// <summary>Cửa có panel gần <paramref name="point"/> nhất trong tầm, hoặc null.</summary>
        private static DoorActionMenu NearestMenu(Vector3 point, float range, out float distance)
        {
            DoorActionMenu best = null;
            distance = float.MaxValue;

            IReadOnlyList<DoorActionMenu> menus = DoorActionMenu.All;

            for (int i = 0; i < menus.Count; i++)
            {
                DoorActionMenu menu = menus[i];

                if (menu == null || menu.Door == null)
                    continue;

                float d = Vector3.Distance(point, menu.Anchor.position);

                if (d <= range && d < distance)
                {
                    distance = d;
                    best = menu;
                }
            }

            return best;
        }

        private void SetPrompt(DoorActionMenu menu)
        {
            if (prompted == menu)
                return;

            if (prompted != null)
                prompted.SetPrompt(false);

            prompted = menu;

            if (prompted != null)
                prompted.SetPrompt(true);
        }

        // ===== đang mở panel =====

        private void OpenMenu(DoorActionMenu menu, XRBaseInteractor hand)
        {
            if (view == null)
            {
                Debug.LogWarning(
                    $"{name}: chưa gán Door Action Menu View nên giữ nút không hiện gì. Dựng " +
                    "panel bằng Tools > VR SWAT > Create Door Action Menu rồi kéo vào ô View.", this);
                enabled = false;
                return;
            }

            BuildEntries(menu, hand);

            if (entries.Count <= 1)
                return;     // chỉ còn mỗi dòng thoát thì mở ra cũng để làm gì

            openMenu = menu;
            openHand = hand;
            openHandHeight = hand.transform.position.y;
            lastHighlight = -1;
            DoorActionMenu.Open = menu;

            Transform h = Head;
            view.Show(PanelPointFor(menu, h, hand), h,
                entries, showHeading ? Heading(menu.Door) : null);

            // Sáng sẵn dòng THOÁT: giữ nút rồi nhả mà không kéo gì thì không có gì xảy ra.
            if (selectMode == SelectMode.HandMovement)
            {
                view.SetHighlight(0);
                lastHighlight = 0;
            }

            Play(openClip);
            HandItemSpawner.SendHaptic(hand, tickHaptic, tickHapticDuration);
        }

        private void TickOpen()
        {
            // Cửa hoặc tay biến mất giữa chừng.
            if (openMenu == null || openMenu.Door == null || openHand == null ||
                !openHand.isActiveAndEnabled)
            {
                CloseMenu(false);
                return;
            }

            // Đi ra xa quá thì coi như đổi ý.
            if (Vector3.Distance(openHand.transform.position, openMenu.Anchor.position) > keepOpenRange)
            {
                CloseMenu(false);
                return;
            }

            bool hit = selectMode == SelectMode.HandMovement
                ? PickByHandHeight(out int index)
                : view.TryPick(HandRay(openHand), out index);

            if (hit)
            {
                if (index != lastHighlight)
                {
                    lastHighlight = index;
                    HandItemSpawner.SendHaptic(openHand, tickHaptic, tickHapticDuration);
                    Play(tickClip);
                }
            }
            else if (lastHighlight != -1)
            {
                lastHighlight = -1;
            }

            if (!IsHeld(openHand))
                Commit();
        }

        /// <summary>
        /// Chỗ panel hiện ra.
        ///
        /// Mặc định là trước mặt người chơi chứ không phải ở ổ khóa, vì lúc mở menu người chơi
        /// đang đứng sát cửa: neo ở ổ khóa thì panel cách mắt chừng một gang tay, vừa tràn ra
        /// ngoài tầm nhìn vừa bị nhìn xiên nên chữ méo. Thu nhỏ panel không cứu được — nó vẫn
        /// lệch sang một bên và vẫn xiên.
        /// </summary>
        private Vector3 PanelPointFor(DoorActionMenu menu, Transform head, XRBaseInteractor hand)
        {
            if (head == null)
                return menu.PanelPoint(hand != null ? hand.transform.position : transform.position);

            if (placement == PanelPlacement.InFrontOfPlayer)
            {
                return head.position
                       + head.forward * Mathf.Max(0.2f, panelDistance)
                       + Vector3.up * panelHeightOffset;
            }

            Vector3 point = menu.PanelPoint(head.position);
            Vector3 away = point - head.position;
            float distance = away.magnitude;

            if (distance >= minPanelDistance)
                return point;

            // Đứng sát quá: đẩy panel lùi ra dọc đúng hướng cũ, nên nó vẫn nằm về phía ổ khóa
            // chứ không nhảy sang chỗ khác.
            Vector3 direction = distance > 0.0001f ? away / distance : head.forward;
            return head.position + direction * minPanelDistance;
        }

        /// <summary>
        /// Dòng sáng theo độ cao bàn tay so với lúc bấm nút: hạ tay xuống thì chạy xuống dòng
        /// dưới, nâng lên thì chạy ngược lên. Kẹp trong khoảng số dòng nên kéo quá tay cũng chỉ
        /// dừng ở dòng đầu hoặc dòng cuối, không bao giờ mất dòng sáng.
        /// </summary>
        private bool PickByHandHeight(out int index)
        {
            index = -1;

            if (view.RowCount == 0)
                return false;

            float drop = openHandHeight - openHand.transform.position.y;
            int row = Mathf.RoundToInt(drop / Mathf.Max(0.005f, metersPerRow));

            index = Mathf.Clamp(row, 0, view.RowCount - 1);
            view.SetHighlight(index);
            return true;
        }

        private void Commit()
        {
            int index = view.Highlight;
            DoorActionMenu menu = openMenu;
            XRBaseInteractor hand = openHand;

            // Dòng 0 là THOÁT, -1 là trỏ ra ngoài panel. Cả hai đều là hủy.
            bool usable = index > 0 && index < rowOptions.Count && view.IsUsable(index);
            DoorActionOption option = usable ? rowOptions[index] : null;

            CloseMenu(option != null);

            if (option == null)
                return;

            var ctx = new DoorActionContext(menu, hand, handsRoot);

            // Xét lại một lần nữa: panel mở được vài giây, trạng thái cửa có thể đã đổi.
            if (option.CanUse(ctx))
                option.Execute(ctx);
        }

        private void CloseMenu(bool confirmed)
        {
            if (openMenu == null)
                return;

            if (view != null)
                view.Hide();

            if (DoorActionMenu.Open == openMenu)
                DoorActionMenu.Open = null;

            Play(confirmed ? confirmClip : cancelClip);

            openMenu = null;
            openHand = null;
            lastHighlight = -1;
        }

        // ===== dựng danh sách dòng =====

        private void BuildEntries(DoorActionMenu menu, XRBaseInteractor hand)
        {
            entries.Clear();
            rowOptions.Clear();

            entries.Add(new DoorActionMenuView.Entry
            {
                Label = exitLabel,
                Tint = exitTint,
                Usable = true,
            });
            rowOptions.Add(null);

            var ctx = new DoorActionContext(menu, hand, handsRoot);
            IReadOnlyList<DoorActionOption> options = menu.ResolveOptions(defaultOptions);

            if (options == null)
                return;

            for (int i = 0; i < options.Count; i++)
            {
                DoorActionOption option = options[i];

                if (option == null)
                    continue;

                bool usable = option.CanUse(ctx);

                if (!usable && option.HideWhenUnusable)
                    continue;

                entries.Add(new DoorActionMenuView.Entry
                {
                    Label = option.Label,
                    Icon = option.Icon,
                    Tint = option.Tint,
                    Usable = usable,
                });
                rowOptions.Add(option);
            }
        }

        private static string Heading(DoorController door)
        {
            switch (door.State)
            {
                case DoorState.Locked: return "CỬA KHÓA";
                case DoorState.Open: return "CỬA ĐANG MỞ";
                case DoorState.Breached: return "CỬA ĐÃ PHÁ";
                default: return "CỬA ĐÓNG";
            }
        }

        // ===== tiện ích =====

        private Transform Head
        {
            get
            {
                if (head != null)
                    return head;

                XROrigin origin = GetComponentInParent<XROrigin>();

                if (origin != null && origin.Camera != null)
                    head = origin.Camera.transform;
                else if (Camera.main != null)
                    head = Camera.main.transform;

                return head;
            }
        }

        /// <summary>Tia ngắm của bàn tay. NearFarInteractor có sẵn gốc tia riêng, dùng cái đó.</summary>
        private static Ray HandRay(XRBaseInteractor hand)
        {
            if (hand is IXRRayProvider provider)
            {
                Transform origin = provider.GetOrCreateRayOrigin();

                if (origin != null)
                    return new Ray(origin.position, origin.forward);
            }

            Transform attach = hand.GetAttachTransform(null);
            Transform t = attach != null ? attach : hand.transform;
            return new Ray(t.position, t.forward);
        }

        private bool WasPressedThisFrame(XRBaseInteractor hand)
        {
            if (keyboardTab && Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
                return true;

            InputAction action = ActionFor(hand);
            return action != null && action.WasPressedThisFrame();
        }

        private bool IsHeld(XRBaseInteractor hand)
        {
            if (keyboardTab && Keyboard.current != null && Keyboard.current.tabKey.isPressed)
                return true;

            InputAction action = ActionFor(hand);
            return action != null && action.IsPressed();
        }

        private InputAction ActionFor(XRBaseInteractor hand)
        {
            if (hand == null)
                return null;

            bool left = hand.handedness == InteractorHandedness.Left;

            if (left)
                return HasBinding(leftMenuButton) ? leftMenuButton.action : fallbackLeft;

            return HasBinding(rightMenuButton) ? rightMenuButton.action : fallbackRight;
        }

        /// <summary>
        /// InputActionProperty để trống trong Inspector vẫn trả về một action rỗng chứ không phải
        /// null, nên phải xét số binding mới biết người ta có gán gì không.
        /// </summary>
        private static bool HasBinding(InputActionProperty property)
        {
            return property.action != null && property.action.bindings.Count > 0;
        }

        private static InputAction MakeFallback(string name, string binding)
        {
            var action = new InputAction(name, InputActionType.Button, binding);
            action.Enable();
            return action;
        }

        private void Play(AudioClip clip)
        {
            if (audioSource != null && clip != null)
                audioSource.PlayOneShot(clip);
        }
    }
}
