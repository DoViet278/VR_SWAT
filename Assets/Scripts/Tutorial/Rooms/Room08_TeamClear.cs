using System.Collections;
using UnityEngine;
using VRSwat.Doors;
using VRSwat.Tutorial;
using static CONST;

public class Room08_TeamClear : TutorialRoomBase
{
    [SerializeField] DoorController doorA;   // mở được
    [SerializeField] DoorController doorB;   // Start Locked

    protected override IEnumerator Script()
    {
        Say(TutorialLine.R8_OPEN_CLEAR_A);
        yield return WaitSignal(Sig.SquadRoomClear, source: doorA.gameObject);

        Say(TutorialLine.R8_TRY_B);
        yield return WaitSignal(Sig.SquadDoorLocked, source: doorB.gameObject);

        Say(TutorialLine.R8_BREACH_B);
        yield return WaitSignal(Sig.SquadRoomClear, source: doorB.gameObject);
    }
}