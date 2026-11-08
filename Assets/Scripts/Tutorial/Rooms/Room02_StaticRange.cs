using System.Collections;
using UnityEngine;
using VRSwat.Doors;
using VRSwat.Tutorial;
using static CONST;

public class Room02_StaticRange : TutorialRoomBase
{
    public enum GunKind { Pistol, Rifle, Shotgun }

    [System.Serializable]
    public class Gun
    {
        public GunKind kind;
        public GameObject gun;
        public TargetGroup targets;       // bia riêng của khẩu này
        public GameObject pouchGlow, portGlow, pumpGlow;   // chỉ shotgun, tắt sẵn
    }
    [SerializeField] Gun[] guns;          // 3 phần tử theo thứ tự

    static string GrabLine(GunKind k) => k switch
    {
        GunKind.Pistol => TutorialLine.R2_GRAB_PISTOL,
        GunKind.Rifle  => TutorialLine.R2_GRAB_RIFLE,
        _              => TutorialLine.R2_GRAB_SHOTGUN
    };

    protected override IEnumerator Script()
    {
        yield return SayAndWait(TutorialLine.R2_INTRO);
        foreach (var g in guns)
        {
            Say(GrabLine(g.kind));
            yield return WaitSignal(Sig.GrabWeapon, source: g.gun);

            Say(TutorialLine.R2_SHOOT3);
            yield return WaitTargets(g.targets, 3);

            if (g.kind != GunKind.Shotgun)
            {
                Say(TutorialLine.R2_RELOAD_MAG);
                yield return WaitSignal(Sig.MagInserted, source: g.gun);
            }
            else
            {
                Highlight(g.pouchGlow);
                Say(TutorialLine.R2_SHELL_TAKE);
                yield return WaitSignal(Sig.ShellTaken);

                Highlight(g.portGlow);
                Say(TutorialLine.R2_SHELL_INSERT);
                yield return WaitSignal(Sig.ShellInserted, source: g.gun);

                Highlight(g.pumpGlow);
                Say(TutorialLine.R2_PUMP);
                yield return WaitSignal(Sig.PumpRacked, source: g.gun);
                Highlight(null);
            }

            Say(TutorialLine.R2_FINISH);
            yield return WaitTargets(g.targets);   // hết bia của khẩu này
        }
    }
}