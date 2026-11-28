using System.Collections.Generic;
using System.Linq;
using Unity.Behavior;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using T = NPCPlayModeTests;

/// <summary>
/// Test Play Mode: hệ thống sát thương / máu thiết kế lại (DamageInfo, vùng trúng, giáp, chết ngay + anim chết,
/// SwatPlayerHealth, flashbang nổ sát người, lựu đạn sát thương của NPC).
/// Chạy: Tools/VR SWAT/Tests/Phase 4 - Damage, hoặc NPCPlayModeTests.Run("Phase4.Damage").
/// Cần scene có 1 nghi phạm, 2 đồng đội, 1 con tin và người chơi (tag Player).
/// </summary>
[InitializeOnLoad]
public static class DamagePlayModeTests
{
    static DamagePlayModeTests() => T.Register("Phase4.Damage", DamageTest, 120f, true);

    [MenuItem("Tools/VR SWAT/Tests/Phase 4 - Damage")]
    private static void RunDamage() => T.Run("Phase4.Damage");

    private static IEnumerator<float> DamageTest()
    {
        NPC enemy = T.FirstSuspect();
        Hostage hostage = T.FirstHostage();
        if (!T.Check(enemy != null && hostage != null, "scene có nghi phạm + con tin")) yield break;
        SwatPlayerHealth player = SwatPlayerHealth.Instance;
        T.Check(player != null, $"SwatPlayerHealth tự gắn lên người chơi ({(player != null ? player.name : "-")})");
        T.MovePlayerAway(enemy.transform.position);

        // ---------- 1. Đồng đội: máu 150 + giáp ----------
        SquadController.GetOrCreate(); // nghe Teammate.Died -> "Officer down!"
        List<Teammate> mates = T.ShowTeammates();
        Teammate alpha = mates.FirstOrDefault(m => m.Slot == 0) ?? mates.FirstOrDefault();
        Teammate bravo = mates.FirstOrDefault(m => m != alpha);
        if (!T.Check(alpha != null && bravo != null, "có 2 đồng đội")) yield break;
        foreach (Teammate m in mates) Freeze(m);
        var said = new List<string>();
        alpha.OnSay.AddListener(s => said.Add(s));
        NPC a = alpha.Npc;
        T.Check(Mathf.Approximately(a.MaxHealth, 150f) && Mathf.Approximately(a.Health, 150f), $"Teammate: máu 150 ({a.Health:0}/{a.MaxHealth:0})");
        Damage.Apply(alpha.gameObject, DamageInfo.Bullet(25f, enemy.transform.position).WithZone(HitZone.Torso));
        T.Check(Near(a.Health, 150f - 12.5f), $"Teammate: đạn 25 trúng thân, giáp 50% -> mất 12.5 (còn {a.Health:0.##})");
        float h0 = a.Health;
        Damage.Apply(alpha.gameObject, DamageInfo.Bullet(25f, enemy.transform.position).WithZone(HitZone.Head));
        T.Check(Near(a.Health, h0 - 25f * 3f * 0.65f), $"Teammate: trúng đầu x3, mũ 35% -> mất 48.75 (còn {a.Health:0.##})");
        h0 = a.Health;
        Damage.Apply(alpha.gameObject, DamageInfo.Bullet(25f, enemy.transform.position).WithZone(HitZone.Limb));
        T.Check(Near(a.Health, h0 - 15f), $"Teammate: trúng tay chân x0.6 -> mất 15 (còn {a.Health:0.##})");
        T.Check(a.LastDamage.Type == DamageType.Bullet && a.LastDamage.Zone == HitZone.Limb, $"Teammate: LastDamage = {a.LastDamage}");

        // ---------- 2. Flashbang nổ sát người: sát thương nhẹ ----------
        h0 = a.Health;
        T.DetonateFlashbang(NPCConditionUtils.CenterOf(alpha.gameObject) + alpha.transform.forward * 0.4f);
        T.Check(a.Health < h0 && a.Health > h0 - 16f, $"Flash sát người: mất {h0 - a.Health:0.#} máu (<= 15)");
        T.Check(a.LastDamage.Type == DamageType.Flash, $"Flash sát người: loại {a.LastDamage.Type}");
        yield return 0.5f;

        // ---------- 3. Enemy đang bắn thì bị người chơi bắn: chân rồi đầu -> chết ngay, anim chết ----------
        Vector3 copPos = enemy.transform.position + Flatten(enemy.transform.forward).normalized * 5f;
        if (NavMesh.SamplePosition(copPos, out NavMeshHit ch, 2f, NavMesh.AllAreas)) Warp(bravo, ch.position);
        bravo.gameObject.SetActive(true);
        enemy.SetTarget(bravo.gameObject);
        enemy.NotifyThreat();
        if (enemy.TryGetComponent(out BehaviorGraphAgent eg) && eg.enabled) eg.Restart();
        NPCWeaponHolder eh = enemy.GetComponentInChildren<NPCWeaponHolder>();
        float bravoHp = bravo.Npc.Health;
        for (float t = 0f; t < 8f && (eh == null || Time.time - eh.LastFireTime > 0.5f); t += 0.25f) yield return 0.25f;
        bool firing = eh != null && Time.time - eh.LastFireTime <= 0.5f;
        T.Log($"   (thông tin) Enemy đang bắn = {firing}, Bravo máu {bravoHp:0} -> {bravo.Npc.Health:0}");

        Animator ea = enemy.GetComponentInChildren<Animator>();
        Transform hips = ea != null && ea.isHuman ? ea.GetBoneTransform(HumanBodyBones.Hips) : enemy.transform;
        Transform head = ea != null && ea.isHuman ? ea.GetBoneTransform(HumanBodyBones.Head) : enemy.transform;
        IDamageable target = enemy;
        target.ApplyDamage(35f, FakeHit(hips.position - Vector3.up * 0.45f));
        T.Check(enemy.LastDamage.Zone == HitZone.Limb && Near(enemy.Health, 100f - 21f), $"Player bắn chân Enemy: {enemy.LastDamage}, máu {enemy.Health:0.#} (cần 79)");
        target.ApplyDamage(35f, FakeHit(head.position + Vector3.up * 0.05f));
        T.Check(enemy.LastDamage.Zone == HitZone.Head, $"Player bắn đầu Enemy: {enemy.LastDamage}");
        T.Check(enemy.IsDead, "Enemy chết ngay khi hết máu (đang giao chiến)");
        T.Check(!(eg != null && eg.enabled), "Enemy chết: Behavior Graph dừng ngay");
        T.Check(eh == null || !eh.HasWeapon, "Enemy chết: thả súng");
        T.Check(ea == null || ea.layerCount < 2 || ea.GetLayerWeight(1) < 0.01f, "Enemy chết: tắt layer thân trên");
        yield return 0.6f;
        T.Check(InDeathState(ea), $"Enemy chết: đang chạy anim chết ({StateName(ea)}, hướng {enemy.LastHitSide})");
        T.Check(enemy.GetComponentsInChildren<Collider>().All(c => !c.enabled), "Enemy chết: tắt collider");
        Vector3 deadPos = enemy.transform.position;
        yield return 2f;
        T.Check(enemy != null && Flat(enemy.transform.position - deadPos) < 0.2f, "Enemy chết: nằm yên, xác còn đó sau 2 s");

        // ---------- 4. Lựu đạn sát thương của NPC: con tin + đồng đội trong tầm ----------
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/NPC/Weapon/NPC_Grenade.prefab");
        if (prefab == null) T.Log("   (bỏ qua lựu đạn: không có NPC_Grenade.prefab)");
        else
        {
            Vector3 gp = alpha.transform.position + alpha.transform.right * 3f + Vector3.up * 0.3f;
            float aBefore = a.Health;
            GameObject g = Object.Instantiate(prefab, gp, Quaternion.identity);
            NPCGrenade grenade = g.GetComponent<NPCGrenade>();
            grenade.Launch(Vector3.zero, null, null);
            float expected = grenade.PredictDamage(gp, alpha.gameObject);
            grenade.Explode();
            T.Check(a.LastDamage.Type == DamageType.Explosion && a.Health < aBefore, $"Lựu đạn NPC: Alpha trúng nổ ({a.LastDamage}, mất {aBefore - a.Health:0.#})");
            if (expected > 0f && !a.IsDead)
                T.Check(Near(aBefore - a.Health, expected * (1f - 0.5f * DamageModel.ArmorVsExplosion), 1f),
                    $"Lựu đạn NPC: giáp chặn 25% sát thương nổ ({expected:0.#} -> {expected * 0.75f:0.#})");
        }

        // ---------- 5. Con tin: trúng đầu -> chết, anim chết ----------
        Animator ha = hostage.GetComponentInChildren<Animator>();
        Damage.Apply(hostage.gameObject, DamageInfo.Bullet(20f, hostage.transform.position + hostage.transform.forward * 5f).WithZone(HitZone.Head));
        T.Check(hostage.IsDead, $"Con tin: đạn 20 trúng đầu x3 -> chết ({hostage.LastDamage})");
        yield return 0.6f;
        T.Check(InDeathState(ha), $"Con tin: anim chết ({StateName(ha)})");

        // ---------- 6. Đồng đội chết: anim chết, đồng đội kia báo "Officer down!" ----------
        said.Clear();
        Damage.Apply(bravo.gameObject, DamageInfo.Bullet(500f, alpha.transform.position).WithZone(HitZone.Torso));
        T.Check(bravo.IsDead, "Bravo: hết máu -> chết");
        yield return 0.6f;
        T.Check(InDeathState(bravo.GetComponentInChildren<Animator>()), $"Bravo: anim chết ({StateName(bravo.GetComponentInChildren<Animator>())})");
        T.Check(said.Any(s => s.Contains("Officer down")), "Alpha báo \"Officer down!\"");

        // ---------- 7. Người chơi: máu, giáp, chết ----------
        if (player != null)
        {
            GameObject root = player.gameObject;
            root.SendMessage("TakeDamage", 10f, SendMessageOptions.DontRequireReceiver);
            T.Check(Near(player.Health, 100f - 6f), $"Player: SendMessage TakeDamage 10 (hệ cũ, coi như thân, giáp 40%) -> còn {player.Health:0.#} (cần 94)");
            Damage.Apply(root, DamageInfo.Bullet(25f, Vector3.zero).WithZone(HitZone.Head));
            T.Check(Near(player.Health, 94f - 25f * 3f * 0.75f), $"Player: trúng đầu x3, mũ 25% -> còn {player.Health:0.#} (cần 37.75)");
            yield return 0.1f;
            GameObject overlay = GameObject.Find("SwatPlayerHealth Overlay");
            T.Check(overlay != null, "Player: có lớp phủ đỏ khi trúng / máu thấp");
            Damage.Apply(root, DamageInfo.Explosion(100f, root.transform.position));
            T.Check(player.IsDead, $"Player: lựu đạn 100 (giáp chặn 20%) -> chết (máu {player.Health:0.#})");
            yield return 3f;
            GameObject txt = GameObject.Find("SwatPlayerHealth Overlay/Text");
            T.Check(txt != null && txt.GetComponent<TMPro.TextMeshProUGUI>().enabled, "Player chết: hiện \"BẠN ĐÃ BỊ HẠ\"");
            T.Check(SwatPlayerHealth.IsPlayerDead(root), "Player chết: NPC thôi nhắm (IsPlayerDead)");
        }
    }

    // ---------- Tiện ích ----------

    private static bool Near(float a, float b, float tol = 0.05f) => Mathf.Abs(a - b) <= tol;
    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
    private static Vector3 Flatten(Vector3 v) => new(v.x, 0f, v.z);

    private static RaycastHit FakeHit(Vector3 point) => new() { point = point, normal = Vector3.up };

    private static readonly string[] DeathStates =
        { "DieFront", "DieBack", "DieLeft", "DieRight", "ComplyDieFront", "ComplyDieBack", "ComplyDieLeft", "ComplyDieRight" };

    private static bool InDeathState(Animator a)
    {
        if (a == null) return false;
        AnimatorStateInfo cur = a.GetCurrentAnimatorStateInfo(0), next = a.GetNextAnimatorStateInfo(0);
        return DeathStates.Any(s => cur.IsName(s) || next.IsName(s));
    }

    private static string StateName(Animator a)
    {
        if (a == null) return "không có Animator";
        AnimatorStateInfo cur = a.GetCurrentAnimatorStateInfo(0);
        string n = DeathStates.FirstOrDefault(s => cur.IsName(s));
        return n ?? $"hash {cur.shortNameHash}";
    }

    private static void Warp(Component c, Vector3 pos)
    {
        if (c.TryGetComponent(out NavMeshAgent a) && a.enabled) a.Warp(pos);
        else c.transform.position = pos;
    }

    private static void Freeze(Component c)
    {
        if (c.TryGetComponent(out BehaviorGraphAgent g)) { g.End(); g.enabled = false; }
        if (c.TryGetComponent(out NavMeshAgent a) && a.enabled && a.isOnNavMesh) { a.ResetPath(); a.isStopped = true; }
        foreach (NPCWeaponHolder w in c.GetComponentsInChildren<NPCWeaponHolder>()) { w.StopAiming(); w.enabled = false; }
    }
}
