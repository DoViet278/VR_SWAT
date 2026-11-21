using UnityEngine;

namespace VRSwat.Tutorial
{
    public class TutorialArea : MonoBehaviour
    {
        public Vector3 size = new Vector3(2f, 3f, 2f);   // tâm hộp = vị trí object

        public bool Contains(Vector3 worldPos)
        {
            Vector3 p = transform.InverseTransformPoint(worldPos);
            Vector3 h = size * 0.5f;
            return Mathf.Abs(p.x) <= h.x && Mathf.Abs(p.y) <= h.y && Mathf.Abs(p.z) <= h.z;
        }

        void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0f, 1f, 0.5f, 0.15f);
            Gizmos.DrawCube(Vector3.zero, size);
            Gizmos.color = new Color(0f, 1f, 0.5f, 0.8f);
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }
}