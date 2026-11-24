using System.Collections;
using UnityEngine;
using VRSwat.Doors;
using VRSwat.Tutorial;
using static CONST;

// Phòng 6: dán C4 (BreachCharge) lên cửa rồi bấm Detonator để phá cửa.
// Chỉ kiểm tra trạng thái bom / cửa (polling), không chờ TutorialSignals nên không kẹt vì thiếu người phát.
// Tên class phải trùng tên file Room06_Breach2.cs (trước là Room06_Breach).
public class Room06_Breach2 : TutorialRoomBase
{
    [SerializeField] DoorController lockedDoor;   // Start Locked, có ChargeMount, cũng là lối ra
    [SerializeField] GameObject chargeGlow;       // viền sáng chỗ để C4 (có thể trống)
    [SerializeField] GameObject lockGlow;         // viền sáng ổ dán C4 trên cửa (có thể trống)
    [SerializeField] GameObject detonatorGlow;    // viền sáng chỗ để kíp nổ (có thể trống)

    protected override IEnumerator Script()
    {
        Say(TutorialLine.R6_TRY_HANDLE);
        yield return WaitDoor(lockedDoor, DoorGoal.Rattled);

        // Bước 1: dán C4 lên cửa.
        Highlight(chargeGlow ? chargeGlow : lockGlow);
        Say(TutorialLine.R6_PLACE_C4);
        bool shownMount = false;
        yield return WaitFor(() =>
        {
            // Cầm C4 lên rồi thì chuyển viền sáng sang ổ trên cửa.
            if (!shownMount && chargeGlow && lockGlow && AnyChargeHeldOrPlaced())
            {
                shownMount = true;
                Highlight(lockGlow);
            }
            return ChargeOnDoor() || Breached;
        });

        // Bước 2: cầm kíp, bóp cò. Gỡ bom ra lại thì quay về nhắc dán.
        while (!Breached)
        {
            Highlight(detonatorGlow);
            Say(TutorialLine.R6_DETONATE);
            yield return WaitFor(() => Breached || !ChargeOnDoor());

            if (!Breached)
            {
                Highlight(lockGlow);
                Say(TutorialLine.R6_PLACE_C4);
                yield return WaitFor(() => ChargeOnDoor() || Breached);
            }
        }
    }

    bool Breached => lockedDoor.State == DoorState.Breached;

    bool ChargeOnDoor()
    {
        var all = BreachCharge.All;
        for (int i = 0; i < all.Count; i++)
            if (all[i] && all[i].MountedDoor == lockedDoor) return true;
        return false;
    }

    static bool AnyChargeHeldOrPlaced()
    {
        var all = BreachCharge.All;
        for (int i = 0; i < all.Count; i++)
        {
            if (!all[i]) continue;
            if (all[i].IsPlaced) return true;
            var g = all[i].GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>();
            if (g && g.isSelected) return true;
        }
        return false;
    }
}
