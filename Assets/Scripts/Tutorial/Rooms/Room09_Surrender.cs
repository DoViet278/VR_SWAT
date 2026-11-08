using System.Collections;
using UnityEngine;
using VRSwat.Tutorial;

public class Room09_Surrender : TutorialRoomBase
{
    [SerializeField] GameObject suspect;   // đối tượng cầm súng (con tin không cần kéo)

    protected override IEnumerator Script()
    {
        yield return SayAndWait(CONST.TutorialLine.R9_INTRO);

        Say(CONST.TutorialLine.R9_SHOUT);
        OnceOnSignal(Sig.PlayerShout, () => Note(CONST.TutorialLine.R9_SHOUT_AGAIN));
        yield return WaitSignal(Sig.NpcSurrendered, source: suspect);

        Say(CONST.TutorialLine.R9_CUFF);
        yield return WaitSignal(Sig.NpcRestrained, count: 2, label: "Đã còng");   // đối tượng + con tin
    }
}