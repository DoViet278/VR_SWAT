using System.Collections.Generic;
using System.Reflection;

public static class CONST
{
    public static class TutorialLine
    {
        // Key lời dẫn tutorial. Giá trị = key trong asset TutorialLines (và tên file Resources/Voice/<key>).
        // Comment cuối dòng = nội dung gợi ý. Số phòng = thứ tự phòng hiện tại (Room01..Room09).

        // Chung
        public const string COMMON_ROOM_DONE = "common_room_done";       // Tốt lắm! Đi tiếp sang phòng sau.

        // Phòng 1 (Room01_Start): di chuyển, mở cửa. Chỉ phòng 1 có intro.
        public const string R1_WELCOME = "r1_welcome";                   // Chào mừng đến với khóa huấn luyện SWAT VR.
        public const string R1_MOVE = "r1_move";                         // Dùng cần điều khiển để đi tới vạch trước cửa.
        public const string R1_OPEN_DOOR = "r1_open_door";               // Nắm tay nắm và kéo cửa để mở.

        // Phòng 2 (Room02_StaticRange): mỗi súng một bia đèn, thứ tự nào cũng được.
        public const string R2_GRAB_PISTOL = "r2_grab_pistol";           // Cầm khẩu súng ngắn trên bàn.
        public const string R2_GRAB_RIFLE = "r2_grab_rifle";             // Cầm khẩu súng trường trên bàn.
        public const string R2_GRAB_SHOTGUN = "r2_grab_shotgun";         // Cầm khẩu shotgun trên bàn.
        public const string R2_SHOOT3 = "r2_shoot3";                     // Bắn vào bia của khẩu này cho tới khi sáng hết đèn.

        // Phòng 3 (Room03_PopUp): bia bật lên.
        public const string R3_SHOOT = "r3_shoot";                       // Bia sẽ bật lên vài giây, bắn trúng trước khi bia hạ xuống.
        public const string R3_CIVILIAN = "r3_civilian";                 // Cẩn thận! Đó là dân thường, đừng bắn.

        // Phòng 4 (Room04_Grenades): ném lựu đạn vào đúng vòng, thứ tự nào cũng được.
        public const string R4_FLASH = "r4_flash";                       // Rút chốt và ném lựu đạn choáng vào vòng của nó.
        public const string R4_FRAG = "r4_frag";                         // Rút chốt và ném lựu đạn nổ vào vòng của nó.

        // Phòng 5 (Room05_Breach1): mở khóa bằng LockpickKit.
        public const string R5_TRY_HANDLE = "r5_try_handle";             // Thử vặn tay nắm cửa.
        public const string R5_GRAB_LOCKPICK = "r5_grab_lockpick";       // Cửa bị khóa. Cầm bộ dụng cụ mở khóa.
        public const string R5_PICK_LOCK = "r5_pick_lock";               // Đưa dụng cụ vào ổ khóa và giữ tới khi mở được.

        // Phòng 6 (Room06_Breach2): dán C4 rồi kích nổ.
        public const string R6_TRY_HANDLE = "r6_try_handle";             // Thử vặn tay nắm cửa.
        public const string R6_PLACE_C4 = "r6_place_c4";                 // Cửa bị khóa. Cầm khối C4 và dán lên cửa.
        public const string R6_DETONATE = "r6_detonate";                 // Lùi lại, cầm kíp nổ và bóp cò để phá cửa.

        // Phòng 7 (Room07_Surrender): ra lệnh đầu hàng, còng tay.
        public const string R7_SHOUT = "r7_shout";                       // Hô lớn để đối tượng đầu hàng.
        public const string R7_SHOUT_AGAIN = "r7_shout_again";           // Hắn chưa chịu. Hô lại lần nữa.
        public const string R7_CUFF = "r7_cuff";                         // Còng tay đối tượng và con tin.

        // Phòng 8 (Room08_Teammates): ra lệnh đồng đội.
        public const string R8_OPEN_MENU = "r8_open_menu";               // Mở menu ra lệnh cho đồng đội.
        public const string R8_FALLIN = "r8_fallin";                     // Chọn lệnh "Theo tôi" để đồng đội đi theo.
        public const string R8_STACKUP = "r8_stackup";                   // Ra lệnh đồng đội áp sát cửa.

        // Phòng 9 (Room09_TeamClear): cùng đội dọn phòng.
        public const string R9_OPEN_CLEAR_A = "r9_open_clear_a";         // Ra lệnh đội mở cửa A và dọn phòng.
        public const string R9_TRY_B = "r9_try_b";                       // Ra lệnh đội kiểm tra cửa B.
        public const string R9_BREACH_B = "r9_breach_b";                 // Cửa B bị khóa. Ra lệnh đội phá cửa và dọn phòng.

        public static IEnumerable<string> All()
        {
            foreach (var f in typeof(TutorialLine).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.IsLiteral && f.FieldType == typeof(string))
                    yield return (string)f.GetRawConstantValue();
        }
    }
    
    // Thoại nhân vật trong game (từ swat4_voice_lines.csv). Giá trị = id gốc = tiền tố tên file audio.
    public static class VoiceLine
    {
        // Người chơi (đội trưởng)
        public static class Player
        {
            // Announce/Shout
            public const string ANNOUNCED_COMPLY = "ld_AnnouncedComply"; // Police! Put your hands up!
            public const string ANNOUNCED_COMPLY_WITH_GUN = "ld_AnnouncedComplyWithGun"; // Police! Drop your weapons!

            // Other
            public const string ARRESTED_SUSPECT = "ld_ArrestedSuspect"; // You have the right to remain silent.
            public const string REASSURED_AGGRESSIVE_HOSTAGE = "ld_ReassuredAggressiveHostage"; // Stay quiet..
            public const string REASSURED_PASSIVE_HOSTAGE = "ld_ReassuredPassiveHostage"; // Youre safe now.
            public const string MP_MISSION_DONE_SUCCESS = "ldMP_MissionDoneSuccess"; // Nice work. Let's head back to HQ.

            // Player order
            public const string ORDERED_BANG_N_CLEAR = "ld_OrderedBangNClear"; // Bang and clear!
            public const string ORDERED_BREACH_BANG_N_MAKE_ENTRY = "ld_OrderedBreachBangNMakeEntry"; // Bang and make entry!
            public const string ORDERED_BREACH_N_CLEAR = "ld_OrderedBreachNClear"; // Open and clear!
            public const string ORDERED_BREACH_N_MAKE_ENTRY = "ld_OrderedBreachNMakeEntry"; // Make entry and clear!
            public const string ORDERED_DEPLOY_BANG = "ld_OrderedDeployBang"; // Deploy flashbang.
            public const string ORDERED_FALL_IN = "ld_OrderedFallIn"; // Fall in.
            public const string ORDERED_MOVE_N_CLEAR = "ld_OrderedMoveNClear"; // Move and clear.
            public const string ORDERED_PICK_LOCK = "ld_OrderedPickLock"; // Pick the lock.
            public const string COMMAND_ZULU_GO_GO_GO = "ld_CommandZuluGoGoGo"; // Go, Go, Go!

            // Reaction
            public const string REACTED_BANG = "ld_ReactedBang"; // Aah!
            public const string REACTED_PEPPER = "ld_ReactedPepper"; // Hey!

            // Report
            public const string REPORTED_CIVILIAN_SECURED = "ld_ReportedCivilianSecured"; // TOC, this is entry team. Civilian secured and ready to evacuate.
            public const string REPORTED_DEAD_SUSPECT = "ld_ReportedDeadSuspect"; // TOC, this is entry team. We have a dead suspect.
            public const string REPORTED_DEPLOYING_C2 = "ld_ReportedDeployingC2"; // Deploying charge, prepare for detonation..
            public const string REPORTED_ONE_SUSPECT_SEEN = "ld_ReportedOneSuspectSeen"; // Suspect is visible.
            public const string REPORTED_PICKING_LOCK = "ld_ReportedPickingLock"; // Picking the lock...
        }

        // Đồng đội SWAT
        public static class Officer
        {
            // Announce/Shout
            public const string ANNOUNCED_CIVILIAN_COMPLIED = "b1_AnnouncedCivilianComplied"; // Contact has complied!
            public const string ANNOUNCED_CLEAR = "b1_AnnouncedClear"; // Area clear!
            public const string ANNOUNCED_DOOR_CLOSED = "b1_AnnouncedDoorClosed"; // Door closed.
            public const string ANNOUNCED_LOST_TARGET = "b1_AnnouncedLostTarget"; // Suspect is gone. Orders?
            public const string ANNOUNCED_STILL_COVERING = "b1_AnnouncedStillCovering"; // Still clear.
            public const string ANNOUNCED_SUSPECT_COMPLIED = "b1_AnnouncedSuspectComplied"; // Suspect complied.

            // Compliance
            public const string COMPLETED_FALL_IN = "b1_CompletedFallIn"; // In position.

            // Other
            public const string ASKED_LEAD_TO_RESTRAIN = "b1_AskedLeadToRestrain"; // You ready to cuff, Sir?
            public const string COULDNT_BREACH_LOCKED = "b1_CouldntBreachLocked"; // It's locked; confirm breach?
            public const string COULDNT_COMPLETE_MOVE = "b1_CouldntCompleteMove"; // Can't get there.
            public const string COVERED_COMPLIED_UNRESTRAINED = "b1_CoveredCompliedUnrestrained"; // Stay down.
            public const string MISSION_DONE_SUCCESS = "b1_MissionDoneSuccess"; // Good work, guys. Let's head to debrief.
            public const string REASSURED_AGGRESSIVE_HOSTAGE = "b1_ReassuredAggressiveHostage"; // It's just procedure.
            public const string REASSURED_PASSIVE_HOSTAGE = "b1_ReassuredPassiveHostage"; // Easy, good guys are here.
            public const string STARTED_CLEARING = "b1_StartedClearing"; // Go, go, go! Time to move!
            public const string TOLD_PLAYER_BLOCKS_DOOR = "b1_ToldPlayerBlocksDoor"; // You're blockin' the door.
            public const string TOLD_PLAYER_TO_MOVE = "b1_ToldPlayerToMove"; // You're in my way.

            // Reaction
            public const string REACTED_BANG = "b1_ReactedBang"; // Blinded!
            public const string REACTED_FIRST_SHOT = "b1_ReactedFirstShot"; // Careful!

            // Reply/Ack
            public const string REPLIED_FALL_IN = "b1_RepliedFallIn"; // Roger -- moving to you.
            public const string REPLIED_MOVE_TO = "b1_RepliedMoveTo"; // Understood -- moving.
            public const string REPLIED_TOC = "b1_RepliedTOC"; // Roger.
            public const string REPLIED_TO_TOC_MISSION_START = "b1_RepliedToTOCMissionStart"; // Copy. On our way.

            // Report
            public const string REPORTED_ALREADY_BUSY = "b1_ReportedAlreadyBusy"; // I'm busy!
            public const string REPORTED_CIVILIAN_RESTRAINED = "b1_ReportedCivilianRestrained"; // Civilian restrained.
            public const string REPORTED_CIVILIAN_SPOTTED = "b1_ReportedCivilianSpotted"; // Gotta contact...
            public const string REPORTED_CONTINUING_CLEAR2_MINUS = "b1_ReportedContinuingClear2Minus"; // Moving forward!
            public const string REPORTED_CONTINUING_CLEAR2_PLUS = "b1_ReportedContinuingClear2Plus"; // Moving forward! Trailers!
            public const string REPORTED_DEAD_SUSPECT = "b1_ReportedDeadSuspect"; // Suspect down!
            public const string REPORTED_DEPLOYING_BREACH_SG = "b1_ReportedDeployingBreachSG"; // Breaching now.
            public const string REPORTED_DEPLOYING_C2 = "b1_ReportedDeployingC2"; // Beginning breach.
            public const string REPORTED_DEPLOYING_FLASHBANG = "b1_ReportedDeployingFlashbang"; // Droppin' bang.
            public const string REPORTED_DEPLOYING_TOOLKIT = "b1_ReportedDeployingToolkit"; // Unlocking it.
            public const string REPORTED_DOOR_ALREADY_CLOSED = "b1_ReportedDoorAlreadyClosed"; // Already closed.
            public const string REPORTED_DOOR_BLOCKED = "b1_ReportedDoorBlocked"; // Something's in the way!
            public const string REPORTED_DOOR_LOCKED = "b1_ReportedDoorLocked"; // Door locked.
            public const string REPORTED_DOOR_OPEN = "b1_ReportedDoorOpen"; // Door's unlocked.
            public const string REPORTED_DOOR_UNLOCKED = "b1_ReportedDoorUnlocked"; // It's unlocked.
            public const string REPORTED_GENERIC_ORDER_REPLY = "b1_ReportedGenericOrderReply"; // Copy - on it.
            public const string REPORTED_NO_GUN = "b1_ReportedNoGun"; // Nothing there
            public const string REPORTED_SUSPECT_DOWN = "b1_ReportedSuspectDown"; // Bad guy down!
            public const string REPORTED_SUSPECT_NEUTRALIZED = "b1_ReportedSuspectNeutralized"; // Suspect down!
            public const string REPORTED_SUSPECT_RESTRAINED = "b1_ReportedSuspectRestrained"; // Suspect secured.
            public const string REPORTED_SUSPECTS_AND_CIVS_SEEN = "b1_ReportedSuspectsAndCivsSeen"; // We got a party... Numerous guys and guns...
            public const string REPORTED_SUSPECT_SPOTTED = "b1_ReportedSuspectSpotted"; // Suspect in sight!
            public const string REPORTED_SUSPECT_WILL_NOT_COMPLY = "b1_ReportedSuspectWillNotComply"; // This one won't give up.
            public const string REPORTED_TASER_UNAVAILABLE = "b1_ReportedTaserUnavailable"; // None availble!
            public const string REPORTED_THRESHOLD_CLEAR = "b1_ReportedThresholdClear"; // No visible threats.
            public const string REPORTED_TRAP_DISABLED = "b1_ReportedTrapDisabled"; // Trap removed.
            public const string REPORTED_TWO_CIVILIANS_SEEN = "b1_ReportedTwoCiviliansSeen"; // Two unarmed contacts...
            public const string REPORTED_TWO_SUSPECTS_SEEN = "b1_ReportedTwoSuspectsSeen"; // Two armed suspects visible...
            public const string REPORTED_WEAPON_SECURED = "b1_ReportedWeaponSecured"; // Weapon secured!
        }

        // Nghi phạm chung (ms1, _mXX = biến thể theo màn)
        public static class Suspect
        {
            // Announce/Shout
            public const string ANNOUNCED_COMPLIANT = "ms1_AnnouncedCompliant"; // I give up! Don't shoot!
            public const string ANNOUNCED_COMPLIANT_M01 = "ms1_AnnouncedCompliant_m01"; // Hey, I was just lookin' for dinner, man....
            public const string ANNOUNCED_COMPLIANT_M05 = "ms1_AnnouncedCompliant_m05"; // I don't give a damn about the fucking car!
            public const string ANNOUNCED_COMPLIANT_M07 = "ms1_AnnouncedCompliant_m07"; // How the hell did we get busted?
            public const string ANNOUNCED_COMPLIANT_M09 = "ms1_AnnouncedCompliant_m09"; // No! Don't shoot! The darkness... is coming...
            public const string ANNOUNCED_COMPLIANT_M10 = "ms1_AnnouncedCompliant_m10"; // This isn't over.
            public const string ANNOUNCED_COMPLIANT_M11 = "ms1_AnnouncedCompliant_m11"; // Nothin' here! Leave me alone!
            public const string ANNOUNCED_COMPLIANT_M12 = "ms1_AnnouncedCompliant_m12"; // You think you've won, don't you?
            public const string ANNOUNCED_FLEE = "ms1_AnnouncedFlee"; // Run!
            public const string ANNOUNCED_FLEE_M01 = "ms1_AnnouncedFlee_m01"; // I ain't facin' the cops...
            public const string ANNOUNCED_FLEE_M07 = "ms1_AnnouncedFlee_m07"; // There's a raid comin' thru!
            public const string ANNOUNCED_FLEE_M08 = "ms1_AnnouncedFlee_m08"; // Pull back!
            public const string ANNOUNCED_FLEE_M09 = "ms1_AnnouncedFlee_m09"; // Better to run than to fall!
            public const string ANNOUNCED_INVESTIGATE = "ms1_AnnouncedInvestigate"; // Take a look...
            public const string ANNOUNCED_INVESTIGATE_M04 = "ms1_AnnouncedInvestigate_m04"; // That's no clubbie!
            public const string ANNOUNCED_INVESTIGATE_M05 = "ms1_AnnouncedInvestigate_m05"; // You hear that?
            public const string ANNOUNCED_INVESTIGATE_M07 = "ms1_AnnouncedInvestigate_m07"; // Something's weird here?
            public const string ANNOUNCED_INVESTIGATE_M09 = "ms1_AnnouncedInvestigate_m09"; // That's not the steps of our brothers!
            public const string ANNOUNCED_INVESTIGATE_M10 = "ms1_AnnouncedInvestigate_m10"; // That bastard's around here somewhere!
            public const string ANNOUNCED_INVESTIGATE_M13 = "ms1_AnnouncedInvestigate_m13"; // Too many stupid goddamned scientists...
            public const string ANNOUNCED_NON_COMPLIANT = "ms1_AnnouncedNonCompliant"; // You won't stop us!
            public const string ANNOUNCED_NON_COMPLIANT_M01 = "ms1_AnnouncedNonCompliant_m01"; // Not again, damn it!
            public const string ANNOUNCED_NON_COMPLIANT_M03 = "ms1_AnnouncedNonCompliant_m03"; // Not this time, coppers!
            public const string ANNOUNCED_NON_COMPLIANT_M04 = "ms1_AnnouncedNonCompliant_m04"; // Stay outta this!
            public const string ANNOUNCED_NON_COMPLIANT_M08 = "ms1_AnnouncedNonCompliant_m08"; // I don't think so, not today!
            public const string ANNOUNCED_NON_COMPLIANT_M09 = "ms1_AnnouncedNonCompliant_m09"; // I will die defending Taronne, whose words are Truth!
            public const string ANNOUNCED_NON_COMPLIANT_M10 = "ms1_AnnouncedNonCompliant_m10"; // No mercy!
            public const string ANNOUNCED_NON_COMPLIANT_M11 = "ms1_AnnouncedNonCompliant_m11"; // You won't get us all!
            public const string ANNOUNCED_NON_COMPLIANT_M12 = "ms1_AnnouncedNonCompliant_m12"; // Die, for America!
            public const string ANNOUNCED_NON_COMPLIANT_M13 = "ms1_AnnouncedNonCompliant_m13"; // No! There's a higher law!
            public const string ANNOUNCED_RESTRAINED = "ms1_AnnouncedRestrained"; // Goddamn cops.
            public const string ANNOUNCED_RESTRAINED_M01 = "ms1_AnnouncedRestrained_m01"; // Damn it, I'm clean, didn't do anything...
            public const string ANNOUNCED_RESTRAINED_M04 = "ms1_AnnouncedRestrained_m04"; // Oh shit, my old man's gonna be pissed...
            public const string ANNOUNCED_RESTRAINED_M06 = "ms1_AnnouncedRestrained_m06"; // Didn't mean t'hurt anyone...
            public const string ANNOUNCED_RESTRAINED_M07 = "ms1_AnnouncedRestrained_m07"; // Goddamn cops, sticking their noses where they don't belong...
            public const string ANNOUNCED_RESTRAINED_M09 = "ms1_AnnouncedRestrained_m09"; // Forgive me, Taronne... my fear made me weak....
            public const string ANNOUNCED_RESTRAINED_M10 = "ms1_AnnouncedRestrained_m10"; // You've got me... but we'll get that bastard yet!
            public const string ANNOUNCED_RESTRAINED_M11 = "ms1_AnnouncedRestrained_m11"; // This better not be a double cross!
            public const string ANNOUNCED_RESTRAINED_M12 = "ms1_AnnouncedRestrained_m12"; // We ain't done yet! You won't get us all!
            public const string ANNOUNCED_RESTRAINED_M13 = "ms1_AnnouncedRestrained_m13"; // You should be arresting the scientists who work here, not us!
            public const string ANNOUNCED_SPOTTED_OFFICER = "ms1_AnnouncedSpottedOfficer"; // I see the cops!
            public const string ANNOUNCED_SPOTTED_OFFICER_M09 = "ms1_AnnouncedSpottedOfficer_m09"; // The time is upon us!
            public const string ANNOUNCED_SPOTTED_OFFICER_M11 = "ms1_AnnouncedSpottedOfficer_m11"; // Five-0h! Five-Oh!
            public const string ANNOUNCED_SPOTTED_OFFICER_SCARED = "ms1_AnnouncedSpottedOfficerScared"; // Where'd they come from?
            public const string ANNOUNCED_SPOTTED_OFFICER_SCARED_M08 = "ms1_AnnouncedSpottedOfficerScared_m08"; // Nothin' is goin' right today!
            public const string ANNOUNCED_SPOTTED_OFFICER_SCARED_M09 = "ms1_AnnouncedSpottedOfficerScared_m09"; // Not even for Him can I face them...
            public const string ANNOUNCED_SPOTTED_OFFICER_SCARED_M10 = "ms1_AnnouncedSpottedOfficerScared_m10"; // Move, move, we got trouble!
            public const string ANNOUNCED_SPOTTED_OFFICER_SURPRISED = "ms1_AnnouncedSpottedOfficerSurprised"; // Hey! The cops'r'here!
            public const string ANNOUNCED_SPOTTED_OFFICER_SURPRISED_M07 = "ms1_AnnouncedSpottedOfficerSurprised_m07"; // Damn it! What happened to the alarm?
            public const string ANNOUNCED_SPOTTED_OFFICER_SURPRISED_M09 = "ms1_AnnouncedSpottedOfficerSurprised_m09"; // The Time has come too soon!
            public const string ANNOUNCED_SPOTTED_OFFICER_SURPRISED_M10 = "ms1_AnnouncedSpottedOfficerSurprised_m10"; // Cops incoming...

            // Conversation
            public const string CALLED_FOR_HELP = "ms1_CalledForHelp"; // Somebody get over here!
            public const string CALLED_FOR_HELP_M05 = "ms1_CalledForHelp_m05"; // Forget the garage, help me!
            public const string CALLED_FOR_HELP_M07 = "ms1_CalledForHelp_m07"; // It's some kinda sting! Al!
            public const string CALLED_FOR_HELP_M09 = "ms1_CalledForHelp_m09"; // Help me, my Brothers!
            public const string TALKED_TO_HOSTAGE = "ms1_TalkedToHostage"; // Don't give me any trouble!
            public const string TALKED_TO_HOSTAGE_M09 = "ms1_TalkedToHostage_m09"; // Do you remember the Words of Taronne?

            // Other
            public const string DOOR_BLOCKED = "ms1_DoorBlocked"; // Don't tell me this is locked!
            public const string DOOR_BLOCKED_M12 = "ms1_DoorBlocked_m12"; // Goddamn hotel doors...
            public const string DOOR_BLOCKED_M13 = "ms1_DoorBlocked_m13"; // Locked...
            public const string SHOT_A_FRIEND_M09 = "ms1_ShotAFriend_m09"; // The darkness has claimed you.
            public const string SHOT_AT_BREACHED_DOOR = "ms1_ShotAtBreachedDoor"; // Start shooting!
            public const string SHOT_AT_BREACHED_DOOR_M09 = "ms1_ShotAtBreachedDoor_m09"; // Don't let them stop the the Plan!
            public const string SHOT_AT_BREACHED_DOOR_M13 = "ms1_ShotAtBreachedDoor_m13"; // Hold the line... fire!

            // Reaction
            public const string REACTED_BANG = "ms1_ReactedBang"; // Huh? What!?
            public const string REACTED_BREACH = "ms1_ReactedBreach"; // The door!
            public const string REACTED_DOWN_OFFICER = "ms1_ReactedDownOfficer"; // One less cop!
            public const string REACTED_DOWN_OFFICER_M09 = "ms1_ReactedDownOfficer_m09"; // Out of the Darkness you came, to darkness you go.
            public const string REACTED_DOWN_OFFICER_M10 = "ms1_ReactedDownOfficer_m10"; // Goddamned cops should'a stayed out of it!
            public const string REACTED_DOWN_OFFICER_M13 = "ms1_ReactedDownOfficer_m13"; // You can't stop the truth, or those who defend it!
            public const string REACTED_DOWN_SUSPECT = "ms1_ReactedDownSuspect"; // Hang in there..
            public const string REACTED_DOWN_SUSPECT_M09 = "ms1_ReactedDownSuspect_m09"; // Your next breath will be with Taronne.
            public const string REACTED_DOWN_SUSPECT_M13 = "ms1_ReactedDownSuspect_m13"; // At least you fell a Martyr!
            public const string REACTED_HIT_BY_DOOR = "ms1_ReactedHitByDoor"; // The door!
            public const string REACTED_HIT_BY_DOOR_M05 = "ms1_ReactedHitByDoor_m05"; // What the fuck... the door!
            public const string REACTED_INJURY_INTENSE = "ms1_ReactedInjuryIntense"; // They hit me!
            public const string REACTED_INJURY_NORMAL = "ms1_ReactedInjuryNormal"; // Damn it!

            // Reply/Ack
            public const string REPLIED_TO_HOSTAGE = "ms1_RepliedToHostage"; // Just do what I say!
            public const string REPLIED_TO_HOSTAGE_M09 = "ms1_RepliedToHostage_m09"; // We will be together, in Taronne's Name.
            public const string REPLIED_TO_HOSTAGE_M13 = "ms1_RepliedToHostage_m13"; // I'd be happy to kill you, so shut up!

            // Threat
            public const string THREATENED_HOSTAGE = "ms1_ThreatenedHostage"; // Just keep comin' and see what happens...
            public const string THREATENED_HOSTAGE_M06 = "ms1_ThreatenedHostage_m06"; // Keep goin' and there's one less corporate tool!
            public const string THREATENED_HOSTAGE_M08 = "ms1_ThreatenedHostage_m08"; // You're disposable!
            public const string THREATENED_HOSTAGE_M09 = "ms1_ThreatenedHostage_m09"; // I will save you from these evils!
            public const string THREATENED_HOSTAGE_M12 = "ms1_ThreatenedHostage_m12"; // One more step and I'm wasting this French bastard!
            public const string THREATENED_HOSTAGE_M13 = "ms1_ThreatenedHostage_m13"; // This stinking scientist is dead if you take another step!
        }

        // Nghi phạm giọng ak
        public static class SuspectAK
        {
            // Announce/Shout
            public const string ANNOUNCED_COMPLIANT = "ak_AnnouncedCompliant"; // I dropped it! Don't shoot!
            public const string ANNOUNCED_FLEE = "ak_AnnouncedFlee"; // Get back!
            public const string ANNOUNCED_INVESTIGATE = "ak_AnnouncedInvestigate"; // What's that noise?
            public const string ANNOUNCED_NON_COMPLIANT = "ak_AnnouncedNonCompliant"; // Goddamn it!
            public const string ANNOUNCED_RESTRAINED = "ak_AnnouncedRestrained"; // You got a warrant?
            public const string ANNOUNCED_SPOTTED_OFFICER = "ak_AnnouncedSpottedOfficer"; // Raid! Cops!
            public const string ANNOUNCED_SPOTTED_OFFICER_SCARED = "ak_AnnouncedSpottedOfficerScared"; // Move! Cops!
            public const string ANNOUNCED_SPOTTED_OFFICER_SURPRISED = "ak_AnnouncedSpottedOfficerSurprised"; // What the hell!

            // Conversation
            public const string CALLEDFOR_HELP = "ak_CalledforHelp"; // Get over here!
            public const string TALKED_TO_HOSTAGE = "ak_TalkedToHostage"; // Don't move, and you'll be fine...

            // Other
            public const string DOOR_BLOCKED = "ak_DoorBlocked"; // Shit! It won't budge!
            public const string SHOT_A_FRIEND = "ak_ShotAFriend"; // Goddamn it!
            public const string SHOT_AT_BREACHED_DOOR = "ak_ShotAtBreachedDoor"; // You ain't gettin' in!

            // Reaction
            public const string REACTED_BREACH = "ak_ReactedBreach"; // The door! Watch out!
            public const string REACTED_DOWN_OFFICER = "ak_ReactedDownOfficer"; // Next..
            public const string REACTED_DOWN_SUSPECT = "ak_ReactedDownSuspect"; // Hang in there..
            public const string REACTED_HIT_BY_DOOR = "ak_ReactedHitByDoor"; // What!?
            public const string REACTED_INJURY_NORMAL = "ak_ReactedInjuryNormal"; // Damn it!

            // Reply/Ack
            public const string REPLIED_TO_HOSTAGE = "ak_RepliedToHostage"; // Don't get smart.

            // Threat
            public const string THREATENED_HOSTAGE = "ak_ThreatenedHostage"; // Stop there, or else!
        }

        // Nghi phạm giọng alj
        public static class SuspectALJ
        {
            // Announce/Shout
            public const string ANNOUNCED_COMPLIANT = "alj_AnnouncedCompliant"; // All right, all right...
            public const string ANNOUNCED_FLEE = "alj_AnnouncedFlee"; // Run!
            public const string ANNOUNCED_INVESTIGATE = "alj_AnnouncedInvestigate"; // Who was that!
            public const string ANNOUNCED_NON_COMPLIANT = "alj_AnnouncedNonCompliant"; // I don't think so!!
            public const string ANNOUNCED_RESTRAINED = "alj_AnnouncedRestrained"; // I ain't sayin' nothing to you!
            public const string ANNOUNCED_SPOTTED_OFFICER = "alj_AnnouncedSpottedOfficer"; // I'm not going back!
            public const string ANNOUNCED_SPOTTED_OFFICER_SCARED = "alj_AnnouncedSpottedOfficerScared"; // Shit! Cops!
            public const string ANNOUNCED_SPOTTED_OFFICER_SURPRISED = "alj_AnnouncedSpottedOfficerSurprised"; // Watch out!

            // Conversation
            public const string CALLEDFOR_HELP = "alj_CalledforHelp"; // Over here!
            public const string TALKED_TO_HOSTAGE = "alj_TalkedToHostage"; // Quit your whinin!

            // Other
            public const string DOOR_BLOCKED = "alj_DoorBlocked"; // Open up! Now!
            public const string SHOT_A_FRIEND = "alj_ShotAFriend"; // Oh damn it!!
            public const string SHOT_AT_BREACHED_DOOR = "alj_ShotAtBreachedDoor"; // Don't wait! Shoot!

            // Reaction
            public const string REACTED_BANG = "alj_ReactedBang"; // Oh God, God!!
            public const string REACTED_BREACH = "alj_ReactedBreach"; // They're comin' in!
            public const string REACTED_DOWN_OFFICER = "alj_ReactedDownOfficer"; // That's it. Stay down.
            public const string REACTED_DOWN_SUSPECT = "alj_ReactedDownSuspect"; // Get up, you ass!
            public const string REACTED_HIT_BY_DOOR = "alj_ReactedHitByDoor"; // Hey!
            public const string REACTED_PEPPER = "alj_ReactedPepper"; // Shit! You blinded me you stupid fuck!

            // Reply/Ack
            public const string REPLIED_TO_HOSTAGE = "alj_RepliedToHostage"; // This is your fault!

            // Threat
            public const string THREATENED_HOSTAGE = "alj_ThreatenedHostage"; // I'll shoot! I will!
        }

        // Nghi phạm giọng ln
        public static class SuspectLN
        {
            // Announce/Shout
            public const string ANNOUNCED_COMPLIANT = "ln_AnnouncedCompliant"; // I give up! Don't shoot!
            public const string ANNOUNCED_FLEE = "ln_AnnouncedFlee"; // Run!
            public const string ANNOUNCED_INVESTIGATE = "ln_AnnouncedInvestigate"; // Is somethin' wrong?
            public const string ANNOUNCED_NON_COMPLIANT = "ln_AnnouncedNonCompliant"; // Get away!
            public const string ANNOUNCED_RESTRAINED = "ln_AnnouncedRestrained"; // You got this all wrong..it's just a little hobby, you know?
            public const string ANNOUNCED_SPOTTED_OFFICER = "ln_AnnouncedSpottedOfficer"; // Cops!
            public const string ANNOUNCED_SPOTTED_OFFICER_SCARED = "ln_AnnouncedSpottedOfficerScared"; // Oh, shit! Police!
            public const string ANNOUNCED_SPOTTED_OFFICER_SURPRISED = "ln_AnnouncedSpottedOfficerSurprised"; // What? Cops?

            // Conversation
            public const string TALKED_TO_HOSTAGE = "ln_TalkedToHostage"; // Don't move!

            // Other
            public const string DOOR_BLOCKED = "ln_DoorBlocked"; // Stupid door... open up!
            public const string SHOT_A_FRIEND = "ln_ShotAFriend"; // Sorry, sorry...sorry
            public const string SHOT_AT_BREACHED_DOOR = "ln_ShotAtBreachedDoor"; // Stop them!

            // Reaction
            public const string REACTED_BREACH = "ln_ReactedBreach"; // The door!
            public const string REACTED_DOWN_OFFICER = "ln_ReactedDownOfficer"; // One down...
            public const string REACTED_DOWN_SUSPECT = "ln_ReactedDownSuspect"; // Oh, shit...
            public const string REACTED_HIT_BY_DOOR = "ln_ReactedHitByDoor"; // Hey!

            // Reply/Ack
            public const string REPLIED_TO_HOSTAGE = "ln_RepliedToHostage"; // Shut up!
        }

        // Con tin / dân thường nam
        public static class Hostage
        {
            // Announce/Shout
            public const string ANNOUNCED_COMPLIANT = "mh1_AnnouncedCompliant"; // My hands're up! Don't shoot!
            public const string ANNOUNCED_COMPLIANT_M01 = "mh1_AnnouncedCompliant_m01"; // You're busting the Foodwall? What the hell?
            public const string ANNOUNCED_COMPLIANT_M04 = "mh1_AnnouncedCompliant_m04"; // Don't shoot! Oh, I think I'm gonna puke...
            public const string ANNOUNCED_COMPLIANT_M06 = "mh1_AnnouncedCompliant_m06"; // Feels like I'm in a cop show.
            public const string ANNOUNCED_COMPLIANT_M07 = "mh1_AnnouncedCompliant_m07"; // Oh my god... you can't tell my wife...
            public const string ANNOUNCED_COMPLIANT_M09 = "mh1_AnnouncedCompliant_m09"; // The answer won't be found with violence.
            public const string ANNOUNCED_COMPLIANT_M10 = "mh1_AnnouncedCompliant_m10"; // Ok, Ok, I'm down...quit shoving guns in my face!
            public const string ANNOUNCED_COMPLIANT_M11 = "mh1_AnnouncedCompliant_m11"; // Don't shoot! You're freakin' me out!
            public const string ANNOUNCED_COMPLIANT_M12 = "mh1_AnnouncedCompliant_m12"; // Course, ain't nobody gonna try an' take me hostage...
            public const string ANNOUNCED_COMPLIANT_M13 = "mh1_AnnouncedCompliant_m13"; // Don't shoot! You might break something!
            public const string ANNOUNCED_ENEMY = "mh1_AnnouncedEnemy"; // Hey, I'm not lookin' for trouble..
            public const string ANNOUNCED_ENEMY_M04 = "mh1_AnnouncedEnemy_m04"; // Over there!
            public const string ANNOUNCED_ENEMY_M07 = "mh1_AnnouncedEnemy_m07"; // Get back!
            public const string ANNOUNCED_ENEMY_M09 = "mh1_AnnouncedEnemy_m09"; // You carry a terrible burden...
            public const string ANNOUNCED_ENEMY_M11 = "mh1_AnnouncedEnemy_m11"; // I'll just stay outta yer way, OK?
            public const string ANNOUNCED_FLEE = "mh1_AnnouncedFlee"; // Run!
            public const string ANNOUNCED_FLEE_M07 = "mh1_AnnouncedFlee_m07"; // Lemme out!
            public const string ANNOUNCED_FLEE_M09 = "mh1_AnnouncedFlee_m09"; // Run!
            public const string ANNOUNCED_FLEE_M11 = "mh1_AnnouncedFlee_m11"; // Oh man, I'm outta here!
            public const string ANNOUNCED_FLEE_M13 = "mh1_AnnouncedFlee_m13"; // You can have the damn samples!
            public const string ANNOUNCED_RESTRAINED_M04 = "mh1_AnnouncedRestrained_m04"; // Uhh... I think I'm gonna be sick...
            public const string ANNOUNCED_RESTRAINED_M08 = "mh1_AnnouncedRestrained_m08"; // Do you have to do this? I don't have any weapons.

            // Conversation
            public const string CALLED_FOR_HELP = "mh1_CalledForHelp"; // Over here! Help!
            public const string CALLED_FOR_HELP_M09 = "mh1_CalledForHelp_m09"; // Help me! Brothers!
            public const string CALLED_FOR_HELP_M11 = "mh1_CalledForHelp_m11"; // Help!
        }

        public static IEnumerable<string> All()
        {
            foreach (var t in typeof(VoiceLine).GetNestedTypes(BindingFlags.Public))
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
                    if (f.IsLiteral && f.FieldType == typeof(string))
                        yield return (string)f.GetRawConstantValue();
        }
    }

    #region TUTORIAL

    // Gun phát: (Gun gun, GameObject hitObject, RaycastHit hit)
    public const string PLAYER_SHOOT_TARGET = "player_shoot_target";
    // LightTarget phát khi đủ số phát: (LightTarget target)
    public const string TARGET_COMPLETED = "target_completed";

    #endregion
}