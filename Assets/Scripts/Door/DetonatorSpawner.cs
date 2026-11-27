using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

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
    /// Phần "nhét vật vào tay" nằm ở <see cref="HandItemSpawner"/> vì panel chọn cách mở cửa
    /// cũng dùng (kit phá khóa, bom C4). Kíp nổ dùng chế độ
    /// <see cref="HandItemGrip.HoldMode.StickUntilReleased"/>: bóp grip hay mở tay đều không
    /// rơi, phải bấm nổ hoặc gỡ bom xuống thì kíp mới tự biến mất. Đổi lại người chơi khỏi phải
    /// với tay mò kíp ở túi. Muốn thu kíp sớm thì gọi <see cref="Despawn"/>.
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

        /// <summary>
        /// Tay đang cầm kíp. Giữ riêng ngoài <see cref="Spawned"/> vì kíp có thể bị thứ khác hủy
        /// mất, lúc đó vẫn cần biết tay nào để gỡ cờ manual interaction ra.
        /// </summary>
        private XRBaseInteractor holdingHand;

        private GameObject spawnedInstance;
        private float despawnAt = -1f;

        private void Update()
        {
            // Kíp mất mà tay vẫn ở chế độ manual thì phải trả tay về bình thường, nếu không
            // isSelectActive của tay đó cứ true và tay sẽ tự hít mọi vật đi ngang qua.
            if (holdingHand != null && Spawned == null)
            {
                HandItemSpawner.ForceEndManual(holdingHand);
                holdingHand = null;
                spawnedInstance = null;
            }

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
            HandItemGrip grip = HandItemGrip.Of(spawnedInstance);

            if (grip != null)
                grip.Release();
            else
                HandItemSpawner.ForceEndManual(holdingHand);

            holdingHand = null;
            despawnAt = -1f;
        }

        /// <summary>
        /// Hủy kíp do script này sinh ra. Gọi được từ UnityEvent nếu màn chơi muốn thu kíp sớm.
        /// </summary>
        public void Despawn()
        {
            despawnAt = -1f;

            // DespawnFromHand nhả tay TRƯỚC rồi mới hủy: hủy trước thì EndManualInteraction thao
            // tác trên object đã chết.
            HandItemSpawner.DespawnFromHand(spawnedInstance);

            spawnedInstance = null;
            holdingHand = null;
            Spawned = null;
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

            XRBaseInteractor hand = HandItemSpawner.PickFreeHand(handsRoot, LastHandThatPlaced());

            if (hand == null)
                return;     // hai tay đang đầy, frame sau thử lại

            GameObject instance = HandItemSpawner.SpawnIntoHand(
                detonatorPrefab, hand, HandItemGrip.HoldMode.StickUntilReleased,
                spawnClip, hapticAmplitude, hapticDuration, closeHandWhileHolding);

            if (instance == null)
            {
                // SpawnIntoHand đã log lý do (thiếu interactable, hoặc Interaction Layer sai).
                enabled = false;
                return;
            }

            Detonator detonator = instance.GetComponent<Detonator>();

            if (detonator == null)
            {
                Debug.LogWarning(
                    $"{name}: {detonatorPrefab.name} thiếu component Detonator nên bấm không nổ. " +
                    "Kiểm tra lại prefab.", this);
                HandItemSpawner.DespawnFromHand(instance);
                enabled = false;
                return;
            }

            spawnedInstance = instance;
            holdingHand = hand;
            Spawned = detonator;
        }

        /// <summary>Tay vừa dán quả bom gần nhất — tay đó vừa thả bom nên đang rảnh.</summary>
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
    }
}
