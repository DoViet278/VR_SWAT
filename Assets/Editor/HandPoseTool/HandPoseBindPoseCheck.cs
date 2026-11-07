using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Kiểm tra giả định của phép lật gương.
///
/// <see cref="HandPose.MirrorFrom"/> chép THẲNG góc xoay local từ pose tay này sang tay kia,
/// không lật dấu trục nào. Chỉ đúng khi hai model tay dùng chung một bind pose, nghĩa là xương
/// cùng tên ở hai bên có cùng góc xoay local lúc chưa animate - đúng với bộ tay Oculus, vì tay
/// phải là bản soi gương của tay trái ở mức mesh chứ không phải ở mức xương.
///
/// Đổi sang model tay khác thì giả định có thể sai, và lúc đó pose lật gương ra sẽ méo mà không
/// ai biết vì sao. Nên công cụ tự chạy phép kiểm tra này mỗi lần lật, và có cả menu để chạy tay:
/// Tools &gt; VR SWAT &gt; Kiểm tra bind pose hai tay.
///
/// Đọc thẳng prefab model tay (không nhân bản vào scene) nên số đo là bind pose thật, không bị
/// Animator của scene làm nhiễu.
/// </summary>
public static class HandPoseBindPoseCheck
{
    /// <summary>Lệch tới mức này (độ) thì coi như hai bên vẫn chung bind pose.</summary>
    public const float ToleranceDegrees = 1f;

    [MenuItem("Tools/VR SWAT/Kiểm tra bind pose hai tay")]
    private static void RunFromMenu()
    {
        bool ok = Check(out string report, out float maxAngle);

        EditorUtility.DisplayDialog(
            "Bind pose hai tay",
            report + "\n\n" + (ok
                ? $"Lệch lớn nhất {maxAngle:0.##}° - dưới ngưỡng {ToleranceDegrees}°, lật gương " +
                  "bằng cách chép thẳng góc xoay là đúng."
                : "Lật gương bằng cách chép thẳng góc xoay sẽ KHÔNG đúng với bộ tay này."),
            "OK");
    }

    /// <summary>
    /// Trả về true nếu hai model tay chung bind pose (hoặc nếu không kiểm tra được - lúc đó
    /// <paramref name="report"/> nói rõ là không kiểm tra được chứ không phải đã đạt).
    /// </summary>
    public static bool Check(out string report, out float maxAngle)
    {
        maxAngle = 0f;
        var text = new StringBuilder();

        if (!TryGetHandModelPrefab(HandSide.Left, out GameObject leftPrefab, out string leftSource) ||
            !TryGetHandModelPrefab(HandSide.Right, out GameObject rightPrefab, out string rightSource))
        {
            report = "Không tìm thấy đủ prefab model tay cho hai bên nên không kiểm tra được. " +
                     "Chạy Setup Hand Pose rồi Căn cả hai tay trước.";
            return true;
        }

        text.AppendLine("Tay trái: " + leftSource);
        text.AppendLine("Tay phải: " + rightSource);

        Transform[] left = HandSkeleton.ResolveJoints(leftPrefab.transform, HandSide.Left);
        Transform[] right = HandSkeleton.ResolveJoints(rightPrefab.transform, HandSide.Right);

        var worst = new List<string>();
        int compared = 0;

        for (int i = 0; i < HandJoints.Count; i++)
        {
            if (left[i] == null || right[i] == null)
            {
                worst.Add($"  {HandJoints.Label((HandJointId)i)}: thiếu xương " +
                          (left[i] == null ? "bên trái" : "bên phải"));
                continue;
            }

            compared++;

            float angle = Quaternion.Angle(left[i].localRotation, right[i].localRotation);

            if (angle > maxAngle)
                maxAngle = angle;

            if (angle > ToleranceDegrees)
                worst.Add($"  {HandJoints.Label((HandJointId)i)}: lệch {angle:0.##}°");
        }

        if (compared == 0)
        {
            report = text + "\nKhông dò được xương nào để so sánh.";
            return true;
        }

        text.AppendLine($"So sánh {compared}/{HandJoints.Count} khớp.");

        if (worst.Count == 0)
            return Done(text, out report, true);

        text.AppendLine();
        text.AppendLine("Khớp lệch:");

        foreach (string line in worst)
            text.AppendLine(line);

        return Done(text, out report, false);
    }

    private static bool Done(StringBuilder text, out string report, bool ok)
    {
        report = text.ToString().TrimEnd();
        return ok;
    }

    /// <summary>
    /// Prefab model tay của một bên: lấy từ asset offset trước, rồi tới scene đang mở, cuối cùng
    /// mới quét prefab trong project.
    /// </summary>
    public static bool TryGetHandModelPrefab(HandSide side, out GameObject prefab, out string source)
    {
        var offsets = AssetDatabase.LoadAssetAtPath<HandPoseRigOffsets>(HandPoseRigOffsets.AssetPath);

        if (offsets != null && offsets.Get(side).handModelPrefab != null)
        {
            prefab = offsets.Get(side).handModelPrefab;
            source = prefab.name + " (từ HandPoseRigOffsets)";
            return true;
        }

        HandSkeleton skeleton = HandPoseRigOffsets.FindSkeleton(side);

        if (skeleton != null)
        {
            GameObject fromScene = HandPoseRigOffsets.ResolvePrefab(skeleton.gameObject);

            if (fromScene != null)
            {
                prefab = fromScene;
                source = prefab.name + " (prefab của tay trong scene)";
                return true;
            }
        }

        foreach (string guid in AssetDatabase.FindAssets("Hand t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var candidate = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (candidate == null || HandSkeleton.ResolveWrist(candidate.transform, side) == null)
                continue;

            prefab = candidate;
            source = prefab.name + " (quét project)";
            return true;
        }

        prefab = null;
        source = null;
        return false;
    }
}
