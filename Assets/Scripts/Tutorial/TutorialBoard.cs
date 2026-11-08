using TMPro;
using UnityEngine;

namespace VRSwat.Tutorial
{
    // Canvas World Space gắn cố định trên tường, ngang tầm mắt. Không dính vào đầu.
    public class TutorialBoard : MonoBehaviour
    {
        [SerializeField] TMP_Text titleText;
        [SerializeField] TMP_Text instructionText;
        [SerializeField] TMP_Text progressText;
        [SerializeField] TMP_Text noteText;
        [SerializeField] float noteSeconds = 4f;

        float noteUntil;

        public void SetTitle(string s) => Set(titleText, s);
        public void SetInstruction(string s) => Set(instructionText, s);
        public void SetProgress(string s) => Set(progressText, s);

        public void SetNote(string s)
        {
            Set(noteText, s);
            noteUntil = Time.time + noteSeconds;
            enabled = true;
        }

        // Nối vào OnClick của nút "Nghe lại" trên bảng
        public void ReplayVoice() => Narrator.Instance.Replay();

        void Update()
        {
            if (Time.time < noteUntil) return;
            Set(noteText, null);
            enabled = false;   // chỉ chạy Update khi đang hiện ghi chú
        }

        static void Set(TMP_Text t, string s)
        {
            if (!t) return;
            bool show = !string.IsNullOrEmpty(s);
            if (t.gameObject.activeSelf != show) t.gameObject.SetActive(show);
            if (show && t.text != s) t.text = s;   // chỉ đổi khi khác, tránh rebuild Canvas
        }
    }
}