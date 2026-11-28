using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Behavior;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using VRSwat.Doors;
using T = NPCPlayModeTests;

/// <summary>
/// Test Play Mode giai đoạn 3 (Teammate, SWAT 4): Report status, Mirror under door, AutoRestrain nghi phạm,
/// Restrain con tin, Open and clear (slicing the pie + kiểu vào theo bản lề), flash của đội (quay mặt đi),
/// Move To, Cover, đi hàng một ở chỗ hẹp.
/// Chạy: Tools/VR SWAT/Tests/Phase 3 - Teammate, hoặc NPCPlayModeTests.Run("Phase3.Teammate").
/// Cần scene có 2 đồng đội, 1 nghi phạm, 1 con tin trong phòng sau một cửa có đủ điểm stack / clear (mặc định
/// Door_Room_1 của Duong's Scene, không có thì cửa gần Alpha nhất). Nghi phạm đứng yên (tắt graph + súng).
/// </summary>
[InitializeOnLoad]
public static class TeammatePlayModeTests
{
    private const string DoorName = "Door_Room_1";

    static TeammatePlayModeTests() => T.Register("Phase3.Teammate", SquadTest, 300f);

    [MenuItem("Tools/VR SWAT/Tests/Phase 3 - Teammate")]
    private static void RunSquad() => T.Run("Phase3.Teammate");

    private static IEnumerator<float> SquadTest()
    {
        SquadController squad = SquadController.GetOrCreate();
        Teammate alpha = Teammate.Active.FirstOrDefault(t => t.Slot == 0);
        Teammate bravo = Teammate.Active.FirstOrDefault(t => t != alpha);
        if (!T.Check(alpha != null && bravo != null, "scene có 2 đồng đội")) yield break;
        NPC enemy = T.FirstSuspect();
        Hostage hostage = T.FirstHostage();
        if (!T.Check(enemy != null && hostage != null, "scene có nghi phạm + con tin")) yield break;
        DoorController door = DoorController.All.FirstOrDefault(d => d != null && d.name == DoorName) ?? squad.FindDoorNear(alpha.transform.position, 15f);
        if (!T.Check(door != null, $"có cửa {DoorName} (đủ điểm stack / clear)")) yield break;
        Vector3 issuer = door.stackLeft.position;

        var said = new List<string>();
        alpha.OnSay.AddListener(s => said.Add("A:" + s));
        bravo.OnSay.AddListener(s => said.Add("B:" + s));
        bool Said(string who, string text) => said.Any(s => s.StartsWith(who) && s.Contains(text));
        Freeze(enemy);
        // Đội bắt đầu ở phía stack (ngoài phòng), cửa đóng: không thấy nghi phạm trước khi soi gương.
        Vector3 outward = Flatten(doorMidOf(door) - (door.clearLeft.position + door.clearRight.position) * 0.5f).normalized;
        WarpNear(alpha, door.stackLeft.position + outward * 2f);
        WarpNear(bravo, door.stackRight.position + outward * 2f);
        squad.Issue(SquadCommand.Cover, null, null, null, null, doorMidOf(door) + Vector3.up * 1.3f);
        if (SquadController.IsOpen(door))
        {
            door.Request(DoorAction.Close, alpha, issuer);
            for (float t = 0f; t < 4f && SquadController.IsOpen(door); t += 0.25f) yield return 0.25f;
            door.Release(alpha);
        }
        yield return 0.5f;
        if (!T.Check(!enemy.IsDead, "nghi phạm còn sống lúc bắt đầu")) yield break;
        T.Log($"Cửa {door.name}, Alpha {alpha.transform.position}, Bravo {bravo.transform.position}, Enemy {enemy.transform.position}, Hostage {hostage.transform.position}");

        // ---------- 1. Report status ----------
        T.Check(squad.Issue(SquadCommand.ReportStatus, lead: alpha), "Report: ra lệnh được");
        yield return 0.2f;
        T.Log($"   {squad.LastStatus}");
        T.Check(squad.LastStatus != null && squad.LastStatus.Contains("1 suspect(s) unsecured") && squad.LastStatus.Contains("1 civilian(s) unsecured"),
            "Report: đếm đúng 1 nghi phạm, 1 con tin chưa còng");

        // ---------- 2. Mirror under door (cửa đóng) ----------
        if (SquadController.IsOpen(door))
        {
            door.Request(DoorAction.Close, alpha, issuer);
            for (float t = 0f; t < 4f && SquadController.IsOpen(door); t += 0.25f) yield return 0.25f;
            door.Release(alpha);
        }
        T.Check(!SquadController.IsOpen(door), $"Mirror: cửa đóng ({door.State})");
        int saidBefore = said.Count;
        T.Check(squad.Issue(SquadCommand.Mirror, door, null, issuer, alpha), $"Mirror: ra lệnh được ({squad.LastRejectReason})");
        Vector3 doorMid = (door.stackLeft.position + door.stackRight.position) * 0.5f;
        Vector3 doorPlane = door.transform.TransformPoint(Vector3.Scale(door.transform.InverseTransformPoint(doorMid), new Vector3(1f, 1f, 0f)));
        float minDoor = float.PositiveInfinity;
        for (float t = 0f; t < 45f && squad.IsBusy; t += 0.25f)
        {
            yield return 0.25f;
            minDoor = Mathf.Min(minDoor, Flat(alpha.transform.position - doorPlane));
        }
        T.Check(!squad.IsBusy, "Mirror: làm xong");
        MirrorReport m = squad.LastMirror;
        T.Check(minDoor < 1.0f, $"Mirror: Alpha tới chân cửa ({minDoor:0.00} m)");
        T.Check(m.Door == door && m.Suspects == 1 && m.Civilians == 1, $"Mirror: thấy {m.Suspects} nghi phạm, {m.Civilians} con tin (cần 1 / 1)");
        T.Check(said.Skip(saidBefore).Any(s => s.Contains("I see suspects")), "Mirror: báo \"I see suspects.\"");

        // ---------- 3. AutoRestrain nghi phạm ----------
        enemy.Surrender();
        yield return 1f;
        T.Check(alpha.GuardTarget == enemy.gameObject || bravo.GuardTarget == enemy.gameObject, "AutoRestrain: có người canh nghi phạm đầu hàng");
        door.Request(DoorAction.Open, alpha, issuer);
        for (float t = 0f; t < 4f && !SquadController.IsOpen(door); t += 0.25f) yield return 0.25f;
        door.Release(alpha);
        typeof(SquadController).GetField("_autoRestrainDelay", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(squad, 2f);
        squad.AutoRestrain = true;
        for (float t = 0f; t < 35f && !enemy.IsRestrained; t += 0.25f) yield return 0.25f;
        squad.AutoRestrain = false;
        T.Check(enemy.IsRestrained, "AutoRestrain: nghi phạm bị đồng đội còng");
        T.Check(said.Any(s => s.Contains("Restraining.")), "AutoRestrain: hô \"Restraining.\"");
        T.Check(said.Any(s => s.Contains("Suspect secured.")), "AutoRestrain: báo \"Suspect secured.\"");
        for (float t = 0f; t < 5f && squad.IsBusy; t += 0.25f) yield return 0.25f;

        // ---------- 4. Restrain con tin (Bravo còng) ----------
        hostage.Surrender();
        yield return 1.5f;
        Vector3 hp = hostage.transform.position;
        T.Check(squad.CanIssue(SquadCommand.Restrain, null, hp, hp, out string why), $"Restrain: có người chờ còng ({why})");
        T.Check(squad.Issue(SquadCommand.Restrain, null, null, hp, bravo, hp), "Restrain: ra lệnh cho Bravo");
        for (float t = 0f; t < 35f && squad.IsBusy; t += 0.25f) yield return 0.25f;
        T.Check(hostage.IsRestrained, "Restrain: con tin bị Bravo còng");
        T.Check(Said("B:", "Civilian secured."), "Restrain: Bravo báo \"Civilian secured.\"");

        // ---------- 5. Report lại ----------
        squad.Issue(SquadCommand.ReportStatus, lead: alpha);
        yield return 0.2f;
        T.Log($"   {squad.LastStatus}");
        T.Check(squad.LastStatus.Contains("0 suspect(s) unsecured, 1 restrained") && squad.LastStatus.Contains("0 civilian(s) unsecured, 1 secured"),
            "Report: sau khi còng: 0 chưa còng, 1 nghi phạm + 1 con tin đã còng");

        // ---------- 6. Open and clear: slicing the pie + kiểu vào theo bản lề ----------
        saidBefore = said.Count;
        T.Check(squad.Issue(SquadCommand.OpenAndClear, door, false, issuer), $"Clear: ra lệnh được ({squad.LastRejectReason})");
        var looks = new List<Vector3>();
        Teammate first = null;
        Vector3 firstAtEntry = default;
        for (float t = 0f; t < 70f && squad.IsBusy; t += 0.1f)
        {
            yield return 0.1f;
            if (alpha.Order == TeammateOrder.HoldAim && SquadController.IsOpen(door) && (looks.Count == 0 || Vector3.Distance(looks[^1], alpha.OrderLook) > 0.05f))
                looks.Add(alpha.OrderLook);
            if (first == null && squad.LastEntryFirst != null && squad.LastEntryFirst.Order == TeammateOrder.ClearRoom)
            {
                first = squad.LastEntryFirst;
                firstAtEntry = Flat(first.transform.position - door.stackLeft.position) < Flat(first.transform.position - door.stackRight.position)
                    ? door.stackLeft.position
                    : door.stackRight.position;
            }
        }
        T.Check(!squad.IsBusy, "Clear: làm xong");
        T.Check(looks.Count >= 5, $"Clear: slicing the pie (Alpha đổi hướng súng {looks.Count} lần trước khi vào)");
        // Tiêu chí độc lập: chỗ stack phía tay nắm xa bản lề hơn chỗ stack kia.
        Vector3 hinge = door.transform.TransformPoint(door.HingeLocalPosition);
        Vector3 otherStack = firstAtEntry == door.stackLeft.position ? door.stackRight.position : door.stackLeft.position;
        T.Check(first != null && Flat(firstAtEntry - hinge) > Flat(otherStack - hinge),
            $"Clear: người phía tay nắm (xa bản lề) vào trước ({(first != null ? first.CallSign : "-")}, kiểu {squad.LastEntryStyle})");
        T.Check(said.Skip(saidBefore).Any(s => s.Contains("Room clear!")), "Clear: báo \"Room clear!\"");

        // ---------- 7. Flash của đội: quay mặt khỏi cửa tới khi nổ ----------
        if (!squad.CanIssue(SquadCommand.ThrowFlash, door, issuer, out string flashWhy)) T.Log($"   (bỏ qua Flash: {flashWhy})");
        else
        {
            squad.Issue(SquadCommand.ThrowFlash, door, null, issuer, bravo);
            bool sawFlash = false, turnedAway = false;
            float maxStun = 0f;
            for (float t = 0f; t < 45f && squad.IsBusy; t += 0.1f)
            {
                yield return 0.1f;
                bool flashLive = Object.FindObjectsByType<VRSwat.Grenades.Flashbang>(FindObjectsSortMode.None).Any(f => f != null && f.isActiveAndEnabled
                    && Vector3.Distance(f.transform.position, doorMid) < 20f);
                foreach (Teammate mate in new[] { alpha, bravo })
                    if (mate.TryGetComponent(out NPCStun s) && s.IsStunned) maxStun = Mathf.Max(maxStun, s.Intensity);
                if (!flashLive || alpha.Order != TeammateOrder.HoldAim) continue;
                sawFlash = true;
                float aA = Vector3.Angle(Flatten(alpha.transform.forward), Flatten(doorMid - alpha.transform.position));
                float aB = Vector3.Angle(Flatten(bravo.transform.forward), Flatten(doorMid - bravo.transform.position));
                if (aA > 90f && aB > 90f) turnedAway = true;
            }
            T.Check(sawFlash, "Flash: Bravo ném flash");
            T.Check(turnedAway, "Flash: cả đội quay mặt khỏi cửa khi flash chưa nổ");
            T.Log($"   (thông tin) choáng mạnh nhất của đội: {maxStun:0.00}");
        }

        // ---------- 8. Move To ----------
        Vector3 back = doorMid + Flatten(doorMid - (door.clearLeft.position + door.clearRight.position) * 0.5f).normalized * 4f;
        if (!NavMesh.SamplePosition(back, out NavMeshHit mh, 2f, NavMesh.AllAreas)) T.Log("   (bỏ qua Move To: không có điểm NavMesh)");
        else
        {
            T.Check(squad.Issue(SquadCommand.MoveTo, null, null, null, alpha, mh.position), "MoveTo: ra lệnh được");
            for (float t = 0f; t < 30f && squad.IsBusy; t += 0.25f) yield return 0.25f;
            T.Check(Flat(alpha.transform.position - mh.position) < 1f, $"MoveTo: Alpha tới điểm ({Flat(alpha.transform.position - mh.position):0.0} m)");
            T.Check(Flat(bravo.transform.position - mh.position) < 2.5f, $"MoveTo: Bravo theo sau ({Flat(bravo.transform.position - mh.position):0.0} m)");
        }

        // ---------- 9. Cover ----------
        Vector3 coverPoint = alpha.transform.position + Quaternion.Euler(0f, 120f, 0f) * Flatten(alpha.transform.forward).normalized * 5f + Vector3.up * 1.3f;
        T.Check(squad.Issue(SquadCommand.Cover, null, null, null, null, coverPoint), "Cover: ra lệnh được");
        yield return 2.5f;
        float cA = NPCConditionUtils.HorizontalAngleTo(alpha.gameObject, coverPoint), cB = NPCConditionUtils.HorizontalAngleTo(bravo.gameObject, coverPoint);
        T.Check(cA < 30f && cB < 30f, $"Cover: cả đội chĩa súng vào điểm (lệch {cA:0}° / {cB:0}°)");

        // ---------- 10. Đi hàng một ở chỗ hẹp ----------
        Transform cam = Teammate.PlayerHead;
        if (cam == null || !TryDoorway(door, out Vector3 spot, out Vector3 along)) T.Log("   (bỏ qua hàng một: không có khung cửa trên NavMesh)");
        else
        {
            Transform rig = cam.root;
            float yaw = Vector3.SignedAngle(Flatten(cam.forward), along, Vector3.up);
            rig.RotateAround(cam.position, Vector3.up, yaw);
            Vector3 delta = spot - cam.position;
            delta.y = 0f;
            rig.position += delta;
            yield return 0.3f;
            if (Flat(cam.position - spot) > 0.5f) T.Log("   (bỏ qua hàng một: không dời được người chơi)");
            else
            {
                squad.Issue(SquadCommand.FallIn);
                yield return 8f;
                float dA = Flat(alpha.transform.position - cam.position), dB = Flat(bravo.transform.position - cam.position);
                T.Check(alpha.InSingleFile && bravo.InSingleFile, $"Hàng một: người chơi đứng trong khung cửa {door.name} -> đi hàng một");
                T.Check(dB > dA + 0.6f, $"Hàng một: Bravo sau Alpha (Alpha {dA:0.0} m, Bravo {dB:0.0} m sau người chơi)");
            }
        }
    }

    // ---------- Tiện ích ----------

    private static Vector3 doorMidOf(DoorController d) => (d.stackLeft.position + d.stackRight.position) * 0.5f;

    private static void WarpNear(Component c, Vector3 pos)
    {
        if (!NavMesh.SamplePosition(pos, out NavMeshHit hit, 2f, NavMesh.AllAreas)) return;
        if (c.TryGetComponent(out NavMeshAgent a) && a.enabled) a.Warp(hit.position);
        else c.transform.position = hit.position;
    }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
    private static Vector3 Flatten(Vector3 v) => new(v.x, 0f, v.z);

    private static void Freeze(Component c)
    {
        if (c.TryGetComponent(out BehaviorGraphAgent g)) { g.End(); g.enabled = false; }
        if (c.TryGetComponent(out NavMeshAgent a) && a.enabled && a.isOnNavMesh) { a.ResetPath(); a.isStopped = true; }
        foreach (NPCWeaponHolder w in c.GetComponentsInChildren<NPCWeaponHolder>()) { w.StopAiming(); w.enabled = false; }
    }

    /// <summary>Giữa khung cửa (trên NavMesh), hướng xuyên vào phòng.</summary>
    private static bool TryDoorway(DoorController d, out Vector3 spot, out Vector3 along)
    {
        Vector3 mid = doorMidOf(d);
        along = Flatten((d.clearLeft.position + d.clearRight.position) * 0.5f - mid).normalized;
        Vector3 local = d.transform.InverseTransformPoint(mid);
        local.z = 0f;
        bool ok = NavMesh.SamplePosition(d.transform.TransformPoint(local), out NavMeshHit hit, 0.4f, NavMesh.AllAreas);
        spot = ok ? hit.position : mid;
        return ok;
    }

}
