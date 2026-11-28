using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Chạy test tự động trong Play Mode cho NPC (menu Tools/VR SWAT/Tests, hoặc NPCPlayModeTests.Run("tên") qua MCP).
///
/// - Run(name): ghi tên test vào SessionState rồi vào Play Mode (domain reload xong vẫn nhớ), scene đang mở được dùng.
/// - Test là IEnumerator&lt;float&gt;: mỗi yield return = số giây (thời gian game) chờ trước bước tiếp theo.
/// - Check(điều kiện, mô tả) ghi PASS / FAIL. Xong (hoặc lỗi / quá TimeLimit) thì in tổng kết ra Console,
///   ghi file Library/NPCTestResult.txt (dòng cuối "DONE ...") và thoát Play Mode.
/// - Test của từng phần đăng ký bằng Register(name, factory) trong [InitializeOnLoadMethod].
/// </summary>
[InitializeOnLoad]
public static class NPCPlayModeTests
{
    private const string PendingKey = "NPCPlayModeTests.Pending";
    private const float StartDelay = 2f;   // chờ scene khởi động (NPC Start, graph chạy)
    private const float TimeLimit = 90f;   // một test tối đa (giây game)

    public static string ResultPath => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? "", "Library", "NPCTestResult.txt");

    private static readonly Dictionary<string, Func<IEnumerator<float>>> s_Tests = new();
    private static IEnumerator<float> s_Routine;
    private static string s_Name;
    private static float s_Next;
    private static float s_Start;
    private static int s_Pass, s_Fail;
    private static readonly StringBuilder s_Log = new();

    /// <summary>
    /// Yêu cầu chạy test qua file (không cần MCP): ghi tên test (mỗi dòng một test, chạy lần lượt) vào
    /// Library/NPCTestRequest.txt; editor đọc, xóa file rồi chạy. Kết quả ở Library/NPCTestResult.txt (mỗi test ghi đè)
    /// và Library/NPCTestResult_&lt;tên&gt;.txt.
    /// </summary>
    public static string RequestPath => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? "", "Library", "NPCTestRequest.txt");

    private const string QueueKey = "NPCPlayModeTests.Queue";
    private static double s_NextRequestCheck;

    private static void PollRequests()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        if (EditorApplication.timeSinceStartup < s_NextRequestCheck) return;
        s_NextRequestCheck = EditorApplication.timeSinceStartup + 1.0;

        string queue = SessionState.GetString(QueueKey, "");
        if (File.Exists(RequestPath))
        {
            try
            {
                string text = File.ReadAllText(RequestPath);
                File.Delete(RequestPath);
                queue = string.Join("\n", new[] { queue, text }.Where(s => !string.IsNullOrWhiteSpace(s)));
            }
            catch (IOException) { return; } // đang được ghi: lần sau
        }
        string[] names = queue.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        if (names.Length == 0) return;
        SessionState.SetString(QueueKey, string.Join("\n", names.Skip(1)));
        Debug.Log($"[NPCTest] Yêu cầu qua file: chạy {names[0]}{(names.Length > 1 ? $" (còn {names.Length - 1} test chờ)" : "")}.");
        Run(names[0]);
    }

    static NPCPlayModeTests()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.update += PollRequests;
        Register("Phase0.Flashbang", FlashbangTest, TimeLimit, true);
    }

    private static readonly Dictionary<string, float> s_Limits = new();
    private static readonly HashSet<string> s_Isolate = new();
    private static float s_Limit = TimeLimit;

    /// <summary>
    /// Đăng ký test. timeLimit: giây game tối đa (mặc định 90). isolateTeammates: tắt mọi đồng đội ngay khi vào Play Mode
    /// (trước cả StartDelay), để bố cục scene (đồng đội đứng gần nghi phạm / con tin) không làm nhiễu test.
    /// </summary>
    public static void Register(string name, Func<IEnumerator<float>> factory, float timeLimit = TimeLimit, bool isolateTeammates = false)
    {
        s_Tests[name] = factory;
        s_Limits[name] = timeLimit;
        if (isolateTeammates) s_Isolate.Add(name); else s_Isolate.Remove(name);
    }

    public static IEnumerable<string> TestNames => s_Tests.Keys;

    /// <summary>Chạy test name trong Play Mode (tự vào Play Mode nếu chưa).</summary>
    public static void Run(string name)
    {
        if (!s_Tests.ContainsKey(name))
        {
            Debug.LogError($"[NPCTest] Không có test '{name}'. Có: {string.Join(", ", s_Tests.Keys)}");
            return;
        }
        if (File.Exists(ResultPath)) File.Delete(ResultPath);
        SessionState.SetString(PendingKey, name);
        if (EditorApplication.isPlaying) Begin();
        else EditorApplication.isPlaying = true;
    }

    [MenuItem("Tools/VR SWAT/Tests/Phase 0 - Flashbang")]
    private static void RunFlashbang() => Run("Phase0.Flashbang");

    // ---------- Driver ----------

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode && !string.IsNullOrEmpty(SessionState.GetString(PendingKey, ""))) Begin();
        if (state == PlayModeStateChange.ExitingPlayMode && s_Routine != null) Finish("thoát Play Mode giữa chừng");
    }

    private static void Begin()
    {
        s_Name = SessionState.GetString(PendingKey, "");
        SessionState.EraseString(PendingKey);
        if (!s_Tests.TryGetValue(s_Name, out var factory)) return;
        s_Pass = s_Fail = 0;
        s_Log.Clear();
        s_Limit = s_Limits.TryGetValue(s_Name, out float limit) ? limit : TimeLimit;
        s_Hidden.Clear();
        if (s_Isolate.Contains(s_Name)) HideTeammates();
        s_Routine = factory();
        s_Start = Time.time;
        s_Next = Time.time + StartDelay;
        Log($"=== {s_Name} bắt đầu (scene {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}) ===");
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        if (s_Routine == null || !EditorApplication.isPlaying) return;
        if (Time.time - s_Start > s_Limit) { Fail($"quá {s_Limit} s"); Finish("timeout"); return; }
        if (Time.time < s_Next) return;
        try
        {
            if (s_Routine.MoveNext()) s_Next = Time.time + Mathf.Max(0f, s_Routine.Current);
            else Finish(null);
        }
        catch (Exception e)
        {
            Fail($"lỗi: {e.GetType().Name}: {e.Message}");
            Debug.LogException(e);
            Finish("exception");
        }
    }

    private static void Finish(string abortReason)
    {
        EditorApplication.update -= Tick;
        s_Routine = null;
        string summary = $"DONE {s_Name}: {s_Pass} PASS, {s_Fail} FAIL{(abortReason != null ? $" (dừng: {abortReason})" : "")}";
        Log(summary);
        File.WriteAllText(ResultPath, s_Log.ToString());
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(ResultPath) ?? "", $"NPCTestResult_{s_Name}.txt"), s_Log.ToString());
        if (s_Fail > 0) Debug.LogError($"[NPCTest] {summary}");
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
    }

    // ---------- API cho test ----------

    public static void Log(string line)
    {
        s_Log.AppendLine(line);
        Debug.Log($"[NPCTest] {line}");
    }

    public static bool Check(bool condition, string what)
    {
        if (condition) s_Pass++; else s_Fail++;
        Log($"{(condition ? "PASS" : "FAIL")}  {what}");
        return condition;
    }

    public static void Fail(string what) => Check(false, what);

    public static NPC FirstSuspect() => NPC.Active.FirstOrDefault(n => n != null && n.Team == Team.Suspect && !n.IsDead && !n.IsSurrendered);

    public static Hostage FirstHostage() => Hostage.Active.FirstOrDefault(h => h != null && !h.IsDead);

    public static bool GraphOn(Component c) => c.TryGetComponent(out Unity.Behavior.BehaviorGraphAgent a) && a.enabled;

    private static readonly List<GameObject> s_Hidden = new();

    /// <summary>
    /// Tạm tắt mọi đồng đội (test Enemy / Hostage không bị đồng đội nhìn thấy / hô / bắn làm nhiễu, bất kể bố cục scene).
    /// Bật lại bằng ShowTeammates (Play Mode thoát cũng tự trả lại).
    /// </summary>
    public static void HideTeammates()
    {
        foreach (Teammate t in Teammate.Active.ToList())
        {
            s_Hidden.Add(t.gameObject);
            t.gameObject.SetActive(false);
        }
        if (s_Hidden.Count > 0) Log($"   (tạm tắt {s_Hidden.Count} đồng đội để cô lập test)");
    }

    /// <summary>
    /// Dời rig người chơi (gốc của camera chính) để camera cách from ít nhất distance m (chỉ trong Play Mode, thoát là
    /// trở lại): test không bị NPC / con tin nhìn thấy người chơi, bất kể camera đặt ở đâu trong scene.
    /// </summary>
    public static void MovePlayerAway(Vector3 from, float distance = 40f)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 d = cam.transform.position - from;
        d.y = 0f;
        if (d.magnitude >= distance) return;
        Vector3 dir = d.sqrMagnitude > 0.01f ? d.normalized : Vector3.back;
        Vector3 wanted = from + dir * distance;
        Vector3 delta = wanted - cam.transform.position;
        delta.y = 0f;
        cam.transform.root.position += delta;
        Log($"   (dời người chơi ra xa {distance:0} m để cô lập test)");
    }

    /// <summary>Bật lại các đồng đội đã tắt bằng HideTeammates.</summary>
    public static List<Teammate> ShowTeammates()
    {
        var shown = new List<Teammate>();
        foreach (GameObject go in s_Hidden)
        {
            if (go == null) continue;
            go.SetActive(true);
            if (go.TryGetComponent(out Teammate t)) shown.Add(t);
        }
        s_Hidden.Clear();
        return shown;
    }

    /// <summary>Đặt một quả FlashGrenade (prefab của người chơi) treo tại pos và cho nổ ngay qua VRGrenade.Explode (như hết ngòi).</summary>
    public static void DetonateFlashbang(Vector3 pos)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/FlashGrenade.prefab");
        if (prefab == null) throw new Exception("Không tìm thấy Assets/Prefabs/FlashGrenade.prefab");
        GameObject g = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
        g.name = "NPCTest_Flashbang";
        if (g.TryGetComponent(out Rigidbody rb)) rb.isKinematic = true;
        MethodInfo explode = typeof(VRGrenade).GetMethod("Explode", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (explode == null) throw new Exception("Không tìm thấy VRGrenade.Explode");
        explode.Invoke(g.GetComponent<VRGrenade>(), null);
    }

    // ---------- Test Giai đoạn 0: flashbang / nghe / hô hàng ----------

    private static IEnumerator<float> FlashbangTest()
    {
        NPC enemy = FirstSuspect();
        Hostage hostage = FirstHostage();
        if (!Check(enemy != null, "scene có nghi phạm (Enemy)")) yield break;
        HideTeammates();
        bool hasHostage = hostage != null;
        Log(hasHostage ? $"Enemy {enemy.name}, Hostage {hostage.name}" : $"Enemy {enemy.name}, không có con tin");

        // 1. Nổ thường (không có Flashbang): chỉ nghe tiếng, không choáng.
        Vector3 fragPos = NPCConditionUtils.EyeOf(enemy.gameObject) + enemy.transform.forward * 2f;
        NPCMorale.ReportExplosion(fragPos, 8f);
        Check(!NPCStun.IsStunnedObject(enemy.gameObject), "nổ thường: Enemy KHÔNG bị choáng");
        Check(NPCHearing.TryGetNoise(enemy.gameObject, 1f, out HeardNoise noise) && noise.Type == NoiseType.Explosion,
            "nổ thường: Enemy nghe thấy tiếng nổ");
        yield return 0.5f;

        // 2. Flashbang thật (prefab FlashGrenade, VRGrenade.Explode) giữa Enemy và con tin.
        Vector3 flashPos = hasHostage
            ? (enemy.transform.position + hostage.transform.position) * 0.5f + Vector3.up * 1.2f
            : fragPos;
        float moraleBefore = enemy.TryGetComponent(out NPCMorale morale) ? morale.Morale : -1f;
        Vector3 enemyPos = enemy.transform.position;
        DetonateFlashbang(flashPos);

        NPCStun enemyStun = enemy.GetComponent<NPCStun>();
        Check(enemyStun != null && enemyStun.IsStunned, $"flash: Enemy bị choáng (cường độ {(enemyStun != null ? enemyStun.Intensity : 0f):0.00}, {(enemyStun != null ? enemyStun.StunRemaining : 0f):0.0} s)");
        Check(!GraphOn(enemy), "flash: graph của Enemy dừng trong lúc choáng");
        if (morale != null)
            Check(morale.Morale < moraleBefore || moraleBefore <= 0.01f,
                $"flash: morale Enemy tụt ({moraleBefore:0.00} -> {morale.Morale:0.00}{(moraleBefore <= 0.01f ? ", đã ở mức sàn" : "")})");
        if (hasHostage) Check(NPCStun.IsStunnedObject(hostage.gameObject), "flash: con tin bị choáng");
        foreach (Teammate t in Teammate.Active)
            Log($"   (thông tin) {t.CallSign} cách {Vector3.Distance(t.transform.position, flashPos):0.0} m, choáng = {NPCStun.IsStunnedObject(t.gameObject)}");

        yield return 1.5f;
        Check(Vector3.Distance(enemy.transform.position, enemyPos) < 0.3f, "choáng: Enemy đứng yên");
        // Choáng + morale tụt + thấy người chơi gần: NPCMorale có thể cho tự đầu hàng (NPCStun thôi choáng): cũng đúng.
        Check(enemyStun.IsStunned || enemy.IsSurrendered, $"choáng: sau 1.5 s vẫn còn choáng (hoặc đã đầu hàng: {enemy.IsSurrendered})");

        // 3. Hô hàng lúc đang choáng: con tin giơ tay ngay; nghi phạm rất dễ đầu hàng.
        Vector3 issuer = NPCConditionUtils.EyeOf(enemy.gameObject) - enemy.transform.forward * 3f;
        Compliance.Order(issuer, 15f, 15f);
        if (hasHostage) Check(hostage.IsSurrendered, "hô lúc choáng: con tin giơ tay ngay");
        Log($"   (thông tin) hô lúc choáng: Enemy đầu hàng = {enemy.IsSurrendered} (morale {(morale != null ? morale.Morale : -1f):0.00}, cộng {enemyStun.ComplyBonus:0.00})");

        // 4. Hết choáng: ai chưa đầu hàng thì graph chạy lại.
        float wait = Mathf.Max(enemyStun.StunRemaining, hasHostage ? NPCStun.For(hostage.gameObject).StunRemaining : 0f) + 0.5f;
        yield return wait;
        Check(!enemyStun.IsStunned, "hết choáng: Enemy không còn choáng");
        if (enemy.IsSurrendered) Check(!GraphOn(enemy), "Enemy đã đầu hàng: graph vẫn tắt");
        else Check(GraphOn(enemy), "Enemy chưa đầu hàng: graph chạy lại");
        if (hasHostage) Check(GraphOn(hostage), "con tin: graph chạy lại (nhánh chờ còng)");
    }
}
