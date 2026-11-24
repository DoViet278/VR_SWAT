using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRSwat.Doors
{
    /// <summary>
    /// Khối thuốc nổ phá cửa: cầm lên, dán vào <see cref="ChargeMount"/> trên cánh cửa, rồi
    /// <see cref="Detonator"/> gọi <see cref="Detonate"/> để kích nổ.
    ///
    /// Chỉ nổ khi đã dán vào ổ (<see cref="IsPlaced"/>). Bom đang cầm trên tay bóp kíp không ăn
    /// thua — nếu không thì người chơi tự thổi bay mình ngay trong tay.
    ///
    /// Nổ xong gọi thẳng <see cref="DoorController.Breach"/> chứ không đi qua
    /// <see cref="DoorBreachable"/>: file đó là đường riêng cho đạn hoa cải, điều kiện của nó
    /// (đếm số viên trúng) không áp được cho thuốc nổ.
    ///
    /// Toàn bộ <see cref="Fire"/> chạy đồng bộ trong một frame, KHÔNG dùng coroutine. Lý do:
    /// hàm này kết thúc bằng Destroy, mà lúc object bị hủy thì XRI phát selectExited từ
    /// OnDisable — thời điểm đó StartCoroutine không chạy được nữa (xem ghi chú cùng loại ở
    /// Magazine.cs). Nếu cần hẹn giờ thì đếm bằng mốc thời gian trong Update, như
    /// <see cref="detonateDelay"/> đang làm.
    /// </summary>
    [AddComponentMenu("VR SWAT/Breach Charge")]
    [DisallowMultipleComponent]
    public class BreachCharge : MonoBehaviour
    {
        [Header("Nổ")]
        [Tooltip("Prefab hiệu ứng nổ, sinh ra tại vị trí quả bom. Dùng Blow.prefab hoặc GrenadeExplosion.prefab.")]
        [SerializeField] private GameObject explosionEffect;

        [Tooltip("Bao lâu thì dọn hiệu ứng nổ (giây).")]
        [SerializeField] private float explosionEffectLiveTime = 5f;

        [SerializeField] private AudioClip explosionClip;

        [Tooltip("Trễ từ lúc bấm kíp tới lúc nổ (giây). 0 = nổ ngay. Gỡ bom ra khỏi cửa giữa chừng thì hủy hẹn.")]
        [SerializeField] private float detonateDelay;

        [Header("Báo hiệu")]
        [Tooltip("Object bật lên khi bom đã dán xong (đèn đỏ, LED...). Không bắt buộc.")]
        [SerializeField] private GameObject armedIndicator;

        /// <summary>Danh sách bom đang sống trong scene, để kíp nổ khỏi phải quét scene.</summary>
        public static IReadOnlyList<BreachCharge> All => all;

        /// <summary>Đang nằm trong một ChargeMount (đã dán lên cửa).</summary>
        public bool IsPlaced => mount != null;

        /// <summary>Cửa đang dán bom. Null nếu chưa dán hoặc ổ không thuộc cửa nào.</summary>
        public DoorController MountedDoor { get; private set; }

        /// <summary>Đã bấm kíp và đang đếm ngược.</summary>
        public bool IsCountingDown => fireAt >= 0f;

        /// <summary>
        /// Bàn tay cầm quả bom gần nhất (không tính socket). <see cref="DetonatorSpawner"/> dùng
        /// để biết nên đưa kíp nổ vào tay nào. Null nếu quả bom chưa từng được cầm lên.
        /// </summary>
        public IXRSelectInteractor LastHeldBy { get; private set; }

        public event Action<BreachCharge> Placed;
        public event Action<BreachCharge> Removed;
        public event Action<BreachCharge> Detonated;

        private static readonly List<BreachCharge> all = new List<BreachCharge>();

        private XRBaseInteractable interactable;
        private ChargeMount mount;
        private float fireAt = -1f;
        private bool detonating;

        private void Awake()
        {
            interactable = GetComponent<XRBaseInteractable>();

            if (interactable == null)
                Debug.LogWarning(
                    $"{name}: thiếu XR Grab Interactable nên không dán vào cửa được. Thêm " +
                    "XR Grab Interactable vào object này.", this);

            SetArmedIndicator(false);
        }

        private void OnEnable()
        {
            if (!all.Contains(this))
                all.Add(this);

            if (interactable == null)
                return;

            interactable.selectEntered.AddListener(OnSelectEntered);
            interactable.selectExited.AddListener(OnSelectExited);
        }

        private void OnDisable()
        {
            all.Remove(this);

            if (interactable == null)
                return;

            interactable.selectEntered.RemoveListener(OnSelectEntered);
            interactable.selectExited.RemoveListener(OnSelectExited);
        }

        private void Update()
        {
            if (fireAt >= 0f && Time.time >= fireAt)
                Fire();
        }

        /// <summary>
        /// Kíp nổ gọi hàm này. Trả về true nếu quả bom nhận lệnh (đã dán và chưa nổ).
        /// </summary>
        public bool Detonate()
        {
            if (detonating || !IsPlaced)
                return false;

            if (detonateDelay > 0f)
            {
                if (fireAt < 0f)
                    fireAt = Time.time + detonateDelay;

                return true;
            }

            Fire();
            return true;
        }

        private void Fire()
        {
            detonating = true;
            fireAt = -1f;

            DoorController door = MountedDoor;
            Vector3 center = transform.position;

            // Tắt collider trước: object sắp bị hủy, không để nó kịp va chạm hay bị select lại.
            foreach (Collider own in GetComponentsInChildren<Collider>())
                own.enabled = false;

            // Truyền chính vị trí quả bom làm "phía người đặt". AwayAngle() chỉ đọc dấu của toạ
            // độ Z trong không gian cửa, mà bom thì nhô ra đúng mặt người chơi đang đứng, nên
            // cánh sẽ văng về phía bên kia.
            if (door != null)
                door.Breach(center, center);

            if (explosionEffect != null)
            {
                GameObject effect = Instantiate(explosionEffect, center, Quaternion.identity);
                effect.SetActive(true);
                Destroy(effect, explosionEffectLiveTime);
            }

            if (explosionClip != null)
                AudioSource.PlayClipAtPoint(explosionClip, center);

            Detonated?.Invoke(this);
            Destroy(gameObject);
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            // Tay cầm cũng là một lần select; chỉ quan tâm lúc vào ổ trên cửa.
            if (!(args.interactorObject is ChargeMount entered))
            {
                // Nhớ bàn tay vừa cầm quả bom để DetonatorSpawner đưa kíp nổ vào đúng tay đó.
                // Phải nhớ ở đây chứ không đọc lúc dán được: khi ổ select quả bom thì tay đã thả
                // ra rồi, interactorsSelecting không còn bàn tay nào.
                if (!(args.interactorObject is XRSocketInteractor))
                    LastHeldBy = args.interactorObject;

                return;
            }

            mount = entered;
            MountedDoor = entered.Door;
            SetArmedIndicator(true);
            Placed?.Invoke(this);
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            if (detonating || !ReferenceEquals(args.interactorObject, mount))
                return;

            mount = null;
            MountedDoor = null;
            fireAt = -1f;   // gỡ bom xuống thì hủy đếm ngược
            SetArmedIndicator(false);
            Removed?.Invoke(this);
        }

        private void SetArmedIndicator(bool on)
        {
            if (armedIndicator != null)
                armedIndicator.SetActive(on);
        }
    }
}
