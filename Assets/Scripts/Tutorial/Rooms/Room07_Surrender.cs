using System.Collections;
using UnityEngine;
using VRSwat.Tutorial;

public class Room07_Surrender : TutorialRoomBase
{
    [SerializeField] GameObject suspect;   // đối tượng cầm súng (con tin không cần kéo)

    protected override IEnumerator Script()
    {
        Say(CONST.TutorialLine.R7_SHOUT);
        OnceOnSignal(Sig.PlayerShout, () => Note(CONST.TutorialLine.R7_SHOUT_AGAIN));
        yield return WaitSignal(Sig.NpcSurrendered, source: suspect);

        Say(CONST.TutorialLine.R7_CUFF);
        yield return WaitSignal(Sig.NpcRestrained, count: 2, label: "Đã còng");   // đối tượng + con tin
    }
}