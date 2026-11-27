using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRSwat.Tutorial;
using static CONST;

// Phòng 2: mỗi khẩu súng có 1 LightTarget riêng, bắn đủ phát (mặc định 3) để sáng hết đèn. Thứ tự nào cũng được.
// Bia do script điều khiển: vào scene thì tắt hết (Disarm). Phòng 2 bắt đầu thì Arm cả 3 bia,
// mỗi bia chỉ đếm phát bắn từ đúng khẩu súng của nó. Xong cả 3 bia thì mở cửa.
// Lời dẫn: đang cầm khẩu nào chưa xong thì bảo bắn khẩu đó, không cầm thì bảo cầm khẩu chưa xong đầu tiên.
public class Room02_StaticRange : TutorialRoomBase
{
    [System.Serializable]
    public class Gun
    {
        public WeaponType kind;
        public GameObject gun;
        public LightTarget target; 
    }
    [SerializeField] Gun[] guns;

    static string GrabLine(WeaponType k) => k switch
    {
        WeaponType.Pistol => TutorialLine.R2_GRAB_PISTOL,
        WeaponType.Rifle  => TutorialLine.R2_GRAB_RIFLE,
        _                 => TutorialLine.R2_GRAB_SHOTGUN
    };

    void Start()
    {
        foreach (var g in guns)
        {
            if (g.target == null)
                Debug.LogError($"[Room02] {g.kind}: chưa gán LightTarget", this);
            if (g.gun == null)
                Debug.LogError($"[Room02] {g.kind}: chưa gán súng", this);
            if (g.target != null && !IsComplete)
            {
                g.target.ResetTarget();
                g.target.Disarm();   // chưa tới lượt thì bia không nhận đạn
            }
        }
    }

    protected override IEnumerator Script()
    {
        Debug.Log("[Room02] Script bắt đầu: Arm cả 3 bia", this);
        foreach (var g in guns)
        {
            if (g.target == null) continue;
            g.target.ResetTarget();
            g.target.Arm(g.kind);   // bia chỉ nhận đạn từ đúng loại súng của nó (khẩu nào cùng loại cũng được)
        }

        while (!AllDone())
        {
            Gun held = HeldUnfinished();
            if (held == null)
            {
                Say(GrabLine(FirstUnfinished().kind));
                yield return WaitFor(() => HeldUnfinished() != null || AllDone(), TotalHits, TotalRequired(), "Đèn");
                continue;
            }

            Say(TutorialLine.R2_SHOOT3);
            yield return WaitFor(() => held.target.IsCompleted || !IsHeldByHand(held.gun),
                                 TotalHits, TotalRequired(), "Đèn");
        }

        foreach (var g in guns)
            if (g.target != null) g.target.Disarm();   // đèn giữ xanh, bia không nhận thêm
    }

    bool AllDone() => FirstUnfinished() == null;

    Gun FirstUnfinished()
    {
        foreach (var g in guns)
            if (g.target != null && !g.target.IsCompleted) return g;
        return null;
    }

    // Khẩu đang cầm trên tay mà bia của nó chưa xong
    Gun HeldUnfinished()
    {
        foreach (var g in guns)
            if (g.target != null && !g.target.IsCompleted && IsHeldByHand(g.gun)) return g;
        return null;
    }

    int TotalHits()
    {
        int n = 0;
        foreach (var g in guns)
            if (g.target != null) n += Mathf.Min(g.target.Hits, g.target.HitsRequired);
        return n;
    }

    int TotalRequired()
    {
        int n = 0;
        foreach (var g in guns)
            if (g.target != null) n += g.target.HitsRequired;
        return n;
    }

    // Đang nằm trong tay người chơi (không tính socket/giá súng)
    static bool IsHeldByHand(GameObject go)
    {
        if (!go) return false;
        var grab = go.GetComponentInChildren<XRBaseInteractable>();
        if (!grab || !grab.isSelected) return false;
        foreach (var it in grab.interactorsSelecting)
            if (!(it is XRSocketInteractor)) return true;
        return false;
    }
}
