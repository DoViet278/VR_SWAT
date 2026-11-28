using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Behavior;
using Unity.Behavior.GraphFramework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Nâng cấp nghi phạm theo SWAT 4 (menu Tools/VR SWAT/Enemy):
///  1. Add SWAT 4 Branches : CHÈN thêm nhánh vào Behaviour Graphs/NPC/Enemy.asset (giữ nguyên các nhánh làm tay):
///       sau Dead            : Flee (ShouldFlee), Threaten Hostage (CanThreatenHostage)
///       trước Patrol        : Barricade (IsBarricaded), Investigate (HeardNoise)
///     Selector chạy con theo vị trí X, nên tool xếp lại X của mọi nhánh theo thứ tự ưu tiên.
///     Chạy lại không tạo trùng (nhánh đã có thì bỏ qua).
///  2. Setup Prefab        : gắn EnemyTactics (+ NPCStun, NPCHearing nếu thiếu) vào Prefabs/NPC/Enemy.prefab.
///  3. Setup Animator      : thêm bool + state "Stunned" vào Enemy.controller (khi đã có clip choáng).
///  Check Setup            : in thứ tự nhánh của graph.
/// Choáng (NPCStun) không cần nhánh: NPCStun tự dừng / chạy lại graph.
/// </summary>
public static class EnemySetupTool
{
    private const string GraphPath = "Assets/Behaviour Graphs/NPC/Enemy.asset";
    private const string PrefabPath = "Assets/Prefabs/NPC/Enemy.prefab";
    private const float Spacing = 380f;
    private const float BulletDamage = 25f;

    [MenuItem("Tools/VR SWAT/Enemy/Build All")]
    public static void BuildAll()
    {
        AddSwat4Branches();
        SetupPrefab();
        SetupAnimator();
    }

    // ---------------- 1. Graph ----------------

    [MenuItem("Tools/VR SWAT/Enemy/1. Add SWAT 4 Branches")]
    public static void AddSwat4Branches()
    {
        var graph = AssetDatabase.LoadAssetAtPath<BehaviorAuthoringGraph>(GraphPath);
        if (graph == null) { Debug.LogError($"[Enemy] Không tìm thấy {GraphPath}"); return; }
        NodeModel selector = graph.Nodes.FirstOrDefault(n => n is BehaviorGraphNodeModel b && TypeOf(b) == typeof(SelectorComposite));
        if (selector == null) { Debug.LogError("[Enemy] Graph không có Selector."); return; }

        List<NodeModel> order = Children(selector).OrderBy(n => n.Position.x).ToList();
        int deadIndex = order.FindIndex(n => HasCondition(n, typeof(IsDeadCondition)));
        int patrolIndex = order.FindIndex(n => HasCondition(n, typeof(GetNextWayPointCondition)));
        float y = order.Count > 0 ? order[0].Position.y : 300f;

        var afterDead = new List<NodeModel>();
        var beforePatrol = new List<NodeModel>();
        AddBranch(graph, selector, order, typeof(ShouldFleeCondition), typeof(FleeAction), y, afterDead);
        AddBranch(graph, selector, order, typeof(CanThreatenHostageCondition), typeof(ThreatenHostageAction), y, afterDead);
        AddBranch(graph, selector, order, typeof(IsBarricadedCondition), typeof(BarricadeAction), y, beforePatrol);
        AddBranch(graph, selector, order, typeof(HeardNoiseCondition), typeof(InvestigateAction), y, beforePatrol);

        // Chèn vào đúng chỗ (tính lại chỉ số sau khi chèn nhóm đầu).
        order.InsertRange(deadIndex >= 0 ? deadIndex + 1 : 0, afterDead);
        patrolIndex = order.FindIndex(n => HasCondition(n, typeof(GetNextWayPointCondition)));
        if (patrolIndex < 0) patrolIndex = Mathf.Max(0, order.Count - 1); // không có Patrol: trước nhánh cuối (Idle)
        order.InsertRange(patrolIndex, beforePatrol);

        // Xếp lại X (Selector thử từ trái sang phải), dời cả cây con theo.
        float x0 = -Spacing * (order.Count - 1) * 0.5f;
        for (int i = 0; i < order.Count; i++) MoveSubtree(order[i], new Vector2(x0 + i * Spacing - order[i].Position.x, 0f));

        EditorUtility.SetDirty(graph.Blackboard);
        EditorUtility.SetDirty(graph);
        graph.BuildRuntimeGraph(true);
        graph.SaveAsset();
        AssetDatabase.SaveAssets();
        Debug.Log($"[Enemy] Thêm {afterDead.Count + beforePatrol.Count} nhánh SWAT 4.\n{Describe(graph)}");
    }

    private static void AddBranch(BehaviorAuthoringGraph graph, NodeModel selector, List<NodeModel> existing, Type condition,
        Type action, float y, List<NodeModel> added)
    {
        if (existing.Any(n => HasCondition(n, condition))) return;
        var guard = (ConditionalGuardNodeModel)CreateNode(graph, typeof(ConditionalGuardModifier), new Vector2(0f, y));
        AddCondition(graph, guard, condition);
        Connect(graph, selector, guard);
        NodeModel act = CreateNode(graph, action, new Vector2(0f, y + 160f));
        LinkFields(graph, (BehaviorGraphNodeModel)act);
        Connect(graph, guard, act);
        added.Add(guard);
    }

    // ---------------- 2. Prefab ----------------

    [MenuItem("Tools/VR SWAT/Enemy/2. Setup Prefab")]
    public static void SetupPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            GetOrAdd<EnemyTactics>(root);
            GetOrAdd<NPCStun>(root);
            GetOrAdd<NPCHearing>(root);

            // Sát thương mỗi viên (hệ thống mới: đầu x3, tay chân x0.6, giáp bên nhận chặn bớt). Blackboard là nguồn chính.
            var npc = new SerializedObject(root.GetComponent<NPC>());
            npc.FindProperty("_damage").floatValue = BulletDamage;
            npc.ApplyModifiedPropertiesWithoutUndo();
            if (root.TryGetComponent(out BehaviorGraphAgent agent)) agent.SetVariableValue(NPC.DamageVar, BulletDamage);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[Enemy] Đã setup {PrefabPath} (EnemyTactics, NPCStun, NPCHearing, sát thương {BulletDamage}/viên).");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ---------------- 3. Animator ----------------

    private const string ControllerPath = "Assets/Animators/NPC/Enemy.controller";
    private const string StunClipFolder = "Assets/Animation Clips/NPC/Enemy";

    /// <summary>
    /// Thêm bool "Stunned" + state "Stunned" (Base Layer): Any State -> Stunned khi Stunned = true, Stunned -> Locomotion
    /// khi false. Clip: file .anim / .fbx có tên chứa "stun" trong Animation Clips/NPC/Enemy (vd. Mixamo "Stunned",
    /// retarget Humanoid). Chưa có clip thì bỏ qua: NPCStun ngồi thụp tại chỗ thay thế (không tạo state rỗng).
    /// </summary>
    [MenuItem("Tools/VR SWAT/Enemy/3. Setup Animator (Stunned)")]
    public static void SetupAnimator()
    {
        var ac = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
        if (ac == null) { Debug.LogError($"[Enemy] Không tìm thấy {ControllerPath}"); return; }
        AnimationClip clip = FindStunClip();
        if (clip == null)
        {
            Debug.LogWarning($"[Enemy] Chưa có clip choáng (tên chứa \"stun\") trong {StunClipFolder}: bỏ qua, NPCStun dùng ngồi thụp tại chỗ.");
            return;
        }

        if (!ac.parameters.Any(p => p.name == "Stunned"))
            ac.AddParameter("Stunned", AnimatorControllerParameterType.Bool);

        var sm = ac.layers[0].stateMachine;
        var state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Stunned");
        if (state == null) state = sm.AddState("Stunned", new Vector3(500f, 400f, 0f));
        state.motion = clip;

        foreach (var t in sm.anyStateTransitions.Where(t => t.destinationState == state).ToArray()) sm.RemoveAnyStateTransition(t);
        var enter = sm.AddAnyStateTransition(state);
        enter.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0f, "Stunned");
        enter.hasExitTime = false;
        enter.duration = 0.15f;
        enter.canTransitionToSelf = false;

        var locomotion = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Locomotion");
        foreach (var t in state.transitions.ToArray()) state.RemoveTransition(t);
        var exit = locomotion != null ? state.AddTransition(locomotion) : state.AddExitTransition();
        exit.AddCondition(UnityEditor.Animations.AnimatorConditionMode.IfNot, 0f, "Stunned");
        exit.hasExitTime = false;
        exit.duration = 0.25f;

        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Enemy] Đã thêm state Stunned ({clip.name}) vào {ControllerPath}.");
    }

    private static AnimationClip FindStunClip()
    {
        if (!AssetDatabase.IsValidFolder(StunClipFolder)) return null;
        foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { StunClipFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (AnimationClip c in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                if (!c.name.StartsWith("__preview") && c.name.IndexOf("stun", StringComparison.OrdinalIgnoreCase) >= 0) return c;
        }
        return null;
    }

    [MenuItem("Tools/VR SWAT/Enemy/Check Setup")]
    public static string CheckSetup()
    {
        var graph = AssetDatabase.LoadAssetAtPath<BehaviorAuthoringGraph>(GraphPath);
        string s = graph != null ? Describe(graph) : "Graph: THIẾU";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab != null) s += $"\nPrefab: {string.Join(", ", prefab.GetComponents<Component>().Select(c => c.GetType().Name))}";
        Debug.Log("[Enemy] Check Setup\n" + s);
        return s;
    }

    /// <summary>Thứ tự nhánh của Selector (trái -> phải): điều kiện -> action.</summary>
    private static string Describe(BehaviorAuthoringGraph graph)
    {
        var sb = new StringBuilder();
        NodeModel selector = graph.Nodes.FirstOrDefault(n => n is BehaviorGraphNodeModel b && TypeOf(b) == typeof(SelectorComposite));
        if (selector == null) return "Graph không có Selector";
        int i = 1;
        foreach (NodeModel child in Children(selector).OrderBy(n => n.Position.x))
        {
            var condTypes = Conditions(child).Select(t => t.Name).ToList();
            string conds = condTypes.Count > 0 ? string.Join(" & ", condTypes) : "(không điều kiện)";
            string acts = string.Join(", ", Descendants(child).OfType<BehaviorGraphNodeModel>().Select(TypeOf)
                .Where(t => t != null && typeof(Unity.Behavior.Action).IsAssignableFrom(t)).Select(t => t.Name));
            if (child is BehaviorGraphNodeModel cb && TypeOf(cb) is Type ct && typeof(Unity.Behavior.Action).IsAssignableFrom(ct)) acts = ct.Name;
            sb.AppendLine($"{i++,2}. {conds} -> {acts}");
        }
        return sb.ToString();
    }

    // ---------------- Tiện ích graph (giống TeammateSetupTool) ----------------

    private static Type TypeOf(BehaviorGraphNodeModel node) => Unity.Behavior.NodeRegistry.GetInfoFromTypeID(node.NodeTypeID)?.Type;

    private static IEnumerable<NodeModel> Children(NodeModel node) =>
        node.OutputPortModels.SelectMany(p => p.Connections).Select(c => c.NodeModel).Distinct();

    private static IEnumerable<NodeModel> Descendants(NodeModel node)
    {
        foreach (NodeModel c in Children(node))
        {
            yield return c;
            foreach (NodeModel d in Descendants(c)) yield return d;
        }
    }

    private static void MoveSubtree(NodeModel node, Vector2 delta)
    {
        node.Position += delta;
        foreach (NodeModel d in Descendants(node).Distinct()) d.Position += delta;
    }

    /// <summary>Nhánh (node hoặc cây con của nó) có ConditionalGuard chứa điều kiện conditionType.</summary>
    private static bool HasCondition(NodeModel node, Type conditionType) =>
        Conditions(node).Contains(conditionType);

    private static IEnumerable<Type> Conditions(NodeModel node) =>
        new[] { node }.Concat(Descendants(node)).OfType<ConditionalGuardNodeModel>()
            .SelectMany(g => g.ConditionModels).Select(c => c.ConditionType?.Type).Where(t => t != null);

    private static NodeModel CreateNode(BehaviorAuthoringGraph graph, Type runtimeType, Vector2 position)
    {
        NodeInfo info = Unity.Behavior.NodeRegistry.GetInfo(runtimeType);
        if (info == null) throw new Exception($"[Enemy] NodeRegistry không có {runtimeType.Name}");
        return graph.CreateNode(info.ModelType, position, null, new object[] { info });
    }

    private static void Connect(BehaviorAuthoringGraph graph, NodeModel parent, NodeModel child)
    {
        // Node nạp từ asset có thể chưa dựng lại cache port mặc định: lấy port đầu tiên.
        if (!parent.TryDefaultOutputPortModel(out PortModel output)) output = parent.OutputPortModels.FirstOrDefault();
        if (!child.TryDefaultInputPortModel(out PortModel input)) input = child.InputPortModels.FirstOrDefault();
        if (output == null || input == null)
            throw new Exception($"[Enemy] Không nối được {parent} (out: {string.Join(",", parent.OutputPortModels.Select(p => p.Name))}) -> " +
                                $"{child} (in: {string.Join(",", child.InputPortModels.Select(p => p.Name))})");
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

    private static T GetOrAdd<T>(GameObject go) where T : Component =>
        go.TryGetComponent(out T c) ? c : go.AddComponent<T>();
}
