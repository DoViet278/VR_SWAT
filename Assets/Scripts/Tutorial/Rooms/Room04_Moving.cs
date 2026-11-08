using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using VRSwat.Doors;
using VRSwat.Tutorial;
using static CONST;

public class Room04_Moving : TutorialRoomBase
{
    [SerializeField] TargetGroup targets;
    [SerializeField] GameObject movers;    // cụm bia di chuyển, tắt sẵn

    protected override IEnumerator Script()
    {
        yield return SayAndWait(TutorialLine.R4_INTRO);
        movers.SetActive(true);
        Say(TutorialLine.R4_SHOOT);
        yield return WaitTargets(targets);   // hết bia địch
    }
}