using System.Collections.Generic;
using UnityEngine;

namespace VRSwat
{
    // Create > VRSwat > Voice Lines. Mỗi asset = thoại của một nhân vật (một class con trong CONST.VoiceLine).
    // CONST chỉ giữ key; chữ và file giọng sửa trong Inspector.
    [CreateAssetMenu(menuName = "VRSwat/Voice Lines")]
    public class VoiceLines : ScriptableObject
    {
        [System.Serializable]
        public class Line
        {
            public string key;                 // giá trị const trong CONST.VoiceLine.<Speaker>, ví dụ ld_OrderedFallIn
            public string category;            // nhóm trong CONST: Announce/Shout, Report...
            [TextArea(2, 4)] public string text;
            public AudioClip[] clips;          // nhiều biến thể cùng một câu: phát ngẫu nhiên 1 clip

            // Chữ hiện kèm luôn là text của dòng này, nên mọi clip trong mảng phải đọc cùng một nội dung.
            public AudioClip PickClip()
            {
                if (clips == null || clips.Length == 0) return null;
                return clips[Random.Range(0, clips.Length)];
            }
        }

        [Tooltip("Tên class con trong CONST.VoiceLine: Player, Officer, Suspect, SuspectAK, SuspectALJ, SuspectLN, Hostage")]
        public string speaker;
        public Line[] lines;

        Dictionary<string, Line> map;

        public Line Get(string key)
        {
            if (map == null)
            {
                map = new Dictionary<string, Line>();
                if (lines != null)
                    foreach (var l in lines)
                        if (!string.IsNullOrEmpty(l.key)) map[l.key] = l;
            }
            if (map.TryGetValue(key, out var line)) return line;
            Debug.LogWarning($"[VoiceLines] {name}: thiếu câu thoại {key}", this);
            return null;
        }

        void OnValidate() { map = null; }   // sửa trong Inspector thì dựng lại bảng tra

#if UNITY_EDITOR
        // Chuột phải tên component trong Inspector > Thêm key còn thiếu.
        // Thêm một dòng (Key đã điền, Text trống) cho mỗi const trong CONST.VoiceLine.<speaker> chưa có trong asset.
        [ContextMenu("Thêm key còn thiếu")]
        void AddMissingKeys()
        {
            var type = typeof(CONST.VoiceLine).GetNestedType(speaker ?? "");
            if (type == null)
            {
                Debug.LogError($"[VoiceLines] Không có CONST.VoiceLine.{speaker}", this);
                return;
            }

            var list = new List<Line>(lines ?? new Line[0]);
            var have = new HashSet<string>();
            foreach (var l in list) have.Add(l.key);

            foreach (var f in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (!f.IsLiteral || f.FieldType != typeof(string)) continue;
                var k = (string)f.GetRawConstantValue();
                if (have.Add(k))
                {
                    list.Add(new Line { key = k });
                    Debug.Log($"[VoiceLines] Thêm key: {k}");
                }
            }

            lines = list.ToArray();
            UnityEditor.EditorUtility.SetDirty(this);
        }

        // Chuột phải > Tìm clip theo tên file. Gán mọi AudioClip có tên = key hoặc key + số (key_1, key2...).
        // Không lấy key_m09: đó là câu khác có key riêng. Dòng đã có clip thì giữ nguyên.
        [ContextMenu("Tìm clip theo tên file")]
        void FindClips()
        {
            if (lines == null) return;
            int found = 0;
            foreach (var l in lines)
            {
                if (string.IsNullOrEmpty(l.key) || (l.clips != null && l.clips.Length > 0)) continue;
                var clips = new List<AudioClip>();
                foreach (var guid in UnityEditor.AssetDatabase.FindAssets(l.key + " t:AudioClip"))
                {
                    var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                    if (clip && IsVariantOf(clip.name, l.key)) clips.Add(clip);
                }
                if (clips.Count == 0) continue;
                clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                l.clips = clips.ToArray();
                found++;
            }
            Debug.Log($"[VoiceLines] {name}: gán clip cho {found} câu");
            UnityEditor.EditorUtility.SetDirty(this);
        }

        static bool IsVariantOf(string clipName, string key)
        {
            if (!clipName.StartsWith(key, System.StringComparison.OrdinalIgnoreCase)) return false;
            var rest = clipName.Substring(key.Length).TrimStart('_');
            foreach (var c in rest) if (!char.IsDigit(c)) return false;
            return true;
        }
#endif
    }
}
