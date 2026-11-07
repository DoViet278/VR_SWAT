using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Công cụ tạo dáng tay, chạy ngay trên Inspector của <see cref="HandPoseAuthoring"/>.
///
/// Ý tưởng: dựng một bàn tay XEM TRƯỚC gắn vào đúng điểm bám của vật, lệch so với điểm bám đúng
/// bằng độ lệch đã đo trong <see cref="HandPoseRigOffsets"/>. Nhờ vậy bàn tay trong Editor đứng
/// y như lúc chơi, nên chỉnh cho nó trông đúng là xong - và vì chính điểm bám là thứ lúc chơi
/// súng sẽ snap vào, nên khi bàn tay trông đúng thì điểm bám cũng đã đúng.
///
/// Bàn tay xem trước mang <c>HideFlags.DontSaveInEditor</c>, nên quên xoá cũng không bị lưu vào
/// scene.
/// </summary>
[CustomEditor(typeof(HandPoseAuthoring))]
public class HandPoseAuthoringEditor : Editor
{
    private const string PoseFolder = "Assets/Hands/Poses";

    private SerializedProperty handednessProperty;
    private SerializedProperty supportHandProperty;
    private SerializedProperty poseProperty;
    private SerializedProperty mirrorSourceProperty;
    private SerializedProperty curlProperty;

    private void OnEnable()
    {
        handednessProperty = serializedObject.FindProperty("handedness");
        supportHandProperty = serializedObject.FindProperty("supportHand");
        poseProperty = serializedObject.FindProperty("pose");
        mirrorSourceProperty = serializedObject.FindProperty("mirrorSource");
        curlProperty = serializedObject.FindProperty("curl");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var authoring = (HandPoseAuthoring)target;

        EditorGUILayout.HelpBox(
            "Component tạm, chỉ dùng lúc thiết kế. Bước 10 sẽ tự gỡ nó.",
            MessageType.None);

        DrawRigOffsetSection();
        DrawStep("1-2. Tay nào", DrawHandSection);
        DrawStep("3. Pose asset", () => DrawPoseSection(authoring));
        DrawStep("4-5. Bàn tay xem trước và điểm bám", () => DrawPreviewSection(authoring));
        DrawStep("6. Dáng ngón", () => DrawCurlSection(authoring));
        DrawStep("7-8. Lưu dáng", () => DrawSaveSection(authoring));
        DrawStep("9. Gắn vào vật", () => DrawProviderSection(authoring));
        DrawStep("10. Dọn dẹp", () => DrawCleanupSection(authoring));
        DrawStep("Tay còn lại", () => DrawMirrorSection(authoring));

        serializedObject.ApplyModifiedProperties();
    }

    private static void DrawStep(string title, System.Action body)
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            body();
    }

    // ------------------------------------------------------------ căn offset

    private void DrawRigOffsetSection()
    {
        var offsets = AssetDatabase.LoadAssetAtPath<HandPoseRigOffsets>(HandPoseRigOffsets.AssetPath);
        bool ready = offsets != null && offsets.IsCaptured(HandSide.Left) && offsets.IsCaptured(HandSide.Right);

        if (!ready)
        {
            EditorGUILayout.HelpBox(
                "Chưa căn offset tay theo rig. Bước này đo xem model tay đứng lệch bao nhiêu so " +
                "với điểm bám của interactor; không căn thì bàn tay xem trước lệch đúng chừng ấy " +
                "so với lúc chơi. Làm một lần cho cả dự án.",
                MessageType.Warning);
        }

        if (GUILayout.Button(ready ? "Căn lại cả hai tay" : "Căn cả hai tay"))
        {
            bool ok = HandPoseRigOffsets.CalibrateBoth(out string report);
            EditorUtility.DisplayDialog("Căn offset tay theo rig", report, "OK");

            if (ok)
                HandPoseCurl.ClearCache();
        }
    }

    // --------------------------------------------------------------- bước 1-2

    private void DrawHandSection()
    {
        EditorGUILayout.PropertyField(handednessProperty, new GUIContent("Tay"));
        EditorGUILayout.PropertyField(supportHandProperty, new GUIContent("Là tay đỡ"));

        if (supportHandProperty.boolValue)
        {
            EditorGUILayout.HelpBox(
                "Dáng cho bàn tay thứ hai. Lưu ý: XRGeneralGrabTransformer không đọc điểm bám phụ " +
                "của vật - nó chỉ hướng súng theo tay thứ hai. Nên dáng sẽ đúng về hình, nhưng bàn " +
                "tay vẫn nằm ở vị trí controller thật.",
                MessageType.Info);
        }
    }

    // ----------------------------------------------------------------- bước 3

    private void DrawPoseSection(HandPoseAuthoring authoring)
    {
        EditorGUILayout.PropertyField(poseProperty, new GUIContent("Pose asset"));

        if (poseProperty.objectReferenceValue != null)
            return;

        if (GUILayout.Button("Tạo pose asset mới"))
            CreatePoseAsset(authoring);
    }

    private void CreatePoseAsset(HandPoseAuthoring authoring)
    {
        if (!AssetDatabase.IsValidFolder(PoseFolder))
            CreateFolders(PoseFolder);

        var side = (HandSide)handednessProperty.enumValueIndex;
        bool support = supportHandProperty.boolValue;

        string name = $"{authoring.name} {(side == HandSide.Left ? "L" : "R")}" +
                      (support ? " Support" : " Grip");

        string path = AssetDatabase.GenerateUniqueAssetPath($"{PoseFolder}/{Sanitize(name)}.asset");

        var pose = CreateInstance<HandPose>();
        pose.Handedness = side;
        pose.SupportHand = support;

        AssetDatabase.CreateAsset(pose, path);
        AssetDatabase.SaveAssets();

        poseProperty.objectReferenceValue = pose;
        serializedObject.ApplyModifiedProperties();

        EditorGUIUtility.PingObject(pose);
    }

    private static void CreateFolders(string folder)
    {
        string[] parts = folder.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];

            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);

            current = next;
        }
    }

    private static string Sanitize(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        return name.Trim();
    }

    // --------------------------------------------------------------- bước 4-5

    private void DrawPreviewSection(HandPoseAuthoring authoring)
    {
        var side = (HandSide)handednessProperty.enumValueIndex;
        GameObject preview = FindPreview(authoring, side);

        Transform attach = ResolveAttach(authoring, side, false, out string attachNote);

        if (!string.IsNullOrEmpty(attachNote))
            EditorGUILayout.HelpBox(attachNote, MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(preview == null ? "Dựng bàn tay xem trước" : "Dựng lại bàn tay xem trước"))
            {
                BuildPreview(authoring, side);
                return;
            }

            using (new EditorGUI.DisabledScope(preview == null))
            {
                if (GUILayout.Button("Xoá bàn tay xem trước"))
                {
                    DestroyPreview(authoring, side);
                    return;
                }
            }
        }

        using (new EditorGUI.DisabledScope(attach == null))
        {
            if (GUILayout.Button("Chọn điểm bám trong Hierarchy"))
            {
                Selection.activeTransform = attach;
                EditorGUIUtility.PingObject(attach);
            }
        }

        if (preview == null)
        {
            EditorGUILayout.HelpBox(
                "Dựng bàn tay xem trước rồi DI CHUYỂN / XOAY ĐIỂM BÁM để đưa cả bàn tay vào đúng " +
                "chỗ cầm. Điểm bám chính là thứ lúc chơi súng sẽ snap vào, nên bàn tay trông đúng " +
                "là điểm bám cũng đúng.",
                MessageType.None);
            return;
        }

        HandSkeleton skeleton = preview.GetComponent<HandSkeleton>();

        if (skeleton == null || !skeleton.IsResolved)
        {
            int missing = skeleton != null ? skeleton.MissingJointCount : HandJoints.Count;

            EditorGUILayout.HelpBox(
                $"Bàn tay xem trước thiếu {missing} xương, không tạo dáng được. Kiểm tra tên xương " +
                "trên model tay.",
                MessageType.Error);
        }
    }

    private void BuildPreview(HandPoseAuthoring authoring, HandSide side)
    {
        HandPoseRigOffsets offsets = HandPoseRigOffsets.GetOrCreate();

        if (!offsets.IsCaptured(side))
        {
            EditorUtility.DisplayDialog("Dựng bàn tay xem trước",
                $"Chưa căn offset cho {HandJoints.Label(side).ToLower()}. Bấm \"Căn cả hai tay\" trước.",
                "OK");
            return;
        }

        HandPoseRigOffsets.SideOffset offset = offsets.Get(side);
        GameObject source = offset.handModelPrefab;

        if (source == null)
        {
            HandSkeleton inScene = HandPoseRigOffsets.FindSkeleton(side);
            source = inScene != null ? inScene.gameObject : null;
        }

        if (source == null)
        {
            EditorUtility.DisplayDialog("Dựng bàn tay xem trước",
                "Không có prefab model tay cũng không có model tay trong scene. Mở BasicScene, " +
                "chạy Setup Hand Pose rồi Căn cả hai tay.",
                "OK");
            return;
        }

        Transform attach = ResolveAttach(authoring, side, true, out _);

        if (attach == null)
        {
            EditorUtility.DisplayDialog("Dựng bàn tay xem trước",
                "Không tạo được điểm bám cho vật này.", "OK");
            return;
        }

        DestroyPreview(authoring, side);

        GameObject preview = Instantiate(source);
        preview.name = PreviewName(side);

        StripPreview(preview);

        var skeleton = preview.GetComponent<HandSkeleton>();

        if (skeleton == null)
            skeleton = preview.AddComponent<HandSkeleton>();

        skeleton.Handedness = side;
        skeleton.Resolve();

        preview.transform.SetParent(attach, false);
        preview.transform.localPosition = offset.localPosition;
        preview.transform.localRotation = offset.localRotation;
        preview.transform.localScale = Vector3.one;

        MarkDontSave(preview);

        authoring.previewHand = preview;

        // Có dáng sẵn thì nạp luôn, chưa có thì dựng dáng thô từ slider.
        var pose = (HandPose)poseProperty.objectReferenceValue;

        if (pose != null)
            skeleton.ApplyLocalRotations(CapturePoseRotations(pose));
        else
            ApplyCurl(authoring, skeleton, null);

        Selection.activeTransform = attach;
        SceneView.RepaintAll();
    }

    /// <summary>Gỡ mọi thứ không cần cho việc xem trước: vật lý, animator, driver.</summary>
    private static void StripPreview(GameObject preview)
    {
        foreach (Rigidbody body in preview.GetComponentsInChildren<Rigidbody>(true))
            DestroyImmediate(body);

        foreach (Collider collider in preview.GetComponentsInChildren<Collider>(true))
            DestroyImmediate(collider);

        foreach (HandPoseDriver driver in preview.GetComponentsInChildren<HandPoseDriver>(true))
            DestroyImmediate(driver);

        foreach (HandPoseProvider provider in preview.GetComponentsInChildren<HandPoseProvider>(true))
            DestroyImmediate(provider);

        // Animator phải còn đó (HandPoseCurl lấy dáng nắm bằng cách sample clip lên hierarchy này),
        // chỉ tắt đi để nó không ghi đè dáng mình đang chỉnh.
        foreach (Animator animator in preview.GetComponentsInChildren<Animator>(true))
            animator.enabled = false;
    }

    private static void MarkDontSave(GameObject preview)
    {
        const HideFlags flags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

        foreach (Transform child in preview.GetComponentsInChildren<Transform>(true))
            child.gameObject.hideFlags = flags;
    }

    private static string PreviewName(HandSide side)
    {
        return side == HandSide.Left ? "HandPosePreview (Left)" : "HandPosePreview (Right)";
    }

    private static GameObject FindPreview(HandPoseAuthoring authoring, HandSide side)
    {
        if (authoring.previewHand != null && authoring.previewHand.name == PreviewName(side))
            return authoring.previewHand;

        // Tham chiếu mất sau khi scene nạp lại (bàn tay không được lưu), tìm lại theo tên.
        foreach (Transform child in authoring.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == PreviewName(side))
            {
                authoring.previewHand = child.gameObject;
                return authoring.previewHand;
            }
        }

        return null;
    }

    private static void DestroyPreview(HandPoseAuthoring authoring, HandSide side)
    {
        GameObject preview = FindPreview(authoring, side);

        if (preview != null)
            DestroyImmediate(preview);

        authoring.previewHand = null;
    }

    private static HandSkeleton GetPreviewSkeleton(HandPoseAuthoring authoring, HandSide side)
    {
        GameObject preview = FindPreview(authoring, side);
        return preview != null ? preview.GetComponent<HandSkeleton>() : null;
    }

    // -------------------------------------------------------------- điểm bám

    /// <summary>
    /// Điểm bám của một bên tay trên vật. <paramref name="create"/> = tạo mới nếu còn trống.
    /// </summary>
    private static Transform ResolveAttach(HandPoseAuthoring authoring, HandSide side, bool create,
        out string note)
    {
        note = null;

        var posed = authoring.GetComponent<HandPosedGrabInteractable>() ??
                    authoring.GetComponentInParent<HandPosedGrabInteractable>();

        if (posed != null)
        {
            Transform attach = posed.GetHandAttachTransform(side);

            if (attach != null || !create)
                return attach;

            attach = CreateAttach(posed.transform, side == HandSide.Left ? "Left Hand Attach" : "Right Hand Attach");

            Undo.RecordObject(posed, "Tạo điểm bám tay");
            posed.SetHandAttachTransform(side, attach);
            EditorUtility.SetDirty(posed);

            return attach;
        }

        var grab = authoring.GetComponent<XRGrabInteractable>() ??
                   authoring.GetComponentInParent<XRGrabInteractable>();

        if (grab != null)
        {
            note = $"'{grab.name}' chưa đổi sang HandPosedGrabInteractable nên hai tay dùng CHUNG " +
                   "một điểm bám. Chạy Tools > VR SWAT > Đổi sang Hand Posed Grab Interactable nếu " +
                   "cần dáng riêng cho từng tay.";

            if (grab.attachTransform != null || !create)
                return grab.attachTransform;

            Transform attach = CreateAttach(grab.transform, "Hand Attach");

            Undo.RecordObject(grab, "Tạo điểm bám tay");
            grab.attachTransform = attach;
            EditorUtility.SetDirty(grab);

            return attach;
        }

        note = $"'{authoring.name}' không có interactable nào, nên bàn tay xem trước gắn thẳng vào " +
               "gốc vật. Dáng vẫn lưu được, nhưng không có điểm bám để canh.";

        return authoring.transform;
    }

    private static Transform CreateAttach(Transform parent, string name)
    {
        var attach = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(attach, "Tạo điểm bám tay");
        attach.transform.SetParent(parent, false);

        return attach.transform;
    }

    // ----------------------------------------------------------------- bước 6

    private void DrawCurlSection(HandPoseAuthoring authoring)
    {
        var side = (HandSide)handednessProperty.enumValueIndex;
        HandSkeleton skeleton = GetPreviewSkeleton(authoring, side);

        if (skeleton == null || !skeleton.IsResolved)
        {
            EditorGUILayout.LabelField("Dựng bàn tay xem trước trước đã.");
            return;
        }

        HandPoseCurl.CurlData data = HandPoseCurl.Get(skeleton);

        if (data == null)
        {
            EditorGUILayout.HelpBox("Không lấy được dáng nắm của model tay này.", MessageType.Error);
            return;
        }

        if (!data.FromClips)
        {
            EditorGUILayout.HelpBox(
                "Không tìm thấy clip tay (*_hand_default_anim / *_hand_fist_anim) nên slider đang " +
                "dùng ĐƯỜNG DỰ PHÒNG: trục gập được suy từ hình học. Thô hơn, chỉnh tay thêm là " +
                "chuyện thường.",
                MessageType.Warning);
        }

        EditorGUILayout.LabelField("Mốc dáng nắm: " + data.Source, EditorStyles.miniLabel);

        if (curlProperty.arraySize != 5)
            curlProperty.arraySize = 5;

        for (int i = 0; i < 5; i++)
        {
            var finger = (HandFinger)i;
            SerializedProperty element = curlProperty.GetArrayElementAtIndex(i);

            EditorGUI.BeginChangeCheck();
            float value = EditorGUILayout.Slider(HandJoints.Label(finger), element.floatValue, 0f, 1f);

            if (!EditorGUI.EndChangeCheck())
                continue;

            element.floatValue = value;
            serializedObject.ApplyModifiedProperties();

            ApplyCurl(authoring, skeleton, finger);
        }

        EditorGUILayout.HelpBox(
            "Kéo slider ra dáng thô, rồi chọn từng xương trong Hierarchy và xoay để chỉnh chi tiết. " +
            "Kéo slider ghi lại CẢ NGÓN đó, nên kéo slider trước rồi chỉnh tay sau.",
            MessageType.None);

        if (GUILayout.Button("Nạp lại dáng từ pose asset vào bàn tay xem trước"))
        {
            var pose = (HandPose)poseProperty.objectReferenceValue;

            if (pose != null)
            {
                skeleton.ApplyLocalRotations(CapturePoseRotations(pose));
                SceneView.RepaintAll();
            }
        }
    }

    /// <summary>Áp độ cong cho một ngón, hoặc cho cả năm ngón khi <paramref name="only"/> là null.</summary>
    private void ApplyCurl(HandPoseAuthoring authoring, HandSkeleton skeleton, HandFinger? only)
    {
        HandPoseCurl.CurlData data = HandPoseCurl.Get(skeleton);

        if (data == null)
            return;

        for (int i = 0; i < 5; i++)
        {
            var finger = (HandFinger)i;

            if (only.HasValue && only.Value != finger)
                continue;

            float t = authoring.curl != null && i < authoring.curl.Length ? authoring.curl[i] : 0f;

            foreach (HandJointId joint in HandJoints.JointsOf(finger))
            {
                Transform bone = skeleton.GetJoint(joint);

                if (bone != null)
                    bone.localRotation = HandPoseCurl.Evaluate(data, joint, t);
            }
        }

        SceneView.RepaintAll();
    }

    // --------------------------------------------------------------- bước 7-8

    private void DrawSaveSection(HandPoseAuthoring authoring)
    {
        var side = (HandSide)handednessProperty.enumValueIndex;
        HandSkeleton skeleton = GetPreviewSkeleton(authoring, side);
        var pose = (HandPose)poseProperty.objectReferenceValue;

        using (new EditorGUI.DisabledScope(skeleton == null || pose == null))
        {
            if (GUILayout.Button("7. Lưu dáng vào asset"))
                SavePose(pose, skeleton, side);

            if (GUILayout.Button("8. Ghi dáng ngón trỏ lúc bóp cò"))
                SaveTriggerPose(pose, skeleton);
        }

        if (pose == null)
            EditorGUILayout.LabelField("Chưa có pose asset.");
        else if (!pose.HasTriggerPose)
            EditorGUILayout.HelpBox(
                "Chưa ghi dáng ngón trỏ lúc bóp cò. Cong ngón trỏ vào sát cò rồi bấm nút 8; không " +
                "ghi thì ngón trỏ cứng một dáng suốt lúc cầm.",
                MessageType.Info);
        else
            EditorGUILayout.LabelField("Đã có dáng ngón trỏ lúc bóp cò.");
    }

    private void SavePose(HandPose pose, HandSkeleton skeleton, HandSide side)
    {
        Undo.RecordObject(pose, "Lưu dáng tay");

        pose.Handedness = side;
        pose.SupportHand = supportHandProperty.boolValue;
        pose.SetJointRotations(skeleton.CaptureLocalRotations());

        EditorUtility.SetDirty(pose);
        AssetDatabase.SaveAssets();
    }

    private static void SaveTriggerPose(HandPose pose, HandSkeleton skeleton)
    {
        var rotations = new Quaternion[HandJoints.IndexJoints.Length];

        for (int i = 0; i < HandJoints.IndexJoints.Length; i++)
        {
            Transform bone = skeleton.GetJoint(HandJoints.IndexJoints[i]);
            rotations[i] = bone != null ? bone.localRotation : Quaternion.identity;
        }

        Undo.RecordObject(pose, "Ghi dáng ngón trỏ lúc bóp cò");
        pose.SetTriggerPose(rotations);

        EditorUtility.SetDirty(pose);
        AssetDatabase.SaveAssets();
    }

    private static Quaternion[] CapturePoseRotations(HandPose pose)
    {
        var rotations = new Quaternion[HandJoints.Count];

        for (int i = 0; i < HandJoints.Count; i++)
            rotations[i] = pose.GetJointRotation((HandJointId)i);

        return rotations;
    }

    // ----------------------------------------------------------------- bước 9

    private void DrawProviderSection(HandPoseAuthoring authoring)
    {
        var pose = (HandPose)poseProperty.objectReferenceValue;
        var provider = authoring.GetComponent<HandPoseProvider>();

        using (new EditorGUI.DisabledScope(pose == null))
        {
            if (GUILayout.Button("Gán pose này vào HandPoseProvider của vật"))
                AssignToProvider(authoring, pose);
        }

        if (provider == null)
        {
            EditorGUILayout.LabelField("Vật chưa có HandPoseProvider (nút trên sẽ tự thêm).");
            return;
        }

        EditorGUILayout.LabelField("Đã gán:");

        foreach (HandSide side in new[] { HandSide.Left, HandSide.Right })
        {
            foreach (bool support in new[] { false, true })
            {
                HandPose assigned = provider.GetPose(side, support);
                string label = HandJoints.Label(side) + (support ? " (tay đỡ)" : " (tay chính)");

                // GetPose rơi về dáng tay chính khi ô tay đỡ trống, nên không in trùng.
                if (support && assigned == provider.GetPose(side, false))
                    assigned = null;

                EditorGUILayout.LabelField("  " + label, assigned != null ? assigned.name : "-");
            }
        }
    }

    private void AssignToProvider(HandPoseAuthoring authoring, HandPose pose)
    {
        var provider = authoring.GetComponent<HandPoseProvider>();

        if (provider == null)
            provider = Undo.AddComponent<HandPoseProvider>(authoring.gameObject);

        Undo.RecordObject(provider, "Gán dáng tay");
        provider.SetPose((HandSide)handednessProperty.enumValueIndex, supportHandProperty.boolValue, pose);
        EditorUtility.SetDirty(provider);
    }

    // ---------------------------------------------------------------- bước 10

    private void DrawCleanupSection(HandPoseAuthoring authoring)
    {
        if (!GUILayout.Button("Xoá bàn tay xem trước rồi gỡ component này"))
            return;

        DestroyPreview(authoring, HandSide.Left);
        DestroyPreview(authoring, HandSide.Right);

        Undo.DestroyObjectImmediate(authoring);
    }

    // ------------------------------------------------------------ lật gương

    private void DrawMirrorSection(HandPoseAuthoring authoring)
    {
        var pose = (HandPose)poseProperty.objectReferenceValue;
        var side = (HandSide)handednessProperty.enumValueIndex;

        if (mirrorSourceProperty.objectReferenceValue == null)
        {
            var provider = authoring.GetComponent<HandPoseProvider>();

            if (provider != null)
            {
                HandPose other = provider.GetPose(HandJoints.Opposite(side), supportHandProperty.boolValue);

                if (other != null && other != pose)
                    mirrorSourceProperty.objectReferenceValue = other;
            }
        }

        EditorGUILayout.PropertyField(mirrorSourceProperty, new GUIContent("Pose tay kia"));

        var source = (HandPose)mirrorSourceProperty.objectReferenceValue;

        using (new EditorGUI.DisabledScope(pose == null || source == null || source == pose))
        {
            if (GUILayout.Button("Lật gương từ pose tay kia"))
                Mirror(authoring, pose, source, side);
        }

        EditorGUILayout.HelpBox(
            "Phép lật chép THẲNG góc xoay (hai model tay dùng chung bind pose). Công cụ tự kiểm " +
            "tra giả định đó và cảnh báo nếu sai. Lật xong vẫn phải chỉnh lại cho khớp vật.",
            MessageType.None);
    }

    private void Mirror(HandPoseAuthoring authoring, HandPose pose, HandPose source, HandSide side)
    {
        bool ok = HandPoseBindPoseCheck.Check(out string report, out float maxAngle);

        if (!ok)
        {
            bool proceed = EditorUtility.DisplayDialog(
                "Bind pose hai tay không khớp",
                report + $"\n\nLệch lớn nhất {maxAngle:0.##}°. Lật gương bằng cách chép thẳng góc " +
                "xoay sẽ cho ra dáng méo.\n\nVẫn lật?",
                "Vẫn lật", "Thôi");

            if (!proceed)
                return;
        }

        Undo.RecordObject(pose, "Lật gương dáng tay");
        pose.MirrorFrom(source);
        pose.Handedness = side;
        pose.SupportHand = supportHandProperty.boolValue;

        EditorUtility.SetDirty(pose);
        AssetDatabase.SaveAssets();

        HandSkeleton skeleton = GetPreviewSkeleton(authoring, side);

        if (skeleton != null)
        {
            skeleton.ApplyLocalRotations(CapturePoseRotations(pose));
            SceneView.RepaintAll();
        }
    }
}
