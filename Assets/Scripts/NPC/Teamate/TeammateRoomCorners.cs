using UnityEngine;
using VRSwat.Doors;

/// <summary>
/// (Tùy chọn) Thông tin phòng phía sau cửa cho đồng đội. Gắn lên cùng object với DoorController.
///
/// - corners: các góc phòng để chĩa súng khi clear. Không gán thì đồng đội tự dò góc bằng NavMesh (tia quét từ điểm
///   clear, lấy chỗ xa nhất cục bộ = góc). Người vào bên trái (clearLeft) lo các góc bên trái trục cửa -> phòng, người
///   bên phải lo bên phải. Thứ tự quét: từ góc sát tường cửa (góc chết) dần về phía giữa phòng.
/// - roomVolume: hộp bao phòng, dùng cho lệnh soi gương dưới cửa (Mirror): ai đứng trong hộp là "trong phòng".
///   Collider chỉ dùng để đọc kích thước, tự tắt lúc chạy (không chặn đạn / raycast). Không gán thì đếm người trong
///   8 m phía bên kia cửa, có đường NavMesh qua cửa.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(DoorController))]
public class TeammateRoomCorners : MonoBehaviour
{
    [Tooltip("Các điểm góc phòng (đặt ngang sàn hoặc ngang ngực đều được).")]
    public Transform[] corners;

    [Tooltip("Hộp bao phòng phía sau cửa (lệnh soi gương). Collider tự tắt lúc chạy.")]
    public BoxCollider roomVolume;

    private void Awake()
    {
        if (roomVolume != null) roomVolume.enabled = false; // chỉ đọc kích thước, không va chạm
    }

    /// <summary>point nằm trong roomVolume (tính theo transform + center + size, kể cả khi collider đã tắt).</summary>
    public bool Contains(Vector3 point)
    {
        if (roomVolume == null) return false;
        Vector3 local = roomVolume.transform.InverseTransformPoint(point) - roomVolume.center;
        Vector3 half = roomVolume.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y + 1f && Mathf.Abs(local.z) <= half.z;
    }

    private void OnDrawGizmosSelected()
    {
        if (corners != null)
        {
            Gizmos.color = new Color(1f, 0.4f, 0.1f);
            foreach (Transform c in corners)
                if (c != null) Gizmos.DrawWireCube(c.position, Vector3.one * 0.25f);
        }
        if (roomVolume != null)
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            Gizmos.matrix = roomVolume.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(roomVolume.center, roomVolume.size);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
