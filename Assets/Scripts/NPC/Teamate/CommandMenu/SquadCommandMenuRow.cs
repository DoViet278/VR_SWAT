using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VRSwat.Squad
{
    /// <summary>
    /// Một dòng của <see cref="SquadCommandMenuView"/>: nền, ô vuông màu bên trái, và chữ.
    ///
    /// Tách thành component riêng để view khỏi phải đi tìm con theo tên — dựng panel bằng
    /// Tools &gt; VR SWAT &gt; Teammate &gt; Create Squad Command Menu thì ba tham chiếu này được
    /// gán sẵn.
    /// </summary>
    [AddComponentMenu("VR SWAT/Squad Command Menu Row")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class SquadCommandMenuRow : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private Image marker;
        [SerializeField] private TMP_Text label;

        private RectTransform rect;

        /// <summary>Màu vạch bên trái lúc dòng không sáng, nhớ lại từ <see cref="Bind"/>.</summary>
        private Color markerColor;

        public RectTransform Rect => rect != null ? rect : rect = (RectTransform)transform;

        /// <summary>Dòng này chọn được không. Dòng xám thì nhả nút cũng không ăn.</summary>
        public bool Usable { get; private set; }

        public void Bind(SquadCommandMenuView.Entry entry, Color normalText, Color disabledText)
        {
            Usable = entry.Usable;
            markerColor = entry.Usable ? entry.Tint : disabledText;

            if (label != null)
            {
                label.text = entry.Label;
                label.color = entry.Usable ? normalText : disabledText;
            }

            if (marker != null)
            {
                marker.sprite = entry.Icon;
                marker.color = markerColor;
            }
        }

        /// <summary>
        /// Dòng sáng lên hay tối đi. Vạch bên trái cũng đổi theo: nằm trên nền sáng thì nó phải
        /// sáng hơn nữa, không thì vạch chìm nghỉm vào nền.
        /// </summary>
        public void SetHighlighted(bool on, Color bg, Color text, Color markerOn)
        {
            if (background != null)
                background.color = bg;

            if (label != null)
                label.color = text;

            if (marker != null)
                marker.color = on ? markerOn : markerColor;
        }
    }
}
