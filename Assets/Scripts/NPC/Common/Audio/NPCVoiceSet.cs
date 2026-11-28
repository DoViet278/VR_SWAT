using System;
using UnityEngine;

/// <summary>Giới tính của bộ giọng (để chọn bộ giọng khớp với model NPC).</summary>
public enum VoiceGender
{
    Male,
    Female,
}

/// <summary>Các loại câu NPC có thể nói: dân thường (con tin), nghi phạm, đồng đội (xem VoiceRole).</summary>
public enum NPCVoiceLine
{
    /// <summary>Kêu cứu khi chưa thấy ai.</summary>
    Help,
    /// <summary>Hoảng sợ: thấy người chơi, nổ gần, bị hô mà chưa giơ tay.</summary>
    Scared,
    /// <summary>Giơ tay đầu hàng ("Đừng bắn!").</summary>
    Comply,
    /// <summary>Bị còng xong (bình tĩnh lại, cảm ơn).</summary>
    Cuffed,
    /// <summary>Trúng đạn / bị thương.</summary>
    Hurt,
    /// <summary>Chết.</summary>
    Death,

    // ----- Nghi phạm (Suspect). Chỉ thêm vào cuối: giá trị enum đã lưu trong asset. -----
    /// <summary>Phát hiện cảnh sát ("Cops!").</summary>
    Spotted,
    /// <summary>Thách thức khi bị hô mà không chịu ("Get out of here!").</summary>
    Taunt,
    /// <summary>Đe dọa con tin ("Back off or he dies!").</summary>
    Threaten,
    /// <summary>Bỏ chạy ("Run!").</summary>
    Flee,
    /// <summary>Nạp đạn ("Reloading!").</summary>
    Reloading,
    /// <summary>Rút / ném lựu đạn ("Grenade!").</summary>
    GrenadeOut,
    /// <summary>Bị choáng ("Ahh, my eyes!").</summary>
    Stunned,
    /// <summary>Nghe tiếng động, đi xem ("What was that?").</summary>
    Investigate,

    // ----- Đồng đội (Officer) -----
    /// <summary>Nhận lệnh ("Copy." / "On it.").</summary>
    Ack,
    /// <summary>Đang di chuyển ("Moving.").</summary>
    Moving,
    /// <summary>Đã vào vị trí cửa ("Stacked up.").</summary>
    Stacked,
    /// <summary>Cửa khóa ("Door's locked.").</summary>
    DoorLocked,
    /// <summary>Đã mở cửa / phá khóa xong ("Door's open." / "Lock's picked.").</summary>
    DoorOpened,
    /// <summary>Ném flash ("Flashbang out!").</summary>
    FlashOut,
    /// <summary>Phòng an toàn ("Room clear!").</summary>
    RoomClear,
    /// <summary>Phát hiện nghi phạm ("Contact!").</summary>
    Contact,
    /// <summary>Hô hàng ("Police! Drop your weapon!").</summary>
    ShoutCompliance,
    /// <summary>Đang còng ("Restraining.").</summary>
    Restraining,
    /// <summary>Còng xong nghi phạm ("Suspect secured.").</summary>
    SuspectSecured,
    /// <summary>Còng xong con tin ("Civilian secured.").</summary>
    CivilianSecured,
    /// <summary>Soi gương: có nghi phạm ("I see suspects.").</summary>
    MirrorSuspects,
    /// <summary>Soi gương: có dân thường ("Civilians inside.").</summary>
    MirrorCivilians,
    /// <summary>Soi gương: trống ("Looks clear.").</summary>
    MirrorClear,
    /// <summary>Báo cáo tình hình (phụ đề đọc số người).</summary>
    ReportStatus,
    /// <summary>Trúng đạn ("I'm hit!").</summary>
    Hit,
    /// <summary>Đồng đội gục ("Officer down!").</summary>
    ManDown,

    // ----- Dân thường (bổ sung) -----
    /// <summary>Được cứu ("Thank god you're here!").</summary>
    Rescued,
    /// <summary>Van xin khi bị hô mà chưa giơ tay ("Please don't shoot!").</summary>
    Pleading,
}

/// <summary>Vai của bộ giọng: quyết định danh sách câu được thêm sẵn.</summary>
public enum VoiceRole
{
    Civilian,
    Suspect,
    Officer,
}

/// <summary>
/// Bộ giọng của một NPC (Create > VR SWAT > NPC Voice Set). Mỗi loại câu có danh sách clip, âm lượng, khoảng pitch,
/// độ ưu tiên và cooldown riêng. Đổi giọng (nam / nữ / người khác) chỉ cần gán bộ giọng khác cho NPCVoice.
/// </summary>
[CreateAssetMenu(menuName = "VR SWAT/NPC Voice Set", fileName = "NPCVoiceSet")]
public class NPCVoiceSet : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public NPCVoiceLine Line;
        public AudioClip[] Clips = Array.Empty<AudioClip>();
        [Range(0f, 1f)] public float Volume = 1f;
        [Tooltip("Pitch random trong khoảng này để các lần nói không giống hệt nhau.")]
        public Vector2 Pitch = new(0.96f, 1.04f);
        [Tooltip("Câu ưu tiên cao hơn cắt câu đang nói; thấp hơn thì bị bỏ qua.")]
        public int Priority;
        [Tooltip("Cùng độ ưu tiên thì cắt câu đang nói (vd. Hurt khi trúng nhiều viên liên tiếp).")]
        public bool InterruptSamePriority;
        [Tooltip("Khoảng nghỉ tối thiểu (giây) giữa 2 lần nói câu loại này.")]
        public float Cooldown;
    }

    [Tooltip("Vai: Civilian (con tin), Suspect (nghi phạm), Officer (đồng đội). Quyết định các câu được thêm sẵn.")]
    [SerializeField] private VoiceRole _role = VoiceRole.Civilian;
    [SerializeField] private VoiceGender _gender = VoiceGender.Male;
    [Tooltip("Pitch nhân thêm cho cả bộ giọng (giọng trầm / cao hơn một chút cho từng NPC).")]
    [SerializeField, Range(0.8f, 1.2f)] private float _basePitch = 1f;
    [SerializeField] private Entry[] _entries = DefaultEntries();

    public VoiceRole Role => _role;
    public VoiceGender Gender => _gender;
    public float BasePitch => _basePitch;

    public Entry Get(NPCVoiceLine line)
    {
        if (_entries == null) return null;
        foreach (Entry e in _entries)
            if (e != null && e.Line == line) return e;
        return null;
    }

    /// <summary>Câu mặc định của dân thường. Ưu tiên: Chết > Bị thương > Đầu hàng / Bị còng > Sợ > Kêu cứu.</summary>
    public static Entry[] DefaultEntries() => DefaultEntries(VoiceRole.Civilian);

    /// <summary>Câu mặc định theo vai (độ ưu tiên, cooldown hợp lý; clip gán sau).</summary>
    public static Entry[] DefaultEntries(VoiceRole role) => role switch
    {
        VoiceRole.Suspect => new[]
        {
            new Entry { Line = NPCVoiceLine.Investigate, Priority = 0, Cooldown = 6f },
            new Entry { Line = NPCVoiceLine.Taunt, Priority = 1, Cooldown = 5f },
            new Entry { Line = NPCVoiceLine.Spotted, Priority = 1, Cooldown = 8f },
            new Entry { Line = NPCVoiceLine.Reloading, Priority = 1, Cooldown = 4f },
            new Entry { Line = NPCVoiceLine.Threaten, Priority = 2, Cooldown = 4f },
            new Entry { Line = NPCVoiceLine.Flee, Priority = 2, Cooldown = 5f },
            new Entry { Line = NPCVoiceLine.GrenadeOut, Priority = 2, Cooldown = 3f },
            new Entry { Line = NPCVoiceLine.Comply, Priority = 2, Cooldown = 1.5f },
            new Entry { Line = NPCVoiceLine.Stunned, Priority = 3, Cooldown = 2f },
            new Entry { Line = NPCVoiceLine.Hurt, Priority = 3, InterruptSamePriority = true, Cooldown = 0.35f, Pitch = new Vector2(0.94f, 1.06f) },
            new Entry { Line = NPCVoiceLine.Death, Priority = 4, Pitch = new Vector2(0.97f, 1.03f) },
        },
        VoiceRole.Officer => new[]
        {
            new Entry { Line = NPCVoiceLine.Moving, Priority = 0, Cooldown = 4f },
            new Entry { Line = NPCVoiceLine.Ack, Priority = 1, Cooldown = 0.8f },
            new Entry { Line = NPCVoiceLine.Stacked, Priority = 1, Cooldown = 2f },
            new Entry { Line = NPCVoiceLine.DoorLocked, Priority = 1, Cooldown = 2f },
            new Entry { Line = NPCVoiceLine.DoorOpened, Priority = 1, Cooldown = 2f },
            new Entry { Line = NPCVoiceLine.MirrorSuspects, Priority = 1, Cooldown = 1f },
            new Entry { Line = NPCVoiceLine.MirrorCivilians, Priority = 1, Cooldown = 1f },
            new Entry { Line = NPCVoiceLine.MirrorClear, Priority = 1, Cooldown = 1f },
            new Entry { Line = NPCVoiceLine.ReportStatus, Priority = 1, Cooldown = 1f },
            new Entry { Line = NPCVoiceLine.Restraining, Priority = 1, Cooldown = 2f },
            new Entry { Line = NPCVoiceLine.SuspectSecured, Priority = 1, Cooldown = 1f },
            new Entry { Line = NPCVoiceLine.CivilianSecured, Priority = 1, Cooldown = 1f },
            new Entry { Line = NPCVoiceLine.RoomClear, Priority = 2, Cooldown = 2f },
            new Entry { Line = NPCVoiceLine.Reloading, Priority = 2, Cooldown = 3f },
            new Entry { Line = NPCVoiceLine.FlashOut, Priority = 2, Cooldown = 1f },
            new Entry { Line = NPCVoiceLine.ShoutCompliance, Priority = 2, Cooldown = 1.5f },
            new Entry { Line = NPCVoiceLine.Contact, Priority = 2, Cooldown = 4f },
            new Entry { Line = NPCVoiceLine.Stunned, Priority = 3, Cooldown = 3f },
            new Entry { Line = NPCVoiceLine.Hit, Priority = 3, InterruptSamePriority = true, Cooldown = 2f },
            new Entry { Line = NPCVoiceLine.ManDown, Priority = 3, Cooldown = 3f },
            new Entry { Line = NPCVoiceLine.Death, Priority = 4, Pitch = new Vector2(0.97f, 1.03f) },
        },
        _ => new[]
        {
            new Entry { Line = NPCVoiceLine.Help, Priority = 0, Cooldown = 3f, Volume = 0.9f },
            new Entry { Line = NPCVoiceLine.Scared, Priority = 1, Cooldown = 2.5f },
            new Entry { Line = NPCVoiceLine.Pleading, Priority = 1, Cooldown = 2f },
            new Entry { Line = NPCVoiceLine.Comply, Priority = 2, Cooldown = 1.5f },
            new Entry { Line = NPCVoiceLine.Cuffed, Priority = 2, Cooldown = 1.5f, Volume = 0.85f },
            new Entry { Line = NPCVoiceLine.Rescued, Priority = 2, Cooldown = 5f },
            new Entry { Line = NPCVoiceLine.Hurt, Priority = 3, InterruptSamePriority = true, Cooldown = 0.35f, Pitch = new Vector2(0.94f, 1.06f) },
            new Entry { Line = NPCVoiceLine.Death, Priority = 4, Pitch = new Vector2(0.97f, 1.03f) },
        },
    };

    /// <summary>Đặt vai / giới tính / pitch và nạp lại danh sách câu mặc định của vai (dùng khi tạo bộ giọng mới).</summary>
    public void Setup(VoiceRole role, VoiceGender gender, float basePitch)
    {
        _role = role;
        _gender = gender;
        _basePitch = Mathf.Clamp(basePitch, 0.8f, 1.2f);
        _entries = DefaultEntries(role);
    }

    /// <summary>Thêm các câu mặc định của vai còn thiếu (giữ nguyên câu đã có và clip đã gán).</summary>
    public void EnsureDefaultEntries() => OnValidate();

    private void OnValidate()
    {
        // Bộ giọng cũ / mới tạo thiếu loại câu nào (theo vai) thì thêm vào với giá trị mặc định.
        Entry[] defaults = DefaultEntries(_role);
        if (_entries == null || _entries.Length == 0) { _entries = defaults; return; }
        foreach (Entry d in defaults)
        {
            if (Get(d.Line) != null) continue;
            Array.Resize(ref _entries, _entries.Length + 1);
            _entries[^1] = d;
        }
    }
}
