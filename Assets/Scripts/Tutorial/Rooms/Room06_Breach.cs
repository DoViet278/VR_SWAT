using System.Collections;
using UnityEngine;
using VRSwat.Doors;
using VRSwat.Tutorial;
using static CONST;

public class Room06_Breach : TutorialRoomBase
{
    [SerializeField] DoorController lockedDoor;   // Start Locked, cũng là lối ra (exitDoor để trống)
    [SerializeField] GameObject lockGlow;

    protected override IEnumerator Script()
    {
        Say(TutorialLine.R6_TRY_HANDLE);
        yield return WaitDoor(lockedDoor, DoorGoal.Rattled);

        Highlight(lockGlow);
        Say(TutorialLine.R6_SHOOT_LOCK);
        yield return WaitDoor(lockedDoor, DoorGoal.Breached);
    }
}