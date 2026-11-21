using UnityEngine;

namespace VRSwat.Doors
{
    public class DoorBreachable : MonoBehaviour
    {
        [SerializeField] DoorController door;
        [SerializeField] float maxDistance = 3f;   // mét từ nòng súng
        [SerializeField] int pelletsNeeded = 3;    // số viên trúng tối thiểu trong 1 phát

        void Awake()
        {
            if (!door) door = GetComponentInParent<DoorController>();
        }

        // Gọi 1 lần cho mỗi phát bắn, sau khi súng đã đếm số viên trúng vùng này
        public bool TryBreach(Vector3 muzzlePos, Vector3 averageHitPoint, int pelletHits)
        {
            if (pelletHits < pelletsNeeded) return false;
            if (Vector3.Distance(muzzlePos, averageHitPoint) > maxDistance) return false;
            return door.Breach(averageHitPoint, muzzlePos);   // cửa văng ra xa người bắn
        }
    }
}