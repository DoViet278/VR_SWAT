using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using VRSwat.Doors;
using VRSwat.Tutorial;
using static CONST;

public class Room08_Teammates : TutorialRoomBase
{
    [SerializeField] DoorController stackDoor;
    [Tooltip("Phúc nối câu tự giới thiệu của 2 đồng đội vào đây")]
    [SerializeField] UnityEvent teammatesIntro;

    protected override IEnumerator Script()
    {
        teammatesIntro.Invoke();
        Say(TutorialLine.R8_OPEN_MENU);
        yield return WaitSignal(Sig.MenuOpened);

        Say(TutorialLine.R8_FALLIN);
        yield return WaitSignal(Sig.CmdFallIn);

        Say(TutorialLine.R8_STACKUP);
        yield return WaitSignal(Sig.SquadStacked, source: stackDoor.gameObject);
    }
}