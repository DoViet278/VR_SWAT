using System.Collections.Generic;
using System.Linq;
using Unity.Behavior;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using T = NPCPlayModeTests;

/// <summary>
/// Test Play Mode giai đoạn 1 (Enemy, chiến thuật SWAT 4): Investigate, Barricade, Threaten hostage, Flee.
/// Chạy: Tools/VR SWAT/Tests/Phase 1 - Enemy, hoặc NPCPlayModeTests.Run("Phase1.Enemy").
/// Cần scene có ít nhất 1 nghi phạm; Threaten / Flee cần thêm 1 đồng đội (được dịch ra trước mặt nghi phạm làm
/// "cảnh sát", tắt graph + súng để đứng yên) và Threaten cần 1 con tin.
/// </summary>
[InitializeOnLoad]
public static class EnemyPlayModeTests
{
    static EnemyPlayModeTests() => T.Register("Phase1.Enemy", EnemyTest, 90f, true);

    [MenuItem("Tools/VR SWAT/Tests/Phase 1 - Enemy")]
    private static void RunEnemy() => T.Run("Phase1.Enemy");

    private static IEnumerator<float> EnemyTest()
    {
        NPC enemy = T.FirstSuspect();
        if (!T.Check(enemy != null, "scene có nghi phạm")) yield break;
        EnemyTactics tactics = enemy.GetComponent<EnemyTactics>();
        if (!T.Check(tactics != null, "Enemy có EnemyTactics (EnemySetupTool đã chạy)")) yield break;
        if (!T.Check(NPCActionUtils.TryGetAgent(enemy.gameObject, out NavMeshAgent agent), "Enemy có NavMeshAgent")) yield break;
        NPCMorale morale = enemy.GetComponent<NPCMorale>();
        T.HideTeammates(); // Investigate / Barricade: chưa ai thấy cảnh sát
        T.MovePlayerAway(enemy.transform.position);
        enemy.ForgetTarget(); // lỡ thấy đồng đội / người chơi ở frame đầu (bố cục scene) thì quên đi
        yield return 0.5f;
        T.Log($"Enemy {enemy.name} tại {enemy.transform.position}, alerted = {enemy.IsAlerted}, morale {(morale != null ? morale.Morale : -1f):0.00}");

        // ---------- 1. Investigate: tiếng súng ở xa, chưa thấy ai -> đi tới xem ----------
        if (enemy.IsAlerted) T.Log("   (bỏ qua Investigate: Enemy đã thấy cảnh sát lúc vào scene)");
        else if (!TryFindReachable(agent, enemy.transform.position, 6f, 9f, out Vector3 noisePos))
            T.Log("   (bỏ qua Investigate: không tìm được điểm NavMesh 6-9 m đi tới được)");
        else
        {
            float startDist = Flat(enemy.transform.position - noisePos);
            NPCHearing.Report(noisePos, 30f, NoiseType.Gunshot);
            T.Check(NPCHearing.TryGetNoise(enemy.gameObject, 1f, out HeardNoise heard) && heard.Type == NoiseType.Gunshot,
                "Investigate: Enemy nghe tiếng súng");
            float closest = startDist;
            for (float t = 0f; t < 8f && closest > startDist - 2f && !enemy.IsAlerted; t += 0.25f)
            {
                yield return 0.25f;
                closest = Mathf.Min(closest, Flat(enemy.transform.position - noisePos));
            }
            if (enemy.IsAlerted) T.Log("   (Investigate bị ngắt: Enemy thấy cảnh sát)");
            else T.Check(closest <= startDist - 2f, $"Investigate: Enemy đi về phía tiếng động ({startDist:0.0} -> {closest:0.0} m)");
        }

        // ---------- 2. Barricade: đứng yên, không tuần tra ----------
        if (enemy.TryGetComponent(out NPCHearing hearing)) hearing.Consume();
        tactics.Barricaded = true;
        RestartGraph(enemy); // graph không Abort: chạy lại để Selector chọn nhánh mới ngay
        yield return 1f;
        if (enemy.IsAlerted) T.Log("   (bỏ qua Barricade: Enemy đang giao chiến)");
        else
        {
            Vector3 p0 = enemy.transform.position;
            yield return 2.5f;
            T.Check(Flat(enemy.transform.position - p0) < 0.3f, $"Barricade: Enemy đứng yên ({Flat(enemy.transform.position - p0):0.00} m)");
            T.Check(!agent.hasPath || agent.isStopped || agent.remainingDistance < 0.3f, "Barricade: không tuần tra");
        }
        tactics.Barricaded = false;

        // ---------- Dựng "cảnh sát" trước mặt Enemy ----------
        List<Teammate> shown = T.ShowTeammates();
        Teammate cop = shown.FirstOrDefault(m => m != null && !m.GetComponent<NPC>().IsDead);
        if (cop == null) { T.Log("   (bỏ qua Threaten / Flee: scene không có đồng đội)"); yield break; }
        NPC copNpc = cop.GetComponent<NPC>();
        Freeze(cop); // ngay khi bật lại: không kịp hô / bắn
        foreach (Teammate m in shown) if (m != cop) m.gameObject.SetActive(false); // chỉ cần 1 "cảnh sát"
        if (!TryFindVisibleSpot(enemy, agent, out Vector3 copPos))
        {
            T.Log("   (bỏ qua Threaten / Flee: không có chỗ trống trước mặt Enemy)");
            yield break;
        }
        Warp(cop, copPos);
        cop.transform.rotation = Quaternion.LookRotation(Flatten(enemy.transform.position - copPos));
        enemy.SetTarget(copNpc.gameObject);
        enemy.NotifyThreat();
        for (float t = 0f; t < 2f && !enemy.CanSeeTarget; t += 0.25f) yield return 0.25f;
        T.Check(enemy.CanSeeTarget && enemy.IsAlerted, $"setup: Enemy thấy {cop.name} tại {copPos}");

        // ---------- 3. Threaten hostage ----------
        Hostage hostage = T.FirstHostage();
        if (hostage == null || hostage.IsSurrendered) T.Log("   (bỏ qua Threaten: không có con tin rảnh)");
        else
        {
            if (morale != null && morale.State != MoraleState.Aggressive) morale.Boost(1f, "test");
            Vector3 side = enemy.transform.right;
            if (NavMesh.SamplePosition(enemy.transform.position + side * 1.5f, out NavMeshHit hh, 1f, NavMesh.AllAreas)) Warp(hostage, hh.position);
            tactics.CanTakeHostage = true;
            RestartGraph(enemy);
            for (float t = 0f; t < 4f && !hostage.IsHeld; t += 0.25f) yield return 0.25f;
            T.Check(hostage.IsHeld && hostage.HeldBy == enemy, $"Threaten: Enemy khống chế con tin (morale {MoraleText(morale)})");
            if (hostage.IsHeld)
            {
                yield return 2f;
                Vector3 toCop = Flatten(copPos - hostage.transform.position);
                Vector3 toEnemy = Flatten(enemy.transform.position - hostage.transform.position);
                T.Check(Vector3.Dot(toCop.normalized, toEnemy.normalized) < 0f, "Threaten: Enemy đứng sau con tin (con tin chắn giữa)");
                T.Check(!hostage.IsSurrendered, "Threaten: con tin bị giữ không giơ tay");

                // Hô 2 lần -> buông.
                Vector3 shoutFrom = NPCConditionUtils.EyeOf(copNpc.gameObject);
                for (int i = 0; i < 2 && hostage.IsHeld; i++)
                {
                    Compliance.Order(shoutFrom, 15f, 15f, copNpc.gameObject);
                    yield return 0.6f;
                }
                for (float t = 0f; t < 2f && hostage.IsHeld; t += 0.25f) yield return 0.25f;
                T.Check(!hostage.IsHeld, $"Threaten: bị hô 2 lần thì buông con tin (Enemy đầu hàng = {enemy.IsSurrendered})");
            }
            tactics.CanTakeHostage = false;
        }

        // ---------- 4. Flee: morale Panicked + cảnh sát gần ----------
        if (enemy.IsSurrendered || enemy.IsDead) { T.Log("   (bỏ qua Flee: Enemy đã đầu hàng / chết)"); yield break; }
        if (morale == null) { T.Log("   (bỏ qua Flee: Enemy không có NPCMorale)"); yield break; }
        morale.Pressure(morale.Morale - morale.ComplyBelow + 0.03f, "test: hoảng loạn");
        T.Check(morale.State == MoraleState.Panicked, $"Flee: morale Panicked ({morale.Morale:0.00})");
        enemy.NotifyThreat();
        float fleeStart = Time.time;
        float d0 = Flat(enemy.transform.position - copPos);
        // Không Restart graph: kiểm tra đúng như lúc chơi (action đang chạy phải nhường cho Flee).
        float dMax = d0;
        for (float t = 0f; t < 8f && !enemy.IsSurrendered; t += 0.25f)
        {
            yield return 0.25f;
            dMax = Mathf.Max(dMax, Flat(enemy.transform.position - copPos));
            if (dMax >= d0 + 3f) break;
        }
        bool tried = tactics.LastFleeTime >= fleeStart;
        T.Check(tried || enemy.IsSurrendered, $"Flee: nhánh Flee được chọn trong lúc chơi (sau {(tried ? tactics.LastFleeTime - fleeStart : -1f):0.0} s, đầu hàng = {enemy.IsSurrendered})");
        if (!tried || enemy.IsSurrendered) yield break;
        T.Check(dMax >= d0 + 3f, $"Flee: Enemy chạy xa cảnh sát ({d0:0.0} -> {dMax:0.0} m)");

        // Chạy tới nơi: cố thủ ở chỗ mới, không quay về chỗ cảnh sát.
        for (float t = 0f; t < 12f && !tactics.Barricaded && !enemy.IsSurrendered; t += 0.25f) yield return 0.25f;
        T.Check(tactics.Barricaded, "Flee: tới nơi thì cố thủ (Barricaded)");
        float dArrive = Flat(enemy.transform.position - copPos);
        yield return 4f;
        float dLater = Flat(enemy.transform.position - copPos);
        T.Check(dLater >= dArrive - 1f, $"Flee: không quay lại chỗ cảnh sát ({dArrive:0.0} -> {dLater:0.0} m sau 4 s)");
        tactics.Barricaded = false;
    }

    // ---------- Tiện ích ----------

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
    private static Vector3 Flatten(Vector3 v) => new(v.x, 0f, v.z);
    private static string MoraleText(NPCMorale m) => m != null ? $"{m.Morale:0.00} {m.State}" : "n/a";

    private static void RestartGraph(Component c)
    {
        if (c.TryGetComponent(out BehaviorGraphAgent g) && g.enabled) g.Restart();
    }

    private static void Warp(Component c, Vector3 pos)
    {
        if (c.TryGetComponent(out NavMeshAgent a) && a.enabled) a.Warp(pos);
        else c.transform.position = pos;
    }

    /// <summary>Đứng yên làm bia: tắt graph, cất súng (không bắn trả).</summary>
    private static void Freeze(Component c)
    {
        if (c.TryGetComponent(out BehaviorGraphAgent g)) { g.End(); g.enabled = false; }
        if (c.TryGetComponent(out NavMeshAgent a) && a.enabled && a.isOnNavMesh) { a.ResetPath(); a.isStopped = true; }
        foreach (NPCWeaponHolder h in c.GetComponentsInChildren<NPCWeaponHolder>()) { h.StopAiming(); h.enabled = false; }
    }

    private static bool TryFindReachable(NavMeshAgent agent, Vector3 from, float min, float max, out Vector3 point)
    {
        var path = new NavMeshPath();
        for (int i = 0; i < 24; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 15f, 0f) * Vector3.forward;
            if (!NavMesh.SamplePosition(from + dir * Mathf.Lerp(min, max, (i % 3) / 2f), out NavMeshHit hit, 1.5f, agent.areaMask)) continue;
            if (Flat(hit.position - from) < min) continue;
            if (NavMesh.CalculatePath(agent.nextPosition, hit.position, agent.areaMask, path) && path.status == NavMeshPathStatus.PathComplete)
            {
                point = hit.position;
                return true;
            }
        }
        point = from;
        return false;
    }

    /// <summary>Chỗ trước mặt Enemy (±40°, 4-7 m) trên NavMesh, nhìn thẳng thấy nhau.</summary>
    private static bool TryFindVisibleSpot(NPC enemy, NavMeshAgent agent, out Vector3 point)
    {
        Vector3 eye = NPCConditionUtils.EyeOf(enemy.gameObject);
        foreach (float angle in new[] { 0f, 20f, -20f, 40f, -40f, 60f, -60f })
            foreach (float dist in new[] { 6f, 5f, 7f, 4f })
            {
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Flatten(enemy.transform.forward).normalized;
                if (!NavMesh.SamplePosition(enemy.transform.position + dir * dist, out NavMeshHit hit, 1f, agent.areaMask)) continue;
                if (!NPCMorale.ClearPath(eye, hit.position + Vector3.up * 1.5f, enemy.transform)) continue;
                point = hit.position;
                return true;
            }
        point = enemy.transform.position;
        return false;
    }
}
