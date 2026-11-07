using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Gắn component <see cref="PhysicsHands"/> lên XR Origin của scene đang mở.
///
/// Vì sao cần cái tool này thay vì tự kéo tay: scene đang mở trong Editor thì sửa file .unity
/// ngoài đĩa là vô nghĩa (Unity giữ bản trong bộ nhớ và sẽ ghi đè lúc save). Tool chạy ngay trong
/// Editor nên không vướng chuyện đó. Thêm nữa, component thêm trong lúc đang Play sẽ bị xoá khi
/// thoát Play - tool này chặn luôn trường hợp đó.
///
/// Chạy qua menu Tools &gt; VR SWAT &gt; Setup Physics Hands.
/// </summary>
public static class PhysicsHandsSetup
{
    [MenuItem("Tools/VR SWAT/Setup Physics Hands")]
    private static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog(
                "Physics Hands",
                "Đang ở Play mode. Component thêm lúc đang Play sẽ bị xoá khi thoát Play.\n\n" +
                "Thoát Play rồi chạy lại.",
                "OK");
            return;
        }

        var xrOrigin = Object.FindFirstObjectByType<XROrigin>();

        if (xrOrigin == null)
        {
            EditorUtility.DisplayDialog("Physics Hands",
                "Không tìm thấy XR Origin trong scene đang mở.", "OK");
            return;
        }

        GameObject target = xrOrigin.gameObject;

        if (target.GetComponent<PhysicsHands>() != null)
        {
            EditorUtility.DisplayDialog("Physics Hands",
                $"'{target.name}' đã có Physics Hands rồi.", "OK");
            Selection.activeGameObject = target;
            return;
        }

        Undo.AddComponent<PhysicsHands>(target);
        EditorSceneManager.MarkSceneDirty(target.scene);
        Selection.activeGameObject = target;

        int handCount = CountHandBodies(xrOrigin);

        EditorUtility.DisplayDialog(
            "Physics Hands",
            $"Đã gắn Physics Hands vào '{target.name}'.\n\n" +
            $"Tìm thấy {handCount} bàn tay sẽ được chuyển sang vật lý thật.\n\n" +
            (handCount == 0
                ? "0 bàn tay: model tay có thể đang tắt, hoặc được spawn lúc runtime. Gán tay vào " +
                  "field Hand Bodies của component."
                : "Nhớ Ctrl+S để lưu scene, rồi Play lại."),
            "OK");
    }

    /// <summary>Đếm Rigidbody kinematic có collider đặc - đúng điều kiện PhysicsHands tự tìm.</summary>
    private static int CountHandBodies(XROrigin xrOrigin)
    {
        Transform root = xrOrigin.Origin != null ? xrOrigin.Origin.transform : xrOrigin.transform;
        int count = 0;

        foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
        {
            if (!body.isKinematic)
                continue;

            foreach (Collider collider in body.GetComponentsInChildren<Collider>(true))
            {
                if (collider.isTrigger || collider is CharacterController)
                    continue;

                count++;
                break;
            }
        }

        return count;
    }
}
