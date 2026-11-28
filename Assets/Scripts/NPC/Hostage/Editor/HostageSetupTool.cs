using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Behavior;
using Unity.Behavior.GraphFramework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Dựng Hostage từ đầu (menu Tools/VR SWAT/Hostage):
///  1. Extract Animation Clips : tách clip từ Animation Clips/Reference sang Animation Clips/Hostage (.anim, bật loop).
///  2. Build Animator Controller: Animators/Hostage/Hostage.controller.
///  3. Build Behavior Graph    : Behaviour Graphs/Hostage/Hostage.asset (tham khảo graph của NPC).
///  4. Setup Prefab            : thêm component cho Prefabs/Hostage/Hostage.prefab và gán controller + graph.
/// "Build All" chạy cả 4 bước. Chạy lại được nhiều lần (ghi đè kết quả cũ; clip chết gán tay trong controller được giữ).
/// Graph (SWAT 4): Restrained > Surrender > Held > Flee (tiếng súng) > Cower (súng sát bên) > Scared > CallForHelp >
/// WayPoint > Idle.
/// </summary>
public static class HostageSetupTool
{
    private const string RefRoot = "Assets/Animation Clips/Reference/";
    private const string Injuries = RefRoot + "InjuriesAnim/SwatInjuries.fbx";
    private const string Movement = RefRoot + "SWATMovement/SWATMovement.fbx";
    private const string Idles = RefRoot + "SwatWithGunAnim/SwatWithGunAnim.fbx";

    private const string ClipRoot = "Assets/Animation Clips/NPC/Hostage";
    private const string ControllerPath = "Assets/Animators/NPC/Hostage.controller";
    private const string GraphPath = "Assets/Behaviour Graphs/NPC/Hostage.asset";
    private const string PrefabPath = "Assets/Prefabs/NPC/Hostage.prefab";
    private const string VoiceSetPath = "Assets/Audios/NPC/Hostage/Hostage_Male_Voice.asset";

    // (thư mục con, FBX nguồn, tên clip, loop)
    private static readonly (string folder, string fbx, string clip, bool loop)[] Clips =
    {
        ("Movement", Idles, "sLookingIdle1", true),
        ("Movement", Movement, "sWalkFWD_normal1", true),
        ("Movement", Movement, "sRunFWD_normal1", true),
        ("Movement", Movement, "sTurnLeftHostage", false),
        ("Movement", Movement, "sTurnRightHostage", false),
        ("Movement", Movement, "sTurn180", false),
        ("Scared", Idles, "sHostageCowerFidget1", true),
        ("CallForHelp", Idles, "cHostageFidget2", false),
        ("Surrender", Injuries, "sComplyIncapacitated", false),
        ("Surrender", Injuries, "complyHitFront", false),
        ("Surrender", Injuries, "complyFlinchGrenade", false),
        ("Cuffed", Injuries, "cuffedHitFront", false),
        ("Cuffed", Injuries, "cuffedFlinchGrenade", false),
        ("Die", Injuries, "sDieFront", false),
        ("Die", Injuries, "sDieBack", false),
        ("Die", Injuries, "complyDieFront", false),
        ("Die", Injuries, "complyDieBack", false),
    };

    [MenuItem("Tools/VR SWAT/Hostage/Build All")]
    public static void BuildAll()
    {
        ExtractClips();
        BuildAnimator();
        BuildGraph();
        SetupPrefab();
        Debug.Log("[Hostage] Build All xong.");
    }

    // ---------------- 1. Clips ----------------

    [MenuItem("Tools/VR SWAT/Hostage/1. Extract Animation Clips")]
    public static void ExtractClips()
    {
        EnsureFolder(ClipRoot);
        foreach (var c in Clips)
        {
            AnimationClip src = AssetDatabase.LoadAllAssetsAtPath(c.fbx).OfType<AnimationClip>().FirstOrDefault(x => x.name == c.clip);
            if (src == null) { Debug.LogError($"[Hostage] Không tìm thấy clip {c.clip} trong {c.fbx}"); continue; }

            string folder = ClipRoot + "/" + c.folder;
            EnsureFolder(folder);
            string path = folder + "/" + c.clip + ".anim";
            AnimationClip dst = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            bool create = dst == null;
            if (create) dst = new AnimationClip();
            EditorUtility.CopySerialized(src, dst);
            dst.name = c.clip;

            AnimationClipSettings s = AnimationUtility.GetAnimationClipSettings(dst);
            s.loopTime = c.loop;
            s.loopBlend = c.loop;
            AnimationUtility.SetAnimationClipSettings(dst, s);

            if (create) AssetDatabase.CreateAsset(dst, path);
            else EditorUtility.SetDirty(dst);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Hostage] Đã tách {Clips.Length} clip vào {ClipRoot}");
    }

    private static AnimationClip Clip(string name) =>
        AssetDatabase.FindAssets(name + " t:AnimationClip", new[] { ClipRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<AnimationClip>)
            .FirstOrDefault(c => c != null && c.name == name)
        ?? throw new Exception($"[Hostage] Thiếu clip {name}, hãy chạy Extract Animation Clips trước.");

    // ---------------- 2. Animator ----------------

    [MenuItem("Tools/VR SWAT/Hostage/2. Build Animator Controller")]
    public static void BuildAnimator()
    {
        EnsureFolder("Assets/Animators/Hostage");
        AnimatorController ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                                  ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        // Clip chết đã gán tay trong controller (vd. HostageDeath) thì giữ lại khi dựng lại.
        var keepMotions = new Dictionary<string, Motion>();
        if (ctrl.layers.Length > 0)
            foreach (ChildAnimatorState cs in ctrl.layers[0].stateMachine.states)
                if (cs.state.name.Contains("Die") && cs.state.motion is AnimationClip kept && AssetDatabase.GetAssetPath(kept) != ControllerPath)
                    keepMotions[cs.state.name] = kept;

        // Xóa sạch nội dung cũ (giữ asset để không mất tham chiếu GUID).
        foreach (var p in ctrl.parameters.ToArray()) ctrl.RemoveParameter(p);
        while (ctrl.layers.Length > 0) ctrl.RemoveLayer(0);
        foreach (UnityEngine.Object sub in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
            if (sub != ctrl && sub != null) AssetDatabase.RemoveObjectFromAsset(sub);

        var floats = new[] { "MoveX", "MoveY" };
        var bools = new[] { "IsCrouching", "Scared", "Surrendered" };
        var triggers = new[] { "TurnLeft", "TurnRight", "Turn180", "CallHelp", "Surrender", "Restrain", "Flinch", "DieFront", "DieBack" };
        foreach (var p in floats) ctrl.AddParameter(p, AnimatorControllerParameterType.Float);
        foreach (var p in bools) ctrl.AddParameter(p, AnimatorControllerParameterType.Bool);
        foreach (var p in triggers) ctrl.AddParameter(p, AnimatorControllerParameterType.Trigger);

        var sm = new AnimatorStateMachine { name = "Base Layer", hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(sm, ctrl);
        ctrl.AddLayer(new AnimatorControllerLayer
        {
            name = "Base Layer",
            stateMachine = sm,
            defaultWeight = 1f,
            iKPass = true, // HostageCuffable dùng IK đưa tay ra khi còng
        });

        // Locomotion: giống NPC_M4A1 (MoveX/MoveY theo world, clip đi tới ở mọi hướng; 1 = đi bộ, 2 = chạy).
        var tree = new BlendTree
        {
            name = "Locomotion",
            blendType = BlendTreeType.FreeformDirectional2D,
            blendParameter = "MoveX",
            blendParameterY = "MoveY",
            hideFlags = HideFlags.HideInHierarchy,
        };
        AssetDatabase.AddObjectToAsset(tree, ctrl);
        AnimationClip idle = Clip("sLookingIdle1"), walk = Clip("sWalkFWD_normal1"), run = Clip("sRunFWD_normal1");
        tree.AddChild(idle, Vector2.zero);
        foreach (Vector2 d in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right })
        {
            tree.AddChild(walk, d);
            tree.AddChild(run, d * 2f);
        }

        AnimatorState State(string name, Motion motion, Vector2 pos, float speed = 1f)
        {
            AnimatorState s = sm.AddState(name, pos);
            s.motion = keepMotions.TryGetValue(name, out Motion kept) ? kept : motion;
            s.speed = speed;
            s.writeDefaultValues = true;
            return s;
        }

        AnimatorState loco = State("Locomotion", tree, new Vector2(300, 0));
        sm.defaultState = loco;
        AnimatorState turnL = State("TurnLeft", Clip("sTurnLeftHostage"), new Vector2(50, -120));
        AnimatorState turnR = State("TurnRight", Clip("sTurnRightHostage"), new Vector2(300, -160));
        AnimatorState turn180 = State("Turn180", Clip("sTurn180"), new Vector2(550, -120));
        AnimatorState scared = State("Scared", Clip("sHostageCowerFidget1"), new Vector2(600, 80));
        AnimatorState help = State("CallHelp", Clip("cHostageFidget2"), new Vector2(0, 80));
        AnimatorState surrender = State("Surrender", Clip("sComplyIncapacitated"), new Vector2(300, 220));
        // Comply / Cuffed giữ nguyên tư thế frame đầu (quỳ giơ tay / quỳ tay sau lưng).
        AnimatorState comply = State("Comply", Clip("complyHitFront"), new Vector2(300, 320), 0f);
        AnimatorState complyFlinch = State("ComplyFlinch", Clip("complyFlinchGrenade"), new Vector2(50, 320));
        AnimatorState cuffed = State("Cuffed", Clip("cuffedHitFront"), new Vector2(300, 420), 0f);
        AnimatorState cuffedFlinch = State("CuffedFlinch", Clip("cuffedFlinchGrenade"), new Vector2(50, 420));
        AnimatorState dieF = State("DieFront", Clip("sDieFront"), new Vector2(650, 260));
        AnimatorState dieB = State("DieBack", Clip("sDieBack"), new Vector2(650, 320));
        AnimatorState cDieF = State("ComplyDieFront", Clip("complyDieFront"), new Vector2(650, 380));
        AnimatorState cDieB = State("ComplyDieBack", Clip("complyDieBack"), new Vector2(650, 440));

        AnimatorStateTransition T(AnimatorState from, AnimatorState to, float duration, float? exitTime = null)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.duration = duration;
            t.hasExitTime = exitTime.HasValue;
            if (exitTime.HasValue) t.exitTime = exitTime.Value;
            return t;
        }

        AnimatorStateTransition Any(AnimatorState to, float duration)
        {
            AnimatorStateTransition t = sm.AddAnyStateTransition(to);
            t.duration = duration;
            t.canTransitionToSelf = false;
            return t;
        }

        // Quay (NPCLocomotion điều khiển giống NPC).
        T(loco, turnL, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "TurnLeft");
        T(loco, turnR, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "TurnRight");
        T(loco, turn180, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "Turn180");
        foreach (AnimatorState s in new[] { turnL, turnR, turn180 }) T(s, loco, 0.15f, 0.9f);

        // Sợ hãi.
        T(loco, scared, 0.3f).AddCondition(AnimatorConditionMode.If, 0, "Scared");
        T(help, scared, 0.25f).AddCondition(AnimatorConditionMode.If, 0, "Scared");
        T(scared, loco, 0.4f).AddCondition(AnimatorConditionMode.IfNot, 0, "Scared");

        // Kêu cứu.
        T(loco, help, 0.3f).AddCondition(AnimatorConditionMode.If, 0, "CallHelp");
        T(help, loco, 0.4f, 0.95f);

        // Đầu hàng -> quỳ giơ tay -> còng.
        Any(surrender, 0.25f).AddCondition(AnimatorConditionMode.If, 0, "Surrender");
        T(surrender, comply, 0.3f, 0.95f);
        T(comply, complyFlinch, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "Flinch");
        T(complyFlinch, comply, 0.2f, 0.95f);
        AnimatorStateTransition toCuffed = Any(cuffed, 0.6f);
        toCuffed.AddCondition(AnimatorConditionMode.If, 0, "Restrain");
        toCuffed.AddCondition(AnimatorConditionMode.If, 0, "Surrendered");
        T(cuffed, cuffedFlinch, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "Flinch");
        T(cuffedFlinch, cuffed, 0.2f, 0.95f);

        // Chết: đứng hoặc đang quỳ.
        void Die(AnimatorState to, string trigger, bool surrendered)
        {
            AnimatorStateTransition t = Any(to, 0.1f);
            t.AddCondition(AnimatorConditionMode.If, 0, trigger);
            t.AddCondition(surrendered ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "Surrendered");
        }
        Die(dieF, "DieFront", false);
        Die(dieB, "DieBack", false);
        Die(cDieF, "DieFront", true);
        Die(cDieB, "DieBack", true);

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Hostage] Đã dựng {ControllerPath}");
    }

    // ---------------- 3. Behavior Graph ----------------

    [MenuItem("Tools/VR SWAT/Hostage/3. Build Behavior Graph")]
    public static void BuildGraph()
    {
        var graph = AssetDatabase.LoadAssetAtPath<BehaviorAuthoringGraph>(GraphPath);
        if (graph == null) { Debug.LogError($"[Hostage] Không tìm thấy {GraphPath}"); return; }

        // Xóa mọi node cũ trừ On Start.
        foreach (NodeModel n in graph.Nodes.ToList())
            if (!(n is StartNodeModel) && graph.Nodes.Contains(n)) graph.DeleteNode(n);
        StartNodeModel start = graph.Nodes.OfType<StartNodeModel>().FirstOrDefault()
                               ?? (StartNodeModel)CreateNode(graph, typeof(Start), Vector2.zero);
        start.Position = Vector2.zero;
        start.Repeat = true;

        // Blackboard: Self có sẵn. Thêm biến dùng chung với Hostage.cs.
        Var<GameObject>(graph, "Self");
        Var<GameObject>(graph, Hostage.TargetVar);
        Var<List<GameObject>>(graph, Hostage.WayPointsVar);
        Var<GameObject>(graph, "CurrentWayPoint");
        Var<int>(graph, "Index");

        NodeModel selector = CreateNode(graph, typeof(SelectorComposite), new Vector2(0, 140));
        Connect(graph, start, selector);

        // Thứ tự ưu tiên (trái -> phải): Try In Order sắp nhánh con theo vị trí X.
        var branches = new (Type condition, Type action)[]
        {
            (typeof(HostageIsRestrainedCondition), typeof(HostageRestrainedAction)),
            (typeof(HostageShouldSurrenderCondition), typeof(HostageSurrenderAction)),
            (typeof(HostageIsHeldCondition), typeof(HostageHeldAction)),
            (typeof(HostageShouldFleeCondition), typeof(HostageFleeAction)),
            (typeof(HostageShouldCowerCondition), typeof(HostageCowerGunfireAction)),
            (typeof(HostageIsScaredCondition), typeof(HostageCowerAction)),
            (typeof(HostageShouldCallForHelpCondition), typeof(HostageCallForHelpAction)),
            (typeof(HostageGetNextWayPointCondition), typeof(HostageMoveToWayPointAction)),
            (null, typeof(HostageIdleAction)),
        };

        const float spacing = 380f;
        float x0 = -spacing * (branches.Length - 1) * 0.5f;
        for (int i = 0; i < branches.Length; i++)
        {
            float x = x0 + i * spacing;
            NodeModel parent = selector;
            if (branches[i].condition != null)
            {
                NodeModel guard = CreateNode(graph, typeof(ConditionalGuardModifier), new Vector2(x, 300));
                AddCondition(graph, (ConditionalGuardNodeModel)guard, branches[i].condition);
                Connect(graph, selector, guard);
                parent = guard;
            }
            NodeModel action = CreateNode(graph, branches[i].action, new Vector2(x, branches[i].condition != null ? 460 : 300));
            LinkFields(graph, (BehaviorGraphNodeModel)action);
            Connect(graph, parent, action);
        }

        // Đổi timestamp của blackboard để BuildRuntimeGraph build lại runtime blackboard (có biến mới)
        // và làm mới bản sao Blackboard trong runtime graph. Không tự gọi BuildRuntimeBlackboard trước,
        // nếu không timestamp khớp và runtime graph giữ bản sao cũ.
        graph.Blackboard.SetAssetDirty();
        EditorUtility.SetDirty(graph.Blackboard);
        EditorUtility.SetDirty(graph);
        graph.BuildRuntimeGraph(true);
        graph.SaveAsset();
        AssetDatabase.SaveAssets();
        Debug.Log($"[Hostage] Đã dựng Behavior Graph {GraphPath} ({graph.Nodes.Count} node)");
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
        if (info == null) throw new Exception($"[Hostage] NodeRegistry không có {runtimeType.Name}");
        return graph.CreateNode(info.ModelType, position, null, new object[] { info });
    }

    private static void Connect(BehaviorAuthoringGraph graph, NodeModel parent, NodeModel child)
    {
        // Node nạp từ asset (On Start) có thể chưa dựng lại cache port mặc định: lấy port đầu tiên.
        if (!parent.TryDefaultOutputPortModel(out PortModel output)) output = parent.OutputPortModels.FirstOrDefault();
        if (!child.TryDefaultInputPortModel(out PortModel input)) input = child.InputPortModels.FirstOrDefault();
        if (output == null || input == null)
            throw new Exception($"[Hostage] Không nối được {parent} -> {child}");
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

    /// <summary>Biến Blackboard cùng tên và cùng kiểu giá trị với ô của node (VariableInfo.Type có thể là BlackboardVariable&lt;T&gt;).</summary>
    private static VariableModel FindVariable(BehaviorAuthoringGraph graph, VariableInfo v)
    {
        Type t = v.Type;
        while (t != null && typeof(BlackboardVariable).IsAssignableFrom(t) && !(t.IsGenericType && t.GetGenericTypeDefinition() == typeof(BlackboardVariable<>)))
            t = t.BaseType;
        if (t != null && t.IsGenericType && t.GetGenericTypeDefinition() == typeof(BlackboardVariable<>))
            t = t.GetGenericArguments()[0];
        // Ô WayPoint của MoveToWayPoint đọc waypoint mà GetNextWayPoint vừa chọn (giống graph NPC).
        string name = v.Name == "WayPoint" ? "CurrentWayPoint" : v.Name;
        VariableModel bb = graph.Blackboard.Variables.FirstOrDefault(x => x.Name == name && x.Type == t);
        if (bb == null && graph.Blackboard.Variables.Any(x => x.Name == v.Name)) Debug.LogWarning($"[Hostage] Ô '{v.Name}' ({v.Type.Type?.Name} -> {t?.Name}) không có biến Blackboard cùng kiểu, để trống.");
        return bb;
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

    [MenuItem("Tools/VR SWAT/Hostage/4. Setup Prefab")]
    public static void SetupPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var animator = root.GetComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            animator.applyRootMotion = false;

            var agent = GetOrAdd<NavMeshAgent>(root);
            agent.radius = 0.3f;
            agent.height = 1.8f;
            agent.speed = 1.4f;
            agent.acceleration = 8f;
            agent.angularSpeed = 120f;
            agent.stoppingDistance = 0.3f;

            var body = GetOrAdd<CapsuleCollider>(root);
            body.radius = 0.3f;
            body.height = 1.8f;
            body.center = new Vector3(0f, 0.9f, 0f);

            // Giọng nói: NPCVoice + AudioSource "Voice" ở xương đầu (âm 3D phát ra từ miệng), bộ giọng nam mặc định.
            var voice = new SerializedObject(GetOrAdd<NPCVoice>(root));
            if (voice.FindProperty("_source").objectReferenceValue == null)
            {
                Transform head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
                var voiceGo = new GameObject("Voice");
                voiceGo.transform.SetParent(head != null ? head : root.transform, false);
                var source = voiceGo.AddComponent<AudioSource>();
                source.playOnAwake = false;
                voice.FindProperty("_source").objectReferenceValue = source;
            }
            if (voice.FindProperty("_voiceSet").objectReferenceValue == null)
                voice.FindProperty("_voiceSet").objectReferenceValue = AssetDatabase.LoadAssetAtPath<NPCVoiceSet>(VoiceSetPath);
            voice.ApplyModifiedPropertiesWithoutUndo();
            // AudioSource cũ trên gốc (hệ thống trước) không còn dùng.
            if (root.TryGetComponent(out AudioSource oldSource) && oldSource != voice.FindProperty("_source").objectReferenceValue)
                UnityEngine.Object.DestroyImmediate(oldSource, true);

            var behavior = GetOrAdd<BehaviorGraphAgent>(root);
            var graph = AssetDatabase.LoadAssetAtPath<BehaviorAuthoringGraph>(GraphPath);
            if (graph != null) behavior.Graph = graph.BuildRuntimeGraph(false);

            // Tốc độ bước chân đo từ clip của Hostage (sWalkFWD_normal1 / sRunFWD_normal1) để chân không trượt.
            var locomotion = new SerializedObject(GetOrAdd<NPCLocomotion>(root));
            locomotion.FindProperty("_walkSpeed").floatValue = 0.8f;
            locomotion.FindProperty("_runSpeed").floatValue = 2.75f;
            locomotion.FindProperty("_crouchWalkSpeed").floatValue = 0.45f;
            locomotion.ApplyModifiedPropertiesWithoutUndo();
            agent.speed = 0.8f;
            GetOrAdd<Hostage>(root);
            GetOrAdd<HostageCuffable>(root);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[Hostage] Đã setup {PrefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component =>
        go.TryGetComponent(out T c) ? c : go.AddComponent<T>();

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
