using System.Collections;
using UnityEngine;
using VRSwat.Doors;
using VRSwat.Tutorial;
using static CONST;

public class Room03_PopUp : TutorialRoomBase
{
    [SerializeField] TargetGroup targets;
    [SerializeField] GameObject spawner;   // PopUpSpawner của Việt, tắt sẵn
    [SerializeField] int required = 8;

    protected override IEnumerator Script()
    {
        yield return SayAndWait(TutorialLine.R3_INTRO);
        OnSignal(Sig.CivilianHit, () => Note(TutorialLine.R3_CIVILIAN));

        spawner.SetActive(true);
        Say(TutorialLine.R3_SHOOT);
        yield return WaitTargets(targets, required);
        spawner.SetActive(false);
    }
}