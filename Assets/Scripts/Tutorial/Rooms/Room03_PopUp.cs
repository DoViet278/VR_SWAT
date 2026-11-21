using System.Collections;
using UnityEngine;
using VRSwat.Tutorial;
using static CONST;

// Phòng 3: người chơi bước vào vùng bắn (shootingArea) thì bia bắt đầu bật lên.
// Mỗi bia đứng 5s (Up Time trên PopUpTarget) rồi hạ. Bắn trúng khi bia đang đứng = 1 điểm.
// Đủ 'required' điểm thì qua phòng (mở exitDoor).
public class Room03_PopUp : TutorialRoomBase
{
    [SerializeField] TargetGroup targets;        // object cha của các PopUpTarget, đếm điểm
    [SerializeField] PopUpSpawner spawner;       // thường đặt cùng object với TargetGroup
    [SerializeField] TutorialArea shootingArea;  // vùng đứng bắn (gán cùng vùng vào PopUpSpawner)
    [Tooltip("Số điểm cần để qua phòng")]
    [SerializeField, Min(1)] int required = 8;

    protected override IEnumerator Script()
    {
        yield return SayAndWait(TutorialLine.R3_INTRO);
        OnSignal(Sig.CivilianHit, () => Note(TutorialLine.R3_CIVILIAN));

        // Chờ người chơi đứng vào vùng bắn
        if (shootingArea) yield return WaitZone(shootingArea);

        spawner.Begin();
        Say(TutorialLine.R3_SHOOT);
        yield return WaitTargets(targets, required);   // bảng hiện "Bia: x/8"
        spawner.Stop(true);
    }
}
