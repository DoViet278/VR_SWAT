using UnityEngine;

/// <summary>
/// Component TẠM, chỉ dùng lúc thiết kế dáng tay. Gỡ khỏi vật trước khi build.
///
/// Bản thân nó không làm gì lúc chơi - toàn bộ công cụ nằm trong
/// <c>Assets/Editor/HandPoseTool/HandPoseAuthoringEditor.cs</c>. Nó ở trong thư mục runtime chỉ
/// vì component phải gắn được lên GameObject của scene, còn class trong thư mục Editor thì
/// không gắn được.
///
/// Quy trình 10 bước nằm ngay trên Inspector của component này.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("VR SWAT/Hand Pose Authoring")]
public class HandPoseAuthoring : MonoBehaviour
{
    [Tooltip("Đang tạo dáng cho tay nào.")]
    public HandSide handedness = HandSide.Right;

    [Tooltip("Dáng này cho bàn tay thứ hai (tay đỡ nòng), không phải tay cầm chính.")]
    public bool supportHand;

    [Tooltip("Asset đang được chỉnh. Bấm Tạo pose asset mới nếu còn trống.")]
    public HandPose pose;

    [Tooltip("Pose của tay kia, dùng cho nút Lật gương.")]
    public HandPose mirrorSource;

    [Tooltip("Độ cong của từng ngón, theo thứ tự HandFinger (cái, trỏ, giữa, áp út, út). " +
             "0 = dáng mở, 1 = dáng nắm.")]
    public float[] curl = new float[5];

    /// <summary>
    /// Bàn tay xem trước đang dựng. Mang <c>HideFlags.DontSaveInEditor</c> nên tham chiếu này
    /// về null sau khi scene được nạp lại - công cụ tìm lại theo tên khi cần.
    /// </summary>
    [HideInInspector] public GameObject previewHand;

    private void OnValidate()
    {
        if (curl == null || curl.Length != 5)
        {
            var resized = new float[5];

            if (curl != null)
            {
                int shared = Mathf.Min(curl.Length, 5);

                for (int i = 0; i < shared; i++)
                    resized[i] = curl[i];
            }

            curl = resized;
        }
    }
}
