using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Dựng nhanh cửa bản lề / cửa lùa / ngăn kéo từ một object mesh có sẵn trong scene.
///
/// Dựng tay thì phải tự tính anchor và axis của HingeJoint trong local space của cửa, tự thêm
/// tay nắm, tự nhớ siết lại Rigidbody cho khỏi rung - rất dễ sai. Tool này làm hết:
/// collider -> Rigidbody (đã siết) -> joint (anchor đúng cạnh bản lề) -> component -> tay nắm.
///
/// Chạy qua menu Tools &gt; VR SWAT &gt; Environment Setup.
/// </summary>
public class EnvironmentSetup : EditorWindow
{
    private enum PartKind
    {
        HingedDoor,
        SlidingPart
    }

    private enum HingeSide
    {
        Left,
        Right,
        Bottom,
        Top
    }

    private enum SlideAxis
    {
        PlusX,
        MinusX,
        PlusY,
        MinusY,
        PlusZ,
        MinusZ
    }

    private PartKind kind = PartKind.HingedDoor;

    private HingeSide hingeSide = HingeSide.Left;
    private float openAngle = 95f;
    private bool swingBothWays;

    private SlideAxis slideAxis = SlideAxis.PlusX;
    private float travel = 0.9f;

    private float mass = 25f;
    private bool addHandle = true;
    private float handleInset = 0.07f;
    private float handleColliderSize = 0.12f;

    private Vector2 scroll;

    [MenuItem("Tools/VR SWAT/Environment Setup")]
    private static void Open()
    {
        GetWindow<EnvironmentSetup>("Environment Setup").minSize = new Vector2(330f, 360f);
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.HelpBox(
            "Chọn (các) object mesh trong scene rồi bấm nút dưới.\n\n" +
            "Object phải đang ở TRẠNG THÁI ĐÓNG: mốc 0 của joint là pose hiện tại.",
            MessageType.Info);

        kind = (PartKind)EditorGUILayout.EnumPopup("Loại", kind);

        EditorGUILayout.Space();

        if (kind == PartKind.HingedDoor)
        {
            hingeSide = (HingeSide)EditorGUILayout.EnumPopup(
                new GUIContent("Cạnh bản lề", "Tính theo local space của object cửa."), hingeSide);
            openAngle = EditorGUILayout.FloatField(
                new GUIContent("Góc mở (độ)", "Dấu quyết định chiều mở. Đảo dấu nếu cửa mở sai hướng."), openAngle);
            swingBothWays = EditorGUILayout.Toggle("Mở được hai chiều", swingBothWays);
        }
        else
        {
            slideAxis = (SlideAxis)EditorGUILayout.EnumPopup(
                new GUIContent("Trục trượt", "Local space. Chiều dương là chiều mở ra."), slideAxis);
            travel = EditorGUILayout.FloatField("Quãng mở (m)", travel);
        }

        EditorGUILayout.Space();
        mass = EditorGUILayout.FloatField(new GUIContent("Khối lượng (kg)", "Cửa gỗ ~25, cửa sắt ~60, ngăn kéo ~4."), mass);

        EditorGUILayout.Space();
        addHandle = EditorGUILayout.Toggle("Thêm tay nắm", addHandle);

        using (new EditorGUI.DisabledScope(!addHandle))
        {
            EditorGUI.indentLevel++;
            handleInset = EditorGUILayout.FloatField("Cách cạnh xa (m)", handleInset);
            handleColliderSize = EditorGUILayout.FloatField("Cỡ vùng nắm (m)", handleColliderSize);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();

        GameObject[] targets = Selection.gameObjects;

        using (new EditorGUI.DisabledScope(targets.Length == 0))
        {
            if (GUILayout.Button(targets.Length <= 1 ? "Dựng" : $"Dựng {targets.Length} object", GUILayout.Height(30f)))
                BuildAll(targets);
        }

        if (targets.Length == 0)
            EditorGUILayout.HelpBox("Chưa chọn object nào.", MessageType.Warning);

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Sau khi dựng:\n" +
            "- Gán mesh quả đấm vào field Knob của DoorHandle nếu muốn nó quay.\n" +
            "- Gán AudioSource + clip cho tiếng chốt / lắc ổ khoá.\n" +
            "- Bật Start Locked trên cửa cần phá hoặc cần chìa.",
            MessageType.None);

        EditorGUILayout.EndScrollView();
    }

    private void BuildAll(GameObject[] targets)
    {
        foreach (GameObject target in targets)
        {
            if (target == null || target.scene.IsValid() == false)
                continue;

            Undo.SetCurrentGroupName("Environment Setup");

            if (kind == PartKind.HingedDoor)
                BuildHingedDoor(target);
            else
                BuildSlidingPart(target);
        }

        EditorSceneManager.MarkAllScenesDirty();
    }

    // ------------------------------------------------------------------ Cửa bản lề

    private void BuildHingedDoor(GameObject target)
    {
        Bounds bounds = GetLocalBounds(target);

        EnsureCollider(target, bounds);
        Rigidbody body = EnsureStableRigidbody(target);

        GetHingeFrame(bounds, out Vector3 anchor, out Vector3 axis);

        HingeJoint hinge = target.GetComponent<HingeJoint>();

        if (hinge == null)
            hinge = Undo.AddComponent<HingeJoint>(target);

        Undo.RecordObject(hinge, "Setup Hinge");
        hinge.anchor = anchor;
        hinge.axis = axis;
        hinge.autoConfigureConnectedAnchor = true;
        hinge.connectedBody = null;
        // Preprocessing tắt đi thì joint ít bị "nổ" khi cửa bị đẩy vào tường hoặc bị kẹt.
        hinge.enablePreprocessing = false;

        HingedDoor door = target.GetComponent<HingedDoor>();

        if (door == null)
            door = Undo.AddComponent<HingedDoor>(target);

        Transform handle = addHandle ? CreateHandle(target, bounds, OppositeEdgeLocalPosition(bounds)) : null;

        SerializedObject so = new SerializedObject(door);
        SetFloat(so, "openAngle", openAngle);
        SetBool(so, "swingBothWays", swingBothWays);

        if (handle != null)
            SetObject(so, "pushAnchor", handle);

        so.ApplyModifiedProperties();

        if (handle != null)
            LinkHandle(handle, door, handle);

        Debug.Log($"[Environment Setup] {target.name}: cửa bản lề, bản lề {hingeSide}, góc {openAngle}°, {body.mass} kg.", target);
    }

    private void GetHingeFrame(Bounds bounds, out Vector3 anchor, out Vector3 axis)
    {
        Vector3 center = bounds.center;

        switch (hingeSide)
        {
            case HingeSide.Right:
                anchor = new Vector3(bounds.max.x, center.y, center.z);
                axis = Vector3.up;
                break;

            case HingeSide.Bottom:
                anchor = new Vector3(center.x, bounds.min.y, center.z);
                axis = Vector3.right;
                break;

            case HingeSide.Top:
                anchor = new Vector3(center.x, bounds.max.y, center.z);
                axis = Vector3.right;
                break;

            default:
                anchor = new Vector3(bounds.min.x, center.y, center.z);
                axis = Vector3.up;
                break;
        }
    }

    /// <summary>Vị trí tay nắm: cạnh đối diện bản lề, lùi vào <see cref="handleInset"/>.</summary>
    private Vector3 OppositeEdgeLocalPosition(Bounds bounds)
    {
        Vector3 center = bounds.center;

        switch (hingeSide)
        {
            case HingeSide.Right:
                return new Vector3(bounds.min.x + handleInset, center.y, center.z);

            case HingeSide.Bottom:
                return new Vector3(center.x, bounds.max.y - handleInset, center.z);

            case HingeSide.Top:
                return new Vector3(center.x, bounds.min.y + handleInset, center.z);

            default:
                return new Vector3(bounds.max.x - handleInset, center.y, center.z);
        }
    }

    // ------------------------------------------------------------------ Cửa lùa / ngăn kéo

    private void BuildSlidingPart(GameObject target)
    {
        Bounds bounds = GetLocalBounds(target);

        EnsureCollider(target, bounds);
        Rigidbody body = EnsureStableRigidbody(target);

        ConfigurableJoint joint = target.GetComponent<ConfigurableJoint>();

        if (joint == null)
            joint = Undo.AddComponent<ConfigurableJoint>(target);

        Undo.RecordObject(joint, "Setup Slide Joint");
        joint.anchor = bounds.center;
        joint.connectedBody = null;
        joint.enablePreprocessing = false;

        SlidingPart part = target.GetComponent<SlidingPart>();

        if (part == null)
            part = Undo.AddComponent<SlidingPart>(target);

        Vector3 axis = GetSlideAxisVector();

        // Tay nắm đặt ở đầu đi ra: với ngăn kéo là mặt trước, với cửa lùa là cạnh mép kéo.
        Vector3 handleLocal = bounds.center + Vector3.Scale(axis, bounds.extents) * 0.85f;
        Transform handle = addHandle ? CreateHandle(target, bounds, handleLocal) : null;

        SerializedObject so = new SerializedObject(part);
        SetVector(so, "slideAxis", axis);
        SetFloat(so, "travel", travel);
        so.ApplyModifiedProperties();

        if (handle != null)
            LinkHandle(handle, part, handle);

        Debug.Log($"[Environment Setup] {target.name}: bộ phận trượt theo {slideAxis}, {travel} m, {body.mass} kg.", target);
    }

    private Vector3 GetSlideAxisVector()
    {
        switch (slideAxis)
        {
            case SlideAxis.MinusX: return Vector3.left;
            case SlideAxis.PlusY: return Vector3.up;
            case SlideAxis.MinusY: return Vector3.down;
            case SlideAxis.PlusZ: return Vector3.forward;
            case SlideAxis.MinusZ: return Vector3.back;
            default: return Vector3.right;
        }
    }

    // ------------------------------------------------------------------ Phần dùng chung

    private static void EnsureCollider(GameObject target, Bounds bounds)
    {
        if (target.GetComponent<Collider>() != null)
            return;

        BoxCollider box = Undo.AddComponent<BoxCollider>(target);
        box.center = bounds.center;
        box.size = bounds.size;
    }

    /// <summary>
    /// Rigidbody đã siết sẵn cho cửa: nếu để mặc định Unity (Discrete + Interpolate None +
    /// maxDepenetrationVelocity 10) thì cửa va vào tường hay sàn sẽ bị nảy và rung lập bập.
    /// </summary>
    private Rigidbody EnsureStableRigidbody(GameObject target)
    {
        Rigidbody body = target.GetComponent<Rigidbody>();

        if (body == null)
            body = Undo.AddComponent<Rigidbody>(target);

        Undo.RecordObject(body, "Setup Rigidbody");
        body.mass = Mathf.Max(0.1f, mass);
        body.useGravity = true;
        body.angularDamping = 0.5f;
        body.linearDamping = 0.05f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.solverIterations = 16;
        body.solverVelocityIterations = 4;
        body.maxDepenetrationVelocity = 1f;

        return body;
    }

    private Transform CreateHandle(GameObject target, Bounds bounds, Vector3 localPosition)
    {
        Transform existing = target.transform.Find("Handle");

        if (existing != null)
            return existing;

        GameObject handle = new GameObject("Handle");
        Undo.RegisterCreatedObjectUndo(handle, "Create Handle");
        handle.transform.SetParent(target.transform, false);
        handle.transform.localPosition = localPosition;
        handle.transform.localRotation = Quaternion.identity;
        handle.layer = target.layer;

        BoxCollider box = handle.AddComponent<BoxCollider>();
        box.size = Vector3.one * Mathf.Max(0.02f, handleColliderSize);
        // Trigger: tay nắm là con của Rigidbody cửa, collider đặc ở đây sẽ làm cửa rung.
        box.isTrigger = true;

        handle.AddComponent<DoorHandle>();

        return handle.transform;
    }

    private static void LinkHandle(Transform handleTransform, MovablePart part, Transform grabAnchor)
    {
        DoorHandle handle = handleTransform.GetComponent<DoorHandle>();

        if (handle == null)
            return;

        SerializedObject so = new SerializedObject(handle);
        SetObject(so, "part", part);
        SetObject(so, "grabAnchor", grabAnchor);
        so.ApplyModifiedProperties();
    }

    /// <summary>
    /// Bounds của toàn bộ mesh con, tính trong local space của <paramref name="go"/>. Ước lượng
    /// bằng 8 góc world AABB của từng renderer nên object con bị xoay sẽ cho bounds hơi rộng.
    /// </summary>
    private static Bounds GetLocalBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
            return new Bounds(Vector3.zero, Vector3.one);

        bool initialized = false;
        Bounds result = new Bounds();

        foreach (Renderer renderer in renderers)
        {
            Bounds world = renderer.bounds;
            Vector3 min = world.min;
            Vector3 max = world.max;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z);

                Vector3 local = go.transform.InverseTransformPoint(corner);

                if (!initialized)
                {
                    result = new Bounds(local, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    result.Encapsulate(local);
                }
            }
        }

        return result;
    }

    private static void SetFloat(SerializedObject so, string path, float value)
    {
        SerializedProperty property = so.FindProperty(path);

        if (property != null)
            property.floatValue = value;
    }

    private static void SetBool(SerializedObject so, string path, bool value)
    {
        SerializedProperty property = so.FindProperty(path);

        if (property != null)
            property.boolValue = value;
    }

    private static void SetVector(SerializedObject so, string path, Vector3 value)
    {
        SerializedProperty property = so.FindProperty(path);

        if (property != null)
            property.vector3Value = value;
    }

    private static void SetObject(SerializedObject so, string path, UnityEngine.Object value)
    {
        SerializedProperty property = so.FindProperty(path);

        if (property != null)
            property.objectReferenceValue = value;
    }
}
