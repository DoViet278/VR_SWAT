using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VRSwat.Doors
{
    /// <summary>
    /// Panel chọn cách mở cửa, kiểu menu cửa của SWAT 4: một cột các dòng, dòng đang trỏ tới thì
    /// sáng lên.
    ///
    /// Chỉ là phần VẼ. Ai mở, chọn dòng nào, chọn xong làm gì là việc của
    /// <see cref="DoorActionMenuInput"/>.
    ///
    /// Đặt MỘT cái duy nhất trong scene, không phải mỗi cửa một cái: <see cref="Show"/> tự dời
    /// panel tới chỗ được truyền vào. Giống cách <see cref="UnlockProgressBar"/> đang làm.
    ///
    /// Panel ĐỨNG YÊN trong lúc đang mở — vị trí và hướng chốt lại ngay lúc hiện ra. Nếu nó cứ
    /// bám theo đầu thì dòng đang trỏ tới nhảy lung tung mỗi khi người chơi ngoái cổ.
    ///
    /// Không dùng Button của uGUI và không cần EventSystem: cách chọn ở đây là giữ nút rồi kéo
    /// tay, không phải bấm. <see cref="TryPick"/> tự chiếu tia tay lên mặt phẳng panel.
    ///
    /// Dựng panel bằng menu Tools &gt; VR SWAT &gt; Create Door Action Menu, đừng dựng tay:
    /// Canvas phải World Space và phải thu nhỏ cỡ 0.001, không thì panel to bằng cả căn phòng.
    /// </summary>
    [AddComponentMenu("VR SWAT/Door Action Menu View")]
    [DisallowMultipleComponent]
    public class DoorActionMenuView : MonoBehaviour
    {
        /// <summary>Một dòng trên panel.</summary>
        public struct Entry
        {
            public string Label;
            public Sprite Icon;
            public Color Tint;

            /// <summary>Dùng được ngay bây giờ. False thì dòng xám lại và chọn không ăn.</summary>
            public bool Usable;
        }

        [Header("Thành phần")]
        [Tooltip("Canvas để bật/tắt. Để trống thì tự tìm trong object này và các con.")]
        [SerializeField] private Canvas canvas;

        [SerializeField] private CanvasGroup canvasGroup;

        [Tooltip("Object chứa các dòng, phải có Vertical Layout Group. Các dòng do script tự sinh.")]
        [SerializeField] private RectTransform rowsRoot;

        [Tooltip("Một dòng mẫu, nằm sẵn trong Rows Root và đang tắt. Script nhân bản nó ra.")]
        [SerializeField] private DoorActionMenuRow rowTemplate;

        [Tooltip("Dòng tiêu đề trên cùng, hiện tên cửa / trạng thái. Không bắt buộc.")]
        [SerializeField] private TMP_Text title;

        [Header("Màu")]
        [SerializeField] private Color rowColor = new Color(0.05f, 0.06f, 0.06f, 0.85f);
        [SerializeField] private Color rowHighlightColor = new Color(0.95f, 0.78f, 0.17f, 0.95f);
        [SerializeField] private Color textColor = new Color(0.86f, 0.89f, 0.85f);
        [SerializeField] private Color textHighlightColor = new Color(0.08f, 0.08f, 0.05f);
        [SerializeField] private Color disabledColor = new Color(0.42f, 0.45f, 0.42f);

        [Header("Cỡ trong tầm nhìn")]
        [Tooltip("Thu nhỏ panel khi nó ở gần mắt, để lúc nào cũng nhìn được trọn panel. " +
                 "Panel đứng cạnh ổ khóa, mà lúc mở cửa thì người chơi đứng sát — không co lại thì " +
                 "nó chàn hết tầm nhìn.")]
        [SerializeField] private bool fitToView = true;

        [Tooltip("Bề ngang panel được phép chiếm tối đa bao nhiêu độ trong tầm nhìn. 0 = không " +
                 "giới hạn. Đây mới là thứ quyết định panel TRÔNG to hay nhỏ — đổi khoảng cách " +
                 "đặt panel không làm nó nhỏ đi, vì panel tự co giãn cho vừa đúng góc này.")]
        [SerializeField, Range(0f, 120f)] private float maxWidthDegrees = 21f;

        [Tooltip("Chiều cao panel được phép chiếm tối đa bao nhiêu độ. Để nhỏ hơn bề ngang vì " +
                 "tầm nhìn dọc hẹp hơn tầm nhìn ngang. 0 = không giới hạn.")]
        [SerializeField, Range(0f, 120f)] private float maxHeightDegrees = 16f;

        [Tooltip("Thu nhỏ nhiết lắm cũng chỉ tới mức này so với cỡ đã dựng, kỏeo chữ nhỏ không đọc nổi.")]
        [SerializeField, Range(0.05f, 1f)] private float minScale = 0.15f;

        [Header("Chọn dòng")]
        [Tooltip("Tia tay lệch ra ngoài panel bao nhiêu dòng thì vẫn tính là đang trỏ vào dòng " +
                 "gần nhất. Lệch xa hơn thì không dòng nào được chọn — nhả nút lúc đó là hủy.")]
        [SerializeField] private float forgivenessRows = 1.5f;

        [Header("Hiện ra")]
        [Tooltip("Tốc độ hiện ra / mờ đi. 0 = bật tắt ngay.")]
        [SerializeField] private float fadeSpeed = 14f;

        [Tooltip("Bật nếu chữ trên panel bị lộn ngược như soi gương: nghĩa là đang nhìn vào lưng " +
                 "Canvas, tick vào đây để quay panel lại.")]
        [SerializeField] private bool flipFacing;

        /// <summary>
        /// Kho dòng. Mở menu lần sau cần ít dòng hơn thì chỉ tắt bớt chứ không hủy, nên mỗi lần
        /// mở panel không sinh rác. <see cref="rowCount"/> là số dòng đang thật sự dùng.
        /// </summary>
        private readonly List<DoorActionMenuRow> rows = new List<DoorActionMenuRow>();

        /// <summary>Cỡ panel lúc dựng, làm mốc cho phép thu nhỏ theo khoảng cách.</summary>
        private Vector3 baseScale = Vector3.one;

        private int rowCount;
        private int highlight = -1;
        private bool visible;

        /// <summary>Panel đang hiện.</summary>
        public bool IsVisible => visible;

        /// <summary>Dòng đang trỏ tới, hoặc -1 nếu tia tay đang chĩa ra ngoài panel.</summary>
        public int Highlight => highlight;

        /// <summary>Số dòng đang hiện.</summary>
        public int RowCount => rowCount;

        private void Awake()
        {
            baseScale = transform.localScale;

            if (canvas == null)
                canvas = GetComponentInChildren<Canvas>(true);

            if (canvasGroup == null)
                canvasGroup = GetComponentInChildren<CanvasGroup>(true);

            if (rowTemplate != null)
                rowTemplate.gameObject.SetActive(false);

            SetVisible(false, true);
        }

        private void Update()
        {
            if (canvasGroup == null)
                return;

            float target = visible ? 1f : 0f;

            if (Mathf.Approximately(canvasGroup.alpha, target))
                return;

            canvasGroup.alpha = fadeSpeed > 0f
                ? Mathf.MoveTowards(canvasGroup.alpha, target, fadeSpeed * Time.deltaTime)
                : target;

            if (!visible && canvasGroup.alpha <= 0f && canvas != null)
                canvas.enabled = false;
        }

        /// <summary>
        /// Hiện panel ở <paramref name="worldPoint"/>, quay mặt về <paramref name="head"/>, với
        /// danh sách dòng cho trước. Gọi một lần lúc mở menu — panel đứng yên sau đó.
        /// </summary>
        public void Show(Vector3 worldPoint, Transform head, IReadOnlyList<Entry> entries, string heading)
        {
            BuildRows(entries);

            if (title != null)
            {
                title.gameObject.SetActive(!string.IsNullOrEmpty(heading));
                title.text = heading;
            }

            transform.position = worldPoint;
            FaceHead(head);
            FitToView(head);

            highlight = -1;
            ApplyHighlight();
            SetVisible(true, false);
        }

        public void Hide()
        {
            SetVisible(false, false);
            highlight = -1;
        }

        /// <summary>
        /// Dòng mà tia <paramref name="ray"/> đang trỏ tới. Trả false khi tia chĩa ra ngoài
        /// panel — lúc đó không dòng nào sáng và nhả nút là hủy.
        /// </summary>
        public bool TryPick(Ray ray, out int index)
        {
            index = -1;

            if (!visible || rowCount == 0)
                return false;

            // Mặt phẳng panel. Plane.Raycast ăn cả hai mặt, chỉ đòi điểm cắt nằm phía trước tia.
            Plane plane = new Plane(transform.forward, transform.position);

            if (!plane.Raycast(ray, out float distance))
                return false;

            Vector3 hit = ray.GetPoint(distance);
            float bestDistance = float.MaxValue;
            int best = -1;

            for (int i = 0; i < rowCount; i++)
            {
                RectTransform rect = rows[i].Rect;
                Vector3 local = rect.InverseTransformPoint(hit);
                Rect r = rect.rect;

                // Cố tình BỎ QUA trục X: người chơi chỉ cần canh cao thấp, khỏi phải trỏ chính
                // xác vào bề ngang của dòng.
                if (local.y >= r.yMin && local.y <= r.yMax)
                {
                    index = i;
                    SetHighlight(i);
                    return true;
                }

                float gap = local.y < r.yMin ? r.yMin - local.y : local.y - r.yMax;
                float rows2 = r.height > 0.0001f ? gap / r.height : float.MaxValue;

                if (rows2 < bestDistance)
                {
                    bestDistance = rows2;
                    best = i;
                }
            }

            if (best >= 0 && bestDistance <= forgivenessRows)
            {
                index = best;
                SetHighlight(best);
                return true;
            }

            SetHighlight(-1);
            return false;
        }

        /// <summary>Dòng được sáng lên. -1 = không dòng nào.</summary>
        public void SetHighlight(int index)
        {
            if (index == highlight)
                return;

            highlight = index;
            ApplyHighlight();
        }

        /// <summary>Dòng này có chọn được không (dòng xám thì không).</summary>
        public bool IsUsable(int index)
        {
            return index >= 0 && index < rowCount && rows[index].Usable;
        }

        private void BuildRows(IReadOnlyList<Entry> entries)
        {
            if (rowTemplate == null || rowsRoot == null)
            {
                Debug.LogWarning(
                    $"{name}: thiếu Rows Root hoặc Row Template nên panel không vẽ được dòng " +
                    "nào. Dựng lại bằng Tools > VR SWAT > Create Door Action Menu.", this);
                return;
            }

            int wanted = entries != null ? entries.Count : 0;

            // Thừa thì tắt bớt, thiếu thì nhân bản thêm — không Destroy để khỏi sinh rác mỗi lần
            // mở menu.
            while (rows.Count < wanted)
            {
                DoorActionMenuRow row = Instantiate(rowTemplate, rowsRoot);
                row.name = $"Row {rows.Count}";
                rows.Add(row);
            }

            for (int i = 0; i < rows.Count; i++)
            {
                if (i >= wanted)
                {
                    rows[i].gameObject.SetActive(false);
                    continue;
                }

                rows[i].gameObject.SetActive(true);
                rows[i].Bind(entries[i], textColor, disabledColor);
            }

            rowCount = wanted;
            LayoutRebuilder.ForceRebuildLayoutImmediate(rowsRoot);
        }

        private void ApplyHighlight()
        {
            for (int i = 0; i < rowCount; i++)
            {
                bool on = i == highlight;
                rows[i].SetHighlighted(on,
                    on ? rowHighlightColor : rowColor,
                    on ? textHighlightColor : rows[i].Usable ? textColor : disabledColor);
            }
        }

        /// <summary>
        /// Thu panel lại cho vừa tầm nhìn. Giữ nguyên CHỖ đẩy panel đẩng — đẩy nó ra xa cho
        /// đỡ to thì nó chóui vào trong cánh cửa và bị che mất.
        ///
        /// Đo bằng góc nhìn chứ không bằng mét: cùng một panel ở 0.3 m và ở 1 m chiếm phần tầm
        /// nhìn khác hẳn nhau. Chỉ THU NHỎ, không bao giờ phóng to quá cỡ đã dựng sẵn.
        /// </summary>
        private void FitToView(Transform head)
        {
            transform.localScale = baseScale;

            if (!fitToView)
                return;

            if (head == null)
            {
                Camera cam = Camera.main;
                head = cam != null ? cam.transform : null;
            }

            if (head == null)
                return;

            RectTransform rect = canvas != null
                ? canvas.transform as RectTransform
                : transform as RectTransform;

            if (rect == null)
                return;

            // Số dòng vừa đổi trong BuildRows nên layout chưa cập nhật. Không ép dựng lại thì đo
            // ra cỡ của lần mở trước.
            if (canvas != null)
                canvas.enabled = true;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

            float distance = Vector3.Distance(transform.position, head.position);

            if (distance < 0.01f)
                return;

            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);

            float width = Vector3.Distance(corners[0], corners[3]);
            float height = Vector3.Distance(corners[0], corners[1]);

            if (width <= 0.0001f || height <= 0.0001f)
                return;

            float k = 1f;

            if (maxWidthDegrees > 0f)
                k = Mathf.Min(k, SpanAt(distance, maxWidthDegrees) / width);

            if (maxHeightDegrees > 0f)
                k = Mathf.Min(k, SpanAt(distance, maxHeightDegrees) / height);

            transform.localScale = baseScale * Mathf.Clamp(k, minScale, 1f);
        }

        /// <summary>Bề ngang thật của một góc nhìn ở khoảng cách cho trước.</summary>
        private static float SpanAt(float distance, float degrees)
        {
            return 2f * distance * Mathf.Tan(degrees * 0.5f * Mathf.Deg2Rad);
        }

        private void FaceHead(Transform head)
        {
            if (head == null)
            {
                Camera cam = Camera.main;
                head = cam != null ? cam.transform : null;
            }

            if (head == null)
                return;

            // Mặt đọc được của Canvas là mặt -Z, nên +Z phải chĩa RA XA đầu người chơi.
            Vector3 away = transform.position - head.position;

            if (away.sqrMagnitude < 0.0001f)
                return;

            transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);

            if (flipFacing)
                transform.Rotate(0f, 180f, 0f, Space.Self);
        }

        private void SetVisible(bool on, bool instant)
        {
            visible = on;

            if (canvas != null && on)
                canvas.enabled = true;

            if (canvasGroup == null)
                return;

            if (instant || fadeSpeed <= 0f)
            {
                canvasGroup.alpha = on ? 1f : 0f;

                if (!on && canvas != null)
                    canvas.enabled = false;
            }
        }
    }
}
