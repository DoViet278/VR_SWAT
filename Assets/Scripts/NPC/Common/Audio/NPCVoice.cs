using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Events;

/// <summary>
/// Giọng nói của NPC: phát câu theo NPCVoiceSet trên một AudioSource 3D gắn ở đầu.
/// - Ưu tiên: câu ưu tiên cao cắt câu thấp (Chết > Bị thương > Đầu hàng / Còng > Sợ > Kêu cứu), câu thấp bị bỏ qua.
/// - Không lặp: mỗi loại câu bốc clip kiểu "túi xáo trộn", không nói cùng clip 2 lần liền.
/// - Cooldown từng loại câu (trúng loạt đạn không kêu chồng chéo), pitch random nhẹ.
/// - Đã chết (Death) thì khóa, không nói thêm câu nào.
/// </summary>
[DisallowMultipleComponent]
public class NPCVoice : MonoBehaviour
{
    [SerializeField] private NPCVoiceSet _voiceSet;
    [Tooltip("Trống: tự tạo AudioSource ở xương đầu (Animator humanoid) hoặc trên object này.")]
    [SerializeField] private AudioSource _source;
    [SerializeField] private AudioMixerGroup _mixerGroup;

    [Header("Âm 3D")]
    [Tooltip("Ghi đè thiết lập AudioSource bằng các giá trị dưới (tắt nếu muốn chỉnh tay trên AudioSource).")]
    [SerializeField] private bool _configureSource = true;
    [SerializeField] private float _minDistance = 1.5f;
    [SerializeField] private float _maxDistance = 25f;
    [Tooltip("Dùng spatializer của project (Meta XR / Steam Audio...) nếu đã chọn trong Project Settings > Audio.")]
    [SerializeField] private bool _spatialize = true;

    [Header("Events")]
    [Tooltip("(loại câu, clip) mỗi khi bắt đầu nói: gắn phụ đề, nhép miệng...")]
    public UnityEvent<NPCVoiceLine, AudioClip> OnLine;

    private readonly Dictionary<NPCVoiceLine, List<AudioClip>> _bags = new();
    private readonly Dictionary<NPCVoiceLine, AudioClip> _last = new();
    private readonly Dictionary<NPCVoiceLine, float> _nextAllowed = new();
    private NPCVoiceSet.Entry _current;
    private bool _silenced;

    public NPCVoiceSet VoiceSet
    {
        get => _voiceSet;
        set
        {
            _voiceSet = value;
            _bags.Clear();
            _last.Clear();
        }
    }

    public AudioSource Source => _source;

    /// <summary>Đang nói một câu.</summary>
    public bool IsSpeaking => _source != null && _source.isPlaying;

    /// <summary>Loại câu đang nói (null nếu im lặng).</summary>
    public NPCVoiceLine? CurrentLine => IsSpeaking && _current != null ? _current.Line : null;

    private void Awake()
    {
        if (_source == null) _source = CreateSource();
        if (_configureSource) Configure(_source);
    }

    /// <summary>
    /// Nói một câu loại line. Trả true nếu đã phát. Bỏ qua khi: không có clip, đang cooldown, đã chết,
    /// hoặc đang nói câu ưu tiên cao hơn.
    /// </summary>
    public bool Play(NPCVoiceLine line)
    {
        if (_silenced || _voiceSet == null || _source == null) return false;
        NPCVoiceSet.Entry entry = _voiceSet.Get(line);
        if (entry == null || entry.Clips == null || entry.Clips.Length == 0) return false;
        if (_nextAllowed.TryGetValue(line, out float next) && Time.time < next) return false;

        if (IsSpeaking && _current != null)
        {
            bool higher = entry.Priority > _current.Priority;
            bool same = entry.Priority == _current.Priority && (entry.InterruptSamePriority || entry.Line != _current.Line);
            if (!higher && !same) return false;
        }

        AudioClip clip = NextClip(line, entry.Clips);
        if (clip == null) return false;

        _source.Stop();
        _source.clip = clip;
        _source.volume = entry.Volume;
        _source.pitch = _voiceSet.BasePitch * Random.Range(entry.Pitch.x, Mathf.Max(entry.Pitch.x, entry.Pitch.y));
        _source.Play();
        _current = entry;
        _nextAllowed[line] = Time.time + entry.Cooldown;
        if (line == NPCVoiceLine.Death) _silenced = true;
        OnLine?.Invoke(line, clip);
        return true;
    }

    /// <summary>Im lặng ngay.</summary>
    public void Stop()
    {
        if (_source != null) _source.Stop();
        _current = null;
    }

    /// <summary>Hồi sinh / tái sử dụng NPC: cho nói lại sau khi đã chết.</summary>
    public void ResetVoice()
    {
        _silenced = false;
        _nextAllowed.Clear();
        Stop();
    }

    // ---------- Nội bộ ----------

    /// <summary>Túi xáo trộn: mỗi clip được nói 1 lần rồi mới tới lượt lặp, không nói cùng clip 2 lần liền.</summary>
    private AudioClip NextClip(NPCVoiceLine line, AudioClip[] clips)
    {
        if (!_bags.TryGetValue(line, out List<AudioClip> bag))
        {
            bag = new List<AudioClip>();
            _bags[line] = bag;
        }
        if (bag.Count == 0)
        {
            foreach (AudioClip c in clips)
                if (c != null) bag.Add(c);
            if (bag.Count == 0) return null;
        }
        int i = Random.Range(0, bag.Count);
        _last.TryGetValue(line, out AudioClip previous);
        if (bag.Count > 1 && bag[i] == previous) i = (i + 1) % bag.Count;
        AudioClip clip = bag[i];
        bag.RemoveAt(i);
        _last[line] = clip;
        return clip;
    }

    private AudioSource CreateSource()
    {
        Transform parent = transform;
        Animator animator = GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman)
        {
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) parent = head;
        }
        if (parent == transform && TryGetComponent(out AudioSource existing)) return existing;

        var go = new GameObject("Voice");
        go.transform.SetParent(parent, false);
        return go.AddComponent<AudioSource>();
    }

    private void Configure(AudioSource s)
    {
        s.playOnAwake = false;
        s.loop = false;
        s.spatialBlend = 1f;
        s.rolloffMode = AudioRolloffMode.Logarithmic;
        s.minDistance = _minDistance;
        s.maxDistance = _maxDistance;
        s.dopplerLevel = 0f;
        s.spread = 0f;
        s.priority = 64; // giọng nói quan trọng hơn tiếng môi trường
        s.spatialize = _spatialize;
        if (_mixerGroup != null) s.outputAudioMixerGroup = _mixerGroup;
    }

    private void OnValidate()
    {
        _minDistance = Mathf.Max(0.1f, _minDistance);
        _maxDistance = Mathf.Max(_minDistance + 0.1f, _maxDistance);
    }
}
