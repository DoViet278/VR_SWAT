using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRSwat.Doors;
using VRSwat.Tutorial;

namespace VRSwat.Squad
{
    /// <summary>
    /// Menu lệnh VR cho đồng đội. Đặt MỘT cái trên XR Origin.
    ///
    /// Luồng chơi:
    ///
    /// 1. GIỮ nút X (tay trái) — hoặc A (tay phải) — thì panel lệnh hiện ra trước mặt, đứng yên
    ///    tại chỗ.
    /// 2. Kéo tay lên xuống → dòng đang sáng chạy theo, kèm một nhịp rung nhẹ.
    /// 3. NHẢ nút → ra lệnh ở dòng đang sáng. Dòng trên cùng luôn là THOÁT và cũng là dòng sáng
    ///    sẵn lúc panel vừa hiện, nên giữ rồi nhả mà không kéo gì = hủy.
    ///
    /// Danh sách dòng phụ thuộc vào việc người chơi có đang NHẮM VÀO CỬA hay không:
    ///
    ///   không nhắm vào cửa : THOÁT · FALL IN · STACK UP
    ///   đang nhắm vào cửa  : THOÁT · FALL IN · STACK UP · OPEN AND CLEAR
    ///
    /// STACK UP vẫn hiện lúc không nhắm vào cửa, vì lệnh này luôn cần một cánh cửa: không nhắm
    /// thì script lấy cửa gần người chơi nhất (<see cref="SquadController.FindDoorNearPlayer"/>).
    /// Quanh đó không có cửa nào hợp lệ thì dòng xám lại chứ không mất, để người chơi biết là CÓ
    /// lệnh này.
    ///
    /// Đây là luồng RIÊNG, không dính gì tới panel chọn cách mở cửa (<c>DoorActionMenuInput</c>):
    /// nút khác (X/A so với Y/B), panel riêng (<see cref="SquadCommandMenuView"/>), danh sách
    /// riêng. Hai menu chạy song song mà không biết gì về nhau.
    ///
    /// Chỉ ĐỌC NÚT và DỰNG DÒNG. Việc chia lệnh thành việc đơn lẻ cho từng đồng đội vẫn là của
    /// <see cref="SquadController"/>: ở đây chỉ gọi <see cref="SquadController.Issue"/>.
    /// </summary>
    [AddComponentMenu("VR SWAT/Squad Command Menu Input")]
    [DisallowMultipleComponent]
    public class SquadCommandMenuInput : MonoBehaviour
    {
        public enum SelectMode
        {
            /// <summary>
            /// Kéo bàn tay lên xuống, dòng sáng chạy theo độ cao của tay so với lúc bấm nút.
            /// Không cần tia nên tắt Far Casting vẫn dùng được.
            /// </summary>
            HandMovement,

            /// <summary>
            /// Chĩa tay vào panel, dòng nào bị tia xuyên qua thì sáng. Chỉ nên dùng khi
            /// NearFarInteractor còn BẬT Far Casting.
            /// </summary>
            HandRay,
        }

        public enum AimSource
        {
            /// <summary>Nhắm bằng tay cầm: tia đi ra từ bàn tay đang giữ nút.</summary>
            Hand,

            /// <summary>Nhắm bằng mắt: tia đi ra từ giữa tầm nhìn.</summary>
            Head,
        }

        /// <summary>Một dòng ra lệnh. Mặc định (Valid = false) là dòng THOÁT.</summary>
        private readonly struct CommandRow
        {
            public readonly SquadCommand Command;
            public readonly DoorController Door;
            public readonly bool Valid;

            public CommandRow(SquadCommand command, DoorController door)
            {
                Command = command;
                Door = door;
                Valid = true;
            }
        }

        [Header("Rig")]
        [Tooltip("Gốc rig để tìm interactor hai bàn tay. Để trống sẽ tự tìm XR Origin trong scene.")]
        [SerializeField] private Transform handsRoot;

        [Tooltip("Đầu người chơi, để panel quay mặt về. Để trống thì lấy camera của XR Origin.")]
        [SerializeField] private Transform head;

        [Header("Nút mở menu")]
        [Tooltip("Action nút X tay trái. Để trống thì tự bind <XRController>{LeftHand}/primaryButton.")]
        [SerializeField] private InputActionProperty leftCommandButton;

        [Tooltip("Action nút A tay phải. Để trống thì tự bind <XRController>{RightHand}/primaryButton. " +
                 "Tắt ô Right Hand Opens Menu nếu chỉ muốn tay trái mở được menu.")]
        [SerializeField] private InputActionProperty rightCommandButton;

        [Tooltip("Cho tay phải (nút A) mở menu luôn. Tắt thì chỉ nút X tay trái mở được.")]
        [SerializeField] private bool rightHandOpensMenu = true;

        [Tooltip("Cho phép giữ một phím bàn phím để mở menu (test trên màn hình, không cần đeo kính).")]
        [SerializeField] private bool keyboardFallback = true;

        [Tooltip("Phím thay cho nút X lúc test trên màn hình. Menu đang mở thì mũi tên lên / " +
                 "xuống đổi dòng, nhả phím là chọn.")]
        [SerializeField] private Key keyboardKey = Key.X;

        [Header("Cách chọn dòng")]
        [Tooltip("Hand Movement: kéo tay lên xuống, không cần tia — dùng cái này nếu đã tắt Far " +
                 "Casting. Hand Ray: chĩa tay vào panel, chỉ hợp khi còn bật Far Casting.")]
        [SerializeField] private SelectMode selectMode = SelectMode.HandMovement;

        [Tooltip("Chỉ cho Hand Movement: kéo tay xuống bao nhiêu mét thì nhảy một dòng. Nhỏ quá " +
                 "thì dòng sáng nhảy loạn vì tay rung, lớn quá thì phải vươn tay mỏi.")]
        [SerializeField] private float metersPerRow = 0.045f;

        [Header("Nhắm cửa")]
        [Tooltip("Hand: tia đi ra từ tay cầm đang giữ nút. Head: tia đi ra từ giữa tầm nhìn.")]
        [SerializeField] private AimSource aimSource = AimSource.Hand;

        [Tooltip("Nhắm được cửa xa tối đa bấy nhiêu mét.")]
        [SerializeField] private float aimRange = 8f;

        [Tooltip("Layer được tính là vật chắn tia. Để Everything thì tường chắn trước cửa cũng " +
                 "tính, nên không ra lệnh xuyên tường được.")]
        [SerializeField] private LayerMask aimMask = ~0;

        [Tooltip("Tia bay vào khoảng không thì vẫn nhận cửa lệch trong bấy nhiêu độ quanh tia, " +
                 "để khỏi phải ngắm chính xác. 0 = tắt, bắt buộc trúng hẳn cánh cửa.")]
        [SerializeField, Range(0f, 45f)] private float aimTolerance = 10f;

        [Header("Panel")]
        [Tooltip("Panel lệnh. Dựng bằng Tools > VR SWAT > Teammate > Create Squad Command Menu.")]
        [SerializeField] private SquadCommandMenuView view;

        [Tooltip("Panel cách mắt bao nhiêu mét. Dưới 0.4 m là mắt phải hội tụ quá gần, đeo lâu " +
                 "sẽ mỏi — đừng hạ thấp hơn nữa.")]
        [SerializeField] private float panelDistance = 0.4f;

        [Tooltip("Hạ panel xuống dưới tầm mắt bao nhiêu mét, để nó không che hết tầm nhìn. Số " +
                 "âm là hạ xuống.")]
        [SerializeField] private float panelHeightOffset = -0.05f;

        [Header("Chữ trên panel")]
        [Tooltip("Chữ của dòng thoát, luôn nằm trên cùng giống SWAT 4.")]
        [SerializeField] private string exitLabel = "THOÁT MENU";

        [SerializeField] private string fallInLabel = "FALL IN";
        [SerializeField] private string stackUpLabel = "STACK UP";
        [SerializeField] private string openAndClearLabel = "OPEN AND CLEAR";

        [Tooltip("Hiện dòng tiêu đề ở đầu panel (đang nhắm vào cửa nào, cửa đó khóa hay mở).")]
        [SerializeField] private bool showHeading = true;

        [Header("Màu ô vuông")]
        [SerializeField] private Color exitTint = new Color(0.62f, 0.66f, 0.62f);

        [Tooltip("Màu lệnh không cần cửa.")]
        [SerializeField] private Color squadTint = new Color(0.35f, 0.72f, 0.95f);

        [Tooltip("Màu lệnh gắn với một cánh cửa cụ thể.")]
        [SerializeField] private Color doorTint = new Color(0.95f, 0.78f, 0.17f);

        [Header("Phản hồi")]
        [Tooltip("Rung nhẹ mỗi khi dòng đang trỏ tới đổi. 0 = tắt.")]
        [SerializeField, Range(0f, 1f)] private float tickHaptic = 0.15f;

        [SerializeField] private float tickHapticDuration = 0.03f;

        [Tooltip("Tiếng lúc panel hiện ra. Không bắt buộc.")]
        [SerializeField] private AudioSource audioSource;

        [SerializeField] private AudioClip openClip, tickClip, confirmClip, cancelClip;

        private readonly List<SquadCommandMenuView.Entry> entries = new List<SquadCommandMenuView.Entry>();

        /// <summary>Lệnh tương ứng từng dòng. Phần tử 0 là dòng thoát nên để default.</summary>
        private readonly List<CommandRow> rowCommands = new List<CommandRow>();

        private InputAction fallbackLeft, fallbackRight;
        private XRBaseInteractor openHand;
        private DoorController openDoor;
        private bool open;
        private bool openByKeyboard;
        private bool openByLeftHand;
        private int lastHighlight = -1;
        private int keyboardRow;

        /// <summary>Độ cao bàn tay lúc bấm nút, làm mốc 0 cho kiểu chọn Hand Movement.</summary>
        private float openHandHeight;

        /// <summary>Panel lệnh đang mở (để script khác chặn tương tác khác nếu cần).</summary>
        public static bool IsOpen { get; private set; }

        private void OnEnable()
        {
            leftCommandButton.action?.Enable();
            rightCommandButton.action?.Enable();

            if (!HasBinding(leftCommandButton))
                fallbackLeft = MakeFallback("SquadMenuLeft", "<XRController>{LeftHand}/primaryButton");

            if (!HasBinding(rightCommandButton))
                fallbackRight = MakeFallback("SquadMenuRight", "<XRController>{RightHand}/primaryButton");
        }

        private void OnDisable()
        {
            CloseMenu(false);

            fallbackLeft?.Disable();
            fallbackLeft?.Dispose();
            fallbackLeft = null;

            fallbackRight?.Disable();
            fallbackRight?.Dispose();
            fallbackRight = null;
        }

        private void Update()
        {
            if (open)
                TickOpen();
            else
                TickClosed();
        }

        // ===== chưa mở panel =====

        private void TickClosed()
        {
            if (TryGetPress(out XRBaseInteractor hand, out bool left, out bool keyboard))
                OpenMenu(hand, left, keyboard);
        }

        private void OpenMenu(XRBaseInteractor hand, bool left, bool keyboard)
        {
            if (view == null)
            {
                Debug.LogWarning(
                    $"{name}: chưa gán Squad Command Menu View nên giữ nút không hiện gì. Dựng " +
                    "panel bằng Tools > VR SWAT > Teammate > Create Squad Command Menu rồi kéo " +
                    "vào ô View.", this);
                enabled = false;
                return;
            }

            if (SquadController.Instance == null)
            {
                Debug.LogWarning(
                    $"{name}: scene chưa có SquadController nên không ra lệnh cho ai được. Chạy " +
                    "Tools > VR SWAT > Teammate > Add SquadController To Scene.", this);
                return;
            }

            BuildEntries(hand, keyboard);

            if (entries.Count <= 1)
                return;     // chỉ còn mỗi dòng thoát thì mở ra cũng để làm gì

            open = true;
            IsOpen = true;
            openHand = hand;
            openByLeftHand = left;
            openByKeyboard = keyboard;
            openHandHeight = hand != null ? hand.transform.position.y : 0f;
            lastHighlight = 0;
            keyboardRow = 0;

            Transform h = Head;
            view.Show(PanelPoint(h), h, entries, showHeading ? Heading(openDoor) : null);

            // Sáng sẵn dòng THOÁT: giữ nút rồi nhả mà không kéo gì thì không có gì xảy ra.
            view.SetHighlight(0);

            Play(openClip);
            HandItemSpawner.SendHaptic(hand, tickHaptic, tickHapticDuration);

            // Tutorial phòng 8 chờ tín hiệu này để bảo người chơi chọn Fall in.
            TutorialSignals.Raise(Sig.MenuOpened, this);
        }

        // ===== đang mở panel =====

        private void TickOpen()
        {
            // Tay biến mất giữa chừng (tắt rig, đổi interactor...).
            if (!openByKeyboard && openHand != null && !openHand.isActiveAndEnabled)
            {
                CloseMenu(false);
                return;
            }

            if (PickRow(out int index))
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

            if (!IsHeld())
                Commit();
        }

        /// <summary>
        /// Dòng đang trỏ tới. Có tay thì theo độ cao tay (hoặc theo tia tay), không có tay — tức
        /// đang test trên màn hình — thì theo mũi tên lên / xuống.
        /// </summary>
        private bool PickRow(out int index)
        {
            index = -1;

            if (view.RowCount == 0)
                return false;

            if (openHand == null)
                return PickByKeyboard(out index);

            if (selectMode == SelectMode.HandRay)
                return view.TryPick(HandRay(openHand), out index);

            // Hạ tay xuống thì chạy xuống dòng dưới, nâng lên thì chạy ngược lên. Kẹp trong
            // khoảng số dòng nên kéo quá tay cũng chỉ dừng ở dòng đầu hoặc dòng cuối.
            float drop = openHandHeight - openHand.transform.position.y;
            int row = Mathf.RoundToInt(drop / Mathf.Max(0.005f, metersPerRow));

            index = Mathf.Clamp(row, 0, view.RowCount - 1);
            view.SetHighlight(index);
            return true;
        }

        private bool PickByKeyboard(out int index)
        {
            Keyboard kb = Keyboard.current;

            if (kb != null)
            {
                if (kb.downArrowKey.wasPressedThisFrame) keyboardRow++;
                if (kb.upArrowKey.wasPressedThisFrame) keyboardRow--;
            }

            keyboardRow = Mathf.Clamp(keyboardRow, 0, view.RowCount - 1);
            index = keyboardRow;
            view.SetHighlight(index);
            return true;
        }

        private void Commit()
        {
            int index = view.Highlight;

            // Dòng 0 là THOÁT, -1 là trỏ ra ngoài panel. Cả hai đều là hủy.
            bool usable = index > 0 && index < rowCommands.Count && view.IsUsable(index);
            CommandRow row = usable ? rowCommands[index] : default;

            CloseMenu(row.Valid);

            if (!row.Valid)
                return;

            SquadController squad = SquadController.Instance;

            if (squad == null)
                return;

            // Xét lại một lần nữa bên trong Issue: panel mở được vài giây, cửa hoặc đội có thể
            // đã đổi trạng thái.
            squad.Issue(row.Command, row.Door);
        }

        private void CloseMenu(bool confirmed)
        {
            if (!open)
                return;

            if (view != null)
                view.Hide();

            Play(confirmed ? confirmClip : cancelClip);

            open = false;
            IsOpen = false;
            openHand = null;
            openDoor = null;
            lastHighlight = -1;
        }

        // ===== dựng danh sách dòng =====

        /// <summary>
        /// Dòng THOÁT, rồi FALL IN và STACK UP, rồi OPEN AND CLEAR nếu đang nhắm vào cửa.
        /// Lệnh không ra được ngay bây giờ thì dòng vẫn hiện nhưng xám lại, để người chơi biết
        /// là CÓ lệnh đó chứ không tưởng menu bị thiếu.
        /// </summary>
        private void BuildEntries(XRBaseInteractor hand, bool keyboard)
        {
            entries.Clear();
            rowCommands.Clear();

            Add(exitLabel, exitTint, true, default);

            SquadController squad = SquadController.Instance;
            Vector3 playerPos = PlayerPoint;

            openDoor = FindAimedDoor(hand, keyboard);

            Add(fallInLabel, squadTint,
                squad.CanIssue(SquadCommand.FallIn, null, playerPos),
                new CommandRow(SquadCommand.FallIn, null));

            // STACK UP luôn cần cửa: không nhắm vào cửa nào thì lấy cửa gần người chơi nhất.
            DoorController stackDoor = openDoor != null ? openDoor : squad.FindDoorNearPlayer();

            Add(stackUpLabel, squadTint,
                stackDoor != null && squad.CanIssue(SquadCommand.StackUp, stackDoor, playerPos),
                new CommandRow(SquadCommand.StackUp, stackDoor));

            // OPEN AND CLEAR chỉ hiện khi đang nhắm hẳn vào một cánh cửa.
            if (openDoor != null)
            {
                Add(openAndClearLabel, doorTint,
                    squad.CanIssue(SquadCommand.OpenAndClear, openDoor, playerPos),
                    new CommandRow(SquadCommand.OpenAndClear, openDoor));
            }
        }

        private void Add(string label, Color tint, bool usable, CommandRow row)
        {
            entries.Add(new SquadCommandMenuView.Entry
            {
                Label = label,
                Tint = tint,
                Usable = usable,
            });
            rowCommands.Add(row);
        }

        private static string Heading(DoorController door)
        {
            if (door == null)
                return "LỆNH ĐỘI";

            switch (door.State)
            {
                case DoorState.Locked: return $"{door.name} — CỬA KHÓA";
                case DoorState.Open: return $"{door.name} — ĐANG MỞ";
                case DoorState.Breached: return $"{door.name} — ĐÃ PHÁ";
                default: return $"{door.name} — CỬA ĐÓNG";
            }
        }

        // ===== nhắm cửa =====

        /// <summary>
        /// Cửa người chơi đang nhắm vào, hoặc null.
        ///
        /// Tia trúng collider của cánh cửa thì lấy cửa đó. Trúng tường hay vật khác thì trả null —
        /// không ra lệnh xuyên tường. Tia bay vào khoảng không thì nhận cửa lệch trong
        /// <see cref="aimTolerance"/> độ quanh tia, để khỏi phải ngắm chính xác từng pixel.
        /// </summary>
        private DoorController FindAimedDoor(XRBaseInteractor hand, bool keyboard)
        {
            if (!TryGetAimRay(hand, keyboard, out Ray ray))
                return null;

            if (Physics.Raycast(ray, out RaycastHit hit, aimRange, aimMask, QueryTriggerInteraction.Ignore))
            {
                // Collider nằm ở cánh (Leaf), DoorController nằm ở gốc cửa nên phải đi ngược lên.
                return hit.collider.GetComponentInParent<DoorController>();
            }

            if (aimTolerance <= 0f)
                return null;

            DoorController best = null;
            float bestAngle = aimTolerance;

            IReadOnlyList<DoorController> doors = DoorController.All;

            for (int i = 0; i < doors.Count; i++)
            {
                DoorController door = doors[i];

                if (door == null)
                    continue;

                Vector3 toDoor = DoorCenter(door) - ray.origin;

                if (toDoor.sqrMagnitude > aimRange * aimRange)
                    continue;

                float angle = Vector3.Angle(ray.direction, toDoor);

                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    best = door;
                }
            }

            return best;
        }

        /// <summary>Giữa ô cửa, ngang tầm ngực. Có điểm stack thì lấy giữa hai điểm đó.</summary>
        private static Vector3 DoorCenter(DoorController door)
        {
            if (door.stackLeft != null && door.stackRight != null)
                return (door.stackLeft.position + door.stackRight.position) * 0.5f + Vector3.up * 1.2f;

            return door.LockPoint.position;
        }

        private bool TryGetAimRay(XRBaseInteractor hand, bool keyboard, out Ray ray)
        {
            // Test trên bàn phím thì không có tay nào đang chĩa đi đâu: nhắm bằng mắt.
            if (aimSource == AimSource.Hand && hand != null && !keyboard)
            {
                ray = HandRay(hand);
                return true;
            }

            Transform h = Head;

            if (h == null)
            {
                ray = default;
                return false;
            }

            ray = new Ray(h.position, h.forward);
            return true;
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

        // ===== input =====

        private bool TryGetPress(out XRBaseInteractor hand, out bool left, out bool keyboard)
        {
            hand = null;
            left = false;
            keyboard = false;

            InputAction leftAction = HasBinding(leftCommandButton) ? leftCommandButton.action : fallbackLeft;

            if (leftAction != null && leftAction.WasPressedThisFrame())
            {
                left = true;
                hand = HandFor(InteractorHandedness.Left);
                return true;
            }

            if (rightHandOpensMenu)
            {
                InputAction rightAction = HasBinding(rightCommandButton)
                    ? rightCommandButton.action
                    : fallbackRight;

                if (rightAction != null && rightAction.WasPressedThisFrame())
                {
                    hand = HandFor(InteractorHandedness.Right);
                    return true;
                }
            }

            if (keyboardFallback && Keyboard.current != null &&
                Keyboard.current[keyboardKey].wasPressedThisFrame)
            {
                keyboard = true;
                hand = HandFor(InteractorHandedness.Left) ?? HandFor(InteractorHandedness.Right);
                return true;
            }

            return false;
        }

        private bool IsHeld()
        {
            if (openByKeyboard)
                return Keyboard.current != null && Keyboard.current[keyboardKey].isPressed;

            InputAction action = openByLeftHand
                ? HasBinding(leftCommandButton) ? leftCommandButton.action : fallbackLeft
                : HasBinding(rightCommandButton) ? rightCommandButton.action : fallbackRight;

            return action != null && action.IsPressed();
        }

        private XRBaseInteractor HandFor(InteractorHandedness want)
        {
            IReadOnlyList<XRBaseInteractor> hands = HandItemSpawner.Hands(handsRoot);

            for (int i = 0; i < hands.Count; i++)
            {
                XRBaseInteractor hand = hands[i];

                if (hand != null && hand.isActiveAndEnabled && hand.handedness == want)
                    return hand;
            }

            return null;
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

        // ===== tiện ích =====

        /// <summary>Chỗ panel hiện ra: giữa tầm nhìn, cách mắt một khoảng cố định.</summary>
        private Vector3 PanelPoint(Transform h)
        {
            if (h == null)
                return transform.position + transform.forward * Mathf.Max(0.2f, panelDistance);

            return h.position
                   + h.forward * Mathf.Max(0.2f, panelDistance)
                   + Vector3.up * panelHeightOffset;
        }

        /// <summary>Điểm đại diện người chơi, đúng cái <c>SquadController.Issue</c> dùng để xét lệnh.</summary>
        private Vector3 PlayerPoint
        {
            get
            {
                Transform player = Teammate.PlayerHead;

                if (player != null)
                    return player.position;

                Transform h = Head;
                return h != null ? h.position : transform.position;
            }
        }

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

        private void Play(AudioClip clip)
        {
            if (audioSource != null && clip != null)
                audioSource.PlayOneShot(clip);
        }
    }
}
