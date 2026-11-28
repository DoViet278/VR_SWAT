using System.Collections.Generic;
using System.Linq;
using Unity.Behavior;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using T = NPCPlayModeTests;

/// <summary>
/// Test Play Mode giai đoạn 2 (Hostage, SWAT 4): Cower (súng sát bên), Flee (nghe súng), không chạy khi thấy cảnh sát,
/// Held (bị khống chế rồi được buông), Surrender (1-3 lần hô), Restrained.
/// Chạy: Tools/VR SWAT/Tests/Phase 2 - Hostage, hoặc NPCPlayModeTests.Run("Phase2.Hostage").
/// Cần scene có 1 con tin; Held cần 1 nghi phạm, "thấy cảnh sát" cần 1 đồng đội. Nghi phạm tạm tắt, đồng đội đứng yên
/// để không gây nhiễu.
/// </summary>
[InitializeOnLoad]
public static class HostagePlayModeTests
{
    private static readonly int ScaredHash = Animator.StringToHash("Scared");

    static HostagePlayModeTests() => T.Register("Phase2.Hostage", HostageTest, 90f, true);

    [MenuItem("Tools/VR SWAT/Tests/Phase 2 - Hostage")]
    private static void RunHostage() => T.Run("Phase2.Hostage");

    private static IEnumerator<float> HostageTest()
    {
        Hostage h = T.FirstHostage();
        if (!T.Check(h != null, "scene có con tin")) yield break;
        if (!T.Check(NPCActionUtils.TryGetAgent(h.gameObject, out NavMeshAgent agent), "con tin có NavMeshAgent trên NavMesh")) yield break;
        Animator anim = h.GetComponentInChildren<Animator>();

        // Cô lập: tắt nghi phạm, đồng đội đứng yên (không bắn, không đi).
        NPC enemy = T.FirstSuspect();
        if (enemy != null) enemy.gameObject.SetActive(false);
        T.HideTeammates(); // con tin chưa thấy cảnh sát (bất kể bố cục scene)
        T.MovePlayerAway(h.transform.position);
        T.Log($"Con tin {h.name} tại {h.transform.position}, thấy cảnh sát = {h.SeesPolice}");
        yield return 1f;

        // ---------- 1. Cower: súng nổ sát bên -> ngồi ôm đầu tại chỗ ----------
        Vector3 near = h.transform.position + h.transform.right * 2f + Vector3.up * 1.4f;
        NPCHearing.Report(near, 30f, NoiseType.Gunshot);
        T.Check(h.ShouldCower, "Cower: súng nổ cách 2 m -> ShouldCower");
        Vector3 p0 = h.transform.position;
        yield return 1f;
        T.Check(anim == null || anim.GetBool(ScaredHash), "Cower: bật anim ôm đầu (Scared)");
        T.Check(Flat(h.transform.position - p0) < 0.3f, $"Cower: ngồi tại chỗ, không chạy ({Flat(h.transform.position - p0):0.00} m)");
        yield return 3f;
        T.Check(!h.ShouldCower, "Cower: yên 3 s thì thôi ôm đầu");

        // ---------- 2. Flee: tiếng súng cách ~10 m, không thấy cảnh sát -> chạy ra xa ----------
        if (h.SeesPolice) T.Log("   (bỏ qua Flee: con tin đang thấy cảnh sát)");
        else if (!TryFindPointAt(agent, h.transform.position, 9f, 12f, out Vector3 shot))
            T.Log("   (bỏ qua Flee: không có điểm 9-12 m quanh con tin)");
        else
        {
            float d0 = Flat(h.transform.position - shot);
            NPCHearing.Report(shot + Vector3.up * 1.4f, 30f, NoiseType.Gunshot);
            T.Check(h.ShouldFlee, $"Flee: nghe tiếng súng cách {d0:0.0} m -> ShouldFlee");
            float dMax = d0, vMax = 0f;
            for (float t = 0f; t < 8f; t += 0.25f)
            {
                yield return 0.25f;
                dMax = Mathf.Max(dMax, Flat(h.transform.position - shot));
                vMax = Mathf.Max(vMax, agent.desiredVelocity.magnitude);
                if (dMax >= d0 + 4f && agent.velocity.magnitude < 0.1f) break;
            }
            T.Check(dMax >= d0 + 3f, $"Flee: chạy xa nguồn tiếng ({d0:0.0} -> {dMax:0.0} m)");
            T.Check(vMax > NPCActionUtils.SpeedFor(h.gameObject, 1f) + 0.2f, $"Flee: chạy (tốc độ {vMax:0.0} m/s > đi bộ {NPCActionUtils.SpeedFor(h.gameObject, 1f):0.0})");

            // Tới chỗ khuất: nấp lại. Chạy tới chỗ thấy cảnh sát: đứng lại sợ, chờ lệnh. Cả hai: không đi quay lại.
            for (float t = 0f; t < 6f && !h.IsHiding && !h.SeesPolice; t += 0.25f) yield return 0.25f;
            bool police = h.SeesPolice;
            if (police) T.Check(h.IsScared, "Flee: chạy tới chỗ thấy cảnh sát -> đứng lại sợ, chờ lệnh");
            else T.Check(h.IsHiding, "Flee: tới chỗ khuất thì nấp lại");
            Vector3 hide = h.transform.position;
            yield return 4f;
            T.Check(Flat(h.transform.position - hide) < 0.5f, $"Flee: đứng yên, không đi quay lại ({Flat(h.transform.position - hide):0.0} m sau 4 s)");
            T.Check(anim == null || anim.GetBool(ScaredHash), $"Flee: ngồi ôm đầu ({(police ? "thấy cảnh sát" : "đang nấp")})");
        }
        yield return 2f;

        // ---------- 3. Thấy cảnh sát thì không chạy ----------
        List<Teammate> shown = T.ShowTeammates();
        foreach (Teammate m in shown) Freeze(m);
        Teammate cop = shown.FirstOrDefault();
        foreach (Teammate m in shown) if (m != cop) m.gameObject.SetActive(false);
        if (cop == null) T.Log("   (bỏ qua 'thấy cảnh sát': không có đồng đội)");
        else if (!TryFindVisibleSpot(h, agent, out Vector3 copPos)) T.Log("   (bỏ qua 'thấy cảnh sát': không có chỗ trước mặt con tin)");
        else
        {
            Vector3 copHome = cop.transform.position;
            Warp(cop, copPos);
            yield return 0.5f;
            T.Check(h.SeesPolice, $"thấy cảnh sát: con tin thấy {cop.name}");
            if (TryFindPointAt(agent, h.transform.position, 9f, 12f, out Vector3 shot2))
            {
                Vector3 p1 = h.transform.position;
                NPCHearing.Report(shot2 + Vector3.up * 1.4f, 30f, NoiseType.Gunshot);
                T.Check(!h.ShouldFlee, "thấy cảnh sát: nghe súng nhưng KHÔNG bỏ chạy");
                yield return 2f;
                T.Check(Flat(h.transform.position - p1) < 1f, $"thấy cảnh sát: đứng lại ({Flat(h.transform.position - p1):0.0} m)");
            }
            Warp(cop, copHome);
            yield return 0.5f;
        }
        T.HideTeammates(); // các bước sau: không có cảnh sát trong tầm nhìn

        // ---------- 4. Held: bị khống chế, được buông -> chạy khỏi kẻ giữ ----------
        if (enemy == null) T.Log("   (bỏ qua Held: không có nghi phạm)");
        else
        {
            enemy.gameObject.SetActive(true);
            Freeze(enemy); // đứng yên, không bắn: chỉ đóng vai kẻ giữ
            yield return 0.5f;
            if (NavMesh.SamplePosition(h.transform.position - h.transform.forward * 0.9f, out NavMeshHit eh, 1.5f, NavMesh.AllAreas)) Warp(enemy, eh.position);
            yield return 0.3f;
            T.Check(h.SetHeld(true, enemy), "Held: nghi phạm bắt được con tin");
            Vector3 ph = h.transform.position;
            yield return 1.5f;
            T.Check(h.IsHeld && Flat(h.transform.position - ph) < 0.3f, "Held: đứng yên khi bị giữ");
            T.Check(anim == null || anim.GetBool(ScaredHash), "Held: co người ôm đầu");
            Compliance.Order(h.transform.position + h.transform.forward * 3f, 10f, 10f);
            T.Check(!h.IsSurrendered && !h.ComplyOrdered, "Held: bị hô vẫn không dám giơ tay");

            h.SetHeld(false, enemy);
            float e0 = Flat(h.transform.position - enemy.transform.position);
            T.Check(h.ShouldFlee || h.SeesPolice, "Held: được buông -> muốn chạy khỏi kẻ giữ");
            float eMax = e0;
            for (float t = 0f; t < 6f; t += 0.25f)
            {
                yield return 0.25f;
                eMax = Mathf.Max(eMax, Flat(h.transform.position - enemy.transform.position));
                if (eMax >= e0 + 4f) break;
            }
            if (!h.SeesPolice) T.Check(eMax >= e0 + 3f, $"Held: chạy khỏi kẻ giữ ({e0:0.0} -> {eMax:0.0} m)");
            enemy.gameObject.SetActive(false);
        }
        yield return 1f;

        // ---------- 5. Surrender (1-3 lần hô) + Restrained ----------
        Vector3 shoutFrom = NPCConditionUtils.EyeOf(h.gameObject) + h.transform.forward * 3f;
        int shouts = 0;
        while (!h.ComplyOrdered && shouts < 5)
        {
            Compliance.Order(shoutFrom, 10f, 10f);
            shouts++;
            yield return 0.6f;
        }
        T.Check(h.ComplyOrdered && shouts <= 3, $"Surrender: giơ tay sau {shouts} lần hô (SWAT 4: 1-3)");
        for (float t = 0f; t < 2f && !h.IsSurrendered; t += 0.25f) yield return 0.25f;
        T.Check(h.IsSurrendered, "Surrender: quỳ giơ tay chờ còng");
        NPCHearing.Report(h.transform.position + Vector3.right * 2f, 30f, NoiseType.Gunshot);
        T.Check(!h.ShouldFlee && !h.ShouldCower, "Surrender: đã giơ tay thì không chạy / ôm đầu nữa");
        T.Check(h.Restrain(), "Restrained: còng được");
        yield return 1f;
        T.Check(h.IsRestrained, "Restrained: con tin bị còng");
    }

    // ---------- Tiện ích ----------

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    private static void Warp(Component c, Vector3 pos)
    {
        if (c.TryGetComponent(out NavMeshAgent a) && a.enabled) a.Warp(pos);
        else c.transform.position = pos;
    }

    /// <summary>Đứng yên làm vai phụ: tắt graph, cất súng.</summary>
    private static void Freeze(Component c)
    {
        if (c.TryGetComponent(out BehaviorGraphAgent g)) { g.End(); g.enabled = false; }
        if (c.TryGetComponent(out NavMeshAgent a) && a.enabled && a.isOnNavMesh) { a.ResetPath(); a.isStopped = true; }
        foreach (NPCWeaponHolder w in c.GetComponentsInChildren<NPCWeaponHolder>()) { w.StopAiming(); w.enabled = false; }
    }

    private static bool TryFindPointAt(NavMeshAgent agent, Vector3 from, float min, float max, out Vector3 point)
    {
        for (int i = 0; i < 24; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 15f, 0f) * Vector3.forward;
            if (!NavMesh.SamplePosition(from + dir * Mathf.Lerp(min, max, (i % 3) / 2f), out NavMeshHit hit, 1.5f, agent.areaMask)) continue;
            float d = Flat(hit.position - from);
            if (d < min || d > 14.5f) continue;
            point = hit.position;
            return true;
        }
        point = from;
        return false;
    }

    /// <summary>Chỗ trước mặt con tin (±40°, 3-6 m) trên NavMesh, nhìn thẳng thấy nhau.</summary>
    private static bool TryFindVisibleSpot(Hostage h, NavMeshAgent agent, out Vector3 point)
    {
        Vector3 eye = NPCConditionUtils.EyeOf(h.gameObject);
        Vector3 fwd = new Vector3(h.transform.forward.x, 0f, h.transform.forward.z).normalized;
        // Trong góc nhìn (±60°) ở 3-6 m, hoặc mọi hướng ở 2.5 m (trong bán kính cảm nhận 3 m của con tin).
        var spots = new System.Collections.Generic.List<(float angle, float dist)>();
        foreach (float a in new[] { 0f, 20f, -20f, 40f, -40f, 60f, -60f })
            foreach (float d in new[] { 4f, 3f, 5f, 6f }) spots.Add((a, d));
        foreach (float a in new[] { 90f, -90f, 120f, -120f, 150f, -150f, 180f }) spots.Add((a, 2.5f));
        foreach ((float angle, float dist) in spots)
            {
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * fwd;
                if (!NavMesh.SamplePosition(h.transform.position + dir * dist, out NavMeshHit hit, 1f, agent.areaMask)) continue;
                if (!NPCMorale.ClearPath(eye, hit.position + Vector3.up * 1.5f, h.transform)) continue;
                point = hit.position;
                return true;
            }
        point = h.transform.position;
        return false;
    }
}
