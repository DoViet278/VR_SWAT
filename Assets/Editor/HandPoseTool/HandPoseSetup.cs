using System;
using System.Collections.Generic;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Hai menu thiết lập cho hệ thống hand pose.
///
/// Phải chạy trong Editor chứ không sửa file <c>.unity</c> được: scene đang mở thì Unity giữ bản
/// trong bộ nhớ và sẽ ghi đè lúc save. Cùng lý do với <c>PhysicsHandsSetup</c>.
/// </summary>
public static class HandPoseSetup
{
    // ------------------------------------------------- gắn component lên tay

    [MenuItem("Tools/VR SWAT/Setup Hand Pose (gắn component lên hai bàn tay)")]
    private static void SetupHands()
    {
        if (BlockedByPlayMode("Setup Hand Pose"))
            return;

        var xrOrigin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();

        if (xrOrigin == null)
        {
            EditorUtility.DisplayDialog("Setup Hand Pose",
                "Không tìm thấy XR Origin trong scene đang mở.", "OK");
            return;
        }

        Transform root = xrOrigin.Origin != null ? xrOrigin.Origin.transform : xrOrigin.transform;

        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Setup Hand Pose");

        var text = new StringBuilder();
        int handCount = 0;

        foreach (KeyValuePair<GameObject, HandSide> hand in FindHandModels(root))
        {
            handCount++;
            SetupHand(hand.Key, hand.Value, text);
        }

        if (handCount == 0)
        {
            text.AppendLine("Không tìm thấy model tay nào dưới XR Origin.");
            text.AppendLine("Model tay cần có Animator và skinned mesh có xương b_l_* / b_r_*.");
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(xrOrigin.gameObject.scene);

        text.AppendLine();
        AppendStatus(root, text);

        Debug.Log("Setup Hand Pose:\n" + text);

        EditorUtility.DisplayDialog("Setup Hand Pose",
            text + "\n\nBản đầy đủ đã in ra Console. Nhớ Ctrl+S để lưu scene.", "OK");
    }

    /// <summary>
    /// Model tay = GameObject có Animator, mang (hoặc có con mang) skinned mesh mà danh sách
    /// xương có <c>b_l_</c> / <c>b_r_</c>. Bên tay suy ra từ tiền tố tên xương.
    /// </summary>
    private static Dictionary<GameObject, HandSide> FindHandModels(Transform root)
    {
        var found = new Dictionary<GameObject, HandSide>();

        foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Transform[] bones = renderer.bones;

            if (bones == null || bones.Length == 0)
                continue;

            HandSide? side = null;

            foreach (Transform bone in bones)
            {
                if (bone == null)
                    continue;

                if (bone.name.Contains("b_l_"))
                {
                    side = HandSide.Left;
                    break;
                }

                if (bone.name.Contains("b_r_"))
                {
                    side = HandSide.Right;
                    break;
                }
            }

            if (side == null)
                continue;

            var animator = renderer.GetComponentInParent<Animator>();
            GameObject modelRoot = animator != null ? animator.gameObject : renderer.gameObject;

            found[modelRoot] = side.Value;
        }

        return found;
    }

    private static void SetupHand(GameObject modelRoot, HandSide side, StringBuilder text)
    {
        var skeleton = modelRoot.GetComponent<HandSkeleton>();

        if (skeleton == null)
            skeleton = Undo.AddComponent<HandSkeleton>(modelRoot);

        Undo.RecordObject(skeleton, "Setup Hand Pose");
        skeleton.Handedness = side;
        skeleton.Resolve();
        EditorUtility.SetDirty(skeleton);

        if (modelRoot.GetComponent<HandPoseDriver>() == null)
            Undo.AddComponent<HandPoseDriver>(modelRoot);

        int resolved = HandJoints.Count - skeleton.MissingJointCount;
        int fingerColliders = skeleton.GetFingerColliders().Length;

        text.AppendLine($"{HandJoints.Label(side)} - '{modelRoot.name}': dò được {resolved}/{HandJoints.Count} " +
                        $"xương, {fingerColliders} collider ngón.");

        if (resolved < HandJoints.Count)
        {
            text.AppendLine("  Thiếu xương: kiểm tra tên xương trên model (cần b_?_thumb1..3, " +
                            "index1..3, middle1..3, ring1..3, pinky0..3).");
        }

        if (fingerColliders == 0)
        {
            text.AppendLine("  Không có collider nào trên xương ngón - model tay này có thể là bản " +
                            "không kèm collider. Không sao, chỉ là không cần tắt collider lúc cầm.");
        }
    }

    // ----------------------------------------------------------- bảng trạng thái

    private static void AppendStatus(Transform root, StringBuilder text)
    {
        text.AppendLine("Trạng thái:");

        AppendInteractorStatus(root, text);
        AppendTriggerStatus(root, text);
        AppendInteractableStatus(text);
        AppendOffsetStatus(text);
    }

    private static void AppendInteractorStatus(Transform root, StringBuilder text)
    {
        var interactors = new List<XRBaseInteractor>(root.GetComponentsInChildren<XRBaseInteractor>(true));
        int none = 0;

        foreach (XRBaseInteractor interactor in interactors)
        {
            if (interactor is NearFarInteractor && interactor.handedness == InteractorHandedness.None)
            {
                none++;
                text.AppendLine($"  [!] '{interactor.name}' chưa đặt Handedness (đang None). " +
                                "Dáng tay và phép căn offset đều cần nó.");
            }
        }

        if (none == 0)
            text.AppendLine("  [ok] Mọi NearFarInteractor đã đặt Handedness.");
    }

    private static void AppendTriggerStatus(Transform root, StringBuilder text)
    {
        var inputs = new List<AnimateHandOnInput>(root.GetComponentsInChildren<AnimateHandOnInput>(true));

        if (inputs.Count == 0)
        {
            text.AppendLine("  [!] Không có AnimateHandOnInput nào. Ngón trỏ sẽ không nhấp nhả theo " +
                            "cò; điền Trigger Action trên HandPoseDriver nếu muốn.");
            return;
        }

        foreach (AnimateHandOnInput input in inputs)
        {
            bool bound = IsBound(input.triggerValue);

            text.AppendLine(bound
                ? $"  [ok] '{input.name}' đã nối triggerValue" +
                  (input.handAnimator != null ? "." : " nhưng chưa gán Hand Animator.")
                : $"  [!] '{input.name}' chưa nối triggerValue - ngón trỏ sẽ không theo cò.");
        }
    }

    private static bool IsBound(UnityEngine.InputSystem.InputActionProperty property)
    {
        return property.reference != null || property.action != null;
    }

    private static void AppendInteractableStatus(StringBuilder text)
    {
        var plain = new List<XRGrabInteractable>();

        foreach (XRGrabInteractable interactable in UnityEngine.Object
                     .FindObjectsByType<XRGrabInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (interactable != null && !(interactable is HandPosedGrabInteractable))
                plain.Add(interactable);
        }

        if (plain.Count == 0)
        {
            text.AppendLine("  [ok] Mọi vật cầm được đã dùng HandPosedGrabInteractable.");
            return;
        }

        text.AppendLine($"  [!] {plain.Count} vật còn dùng XRGrabInteractable thường (chọn rồi chạy " +
                        "Tools > VR SWAT > Đổi sang Hand Posed Grab Interactable):");

        for (int i = 0; i < plain.Count && i < 12; i++)
            text.AppendLine($"      {plain[i].name} ({plain[i].GetType().Name})");

        if (plain.Count > 12)
            text.AppendLine($"      ... và {plain.Count - 12} vật nữa (xem Console).");
    }

    private static void AppendOffsetStatus(StringBuilder text)
    {
        var offsets = AssetDatabase.LoadAssetAtPath<HandPoseRigOffsets>(HandPoseRigOffsets.AssetPath);

        if (offsets == null || !offsets.IsCaptured(HandSide.Left) || !offsets.IsCaptured(HandSide.Right))
        {
            text.AppendLine("  [!] Chưa căn offset tay theo rig. Chọn một vật có HandPoseAuthoring " +
                            "rồi bấm \"Căn cả hai tay\" (làm một lần cho cả dự án).");
            return;
        }

        text.AppendLine("  [ok] Đã căn offset tay theo rig cho cả hai bên.");
    }

    // --------------------------------------- đổi sang HandPosedGrabInteractable

    [MenuItem("Tools/VR SWAT/Đổi sang Hand Posed Grab Interactable (vật đang chọn)")]
    private static void ConvertSelected()
    {
        if (BlockedByPlayMode("Đổi interactable"))
            return;

        GameObject target = Selection.activeGameObject;

        if (target == null)
        {
            EditorUtility.DisplayDialog("Đổi interactable", "Chọn một vật trong Hierarchy trước.", "OK");
            return;
        }

        var old = target.GetComponent<XRGrabInteractable>();

        if (old == null)
        {
            EditorUtility.DisplayDialog("Đổi interactable",
                $"'{target.name}' không có XRGrabInteractable nào.", "OK");
            return;
        }

        if (old is HandPosedGrabInteractable)
        {
            EditorUtility.DisplayDialog("Đổi interactable",
                $"'{target.name}' đã dùng HandPosedGrabInteractable rồi.", "OK");
            return;
        }

        if (!Convert(target, old, out string report))
        {
            EditorUtility.DisplayDialog("Đổi interactable", report, "OK");
            return;
        }

        EditorSceneManager.MarkSceneDirty(target.scene);
        Selection.activeGameObject = target;

        EditorUtility.DisplayDialog("Đổi interactable", report, "OK");
    }

    /// <summary>
    /// Đổi component, giữ nguyên mọi giá trị đã set và mọi tham chiếu trong scene. Undo được.
    /// </summary>
    public static bool Convert(GameObject target, XRGrabInteractable old, out string report)
    {
        MonoScript script = FindMonoScript(typeof(HandPosedGrabInteractable));

        if (script == null)
        {
            report = "Không tìm thấy file script HandPosedGrabInteractable.cs. " +
                     "Chờ Unity biên dịch xong rồi thử lại.";
            return false;
        }

        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Đổi sang Hand Posed Grab Interactable");

        string method;
        HandPosedGrabInteractable result;

        if (HasDependents(target, old))
        {
            // Có component khác RequireComponent cái này (GunRecoilController chẳng hạn) nên Unity
            // không cho xoá. Đổi thẳng m_Script: giá trị khớp theo tên field nên giữ nguyên hết,
            // và không có tham chiếu nào phải trỏ lại vì vẫn là đúng instance đó.
            result = SwapScript(old, script, out method);
        }
        else
        {
            result = RebuildComponent(target, old, out method);
        }

        if (result == null && old != null)
        {
            Undo.CollapseUndoOperations(group);
            report = "Đổi không thành công. Xem Console.";
            return false;
        }

        if (result != null)
        {
            Undo.RecordObject(result, "Đổi sang Hand Posed Grab Interactable");

            // Dáng tay cố định thì không dùng dynamic attach: để bật là XRI tự tạo điểm bám theo
            // vị trí tay lúc cầm, và bàn tay sẽ không khớp dáng đã tạo.
            result.useDynamicAttach = false;
            EditorUtility.SetDirty(result);
        }

        Undo.CollapseUndoOperations(group);

        var text = new StringBuilder();
        text.AppendLine($"'{target.name}' đã đổi sang HandPosedGrabInteractable ({method}).");
        text.AppendLine();
        text.AppendLine("Đã giữ: SelectMode, MovementType, Attach Ease In Time, Throw On Detach, " +
                        "danh sách grab transformer, event.");

        text.AppendLine(result != null
            ? "Đã tắt: Use Dynamic Attach."
            : "CHƯA tắt được Use Dynamic Attach (component vừa đổi script nên chưa đọc lại được). " +
              "Tự tay bỏ tick Use Dynamic Attach trên Inspector.");
        text.AppendLine();
        text.AppendLine("Còn lại: gán Left/Right Hand Attach - hoặc cứ để trống, bước \"Dựng bàn tay " +
                        "xem trước\" của HandPoseAuthoring sẽ tự tạo và tự gán.");

        report = text.ToString();
        return true;
    }

    /// <summary>
    /// Đường chính: chép giá trị qua một GameObject tạm, xoá component cũ, thêm component mới,
    /// chép giá trị vào, rồi trỏ lại mọi tham chiếu trong scene.
    ///
    /// Phải đi qua GameObject tạm vì <see cref="XRGrabInteractable"/> có
    /// <c>[DisallowMultipleComponent]</c>: không thể để component cũ và mới cùng tồn tại để chép
    /// trực tiếp, mà cũng không chép được từ component đã bị xoá.
    /// </summary>
    private static HandPosedGrabInteractable RebuildComponent(GameObject target, XRGrabInteractable old,
        out string method)
    {
        method = "xoá rồi thêm lại";

        var temp = new GameObject("HandPose Convert Temp") { hideFlags = HideFlags.HideAndDontSave };

        try
        {
            if (!UnityEditorInternal.ComponentUtility.CopyComponent(old) ||
                !UnityEditorInternal.ComponentUtility.PasteComponentAsNew(temp))
            {
                Debug.LogError("Không chép được XRGrabInteractable sang GameObject tạm.", old);
                return null;
            }

            var backup = temp.GetComponent<XRGrabInteractable>();

            if (backup == null)
            {
                Debug.LogError("GameObject tạm không nhận được bản chép của XRGrabInteractable.", old);
                return null;
            }

            List<Reference> references = CollectReferences(old);

            Undo.DestroyObjectImmediate(old);

            var created = Undo.AddComponent<HandPosedGrabInteractable>(target);

            if (created == null)
            {
                Debug.LogError("Không thêm được HandPosedGrabInteractable.", target);
                return null;
            }

            CopySerializedValues(backup, created);
            Repoint(references, created);

            return created;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(temp);
        }
    }

    /// <summary>
    /// Đường dự phòng: đổi thẳng <c>m_Script</c>. Dùng khi có component khác
    /// <c>RequireComponent</c> cái cũ nên Unity không cho xoá.
    /// </summary>
    private static HandPosedGrabInteractable SwapScript(XRGrabInteractable old, MonoScript script,
        out string method)
    {
        method = "đổi m_Script, giữ nguyên instance";

        var serialized = new SerializedObject(old);
        SerializedProperty scriptProperty = serialized.FindProperty("m_Script");

        if (scriptProperty == null)
            return null;

        Undo.RecordObject(old, "Đổi sang Hand Posed Grab Interactable");
        scriptProperty.objectReferenceValue = script;
        serialized.ApplyModifiedProperties();

        // old đã là instance của class mới sau khi ApplyModifiedProperties, nhưng biến cũ vẫn giữ
        // kiểu cũ nên phải lấy lại component từ GameObject.
        return old.gameObject.GetComponent<HandPosedGrabInteractable>();
    }

    private static bool HasDependents(GameObject target, Component component)
    {
        Type type = component.GetType();

        foreach (Component other in target.GetComponents<Component>())
        {
            if (other == null || other == component)
                continue;

            foreach (object attribute in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
            {
                var required = (RequireComponent)attribute;

                if (Requires(required.m_Type0, type) || Requires(required.m_Type1, type) ||
                    Requires(required.m_Type2, type))
                    return true;
            }
        }

        return false;
    }

    private static bool Requires(Type required, Type type)
    {
        return required != null && required.IsAssignableFrom(type);
    }

    private static MonoScript FindMonoScript(Type type)
    {
        foreach (string guid in AssetDatabase.FindAssets(type.Name + " t:MonoScript"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);

            if (script != null && script.GetClass() == type)
                return script;
        }

        return null;
    }

    /// <summary>Chép mọi field đã serialize, trừ m_Script. Field nào bên mới không có thì bỏ qua.</summary>
    private static void CopySerializedValues(Component source, Component destination)
    {
        var from = new SerializedObject(source);
        var to = new SerializedObject(destination);

        SerializedProperty property = from.GetIterator();

        while (property.NextVisible(true))
        {
            if (property.propertyPath == "m_Script")
                continue;

            if (to.FindProperty(property.propertyPath) == null)
                continue;

            to.CopyFromSerializedProperty(property);
        }

        to.ApplyModifiedProperties();
    }

    private struct Reference
    {
        public Component Owner;
        public string PropertyPath;
    }

    /// <summary>
    /// Mọi chỗ trong các scene đang mở đang trỏ vào <paramref name="component"/>. Event của XRI
    /// cũng nằm trong đây (target của UnityEvent là một object reference đã serialize).
    /// </summary>
    private static List<Reference> CollectReferences(Component component)
    {
        var references = new List<Reference>();

        for (int sceneIndex = 0; sceneIndex < UnityEngine.SceneManagement.SceneManager.sceneCount; sceneIndex++)
        {
            UnityEngine.SceneManagement.Scene scene =
                UnityEngine.SceneManagement.SceneManager.GetSceneAt(sceneIndex);

            if (!scene.isLoaded)
                continue;

            foreach (GameObject rootObject in scene.GetRootGameObjects())
            {
                foreach (Component other in rootObject.GetComponentsInChildren<Component>(true))
                {
                    if (other == null || other == component)
                        continue;

                    CollectReferences(other, component, references);
                }
            }
        }

        return references;
    }

    private static void CollectReferences(Component owner, Component target, List<Reference> references)
    {
        var serialized = new SerializedObject(owner);
        SerializedProperty property = serialized.GetIterator();

        while (property.NextVisible(true))
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference)
                continue;

            if (property.objectReferenceValue == target)
                references.Add(new Reference { Owner = owner, PropertyPath = property.propertyPath });
        }
    }

    private static void Repoint(List<Reference> references, Component replacement)
    {
        foreach (Reference reference in references)
        {
            if (reference.Owner == null)
                continue;

            Undo.RecordObject(reference.Owner, "Đổi sang Hand Posed Grab Interactable");

            var serialized = new SerializedObject(reference.Owner);
            SerializedProperty property = serialized.FindProperty(reference.PropertyPath);

            if (property == null)
                continue;

            property.objectReferenceValue = replacement;
            serialized.ApplyModifiedProperties();
        }
    }

    private static bool BlockedByPlayMode(string title)
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            return false;

        EditorUtility.DisplayDialog(title,
            "Đang ở Play mode. Thay đổi lúc đang Play sẽ bị xoá khi thoát Play.\n\n" +
            "Thoát Play rồi chạy lại.", "OK");

        return true;
    }
}
