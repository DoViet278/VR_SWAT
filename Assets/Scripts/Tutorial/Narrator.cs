using System.Collections.Generic;
using UnityEngine;

namespace VRSwat.Tutorial
{
    public class Narrator : MonoBehaviour
    {
        public static Narrator Instance { get; private set; }

        [SerializeField] TutorialLines lines;
        [SerializeField] AudioSource voiceSource;     // 2D: Spatial Blend = 0
        [SerializeField] AudioSource sfxSource;
        [SerializeField] AudioClip stepDoneClip;      // tiếng "ting" khi xong một việc
        [Tooltip("Chưa có file giọng: số ký tự đọc được mỗi giây")]
        [SerializeField] float charsPerSecond = 14f;

        readonly Dictionary<string, TutorialLines.Line> map = new Dictionary<string, TutorialLines.Line>();
        readonly Dictionary<string, AudioClip> clipCache = new Dictionary<string, AudioClip>();
        string lastKey;
        TutorialBoard lastBoard;
        float speakingUntil;
        float holdUntil;                  // đang đọc lời giới thiệu: câu hướng dẫn mới phải xếp hàng
        string pendingKey;                // câu hướng dẫn chờ đọc sau lời giới thiệu (chỉ giữ 1 câu)
        TutorialLines.Line pendingLine;
        TutorialBoard pendingBoard;
        bool hasPending;

        public bool IsSpeaking => Time.time < speakingUntil;

        void Awake()
        {
            Instance = this;
            if (lines == null) Debug.LogError("[Narrator] Chưa gán TutorialLines: bảng sẽ hiện mã câu thay cho chữ", this);
            else
                foreach (var l in lines.lines)
                    if (!string.IsNullOrEmpty(l.key)) map[l.key] = l;
            // Thiếu AudioSource thì trước đây Play() lỗi NullReference và kịch bản phòng chết ngay ở câu nói đầu tiên
            if (voiceSource == null) Debug.LogWarning("[Narrator] Chưa gán Voice Source: chỉ hiện chữ, không phát giọng", this);
        }

        // Quy tắc: chữ trên bảng luôn khớp với giọng đang đọc. Chữ của một câu chỉ hiện khi giọng câu đó bắt đầu.
        // Câu hướng dẫn chính: đổi chữ + phát giọng cùng lúc, cắt câu đang đọc.
        // Đang đọc lời giới thiệu thì câu hướng dẫn đầu tiên xếp hàng (bảng vẫn giữ chữ giới thiệu, người chơi vẫn đi lại được).
        // Nếu trong lúc đó lại có câu mới (người chơi đã làm xong bước đang chờ, ví dụ tới khu vực cửa)
        // thì ghi đè: bỏ câu chờ, cắt giới thiệu, đổi chữ + giọng câu mới ngay.
        public void Say(string key, TutorialBoard board)
        {
            lastKey = key;
            lastBoard = board;
            var line = Find(key);
            if (Time.time < holdUntil)
            {
                if (!hasPending)
                {
                    pendingKey = key;
                    pendingLine = line;
                    pendingBoard = board;
                    hasPending = true;
                    return;
                }
                hasPending = false;
                holdUntil = 0f;
            }
            ShowAndPlay(key, line, board);
        }

        // Lời giới thiệu đầu phòng: KHÔNG chặn người chơi, kịch bản phòng chạy tiếp ngay.
        // Câu hướng dẫn kế tiếp chờ giới thiệu đọc xong rồi mới hiện chữ + đọc.
        public void SayIntro(string key, TutorialBoard board)
        {
            hasPending = false;
            lastKey = key;
            lastBoard = board;
            ShowAndPlay(key, Find(key), board);
            holdUntil = speakingUntil;
        }

        void Update()
        {
            if (hasPending && Time.time >= holdUntil)
            {
                hasPending = false;
                holdUntil = 0f;
                ShowAndPlay(pendingKey, pendingLine, pendingBoard);
            }
        }

        void ShowAndPlay(string key, TutorialLines.Line line, TutorialBoard board)
        {
            if (board) board.SetInstruction(line != null ? line.text : key);
            Play(line);
        }

        // Câu chen ngang (phản ứng hành động người chơi): hiện dòng ghi chú vàng + giọng ngay, không thay câu hướng dẫn chính.
        // Nếu chen vào lúc đang giới thiệu thì cắt giới thiệu; câu hướng dẫn đang chờ sẽ đọc (kèm chữ) sau câu chen.
        public void Interject(string key, TutorialBoard board)
        {
            var line = Find(key);
            if (board) board.SetNote(line != null ? line.text : key);
            Play(line);
            holdUntil = hasPending ? speakingUntil : 0f;
        }

        public void Replay()
        {
            if (lastKey != null) Say(lastKey, lastBoard);
        }

        public void PlayStepDone()
        {
            if (sfxSource && stepDoneClip) sfxSource.PlayOneShot(stepDoneClip);
        }

        TutorialLines.Line Find(string key)
        {
            if (map.TryGetValue(key, out var line)) return line;
            Debug.LogWarning($"[Narrator] Thiếu câu thoại: {key}");   // bảng sẽ hiện mã câu
            return null;
        }

        AudioClip ClipFor(TutorialLines.Line line)
        {
            if (line.clip) return line.clip;
            if (!clipCache.TryGetValue(line.key, out var clip))
            {
                clip = Resources.Load<AudioClip>("Voice/" + line.key);   // null nếu chưa có file
                clipCache[line.key] = clip;
            }
            return clip;
        }

        void Play(TutorialLines.Line line)
        {
            if (voiceSource) voiceSource.Stop();
            AudioClip clip = line != null ? ClipFor(line) : null;
            if (clip && voiceSource)
            {
                voiceSource.clip = clip;
                voiceSource.Play();
                speakingUntil = Time.time + clip.length;
            }
            else
            {
                int len = line != null && line.text != null ? line.text.Length : 0;
                speakingUntil = Time.time + Mathf.Max(1.5f, len / charsPerSecond);
            }
        }
    }
}