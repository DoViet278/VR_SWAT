using System;
using System.Collections.Generic;
using UnityEngine;
using VRSwat.Doors;
using static CONST;

/// <summary>
/// Đổi sự kiện của thế giới (script ngoài folder NPC, không sửa) thành tiếng động cho NPCHearing:
/// - Người chơi bắn: event PLAYER_SHOOT_TARGET (Gun, vật trúng, RaycastHit) qua ObserverManager. Gun chỉ phát khi tia
///   đạn trúng vật, nên phát bắn trượt hoàn toàn không nghe được. Mỗi súng chỉ tính 1 lần mỗi frame (súng hoa cải).
/// - Cửa: event DoorController.StateChanged (mở từ trạng thái đóng, bị phá). Quét lại DoorController.All mỗi 2 s
///   để đăng ký cửa mới bật.
/// Tự tạo một object ẩn trong scene khi NPC / con tin đầu tiên gọi Ensure().
/// </summary>
[DisallowMultipleComponent]
public class NPCWorldListener : MonoBehaviour
{
    public const float GunshotRadius = 30f;
    public const float DoorBreachRadius = 25f;
    public const float DoorOpenRadius = 6f;
    private const float DoorRescanInterval = 2f;

    private static NPCWorldListener s_Instance;

    private readonly HashSet<DoorController> _doors = new();
    private readonly Dictionary<Gun, int> _lastShotFrame = new();
    private Action<DoorController, DoorState, DoorState> _onDoorChanged;
    private float _nextRescan;

    /// <summary>Đảm bảo scene có đúng một NPCWorldListener.</summary>
    public static void Ensure()
    {
        if (!Application.isPlaying) return;
        SwatPlayerHealth.EnsureOnPlayer(); // máu người chơi (nhận đạn / lựu đạn của NPC)
        if (s_Instance != null) return;
        s_Instance = FindAnyObjectByType<NPCWorldListener>();
        if (s_Instance == null) s_Instance = new GameObject(nameof(NPCWorldListener)).AddComponent<NPCWorldListener>();
    }

    private void Awake()
    {
        if (s_Instance != null && s_Instance != this)
        {
            Destroy(this);
            return;
        }
        s_Instance = this;
        _onDoorChanged = OnDoorChanged;
    }

    private void OnEnable()
    {
        ObserverManager.Register(PLAYER_SHOOT_TARGET, (Action<Gun, GameObject, RaycastHit>)OnPlayerShoot);
        _nextRescan = 0f;
    }

    private void OnDisable()
    {
        ObserverManager.Unregister(PLAYER_SHOOT_TARGET, (Action<Gun, GameObject, RaycastHit>)OnPlayerShoot);
        foreach (DoorController door in _doors)
            if (door != null) door.StateChanged -= _onDoorChanged;
        _doors.Clear();
    }

    private void OnDestroy()
    {
        if (s_Instance == this) s_Instance = null;
    }

    private void Update()
    {
        if (Time.time < _nextRescan) return;
        _nextRescan = Time.time + DoorRescanInterval;
        _doors.RemoveWhere(d => d == null);
        foreach (DoorController door in DoorController.All)
            if (door != null && _doors.Add(door)) door.StateChanged += _onDoorChanged;
    }

    private void OnPlayerShoot(Gun gun, GameObject hitObject, RaycastHit hit)
    {
        if (gun == null) return;
        int frame = Time.frameCount;
        if (_lastShotFrame.TryGetValue(gun, out int last) && last == frame) return; // nhiều viên ghém cùng một phát
        _lastShotFrame[gun] = frame;
        NPCHearing.Report(gun.transform.position, GunshotRadius, NoiseType.Gunshot, gun.transform.root.gameObject);
    }

    private void OnDoorChanged(DoorController door, DoorState from, DoorState to)
    {
        if (door == null) return;
        if (to == DoorState.Breached)
            NPCHearing.Report(door.transform.position, DoorBreachRadius, NoiseType.DoorBreach, door.gameObject);
        else if (to == DoorState.Open && from == DoorState.Closed)
            NPCHearing.Report(door.transform.position, DoorOpenRadius, NoiseType.DoorOpen, door.gameObject);
    }
}
