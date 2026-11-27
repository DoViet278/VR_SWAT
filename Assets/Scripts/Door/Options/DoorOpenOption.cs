using UnityEngine;

namespace VRSwat.Doors
{
    /// <summary>
    /// Mở hoặc đóng cửa bằng tay không — cánh tự bật ra, khỏi phải nắm tay nắm mà kéo.
    ///
    /// Cửa luôn bật về phía xa người chơi (<see cref="DoorController.Request"/> lo phần đó), nên
    /// đứng bên nào cũng không bị cánh cửa đập vào mặt.
    /// </summary>
    [CreateAssetMenu(fileName = "DoorOpenOption", menuName = "VR SWAT/Door Action/Mở - Đóng cửa")]
    public class DoorOpenOption : DoorActionOption
    {
        [Header("Hành động")]
        [SerializeField] private DoorAction action = DoorAction.Open;

        public override bool CanUse(in DoorActionContext ctx)
        {
            if (!base.CanUse(ctx))
                return false;

            // Cửa đang có người khác giữ (AI đang mở, hoặc tay đang nắm tay nắm) thì để yên.
            return ctx.Menu.DoorIsFree && !ctx.Door.IsGrabbed;
        }

        public override void Execute(in DoorActionContext ctx)
        {
            ctx.Menu.RequestDoor(action, ctx.PlayerPoint);
        }
    }
}
