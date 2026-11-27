using System.Collections;
using UnityEngine;
using VRSwat.Doors;
using VRSwat.Tutorial;
using static CONST;

// Phòng 5: dùng LockpickKit mở khóa cửa.
// Chỉ chờ trạng thái cửa / kit (polling), không chờ TutorialSignals nên không thể kẹt vì thiếu người phát.
public class Room05_Breach1 : TutorialRoomBase
{
    [SerializeField] DoorController lockedDoor;   // Start Locked, cũng là lối ra (exitDoor để trống hoặc trùng)
    [SerializeField] LockpickKit lockpickKit;     // kit đặt sẵn trong phòng (trống = bỏ bước "cầm kit")
    [SerializeField] GameObject kitGlow;          // viền sáng chỗ để kit (có thể trống)
    [SerializeField] GameObject lockGlow;         // viền sáng ổ khóa (có thể trống)

    protected override IEnumerator Script()
    {
        Say(TutorialLine.R5_TRY_HANDLE);
        yield return WaitDoor(lockedDoor, DoorGoal.Rattled);

        if (lockpickKit && !lockpickKit.IsHeld)
        {
            Highlight(kitGlow);
            Say(TutorialLine.R5_GRAB_LOCKPICK);
            yield return WaitFor(() => lockpickKit.IsHeld || !Locked);
        }

        Highlight(lockGlow);
        Say(TutorialLine.R5_PICK_LOCK);
        // Kit gọi door.Unlock() khi đầy thanh -> Locked thành Closed.
        // Cửa bị phá bằng đường khác (thuốc nổ...) cũng cho qua để phòng không kẹt.
        yield return WaitFor(() => !Locked);
    }

    bool Locked => lockedDoor.State == DoorState.Locked;
}
