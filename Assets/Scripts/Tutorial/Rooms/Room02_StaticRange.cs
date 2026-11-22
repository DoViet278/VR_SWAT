using System.Collections;
using UnityEngine;
using VRSwat.Tutorial;
using static CONST;

// Phòng 2: với mỗi khẩu súng, cầm súng rồi bắn trúng LightTarget của khẩu đó đủ số phát (mặc định 3).
// LightTarget tự nghe PLAYER_SHOOT_TARGET qua ObserverManager và chỉ đếm khi đúng loại súng,
// nên phòng chỉ cần chờ target.IsCompleted, không phải đăng ký event nào.
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
                Debug.LogWarning($"[Room02] {g.kind}: chưa gán LightTarget", this);
            else if (g.target.RequiredWeapon != g.kind)
                Debug.LogWarning($"[Room02] {g.kind}: LightTarget '{g.target.name}' đang yêu cầu {g.target.RequiredWeapon}", g.target);
        }
    }

    protected override IEnumerator Script()
    {
        yield return SayAndWait(TutorialLine.R2_INTRO);
        foreach (var g in guns)
        {
            Say(GrabLine(g.kind));
            yield return WaitSignal(Sig.GrabWeapon, source: g.gun);

            Say(TutorialLine.R2_SHOOT3);
            g.target.ResetTarget();
            yield return WaitFor(() => g.target.IsCompleted,
                                 () => g.target.Hits, g.target.HitsRequired, "Trúng");
        }
    }
}
