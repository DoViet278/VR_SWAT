using System.Collections.Generic;
using System.Reflection;

public static class CONST
{
    public static class TutorialLine
    {
        public const string COMMON_ROOM_DONE = "common_room_done";

        public const string R1_WELCOME = "r1_welcome";
        public const string R1_MOVE = "r1_move";
        public const string R1_OPEN_DOOR = "r1_open_door";

        public const string R2_INTRO = "r2_intro";
        public const string R2_GRAB_PISTOL = "r2_grab_pistol";
        public const string R2_GRAB_RIFLE = "r2_grab_rifle";
        public const string R2_GRAB_SHOTGUN = "r2_grab_shotgun";
        public const string R2_SHOOT3 = "r2_shoot3";
        public const string R2_RELOAD_MAG = "r2_reload_mag";
        public const string R2_SHELL_TAKE = "r2_shell_take";
        public const string R2_SHELL_INSERT = "r2_shell_insert";
        public const string R2_PUMP = "r2_pump";
        public const string R2_FINISH = "r2_finish";

        public const string R3_INTRO = "r3_intro";
        public const string R3_SHOOT = "r3_shoot";
        public const string R3_CIVILIAN = "r3_civilian";

        public const string R4_INTRO = "r4_intro";
        public const string R4_SHOOT = "r4_shoot";

        public const string R5_INTRO = "r5_intro";
        public const string R5_FLASH = "r5_flash";
        public const string R5_FRAG = "r5_frag";

        public const string R6_TRY_HANDLE = "r6_try_handle";
        public const string R6_SHOOT_LOCK = "r6_shoot_lock";

        public const string R7_INTRO = "r7_intro";
        public const string R7_OPEN_MENU = "r7_open_menu";
        public const string R7_FALLIN = "r7_fallin";
        public const string R7_STACKUP = "r7_stackup";

        public const string R8_OPEN_CLEAR_A = "r8_open_clear_a";
        public const string R8_TRY_B = "r8_try_b";
        public const string R8_BREACH_B = "r8_breach_b";

        public const string R9_INTRO = "r9_intro";
        public const string R9_SHOUT = "r9_shout";
        public const string R9_SHOUT_AGAIN = "r9_shout_again";
        public const string R9_CUFF = "r9_cuff";

        public const string R10_SUMMARY = "r10_summary";
        public const string R10_EXIT = "r10_exit";
        
        public static IEnumerable<string> All()
        {
            foreach (var f in typeof(TutorialLine).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.IsLiteral && f.FieldType == typeof(string))
                    yield return (string)f.GetRawConstantValue();
        }
    }
}