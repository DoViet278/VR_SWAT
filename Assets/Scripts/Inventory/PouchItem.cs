using UnityEngine;

/// <summary>
/// Dán lên prefab vật phẩm để nói với <see cref="ItemPouch"/> hai chuyện:
///
/// 1. Vật này nhét được vào túi đồ (kể cả khi nó không phải súng hay băng đạn — ví dụ lựu đạn,
///    bộ mở khoá). Túi vẫn nhận súng/băng đạn mà không cần component này.
/// 2. Nằm trong ô túi thì xoay/dịch thế nào. Mỗi vật có hình dáng và Attach Transform riêng
///    (Attach Transform được chỉnh cho hand pose, không phải cho cái túi), nên cùng một ô mà
///    khẩu rifle với băng đạn lại cần hai tư thế khác nhau.
///
/// Để tư thế nằm trên chính vật phẩm (thay vì trên từng ô túi) thì vật nhét vào ô nào cũng đứng
/// đúng, kể cả ô mà người chơi tự chọn khi nhặt được hàng ở ngoài.
/// </summary>
public class PouchItem : MonoBehaviour
{
    [Header("Tư thế trong ô túi")]
    [Tooltip("Bật để dùng hai giá trị bên dưới. Tắt thì vật dùng tư thế mặc định của ô " +
             "(Slot Local Euler / Slot Euler Overrides trên ItemPouch).")]
    [SerializeField] private bool overrideSocketPose = false;

    [Tooltip("Dịch vị trí so với tâm ô, tính theo trục của ô (mét).")]
    [SerializeField] private Vector3 positionOffset = Vector3.zero;

    [Tooltip("Xoay thêm so với hướng của ô (độ).")]
    [SerializeField] private Vector3 eulerOffset = Vector3.zero;

    public bool OverrideSocketPose => overrideSocketPose;

    public Vector3 PositionOffset => positionOffset;

    public Quaternion RotationOffset => Quaternion.Euler(eulerOffset);
}
