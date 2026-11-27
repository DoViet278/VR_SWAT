using System.Collections.Generic;
using UnityEngine;

namespace VRSwat.Tutorial
{
    // Create > VRSwat > Tutorial Lines. Một asset chứa mọi câu thoại của tutorial.
    [CreateAssetMenu(menuName = "VRSwat/Tutorial Lines")]
    public class TutorialLines : ScriptableObject
    {
        [System.Serializable]
        public class Line
        {
            public string key;                 // giá trị const trong CONST.TutorialLine, ví dụ r9_intro
            [TextArea(2, 4)] public string text;
            public AudioClip clip;             // trống = tự tìm Resources/Voice/<key>
        }

        public Line[] lines;

#if UNITY_EDITOR
        // Chuột phải tên component trong Inspector > Thêm key còn thiếu.
        // Tạo sẵn một dòng (Key đã điền, Text trống) cho mỗi const trong CONST.TutorialLine chưa có trong asset.
        [ContextMenu("Thêm key còn thiếu")]
        void AddMissingKeys()
        {
            var list = new List<Line>(lines ?? new Line[0]);
            var have = new HashSet<string>();
            foreach (var l in list) have.Add(l.key);

            foreach (var k in CONST.TutorialLine.All())
                if (have.Add(k))
                {
                    list.Add(new Line { key = k });
                    Debug.Log($"[TutorialLines] Thêm key: {k}");
                }

            lines = list.ToArray();
            UnityEditor.EditorUtility.SetDirty(this);
        }
        // Chuột phải > Tìm clip theo tên file. Gán AudioClip có tên đúng bằng key cho các dòng chưa có clip.
        [ContextMenu("Tìm clip theo tên file")]
        void FindClips()
        {
            if (lines == null) return;
            int found = 0;
            foreach (var l in lines)
            {
                if (string.IsNullOrEmpty(l.key) || l.clip) continue;
                foreach (var guid in UnityEditor.AssetDatabase.FindAssets(l.key + " t:AudioClip"))
                {
                    var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                    if (clip && string.Equals(clip.name, l.key, System.StringComparison.OrdinalIgnoreCase)) { l.clip = clip; found++; break; }
                }
            }
            Debug.Log($"[TutorialLines] Gán clip cho {found} câu");
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}