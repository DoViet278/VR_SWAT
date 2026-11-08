using System.Collections;
using UnityEngine;
using VRSwat.Doors;
using VRSwat.Tutorial;
using static CONST;

public class Room05_Grenades : TutorialRoomBase
{
    [SerializeField] TutorialArea roomA, roomB;          // 2 phòng nhỏ có cửa hé
    [SerializeField] GameObject roomAGlow, roomBGlow;    // viền sáng khe cửa, tắt sẵn

    protected override IEnumerator Script()
    {
        yield return SayAndWait(TutorialLine.R5_INTRO);

        Highlight(roomAGlow);
        Say(TutorialLine.R5_FLASH);
        yield return WaitSignal(Sig.FlashbangBang, inside: roomA);

        Highlight(roomBGlow);
        Say(TutorialLine.R5_FRAG);
        yield return WaitSignal(Sig.FragBang, inside: roomB);
        Highlight(null);
    }
}