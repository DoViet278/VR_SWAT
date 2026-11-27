using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRSwat.Doors
{
    /// <summary>
    /// Chọn xong là món đồ hiện ra ngay trong tay: kit phá khóa, bom C4, gương soi dưới cửa...
    ///
    /// Đồ hiện ra ở chế độ <see cref="HandItemGrip.HoldMode.HandOverOnGrip"/>: nó dính tay cho
    /// tới khi người chơi bóp grip một cái, từ đó là nắm bình thường và nhả grip là rơi. Phải vậy
    /// vì cả kit lẫn C4 đều chỉ dùng được sau khi rời tay — kit đưa vào ổ khóa, C4 dán lên cửa
    /// (<see cref="ChargeMount"/>). Để nó dính cứng như kíp nổ thì không bao giờ dán được bom.
    ///
    /// Dán C4 xong thì <see cref="DetonatorSpawner"/> tự đưa kíp vào tay, nên chuỗi
    /// chọn C4 → dán → bấm nổ chạy hết mà option này không phải biết gì về kíp.
    /// </summary>
    [CreateAssetMenu(fileName = "DoorSpawnItemOption", menuName = "VR SWAT/Door Action/Đưa đồ vào tay")]
    public class DoorSpawnItemOption : DoorActionOption
    {
        [Header("Đồ")]
        [Tooltip("Prefab món đồ, phải có XR Grab Interactable ở object gốc.")]
        [SerializeField] private GameObject prefab;

        [Tooltip("Kiểu cầm. Hand Over On Grip: dính tay tới khi bóp grip rồi cầm bình thường, " +
                 "thả ra được — dùng cho kit và C4. Stick Until Released: dính cứng, không rơi " +
                 "— chỉ dùng cho thứ không được phép đánh rơi.")]
        [SerializeField] private HandItemGrip.HoldMode holdMode = HandItemGrip.HoldMode.HandOverOnGrip;

        [Tooltip("Bật: món này đang có mặt đâu đó thì dòng bị xám, khỏi spawn chồng lên nhau. " +
                 "Tắt: chọn bao nhiêu lần ra bấy nhiêu món.")]
        [SerializeField] private bool oneAtATime = true;

        [Header("Phản hồi")]
        [SerializeField] private AudioClip spawnClip;

        [SerializeField, Range(0f, 1f)] private float hapticAmplitude = 0.3f;

        [SerializeField] private float hapticDuration = 0.08f;

        /// <summary>
        /// Món đang có mặt do option này sinh ra. Để trên asset chứ không phải trên cửa vì giới
        /// hạn là "cả màn chỉ một cái kit", không phải "mỗi cửa một cái".
        ///
        /// Không serialize: trạng thái runtime, vào Play lần sau phải sạch. <see cref="OnEnable"/>
        /// xóa lại phòng khi Editor tắt Domain Reload.
        /// </summary>
        [System.NonSerialized] private GameObject spawned;

        private void OnEnable()
        {
            spawned = null;
        }

        public override bool CanUse(in DoorActionContext ctx)
        {
            if (!base.CanUse(ctx) || prefab == null)
                return false;

            if (oneAtATime && spawned != null)
                return false;

            // Hai tay đang đầy thì chưa có chỗ nhận đồ.
            return HandItemSpawner.PickFreeHand(ctx.HandsRoot, ctx.Hand) != null;
        }

        public override void Execute(in DoorActionContext ctx)
        {
            // Ưu tiên chính tay vừa chọn trên panel: người chơi đang nhìn tay đó.
            XRBaseInteractor hand = HandItemSpawner.PickFreeHand(ctx.HandsRoot, ctx.Hand);

            if (hand == null)
                return;

            spawned = HandItemSpawner.SpawnIntoHand(
                prefab, hand, holdMode, spawnClip, hapticAmplitude, hapticDuration);
        }
    }
}
