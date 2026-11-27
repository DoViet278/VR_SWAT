using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VRSwat.Doors
{
    /// <summary>
    /// Một dòng của <see cref="DoorActionMenuView"/>: nền, ô vuông màu bên trái, và chữ.
    ///
    /// Tách ra thành component riêng để view khỏi phải đi tìm con theo tên — dựng panel bằng
    /// Editor tool thì ba cái tham chiếu này được gán sẵn.
    /// </summary>
    [AddComponentMenu("VR SWAT/Door Action Menu Row")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class DoorActionMenuRow : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private Image marker;
        [SerializeField] private TMP_Text label;

        private RectTransform rect;

        public RectTransform Rect => rect != null ? rect : rect = (RectTransform)transform;

        /// <summary>Dòng này chọn được không. Dòng xám thì nhả nút cũng không ăn.</summary>
        public bool Usable { get; private set; }

        public void Bind(DoorActionMenuView.Entry entry, Color normalText, Color disabledText)
        {
            Usable = entry.Usable;

            if (label != null)
            {
                label.text = entry.Label;
                label.color = entry.Usable ? normalText : disabledText;
            }

            if (marker != null)
            {
                marker.sprite = entry.Icon;
                marker.color = entry.Usable ? entry.Tint : disabledText;
            }
        }

        public void SetHighlighted(bool on, Color bg, Color text)
        {
            if (background != null)
                background.color = bg;

            if (label != null)
                label.color = text;
        }
    }
}
