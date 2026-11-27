using System.Collections;
using UnityEngine;
using VRSwat.Tutorial;
using static CONST;

// Phòng 3: người chơi bước vào vùng bắn (shootingArea) thì bia bắt đầu bật lên.
// Mỗi bia đứng 5s (Up Time trên PopUpTarget) rồi hạ. Bắn trúng khi bia đang đứng = 1 điểm.
// Đủ 'required' điểm thì qua phòng (mở exitDoor).
public class Room03_PopUp : TutorialRoomBase
{
    [Tooltip("PopUpSpawner: vừa bật bia, vừa đếm điểm, vừa giữ vùng bắn")]
    [SerializeField] PopUpSpawner spawner;
    [Tooltip("Số điểm cần để qua phòng")]
    [SerializeField, Min(1)] int required = 5;

    protected override IEnumerator Script()
    {
        OnSignal(Sig.CivilianHit, () => Note(TutorialLine.R3_CIVILIAN));

        if (spawner == null)
        {
            Debug.LogError("[Room03] Chưa gán PopUpSpawner", this);
            yield break;
        }

        // Chờ người chơi đứng vào vùng bắn (lấy luôn vùng của spawner)
        if (spawner.ShootingArea) yield return WaitZone(spawner.ShootingArea);

        spawner.ResetScore();
        spawner.Begin();
        Say(TutorialLine.R3_SHOOT);
        yield return WaitFor(() => spawner.Hits >= required, () => spawner.Hits, required, "Bia");   // bảng hiện "Bia: x/8"
        spawner.Stop(true);
    }
}
