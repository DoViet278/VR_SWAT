using System;
using UnityEngine;

namespace VRSwat.Tutorial
{
    public struct TutorialSignal
    {
        public string id;
        public UnityEngine.Object source;   // súng, cửa, NPC... (có thể null)
        public Vector3 position;            // chỗ nổ... (có thể bỏ trống)
    }

    public static class TutorialSignals
    {
        public static event Action<TutorialSignal> Raised;

        public static void Raise(string id, UnityEngine.Object source = null, Vector3 position = default)
        {
            Raised?.Invoke(new TutorialSignal { id = id, source = source, position = position });
        }
    }

    public static class Sig
    {
        public const string GrabWeapon      = "weapon.grabbed";     // source = súng
        public const string MagInserted     = "mag.inserted";       // source = súng
        public const string ShellTaken      = "shell.taken";
        public const string ShellInserted   = "shell.inserted";     // source = shotgun
        public const string PumpRacked      = "pump.racked";        // source = shotgun
        public const string CivilianHit     = "target.civilian";
        public const string FlashbangBang   = "flashbang.exploded"; // position = chỗ nổ
        public const string FragBang        = "frag.exploded";      // position = chỗ nổ
        public const string MenuOpened      = "cmd.menuopened";     // CommandMenu khi giữ Y
        public const string CmdFallIn       = "cmd.fallin";
        public const string CmdDoor         = "cmd.door";           // source = cửa
        public const string SquadStacked    = "squad.stacked";      // source = cửa
        public const string SquadDoorLocked = "squad.doorlocked";   // source = cửa
        public const string SquadRoomClear  = "squad.roomclear";    // source = cửa
        public const string PlayerShout     = "player.shout";
        public const string NpcSurrendered  = "npc.surrendered";    // source = NPC
        public const string NpcRestrained   = "npc.restrained";     // source = NPC
    }
}