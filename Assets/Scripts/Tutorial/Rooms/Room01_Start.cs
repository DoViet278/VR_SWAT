using System.Collections;
using UnityEngine;
using VRSwat.Doors;
using VRSwat.Tutorial;

public class Room01_Start : TutorialRoomBase
{
    [SerializeField] GameObject floorArrows;     // mũi tên dưới sàn, tắt sẵn
    [SerializeField] TutorialArea doorArea;      // vạch trước cửa
    [SerializeField] DoorController firstDoor;   // cửa không khóa, cũng là lối ra (exitDoor để trống)

    protected override IEnumerator Script()
    {
        yield return SayAndWait(CONST.TutorialLine.R1_WELCOME);

        Highlight(floorArrows);
        Say(CONST.TutorialLine.R1_MOVE);
        yield return WaitZone(doorArea);

        Highlight(null);
        Say(CONST.TutorialLine.R1_OPEN_DOOR);
        yield return WaitDoor(firstDoor, DoorGoal.OpenedPastAngle, 45f);
    }
}