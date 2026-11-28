using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dựng giọng nói NPC kiểu SWAT 4 (menu Tools/VR SWAT/Voice):
///  1. Create Voice Sets   : tạo các bộ giọng (NPCVoiceSet) theo vai, kèm thư mục để thả clip cho từng câu:
///       Audios/NPC/Teamate/Teammate_Alpha_Voice, Teammate_Bravo_Voice   (Officer)
///       Audios/NPC/Enemy/Enemy_Male_A_Voice, Enemy_Male_B_Voice         (Suspect)
///       Audios/NPC/Hostage/Hostage_Female_Voice                         (Civilian; bộ giọng nam có sẵn được bổ sung câu mới)
///     Thư mục clip: Audios/NPC/&lt;Role&gt;/Lines/&lt;Câu&gt;/ (vd. Teamate/Lines/RoomClear). Thả clip vào rồi chạy bước 3.
///  2. Assign To Prefabs   : gắn NPCVoice (AudioSource "Voice" ở xương đầu) + NPCStun + NPCHearing vào prefab
///       Teamate (bộ Alpha) và Enemy (bộ Male A); Hostage chỉ thêm NPCStun + NPCHearing (đã có NPCVoice).
///  3. Fill Clips From Folders: mỗi bộ giọng lấy clip trong thư mục Lines/&lt;Câu&gt;/ gán cho câu tương ứng.
/// Chạy lại được nhiều lần: không ghi đè bộ giọng / clip đã có.
/// Câu thoại tiếng Anh: tự thu âm / TTS / nguồn miễn phí, không dùng file âm thanh gốc của SWAT 4.
/// </summary>
public static class NPCVoiceSetupTool
{
    private const string Root = "Assets/Audios/NPC";
    private const string TeammatePrefab = "Assets/Prefabs/NPC/Teamate.prefab";
    private const string EnemyPrefab = "Assets/Prefabs/NPC/Enemy.prefab";
    private const string HostagePrefab = "Assets/Prefabs/NPC/Hostage.prefab";
    private const string HostageMaleVoice = Root + "/Hostage/Hostage_Male_Voice.asset";

    private static readonly (string folder, string name, VoiceRole role, VoiceGender gender, float pitch)[] Sets =
    {
        ("Teamate", "Teammate_Alpha_Voice", VoiceRole.Officer, VoiceGender.Male, 1f),
        ("Teamate", "Teammate_Bravo_Voice", VoiceRole.Officer, VoiceGender.Male, 0.94f),
        ("Enemy", "Enemy_Male_A_Voice", VoiceRole.Suspect, VoiceGender.Male, 1f),
        ("Enemy", "Enemy_Male_B_Voice", VoiceRole.Suspect, VoiceGender.Male, 0.92f),
        ("Hostage", "Hostage_Female_Voice", VoiceRole.Civilian, VoiceGender.Female, 1f),
    };

    private static string PathOf(int i) => $"{Root}/{Sets[i].folder}/{Sets[i].name}.asset";

    [MenuItem("Tools/VR SWAT/Voice/Build All")]
    public static void BuildAll()
    {
        CreateVoiceSets();
        FillClipsFromFolders();
        AssignToPrefabs();
    }

    // ---------------- 1. Bộ giọng ----------------

    [MenuItem("Tools/VR SWAT/Voice/1. Create Voice Sets")]
    public static void CreateVoiceSets()
    {
        for (int i = 0; i < Sets.Length; i++)
        {
            var s = Sets[i];
            EnsureFolder($"{Root}/{s.folder}");
            string path = PathOf(i);
            var set = AssetDatabase.LoadAssetAtPath<NPCVoiceSet>(path);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<NPCVoiceSet>();
                set.Setup(s.role, s.gender, s.pitch);
                AssetDatabase.CreateAsset(set, path);
                Debug.Log($"[Voice] Tạo {path}");
            }
            else
            {
                set.EnsureDefaultEntries();
                EditorUtility.SetDirty(set);
            }
            foreach (NPCVoiceSet.Entry e in NPCVoiceSet.DefaultEntries(s.role))
                EnsureFolder($"{Root}/{s.folder}/Lines/{e.Line}");
        }

        // Bộ giọng con tin nam có sẵn (Civilian): bổ sung câu mới (Pleading, Rescued), giữ clip cũ.
        var male = AssetDatabase.LoadAssetAtPath<NPCVoiceSet>(HostageMaleVoice);
        if (male != null)
        {
            male.EnsureDefaultEntries();
            EditorUtility.SetDirty(male);
        }
        AssetDatabase.SaveAssets();
    }

    // ---------------- 2. Prefab ----------------

    [MenuItem("Tools/VR SWAT/Voice/2. Assign To Prefabs")]
    public static void AssignToPrefabs()
    {
        EditPrefab(TeammatePrefab, root =>
        {
            NPCVoice voice = SetupVoice(root, AssetDatabase.LoadAssetAtPath<NPCVoiceSet>(PathOf(0)));
            if (root.TryGetComponent(out Teammate teammate))
            {
                var so = new SerializedObject(teammate);
                so.FindProperty("_npcVoice").objectReferenceValue = voice;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            var stun = new SerializedObject(GetOrAdd<NPCStun>(root));
            stun.FindProperty("_resistance").floatValue = 0.6f;
            stun.ApplyModifiedPropertiesWithoutUndo();
            GetOrAdd<NPCHearing>(root);
        });

        EditPrefab(EnemyPrefab, root =>
        {
            SetupVoice(root, AssetDatabase.LoadAssetAtPath<NPCVoiceSet>(PathOf(2)));
            GetOrAdd<NPCStun>(root);
            GetOrAdd<NPCHearing>(root);
        });

        EditPrefab(HostagePrefab, root =>
        {
            GetOrAdd<NPCStun>(root);
            GetOrAdd<NPCHearing>(root);
        });
    }

    /// <summary>NPCVoice + AudioSource 3D "Voice" ở xương đầu (âm phát ra từ miệng). Không đổi bộ giọng đã gán.</summary>
    private static NPCVoice SetupVoice(GameObject root, NPCVoiceSet set)
    {
        NPCVoice voice = GetOrAdd<NPCVoice>(root);
        var so = new SerializedObject(voice);
        if (so.FindProperty("_source").objectReferenceValue == null)
        {
            Animator animator = root.GetComponentInChildren<Animator>();
            Transform head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            Transform parent = head != null ? head : root.transform;
            Transform existing = parent.Find("Voice");
            GameObject voiceGo = existing != null ? existing.gameObject : new GameObject("Voice");
            voiceGo.transform.SetParent(parent, false);
            AudioSource source = GetOrAdd<AudioSource>(voiceGo);
            source.playOnAwake = false;
            so.FindProperty("_source").objectReferenceValue = source;
        }
        if (so.FindProperty("_voiceSet").objectReferenceValue == null) so.FindProperty("_voiceSet").objectReferenceValue = set;
        so.ApplyModifiedPropertiesWithoutUndo();
        if (set == null) Debug.LogWarning($"[Voice] {root.name}: chưa có bộ giọng, chạy '1. Create Voice Sets' trước.");
        return voice;
    }

    // ---------------- 3. Clip ----------------

    [MenuItem("Tools/VR SWAT/Voice/3. Fill Clips From Folders")]
    public static void FillClipsFromFolders()
    {
        for (int i = 0; i < Sets.Length; i++)
            Fill(AssetDatabase.LoadAssetAtPath<NPCVoiceSet>(PathOf(i)), $"{Root}/{Sets[i].folder}/Lines");
        AssetDatabase.SaveAssets();
    }

    /// <summary>Câu nào chưa có clip thì lấy mọi AudioClip trong thư mục Lines/&lt;Câu&gt;/ (không ghi đè clip đã gán).</summary>
    private static void Fill(NPCVoiceSet set, string linesFolder)
    {
        if (set == null) return;
        var so = new SerializedObject(set);
        SerializedProperty entries = so.FindProperty("_entries");
        int filled = 0;
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty e = entries.GetArrayElementAtIndex(i);
            SerializedProperty clips = e.FindPropertyRelative("Clips");
            if (clips.arraySize > 0) continue;
            string line = ((NPCVoiceLine)e.FindPropertyRelative("Line").enumValueIndex).ToString();
            string folder = $"{linesFolder}/{line}";
            if (!AssetDatabase.IsValidFolder(folder)) continue;
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folder });
            clips.arraySize = guids.Length;
            for (int c = 0; c < guids.Length; c++)
                clips.GetArrayElementAtIndex(c).objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guids[c]));
            if (guids.Length > 0) filled++;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        if (filled > 0) Debug.Log($"[Voice] {set.name}: gán clip cho {filled} câu.");
    }

    // ---------------- Tiện ích ----------------

    private static void EditPrefab(string path, System.Action<GameObject> edit)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            Debug.LogWarning($"[Voice] Không tìm thấy {path}.");
            return;
        }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            edit(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[Voice] Đã setup {path}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component =>
        go.TryGetComponent(out T c) ? c : go.AddComponent<T>();

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
