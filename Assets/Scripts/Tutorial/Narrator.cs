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

        public bool IsSpeaking => Time.time < speakingUntil;

        void Awake()
        {
            Instance = this;
            foreach (var l in lines.lines)
                if (!string.IsNullOrEmpty(l.key)) map[l.key] = l;
        }

        // Câu hướng dẫn chính: thay chữ trên bảng + phát giọng
        public void Say(string key, TutorialBoard board)
        {
            lastKey = key;
            lastBoard = board;
            var line = Find(key);
            if (board) board.SetInstruction(line != null ? line.text : key);
            Play(line);
        }

        // Câu chen ngang: hiện dòng ghi chú vàng, không thay câu hướng dẫn chính
        public void Interject(string key, TutorialBoard board)
        {
            var line = Find(key);
            if (board) board.SetNote(line != null ? line.text : key);
            Play(line);
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
            voiceSource.Stop();
            AudioClip clip = line != null ? ClipFor(line) : null;
            if (clip)
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