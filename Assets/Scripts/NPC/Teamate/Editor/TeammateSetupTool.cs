using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Behavior;
using Unity.Behavior.GraphFramework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Dựng đồng đội AI (menu Tools/VR SWAT/Teammate), mọi thứ nằm trong các folder Teamate:
///  1. Copy Animation Clips   : clip của Animators/NPC/Enemy.controller -> Animation Clips/NPC/Teamate (giữ thư mục con).
///  2. Build Animator         : Animators/NPC/Teamate.controller = bản sao Enemy.controller dùng clip trong folder Teamate.
///  3. Build Behavior Graph   : Behaviour Graphs/NPC/Teamate.asset (TeammateGraph: Selector ưu tiên).
///  4. Setup Prefab           : Prefabs/NPC/Teamate.prefab (phe Police, 300 máu, Teammate, vô hạn băng, flashbang, graph).
/// "Build All" chạy cả 4 bước. Chạy lại được nhiều lần (ghi đè kết quả cũ, giữ GUID của asset).
/// "Add SquadController To Scene": thêm object Squad vào scene đang mở (chạy tay khi cần).
/// </summary>
public static class TeammateSetupTool
{
    private const string EnemyController = "Assets/Animators/NPC/Enemy.controller";
    private const string EnemyClipRoot = "Assets/Animation Clips/NPC/Enemy";
    private const string ClipRoot = "Assets/Animation Clips/NPC/Teamate";
    private const string ControllerPath = "Assets/Animators/NPC/Teamate.controller";
    private const string GraphPath = "Assets/Behaviour Graphs/NPC/Teamate.asset";
    private const string PrefabPath = "Assets/Prefabs/NPC/Teamate.prefab";
    private const string FlashbangPrefabPath = "Assets/Prefabs/FlashGrenade.prefab";

    [MenuItem("Tools/VR SWAT/Teammate/Build All")]
    public static void BuildAll()
    {
        BuildAnimator();
        BuildGraph();
        SetupPrefab();
        Debug.Log("[Teammate] Build All xong.");
    }

    // ---------------- 1 + 2. Clips + Animator ----------------

    /// <summary>Copy mọi clip Enemy.controller dùng sang folder Teamate. Trả map GUID cũ -> GUID mới.</summary>
    [MenuItem("Tools/VR SWAT/Teammate/1. Copy Animation Clips")]
    public static Dictionary<string, string> CopyClips()
    {
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(EnemyController);
        if (ctrl == null) throw new Exception($"[Teammate] Không tìm thấy {EnemyController}");

        var map = new Dictionary<string, string>();
        foreach (AnimationClip clip in ctrl.animationClips.Distinct())
        {
            string src = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(src) || !src.EndsWith(".anim")) continue; // clip nằm trong FBX: giữ nguyên tham chiếu
            string rel = src.StartsWith(EnemyClipRoot + "/") ? src.Substring(EnemyClipRoot.Length + 1) : Path.GetFileName(src);
            string dst = ClipRoot + "/" + rel;
            EnsureFolder(Path.GetDirectoryName(dst).Replace('\\', '/'));
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(dst) == null && !AssetDatabase.CopyAsset(src, dst))
            {
                Debug.LogError($"[Teammate] Không copy được {src} -> {dst}");
                continue;
            }
            map[AssetDatabase.AssetPathToGUID(src)] = AssetDatabase.AssetPathToGUID(dst);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Teammate] {map.Count} clip trong {ClipRoot}");
        return map;
    }

    /// <summary>Teamate.controller = Enemy.controller (cùng state, layer, transition) nhưng trỏ tới clip trong folder Teamate.</summary>
    [MenuItem("Tools/VR SWAT/Teammate/2. Build Animator Controller")]
    public static void BuildAnimator()
    {
        Dictionary<string, string> map = CopyClips();
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) == null)
            AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        // Chép YAML của Enemy.controller (file .meta của Teamate.controller giữ nguyên nên prefab vẫn trỏ đúng).
        string yaml = File.ReadAllText(EnemyController);
        foreach (var kv in map) yaml = yaml.Replace("guid: " + kv.Key, "guid: " + kv.Value);
        yaml = yaml.Replace("m_Name: Enemy\n", "m_Name: Teamate\n");
        File.WriteAllText(ControllerPath, yaml);
        AssetDatabase.ImportAsset(ControllerPath, ImportAssetOptions.ForceUpdate);
        Debug.Log($"[Teammate] Đã dựng {ControllerPath} từ {EnemyController}");
    }

    // ---------------- 3. Behavior Graph ----------------

    [MenuItem("Tools/VR SWAT/Teammate/3. Build Behavior Graph")]
    public static void BuildGraph()
    {
        var graph = AssetDatabase.LoadAssetAtPath<BehaviorAuthoringGraph>(GraphPath);
        if (graph == null) { Debug.LogError($"[Teammate] Không tìm thấy {GraphPath}"); return; }

        foreach (NodeModel n in graph.Nodes.ToList())
            if (!(n is StartNodeModel) && graph.Nodes.Contains(n)) graph.DeleteNode(n);
        StartNodeModel start = graph.Nodes.OfType<StartNodeModel>().FirstOrDefault()
                               ?? (StartNodeModel)CreateNode(graph, typeof(Start), Vector2.zero);
        start.Position = Vector2.zero;
        start.Repeat = true;

        // Biến NPC đọc / ghi (NPC.SyncWithBlackboard) + biến lệnh của Teammate.
        Var<GameObject>(graph, "Self");
        Var<GameObject>(graph, NPC.TargetVar);
        // Health > 0 ngay từ đầu: graph có thể chạy trước NPC.Start (lúc NPC ghi máu thật), Health = 0 thì IsDead -> Die.
        ((TypedVariableModel<float>)Var<float>(graph, NPC.HealthVar)).m_Value = 300f;
        Var<List<GameObject>>(graph, NPC.WayPointsVar);
        foreach (string f in new[] { NPC.AttackRangeVar, NPC.DamageVar, NPC.ViewDistanceVar, NPC.ViewAngleVar, NPC.HideThresholdVar, NPC.SearchRadiusVar })
            Var<float>(graph, f);
        Var<string>(graph, NPC.CoverTagVar);
        Var<TeammateOrder>(graph, Teammate.OrderTypeVar);
        Var<Vector3>(graph, Teammate.OrderPosVar);
        Var<Vector3>(graph, Teammate.OrderLookVar);
        Var<GameObject>(graph, Teammate.OrderDoorVar);
        Var<int>(graph, Teammate.OrderIdVar);
        Var<GameObject>(graph, Teammate.GuardTargetVar);

        NodeModel selector = CreateNode(graph, typeof(SelectorComposite), new Vector2(0, 140));
        Connect(graph, start, selector);

        // Thứ tự ưu tiên trái -> phải (Selector thử theo vị trí X).
        var branches = new (Type condition, Type[] actions)[]
        {
            (typeof(IsDeadCondition), new[] { typeof(DieAction) }),
            (typeof(TeammateHasEnemyTargetCondition), new[] { typeof(TeammateEngageAction) }),
            (typeof(TeammateHasGuardTargetCondition), new[] { typeof(TeammateGuardSuspectAction) }),
            (typeof(OrderIsFollowCondition), new[] { typeof(TeammateFollowPlayerAction) }),
            (typeof(OrderIsMoveToCondition), new[] { typeof(TeammateMoveToOrderAction), typeof(TeammateReportDoneAction) }),
            (typeof(OrderIsHoldAimCondition), new[] { typeof(TeammateHoldAimAction) }),
            (typeof(OrderIsOpenDoorCondition), new[] { typeof(TeammateOpenDoorAction), typeof(TeammateReportDoneAction) }),
            (typeof(OrderIsBreachDoorCondition), new[] { typeof(TeammateBreachDoorAction), typeof(TeammateReportDoneAction) }),
            (typeof(OrderIsThrowFlashCondition), new[] { typeof(TeammateThrowFlashAction), typeof(TeammateReportDoneAction) }),
        };

        const float spacing = 420f;
        float x0 = -spacing * (branches.Length - 1) * 0.5f;
        for (int i = 0; i < branches.Length; i++)
        {
            float x = x0 + i * spacing;
            var guard = (ConditionalGuardNodeModel)CreateNode(graph, typeof(ConditionalGuardModifier), new Vector2(x, 300));
            AddCondition(graph, guard, branches[i].condition);
            Connect(graph, selector, guard);

            Type[] actions = branches[i].actions;
            NodeModel parent = guard;
            if (actions.Length > 1)
            {
                parent = CreateNode(graph, typeof(SequenceComposite), new Vector2(x, 460));
                Connect(graph, guard, parent);
            }
            float y = actions.Length > 1 ? 620 : 460;
            for (int k = 0; k < actions.Length; k++)
            {
                float ax = x + (k - (actions.Length - 1) * 0.5f) * 200f;
                NodeModel action = CreateNode(graph, actions[k], new Vector2(ax, y));
                LinkFields(graph, (BehaviorGraphNodeModel)action);
                Connect(graph, parent, action);
            }
        }

        graph.Blackboard.SetAssetDirty();
        EditorUtility.SetDirty(graph.Blackboard);
        EditorUtility.SetDirty(graph);
        graph.BuildRuntimeGraph(true);
        graph.SaveAsset();
        AssetDatabase.SaveAssets();
        Debug.Log($"[Teammate] Đã dựng Behavior Graph {GraphPath} ({graph.Nodes.Count} node)");
    }

    private static VariableModel Var<T>(BehaviorAuthoringGraph graph, string name)
    {
        VariableModel v = graph.Blackboard.Variables.FirstOrDefault(x => x.Name == name && x.Type == typeof(T));
        if (v != null) return v;
        v = new TypedVariableModel<T> { Name = name };
        graph.Blackboard.Variables.Add(v);
        return v;
    }

    private static NodeModel CreateNode(BehaviorAuthoringGraph graph, Type runtimeType, Vector2 position)
    {
        NodeInfo info = Unity.Behavior.NodeRegistry.GetInfo(runtimeType);
        if (info == null) throw new Exception($"[Teammate] NodeRegistry không có {runtimeType.Name}");
        return graph.CreateNode(info.ModelType, position, null, new object[] { info });
    }

    private static void Connect(BehaviorAuthoringGraph graph, NodeModel parent, NodeModel child)
    {
        if (!parent.TryDefaultOutputPortModel(out PortModel output) || !child.TryDefaultInputPortModel(out PortModel input))
            throw new Exception($"[Teammate] Không nối được {parent} -> {child}");
        graph.ConnectEdge(output, input);
    }

    private static void LinkFields(BehaviorAuthoringGraph graph, BehaviorGraphNodeModel node)
    {
        NodeInfo info = Unity.Behavior.NodeRegistry.GetInfoFromTypeID(node.NodeTypeID);
        foreach (VariableInfo v in info.Variables)
        {
            VariableModel bb = FindVariable(graph, v);
            if (bb != null) node.SetField(v.Name, bb, bb.Type);
        }
    }

    /// <summary>Biến Blackboard cùng tên và cùng kiểu giá trị với ô của node.</summary>
    private static VariableModel FindVariable(BehaviorAuthoringGraph graph, VariableInfo v)
    {
        Type t = v.Type;
        while (t != null && typeof(BlackboardVariable).IsAssignableFrom(t) && !(t.IsGenericType && t.GetGenericTypeDefinition() == typeof(BlackboardVariable<>)))
            t = t.BaseType;
        if (t != null && t.IsGenericType && t.GetGenericTypeDefinition() == typeof(BlackboardVariable<>))
            t = t.GetGenericArguments()[0];
        return graph.Blackboard.Variables.FirstOrDefault(x => x.Name == v.Name && x.Type == t);
    }

    private static void AddCondition(BehaviorAuthoringGraph graph, ConditionalGuardNodeModel guard, Type conditionType)
    {
        ConditionInfo info = ConditionUtility.GetInfoForConditionType(conditionType);
        var model = new ConditionModel(guard, (Condition)Activator.CreateInstance(conditionType), info);
        guard.ConditionModels.Add(model);
        foreach (VariableInfo v in info.Variables)
        {
            VariableModel bb = FindVariable(graph, v);
            if (bb != null) model.SetField(v.Name, bb, bb.Type);
        }
    }

    // ---------------- 4. Prefab ----------------

    [MenuItem("Tools/VR SWAT/Teammate/4. Setup Prefab")]
    public static void SetupPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            // Đồng đội không đầu hàng, không bị còng.
            foreach (var c in root.GetComponents<NPCCuffable>()) UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in root.GetComponents<NPCMorale>()) UnityEngine.Object.DestroyImmediate(c);

            var animator = root.GetComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            animator.applyRootMotion = false;

            var npc = new SerializedObject(root.GetComponent<NPC>());
            npc.FindProperty("_team").enumValueIndex = (int)Team.Police;
            npc.FindProperty("_maxHealth").floatValue = 300f;
            npc.FindProperty("_regenPerSecond").floatValue = 0f;
            npc.FindProperty("_damageTakenScale").floatValue = 0.5f;
            npc.FindProperty("_alertMemory").floatValue = 2f; // mất dấu nghi phạm 2 s thì thôi giao chiến
            npc.FindProperty("_target").objectReferenceValue = null;
            npc.ApplyModifiedPropertiesWithoutUndo();

            var voice = GetOrAdd<AudioSource>(root);
            voice.playOnAwake = false;
            voice.spatialBlend = 1f;
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.minDistance = 1f;
            voice.maxDistance = 25f;

            GetOrAdd<Teammate>(root);

            // Vô hạn băng đạn.
            NPCWeapon weapon = root.GetComponentInChildren<NPCWeapon>(true);
            if (weapon != null)
            {
                var w = new SerializedObject(weapon);
                w.FindProperty("_spareMagazines").intValue = -1;
                w.ApplyModifiedPropertiesWithoutUndo();
            }

            // Không ném bom, chỉ ném flashbang của người chơi (VRGrenade + Flashbang).
            NPCGrenadeThrower thrower = root.GetComponentInChildren<NPCGrenadeThrower>(true);
            if (thrower != null)
            {
                var g = new SerializedObject(thrower);
                g.FindProperty("_grenades").intValue = 0;
                var flash = AssetDatabase.LoadAssetAtPath<GameObject>(FlashbangPrefabPath);
                g.FindProperty("_flashbangPrefab").objectReferenceValue = flash != null ? flash.GetComponent<VRGrenade>() : null;
                g.FindProperty("_flashbangs").intValue = -1;
                g.ApplyModifiedPropertiesWithoutUndo();
                if (flash == null) Debug.LogWarning($"[Teammate] Không tìm thấy {FlashbangPrefabPath}, hãy gán Flashbang Prefab tay.");
            }

            var behavior = GetOrAdd<BehaviorGraphAgent>(root);
            var graph = AssetDatabase.LoadAssetAtPath<BehaviorAuthoringGraph>(GraphPath);
            if (graph != null) behavior.Graph = graph.BuildRuntimeGraph(false);

            var agent = GetOrAdd<NavMeshAgent>(root);
            agent.avoidancePriority = 40; // nhường đường cho nhau ít hơn địch (50)

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[Teammate] Đã setup {PrefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ---------------- Kiểm tra ----------------

    /// <summary>In ra Console tình trạng controller / graph / prefab của đồng đội.</summary>
    [MenuItem("Tools/VR SWAT/Teammate/Check Setup")]
    public static string CheckSetup()
    {
        var sb = new System.Text.StringBuilder("[Teammate] Check Setup\n");
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        string[] clips = ctrl != null ? ctrl.animationClips.Select(AssetDatabase.GetAssetPath).Distinct().ToArray() : new string[0];
        sb.AppendLine($"Animator: {(ctrl != null ? $"{ctrl.layers.Length} layer, {ctrl.parameters.Length} tham số, {clips.Length} clip ({clips.Count(p => !p.StartsWith(ClipRoot))} clip ngoài folder Teamate)" : "THIẾU")}");

        var graph = AssetDatabase.LoadAssetAtPath<BehaviorAuthoringGraph>(GraphPath);
        if (graph != null)
        {
            var names = graph.Nodes.OfType<BehaviorGraphNodeModel>()
                .Select(n => Unity.Behavior.NodeRegistry.GetInfoFromTypeID(n.NodeTypeID)?.Name ?? n.GetType().Name);
            var conditions = graph.Nodes.OfType<ConditionalGuardNodeModel>().SelectMany(g => g.ConditionModels).Select(c => c.ConditionType?.Type?.Name);
            sb.AppendLine($"Graph: {graph.Nodes.Count} node: {string.Join(", ", names)}");
            sb.AppendLine($"Conditions: {string.Join(", ", conditions)}");
            sb.AppendLine($"Blackboard: {string.Join(", ", graph.Blackboard.Variables.Select(v => v.Name + ":" + v.Type.Name))}");
        }
        else sb.AppendLine("Graph: THIẾU");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab != null)
        {
            sb.AppendLine($"Prefab: {string.Join(", ", prefab.GetComponents<Component>().Select(c => c.GetType().Name))}");
            var agent = prefab.GetComponent<BehaviorGraphAgent>();
            sb.AppendLine($"Prefab graph: {(agent != null && agent.Graph != null ? AssetDatabase.GetAssetPath(agent.Graph) : "chưa gán")}");
            var npc = prefab.GetComponent<NPC>();
            sb.AppendLine($"NPC: phe {npc.Team}, {npc.MaxHealth} máu, nhận {npc.DamageTakenScale:P0} sát thương");
        }
        Debug.Log(sb.ToString());
        return sb.ToString();
    }

    // ---------------- Scene ----------------

    [MenuItem("Tools/VR SWAT/Teammate/Add SquadController To Scene")]
    public static void AddSquadToScene()
    {
        SquadController squad = UnityEngine.Object.FindAnyObjectByType<SquadController>();
        if (squad == null)
        {
            squad = new GameObject("Squad").AddComponent<SquadController>();
            Undo.RegisterCreatedObjectUndo(squad.gameObject, "Add SquadController");
        }
        Selection.activeObject = squad.gameObject;
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component =>
        go.TryGetComponent(out T c) ? c : go.AddComponent<T>();

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
