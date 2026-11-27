using System.Collections;
using UnityEngine;
using VRSwat.Tutorial;
using static CONST;

// Phòng 4: ném lựu đạn choáng và lựu đạn nổ vào đúng vòng tròn của nó.
// Thứ tự nào cũng được. Phòng hoàn thành khi CẢ HAI vòng đều đã trúng.
// Ném trượt hoặc ném nhầm vòng thì không tính, cứ lấy quả khác ném lại.
public class Room04_Grenades : TutorialRoomBase
{
    [Header("Vòng mục tiêu")]
    [SerializeField] GrenadeTargetZone flashZone;   // vòng cho lựu đạn choáng (flashbang)
    [SerializeField] GrenadeTargetZone fragZone;    // vòng cho lựu đạn nổ (frag)

    protected override IEnumerator Script()
    {
        flashZone.ResetZone();
        fragZone.ResetZone();

        OnSignal(Sig.FlashbangBang, s => TryHit(flashZone, s.position, fragZone, TutorialLine.R4_FRAG));
        OnSignal(Sig.FragBang,      s => TryHit(fragZone,  s.position, flashZone, TutorialLine.R4_FLASH));

        Say(TutorialLine.R4_FLASH);
        yield return WaitFor(() => flashZone.IsDone && fragZone.IsDone,
                             () => (flashZone.IsDone ? 1 : 0) + (fragZone.IsDone ? 1 : 0),
                             2, "Trúng vòng");
    }

    void TryHit(GrenadeTargetZone zone, Vector3 blastPos, GrenadeTargetZone other, string otherLine)
    {
        if (zone.IsDone || !zone.Contains(blastPos)) return;

        zone.MarkDone();
        if (other.IsDone) return;               // xong cả 2: WaitFor tự kết thúc

        Narrator.Instance.PlayStepDone();
        Say(otherLine);                          // nhắc quả còn lại (lời nhắc lặp lại cũng dùng câu này)
    }
}
