using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VRSwat.Doors
{
    /// <summary>
    /// Thanh tiến trình nổi trong không gian, dùng cho <see cref="LockpickKit"/>.
    ///
    /// Bật/tắt bằng <c>Canvas.enabled</c> chứ không <c>SetActive</c> cả object: component này
    /// nằm ngay trên object chứa Canvas, tắt object là tắt luôn chính nó, hết đường bật lại.
    ///
    /// Object này KHÔNG làm con của cửa hay của kit. Mỗi frame <see cref="Show"/> tự dời nó tới
    /// mốc được truyền vào, nên một thanh dùng chung cho mọi cửa trong scene.
    /// </summary>
    [AddComponentMenu("VR SWAT/Unlock Progress Bar")]
    [DisallowMultipleComponent]
    public class UnlockProgressBar : MonoBehaviour
    {
        [Header("Thành phần")]
        [Tooltip("Canvas để bật/tắt. Để trống thì tự tìm trong object này và các con.")]
        [SerializeField] private Canvas canvas;

        [Tooltip("Image kiểu Filled (Fill Method: Horizontal) làm phần đầy của thanh.")]
        [SerializeField] private Image fill;

        [Tooltip("Chữ phần trăm. Không bắt buộc.")]
        [SerializeField] private TMP_Text label;

        [SerializeField] private string labelFormat = "{0:0}%";

        [Header("Vị trí")]
        [Tooltip("Lệch so với mốc, tính theo world space (m). Y dương là nhổng lên trên ổ khóa.")]
        [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.15f, 0f);

        [Tooltip("Luôn quay mặt về camera người chơi.")]
        [SerializeField] private bool faceCamera = true;

        [Tooltip("Độ mượt khi bám theo mốc. 0 = dính cứng, không trễ.")]
        [SerializeField] private float followSpeed = 18f;

        private Camera cam;
        private bool visible;

        private Camera Cam
        {
            get
            {
                if (cam == null || !cam.isActiveAndEnabled)
                    cam = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();

                return cam;
            }
        }

        private void Awake()
        {
            if (canvas == null)
                canvas = GetComponentInChildren<Canvas>(true);

            SetVisible(false);
        }

        /// <summary>Hiện thanh ở mốc <paramref name="anchor"/> với tiến trình 0..1.</summary>
        public void Show(Transform anchor, float progress01)
        {
            if (anchor == null)
                return;

            progress01 = Mathf.Clamp01(progress01);
            Vector3 wanted = anchor.position + worldOffset;

            if (!visible)
            {
                SetVisible(true);
                transform.position = wanted;
            }
            else
            {
                transform.position = followSpeed > 0f
                    ? Vector3.Lerp(transform.position, wanted,
                        1f - Mathf.Exp(-followSpeed * Time.deltaTime))
                    : wanted;
            }

            if (faceCamera)
            {
                Camera c = Cam;

                if (c != null)
                {
                    Vector3 away = transform.position - c.transform.position;

                    if (away.sqrMagnitude > 0.0001f)
                        transform.rotation = Quaternion.LookRotation(away, Vector3.up);
                }
            }

            if (fill != null)
                fill.fillAmount = progress01;

            if (label != null)
                label.text = string.Format(labelFormat, progress01 * 100f);
        }

        public void Hide() => SetVisible(false);

        private void SetVisible(bool on)
        {
            visible = on;

            if (canvas != null)
                canvas.enabled = on;
        }
    }
}
