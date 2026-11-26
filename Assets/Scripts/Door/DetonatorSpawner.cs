using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

namespace VRSwat.Doors
{
    /// <summary>
    /// Dán bom xong là kíp nổ tự hiện ra trong tay người chơi; bóp cò là nổ (phần nổ do
    /// <see cref="Detonator"/> lo). Gỡ bom xuống hoặc nổ xong thì kíp tự biến mất.
    ///
    /// Đặt một cái duy nhất trong scene (object rỗng cũng được). Không cần kéo quả bom hay cánh
    /// cửa nào vào đây: script đọc <see cref="BreachCharge.All"/> nên bom spawn lúc runtime, bom
    /// đặt sẵn trong scene, hay dán mấy quả cùng lúc đều tính.
    ///
    /// Trạng thái đọc bằng poll trong <see cref="Update"/> chứ không bắt sự kiện của từng quả
    /// bom. Hai lý do:
    ///
    /// 1. Quả bom tự hủy khi nổ, lại có thể được spawn thêm lúc runtime, nên đăng ký sự kiện theo
    ///    instance là phải tự quản lý vòng đời của từng listener.
    /// 2. Lúc bom vào ổ, XRI đang chạy dở callback selectEntered của chính nó. Gọi
    ///    <c>SelectEnter</c> ngay trong đó là sửa trạng thái interaction khi XRI chưa thoát khỏi
    ///    vòng duyệt. Chờ sang Update là tránh hẳn chuyện đó, và cũng chắc chắn bàn tay vừa thả
    ///    bom đã rảnh.
    ///
    /// Tay nhận kíp ưu tiên là tay vừa dán bom (<see cref="BreachCharge.LastHeldBy"/>) — tay đó
    /// vừa thả bom nên đang rảnh. Nếu tay đó đã cầm món khác thì lấy tay rảnh còn lại; hai tay
    /// đều đầy thì chờ, frame nào có tay rảnh là đưa kíp vào tay đó.
    ///
    /// Lúc kíp hiện ra, bàn tay được quặp lại như đang bóp grip (xem <see cref="CloseHand"/>),
    /// nên trông đúng là đang nắm kíp chứ không phải kíp dính vào một bàn tay xòe.
    ///
    /// Kíp DÍNH tay chứ không thả ra được (lý do ở <see cref="TrySpawn"/>): bóp grip hay mở tay
    /// đều không rơi, phải bấm nổ hoặc gỡ bom xuống thì kíp mới tự biến mất. Đổi lại người chơi
    /// khỏi phải với tay mò kíp ở túi. Muốn thu kíp sớm thì gọi <see cref="Despawn"/>.
    /// </summary>
    [AddComponentMenu("VR SWAT/Detonator Spawner")]
    [DisallowMultipleComponent]
    public class DetonatorSpawner : MonoBehaviour
    {
        [Header("Kíp nổ")]
        [Tooltip("Prefab kíp nổ, phải có component Detonator + XR Grab Interactable. Dùng Detonator.prefab.")]
        [SerializeField] private GameObject detonatorPrefab;

        [Header("Tay người chơi")]
        [Tooltip("Gốc của rig để tìm interactor của hai bàn tay. Để trống sẽ tự tìm XR Origin trong scene.")]
        [SerializeField] private Transform handsRoot;

        [Header("Dọn kíp")]
        [Tooltip("Bật: không còn quả bom nào dán trên cửa (nổ xong, hoặc bị gỡ xuống) thì kíp tự " +
                 "biến mất, khỏi phải cầm kíp rỗng đi suốt màn.")]
        [SerializeField] private bool despawnWhenNoCharge = true;

        [Tooltip("Chờ bao lâu rồi mới hủy kíp (giây). Để tiếng bấm kíp kêu xong đã.")]
        [SerializeField] private float despawnDelay = 1.5f;

        [Header("Bàn tay")]
        [Tooltip("Ép bàn tay quặp lại như đang bóp grip trong suốt lúc cầm kíp. Tắt thì bàn tay " +
                 "xòe ra (vì nút grip đang nhả) và kíp trông như dính lơ lửng.")]
        [SerializeField] private bool closeHandWhileHolding = true;

        [Header("Phản hồi")]
        [Tooltip("Tiếng kíp hiện ra trong tay. Không bắt buộc.")]
        [SerializeField] private AudioClip spawnClip;

        [Tooltip("Rung tay lúc kíp hiện ra, để người chơi biết tay mình vừa có đồ. 0 = tắt.")]
        [SerializeField, Range(0f, 1f)] private float hapticAmplitude = 0.3f;

        [SerializeField] private float hapticDuration = 0.08f;

        /// <summary>Kíp do script này sinh ra. Null nếu chưa sinh hoặc đã dọn.</summary>
        public Detonator Spawned { get; private set; }

        private readonly List<XRBaseInteractor> handInteractors = new List<XRBaseInteractor>();

        private XRBaseInteractor manualHand;
        private XRBaseInteractable spawnedInteractable;
        private HandPoseDriver grippedHand;
        private float despawnAt = -1f;

        private void Update()
        {
            // Kíp mất mà tay vẫn ở chế độ manual thì phải trả tay về bình thường, nếu không
            // isSelectActive của tay đó cứ true và tay sẽ tự hít mọi vật đi ngang qua.
            if (manualHand != null && Spawned == null)
                EndManualHold();

            if (AnyChargePlaced())
            {
                despawnAt = -1f;

                // Có kíp nào đang sống thì thôi, kể cả quả đặt sẵn trong scene hay người chơi
                // đang cầm: hiện thêm quả thứ hai chỉ làm người chơi tưởng hai kíp nổ hai thứ
                // khác nhau.
                if (Detonator.All.Count == 0)
                    TrySpawn();

                return;
            }

            if (!despawnWhenNoCharge || Spawned == null)
                return;

            if (despawnAt < 0f)
                despawnAt = Time.time + Mathf.Max(0f, despawnDelay);
            else if (Time.time >= despawnAt)
                Despawn();
        }

        private void OnDisable()
        {
            // Nhả tay nhưng KHÔNG hủy kíp: script này tắt giữa màn thì kíp rơi xuống đất, còn
            // hơn là bàn tay mắc cờ manual và từ đó hít mọi thứ đi ngang qua.
            EndManualHold();
            handInteractors.Clear();
            despawnAt = -1f;
        }

        /// <summary>
        /// Hủy kíp do script này sinh ra. Gọi được từ UnityEvent nếu màn chơi muốn thu kíp sớm.
        /// </summary>
        public void Despawn()
        {
            despawnAt = -1f;

            // Nhả tay TRƯỚC khi hủy: EndManualInteraction gọi SelectExit với chính object này,
            // hủy trước thì nó thao tác trên object đã chết.
            EndManualHold();

            if (Spawned == null)
                return;

            Destroy(Spawned.gameObject);
            Spawned = null;
        }

        /// <summary>
        /// Quặp bàn tay lại quanh kíp.
        ///
        /// Cần làm tay riêng vì cái "đang cầm" của XRI chỉ là quan hệ interactor ↔ interactable:
        /// ngón tay của model lại chạy theo nút grip qua <see cref="AnimateHandOnInput"/>, mà kíp
        /// này tự hiện ra chứ không ai bóp grip. Không ép thì vật nằm trong một bàn tay xòe.
        ///
        /// Prefab kíp có <see cref="HandPoseProvider"/> kèm dáng tạo sẵn thì dáng đó vẫn ăn (nó
        /// nghe select của chính interactable) và đè lên cái này — đẹp hơn, nên ép grip chỉ là
        /// mức tối thiểu cho prefab chưa có dáng.
        /// </summary>
        private void CloseHand(XRBaseInteractor hand)
        {
            if (!closeHandWhileHolding)
                return;

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

            grippedHand = HandPoseDriver.Find(side);

            if (grippedHand != null)
                grippedHand.SetGripOverride(1f);
        }

        /// <summary>Trả bàn tay về chế độ cầm bình thường (xem <see cref="TrySpawn"/>).</summary>
        private void EndManualHold()
        {
            XRBaseInteractor hand = manualHand;
            XRBaseInteractable held = spawnedInteractable;

            if (grippedHand != null)
            {
                grippedHand.ClearGripOverride();
                grippedHand = null;
            }

            manualHand = null;
            spawnedInteractable = null;

            if (hand == null || !hand.isPerformingManualInteraction)
                return;

            // Chỉ nhả khi tay THẬT SỰ còn đang cầm đúng cái kíp đó: EndManualInteraction chạy
            // thẳng SelectExit lên interactable mà XRI không kiểm tra gì, nên gọi lúc kíp đã bị
            // hủy là đụng vào object chết.
            if (held != null && hand.IsSelecting((IXRSelectInteractable)held))
                hand.EndManualInteraction();
            else
                Debug.LogWarning(
                    $"{name}: kíp nổ bị hủy trước khi nhả tay nên không gọi được " +
                    "EndManualInteraction. Dọn kíp bằng Despawn() thay vì Destroy().", this);
        }

        private static bool AnyChargePlaced()
        {
            var charges = BreachCharge.All;

            for (int i = 0; i < charges.Count; i++)
            {
                if (charges[i] != null && charges[i].IsPlaced)
                    return true;
            }

            return false;
        }

        private void TrySpawn()
        {
            if (detonatorPrefab == null)
            {
                Debug.LogWarning(
                    $"{name}: chưa gán Detonator Prefab nên dán bom xong không có kíp để bấm. " +
                    "Kéo Detonator.prefab vào ô Detonator Prefab.", this);
                enabled = false;
                return;
            }

            XRBaseInteractor hand = PickHand();

            if (hand == null)
                return;     // hai tay đang đầy, frame sau thử lại

            Transform attach = hand.GetAttachTransform(null);
            Vector3 position = attach != null ? attach.position : hand.transform.position;
            Quaternion rotation = attach != null ? attach.rotation : hand.transform.rotation;

            GameObject instance = Instantiate(detonatorPrefab, position, rotation);
            instance.SetActive(true);

            Detonator detonator = instance.GetComponent<Detonator>();
            XRBaseInteractable component = instance.GetComponent<XRBaseInteractable>();
            var interactable = component as IXRSelectInteractable;

            if (detonator == null || interactable == null)
            {
                Debug.LogWarning(
                    $"{name}: {detonatorPrefab.name} thiếu Detonator hoặc XR Grab Interactable ở " +
                    "object gốc nên không nhét vào tay được. Kiểm tra lại prefab.", this);
                Destroy(instance);
                enabled = false;
                return;
            }

            // StartManualInteraction chứ không phải XRInteractionManager.SelectEnter: SelectEnter
            // thường chỉ giữ được một frame. Mỗi frame XRI xét lại vật đang cầm qua
            // XRInteractionManager.CanSelect, mà hàm đó đòi interactor.isSelectActive — tức là
            // người chơi phải đang bóp nút grip. Kíp này tự hiện ra chứ không ai bóp grip, nên nó
            // sẽ rơi ngay frame sau. Manual interaction bật isSelectActive của bàn tay cho tới
            // khi EndManualInteraction, nên kíp dính tay cho tới lúc bị dọn.
            hand.StartManualInteraction(interactable);

            if (!hand.IsSelecting(interactable))
            {
                // StartManualInteraction đã log lý do. Hay gặp nhất: Interaction Layer của kíp
                // không trùng với Interaction Layer của bàn tay.
                Debug.LogWarning(
                    $"{name}: tay không nhận được {instance.name}. Kiểm tra Interaction Layer Mask " +
                    "của XR Grab Interactable trên prefab kíp.", this);
                Destroy(instance);
                enabled = false;
                return;
            }

            manualHand = hand;
            spawnedInteractable = component;
            Spawned = detonator;
            CloseHand(hand);

            if (spawnClip != null)
                AudioSource.PlayClipAtPoint(spawnClip, position);

            SendHaptic(hand);
        }

        /// <summary>
        /// Tay rảnh để nhận kíp: ưu tiên tay vừa dán bom, không thì tay rảnh nào cũng được.
        /// </summary>
        private XRBaseInteractor PickHand()
        {
            CacheHandInteractors();

            XRBaseInteractor preferred = LastHandThatPlaced();

            if (IsFree(preferred))
                return preferred;

            for (int i = 0; i < handInteractors.Count; i++)
            {
                if (IsFree(handInteractors[i]))
                    return handInteractors[i];
            }

            return null;
        }

        private static XRBaseInteractor LastHandThatPlaced()
        {
            var charges = BreachCharge.All;

            for (int i = charges.Count - 1; i >= 0; i--)
            {
                BreachCharge charge = charges[i];

                if (charge != null && charge.IsPlaced && charge.LastHeldBy is XRBaseInteractor hand)
                    return hand;
            }

            return null;
        }

        private static bool IsFree(XRBaseInteractor hand)
        {
            return hand != null &&
                   hand.isActiveAndEnabled &&
                   !hand.hasSelection;
        }

        private void CacheHandInteractors()
        {
            // Lọc rụng cái đã bị hủy (model tay đổi theo chế độ nhập liệu) rồi mới quyết định có
            // phải quét lại scene không.
            for (int i = handInteractors.Count - 1; i >= 0; i--)
            {
                if (handInteractors[i] == null)
                    handInteractors.RemoveAt(i);
            }

            if (handInteractors.Count > 0)
                return;

            Transform root = handsRoot;

            if (root == null)
            {
                XROrigin origin = FindFirstObjectByType<XROrigin>();
                root = origin != null ? origin.transform : null;
            }

            if (root == null)
            {
                Debug.LogWarning(
                    $"{name}: không tìm thấy XR Origin nên không biết tay người chơi ở đâu. Gán " +
                    "ô Hands Root.", this);
                enabled = false;
                return;
            }

            foreach (XRBaseInteractor interactor in root.GetComponentsInChildren<XRBaseInteractor>(true))
            {
                if (!(interactor is IXRSelectInteractor))
                    continue;

                // Socket (túi đồ, ổ dán bom, giá treo) cũng select vật nhưng không phải bàn tay.
                if (interactor is XRSocketInteractor)
                    continue;

                handInteractors.Add(interactor);
            }

            if (handInteractors.Count == 0)
                Debug.LogWarning(
                    $"{name}: không có interactor nào của bàn tay dưới {root.name}, kíp sẽ không " +
                    "bao giờ hiện ra. Kiểm tra lại ô Hands Root.", this);
        }

        private void SendHaptic(XRBaseInteractor hand)
        {
            if (hapticAmplitude <= 0f || hand == null)
                return;

            HapticImpulsePlayer player = hand.transform.GetComponentInParent<HapticImpulsePlayer>();

            if (player != null)
                player.SendHapticImpulse(hapticAmplitude, hapticDuration);
        }
    }
}
