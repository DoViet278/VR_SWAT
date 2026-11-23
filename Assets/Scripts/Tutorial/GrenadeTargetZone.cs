using UnityEngine;

namespace VRSwat.Tutorial
{
    // Vòng tròn mục tiêu trên sàn cho Phòng 4.
    // Đặt object ở tâm vòng, trục Y (xanh lá) hướng lên. Lựu đạn nổ trong vòng thì tính là trúng.
    public class GrenadeTargetZone : MonoBehaviour
    {
        [Tooltip("Bán kính vòng (m), đo trên mặt sàn")]
        public float radius = 1.5f;
        [Tooltip("Lựu đạn nổ cao hơn / thấp hơn tâm vòng quá mức này thì không tính")]
        public float heightTolerance = 1f;

        [Header("Hiển thị (tùy chọn)")]
        [Tooltip("Vòng đang chờ ném, ví dụ vòng đỏ / vàng. Tắt đi khi trúng")]
        public GameObject pendingVisual;
        [Tooltip("Vòng đã hoàn thành, ví dụ vòng xanh. Tắt sẵn, bật lên khi trúng")]
        public GameObject doneVisual;

        public bool IsDone { get; private set; }

        public bool Contains(Vector3 worldPos)
        {
            Vector3 d = worldPos - transform.position;
            float h = Vector3.Dot(d, transform.up);
            Vector3 flat = d - transform.up * h;
            return flat.magnitude <= radius && Mathf.Abs(h) <= heightTolerance;
        }

        public void ResetZone()
        {
            IsDone = false;
            if (pendingVisual) pendingVisual.SetActive(true);
            if (doneVisual) doneVisual.SetActive(false);
        }

        public void MarkDone()
        {
            IsDone = true;
            if (pendingVisual) pendingVisual.SetActive(false);
            if (doneVisual) doneVisual.SetActive(true);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = IsDone ? Color.green : new Color(1f, 0.6f, 0f);
            const int seg = 48;
            Vector3 prev = transform.position + transform.right * radius;
            for (int i = 1; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                Vector3 p = transform.position + (transform.right * Mathf.Cos(a) + transform.forward * Mathf.Sin(a)) * radius;
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
            Gizmos.DrawLine(transform.position - transform.up * heightTolerance, transform.position + transform.up * heightTolerance);
        }
    }
}
