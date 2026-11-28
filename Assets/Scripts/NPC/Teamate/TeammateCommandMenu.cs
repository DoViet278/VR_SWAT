using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRSwat.Doors;
using CommonUsages = UnityEngine.XR.CommonUsages;
using InputDevice = UnityEngine.XR.InputDevice;

/// <summary>
/// Menu lệnh đồng đội trong VR.
///
/// GIỮ nút B (tay phải Meta Quest; chọn tay trái thì là nút Y): bảng lệnh hiện trước mặt và đứng yên trong không gian,
/// tay đó bắn ra tia trỏ. Trỏ vào lệnh (sáng lên, rung nhẹ) rồi NHẢ nút để chọn; nhả khi không trỏ vào lệnh nào = hủy.
/// (Tùy chọn) bóp cò khi đang trỏ cũng chọn được.
///
/// Lệnh (2 cột): theo cửa: Vào vị trí cửa, Mở cửa, Phá khóa, Soi gương dưới cửa, Ném lựu đạn choáng, Xông vào,
/// Choáng + xông vào; khác: Đi theo, Di chuyển tới đó, Yểm trợ chỗ đó, Còng người đầu hàng, Báo cáo tình hình.
/// Cửa nhận lệnh = cửa gần người chơi nhất và gần hướng nhìn (SquadController.FindTargetDoor), tên cửa ghi trên tiêu đề.
/// Điểm chỉ = chỗ hướng nhìn chạm vào lúc mở menu (Di chuyển tới / Yểm trợ / Còng người đầu hàng gần đó).
/// Lệnh không ra được (vd. Phá khóa khi cửa không khóa) bị làm mờ, trỏ vào thì hiện lý do.
///
/// Bảng dựng bằng uGUI world-space lúc chạy, tia tự tính giao với mặt bảng (không cần XR UI Input Module /
/// TrackedDeviceGraphicRaycaster). Test không kính: giữ phím Tab, trỏ bằng hướng nhìn camera, nhả Tab để chọn.
/// SquadController tự thêm component này nếu scene chưa có.
/// </summary>
[DisallowMultipleComponent]
public class TeammateCommandMenu : MonoBehaviour
{
    public enum Hand { Left, Right }

    private struct Entry
    {
        public string Label;
        public SquadCommand Command;
        public bool Flash;

        public Entry(string label, SquadCommand command, bool flash = false)
        {
            Label = label;
            Command = command;
            Flash = flash;
        }
    }

    private class Item
    {
        public Entry Entry;
        public RectTransform Rect;
        public Image Background;
        public TextMeshProUGUI Text;
        public bool Enabled;
        public string Reason;
    }

    // Cột trái: lệnh theo cửa (cửa gần + hướng nhìn). Cột phải: lệnh khác (điểm chỉ = chỗ đang nhìn lúc mở menu).
    private static readonly Entry[] DoorEntries =
    {
        new("Vào vị trí cửa", SquadCommand.StackUp),
        new("Mở cửa", SquadCommand.OpenDoor),
        new("Phá khóa", SquadCommand.PickLock),
        new("Soi gương dưới cửa", SquadCommand.Mirror),
        new("Ném lựu đạn choáng", SquadCommand.ThrowFlash),
        new("Xông vào", SquadCommand.OpenAndClear),
        new("Choáng + xông vào", SquadCommand.OpenAndClear, true),
    };

    private static readonly Entry[] OtherEntries =
    {
        new("Đi theo tôi", SquadCommand.FallIn),
        new("Di chuyển tới đó", SquadCommand.MoveTo),
        new("Yểm trợ chỗ đó", SquadCommand.Cover),
        new("Còng người đầu hàng", SquadCommand.Restrain),
        new("Báo cáo tình hình", SquadCommand.ReportStatus),
    };

    // Kích thước bảng theo đơn vị canvas (1 đơn vị = 1 mm vì canvas scale 0.001).
    private const float ColumnWidth = 300f;
    private const float ColumnGap = 12f;
    private const float Width = ColumnWidth * 2f + ColumnGap + 2f * Padding;
    private const float Padding = 14f;
    private const float TitleHeight = 70f;
    private const float ItemHeight = 42f;
    private const float ItemGap = 5f;
    private const float PointRange = 30f;
    private const float FooterHeight = 44f;
    private const float RefreshInterval = 0.25f;

    [Header("Mở menu")]
    [Tooltip("Tay giữ nút để mở menu và bắn tia trỏ. Meta Quest: tay phải = nút B, tay trái = nút Y.")]
    [SerializeField] private Hand _hand = Hand.Right;
    [Tooltip("Nhả nút khi tia đang trỏ vào một lệnh = chọn lệnh đó. Nhả khi không trỏ vào lệnh nào = hủy.")]
    [SerializeField] private bool _releaseToSelect = true;
    [Tooltip("Bóp cò khi đang trỏ vào lệnh cũng chọn. Cẩn thận: tay đang cầm súng sẽ bắn luôn.")]
    [SerializeField] private bool _triggerToSelect;
    [Tooltip("Test không kính: giữ phím này để mở menu, trỏ bằng hướng nhìn camera, nhả phím để chọn. None = tắt.")]
    [SerializeField] private Key _keyboardKey = Key.Tab;

    [Header("Tia trỏ")]
    [Tooltip("Gốc tia (controller). Trống = tự tìm interactor của tay mở menu.")]
    [SerializeField] private Transform _pointer;
    [Tooltip("Vật liệu của tia. Trống = Sprites/Default.")]
    [SerializeField] private Material _lineMaterial;
    [SerializeField] private float _lineWidth = 0.004f;
    [SerializeField] private Color _lineColor = new(0.35f, 0.85f, 1f, 0.9f);
    [SerializeField] private float _maxRayLength = 3f;

    [Header("Bảng")]
    [Tooltip("Khoảng cách từ mắt tới bảng (m).")]
    [SerializeField] private float _distance = 0.55f;
    [Tooltip("Bảng thấp hơn tầm mắt ngần này (m).")]
    [SerializeField] private float _heightOffset = -0.12f;
    [Tooltip("Font có dấu tiếng Việt. Trống = font mặc định của TextMeshPro.")]
    [SerializeField] private TMP_FontAsset _font;
    [SerializeField] private Color _panelColor = new(0.05f, 0.07f, 0.09f, 0.88f);
    [SerializeField] private Color _itemColor = new(0.16f, 0.2f, 0.24f, 0.95f);
    [SerializeField] private Color _hoverColor = new(0.1f, 0.55f, 0.85f, 1f);
    [SerializeField] private Color _disabledColor = new(0.12f, 0.13f, 0.14f, 0.7f);
    [SerializeField] private Color _textColor = Color.white;
    [SerializeField] private Color _disabledTextColor = new(1f, 1f, 1f, 0.35f);

    [Header("Rung tay")]
    [SerializeField, Range(0f, 1f)] private float _hoverHaptic = 0.15f;
    [SerializeField, Range(0f, 1f)] private float _selectHaptic = 0.5f;

    /// <summary>Người chơi vừa chọn lệnh (lệnh, cửa, có ném flash trước không, ra lệnh thành công không).</summary>
    public event Action<SquadCommand, DoorController, bool, bool> CommandSelected;

    public bool IsOpen { get; private set; }

    /// <summary>Cửa đang nhận lệnh (chọn lúc mở menu).</summary>
    public DoorController TargetDoor { get; private set; }

    /// <summary>Điểm chỉ (chỗ người chơi nhìn lúc mở menu) cho Di chuyển / Yểm trợ / Còng. null = không trúng gì.</summary>
    public Vector3? TargetPoint { get; private set; }

    private readonly List<Item> _items = new();
    private Transform _root;
    private TextMeshProUGUI _title;
    private TextMeshProUGUI _footer;
    private RectTransform _reticle;
    private LineRenderer _line;

    private Item _hovered;
    private bool _openedByKeyboard;
    private bool _prevTrigger;
    private float _nextRefresh;
    private float _nextPointerSearch;
    private Transform _autoPointer;

    private XRNode Node => _hand == Hand.Left ? XRNode.LeftHand : XRNode.RightHand;

    private void Awake() => Build();

    private void OnDisable() => Close();

    private void OnDestroy()
    {
        if (_root != null) Destroy(_root.gameObject);
        if (_line != null) Destroy(_line.gameObject);
    }

    private void Update()
    {
        bool xrHeld = ReadXRButton(CommonUsages.secondaryButton);
        bool keyHeld = _keyboardKey != Key.None && Keyboard.current != null && Keyboard.current[_keyboardKey].isPressed;
        bool trigger = ReadXRButton(CommonUsages.triggerButton);
        bool triggerDown = trigger && !_prevTrigger;
        _prevTrigger = trigger;

        if (!IsOpen)
        {
            if (xrHeld || keyHeld) Open(!xrHeld);
            return;
        }

        bool held = _openedByKeyboard ? keyHeld : xrHeld;
        if (Time.time >= _nextRefresh) RefreshItems();
        UpdatePointer();

        if (_triggerToSelect && !_openedByKeyboard && triggerDown && _hovered != null)
        {
            Select(_hovered);
            Close();
            return;
        }
        if (!held)
        {
            if (_releaseToSelect && _hovered != null) Select(_hovered);
            Close();
        }
    }

    // ---------- Mở / đóng ----------

    /// <summary>Mở menu trước mặt người chơi.</summary>
    public void Open(bool byKeyboard = false)
    {
        Transform head = Teammate.PlayerHead;
        if (head == null || _root == null) return;

        Vector3 fwd = head.forward;
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
        Vector3 pos = head.position + fwd * _distance + Vector3.up * _heightOffset;
        _root.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - head.position, Vector3.up));

        SquadController squad = SquadController.GetOrCreate();
        TargetDoor = squad.FindTargetDoor(head.position, head.forward);
        TargetPoint = Physics.Raycast(head.position + head.forward * 0.3f, head.forward, out RaycastHit hit, PointRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            ? hit.point
            : null;

        IsOpen = true;
        _openedByKeyboard = byKeyboard;
        _hovered = null;
        RefreshItems();
        _root.gameObject.SetActive(true);
        if (_line != null) _line.enabled = true;
        Haptic(_hoverHaptic, 0.05f);
    }

    public void Close()
    {
        IsOpen = false;
        _hovered = null;
        if (_root != null) _root.gameObject.SetActive(false);
        if (_line != null) _line.enabled = false;
    }

    private void Select(Item item)
    {
        if (!item.Enabled)
        {
            Debug.Log($"[Menu lệnh] {item.Entry.Label}: {item.Reason}.", this);
            Haptic(_hoverHaptic, 0.15f);
            return;
        }
        SquadController squad = SquadController.GetOrCreate();
        DoorController door = SquadController.NeedsDoor(item.Entry.Command) ? TargetDoor : null;
        bool ok = squad.Issue(item.Entry.Command, door, item.Entry.Flash, null, null, TargetPoint);
        Haptic(_selectHaptic, 0.1f);
        CommandSelected?.Invoke(item.Entry.Command, door, item.Entry.Flash, ok);
    }

    // ---------- Trạng thái các lệnh ----------

    private void RefreshItems()
    {
        _nextRefresh = Time.time + RefreshInterval;
        SquadController squad = SquadController.GetOrCreate();
        Transform head = Teammate.PlayerHead;
        Vector3 playerPos = head != null ? head.position : transform.position;

        foreach (Item item in _items)
        {
            DoorController d = SquadController.NeedsDoor(item.Entry.Command) ? TargetDoor : null;
            item.Enabled = squad.CanIssue(item.Entry.Command, d, playerPos, TargetPoint, out string reason);
            item.Reason = reason;
        }

        string door = TargetDoor != null ? $"{TargetDoor.name} ({DoorStateText(TargetDoor.State)})" : "không có cửa gần";
        string point = TargetPoint.HasValue && head != null ? $"{Vector3.Distance(head.position, TargetPoint.Value):0} m trước mặt" : "không có";
        _title.text = $"<b>LỆNH ĐỒNG ĐỘI</b>\n<size=70%>Cửa: {door}   |   Điểm chỉ: {point}</size>";
        UpdateVisuals();
    }

    private static string DoorStateText(DoorState state) => state switch
    {
        DoorState.Locked => "khóa",
        DoorState.Open => "đang mở",
        DoorState.Breached => "đã phá",
        _ => "đóng",
    };

    private void UpdateVisuals()
    {
        foreach (Item item in _items)
        {
            bool hover = item == _hovered;
            item.Background.color = !item.Enabled ? (hover ? Color.Lerp(_disabledColor, _hoverColor, 0.25f) : _disabledColor)
                : hover ? _hoverColor : _itemColor;
            item.Text.color = item.Enabled ? _textColor : _disabledTextColor;
        }

        if (_hovered == null) _footer.text = "Trỏ vào lệnh, nhả nút để chọn";
        else if (!_hovered.Enabled) _footer.text = $"<color=#FFB347>Không được: {_hovered.Reason}</color>";
        else _footer.text = "Nhả nút để ra lệnh";
    }

    // ---------- Tia trỏ ----------

    private void UpdatePointer()
    {
        Transform origin = _openedByKeyboard ? Teammate.PlayerHead : ResolvePointer();
        if (origin == null)
        {
            SetHovered(null);
            return;
        }

        var ray = new Ray(origin.position, origin.forward);
        var plane = new Plane(_root.forward, _root.position);
        Item hovered = null;
        bool onPanel = false;
        Vector3 end = ray.GetPoint(_maxRayLength * 0.5f);

        if (plane.Raycast(ray, out float enter) && enter <= _maxRayLength)
        {
            Vector3 hit = ray.GetPoint(enter);
            var panel = (RectTransform)_root;
            onPanel = panel.rect.Contains((Vector2)panel.InverseTransformPoint(hit));
            if (onPanel)
            {
                end = hit;
                foreach (Item item in _items)
                {
                    if (!item.Rect.rect.Contains((Vector2)item.Rect.InverseTransformPoint(hit))) continue;
                    hovered = item;
                    break;
                }
                Vector3 local = panel.InverseTransformPoint(hit);
                _reticle.localPosition = new Vector3(local.x, local.y, -1f);
            }
        }

        _reticle.gameObject.SetActive(onPanel);
        if (_line != null)
        {
            _line.SetPosition(0, origin.position);
            _line.SetPosition(1, end);
        }
        SetHovered(hovered);
    }

    private void SetHovered(Item item)
    {
        if (item == _hovered) return;
        _hovered = item;
        if (item != null) Haptic(item.Enabled ? _hoverHaptic : _hoverHaptic * 0.5f, 0.04f);
        UpdateVisuals();
    }

    /// <summary>Gốc tia: _pointer, không thì interactor (ưu tiên Ray / Far) của tay mở menu, không có thì đầu.</summary>
    private Transform ResolvePointer()
    {
        if (_pointer != null) return _pointer;
        if (_autoPointer != null && _autoPointer.gameObject.activeInHierarchy) return _autoPointer;
        if (Time.unscaledTime >= _nextPointerSearch)
        {
            _nextPointerSearch = Time.unscaledTime + 2f;
            _autoPointer = FindHandInteractor();
        }
        return _autoPointer != null ? _autoPointer : Teammate.PlayerHead;
    }

    private Transform FindHandInteractor()
    {
        InteractorHandedness want = _hand == Hand.Left ? InteractorHandedness.Left : InteractorHandedness.Right;
        Transform any = null;
        foreach (XRBaseInputInteractor interactor in FindObjectsByType<XRBaseInputInteractor>(FindObjectsSortMode.None))
        {
            if (interactor.handedness != want || !interactor.gameObject.activeInHierarchy) continue;
            string n = interactor.name;
            if (n.IndexOf("Ray", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Far", StringComparison.OrdinalIgnoreCase) >= 0)
                return interactor.transform;
            if (any == null) any = interactor.transform;
        }
        return any;
    }

    // ---------- Input / rung ----------

    private bool ReadXRButton(InputFeatureUsage<bool> usage)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(Node);
        return device.isValid && device.TryGetFeatureValue(usage, out bool pressed) && pressed;
    }

    private void Haptic(float amplitude, float duration)
    {
        if (amplitude <= 0f || _openedByKeyboard) return;
        InputDevice device = InputDevices.GetDeviceAtXRNode(Node);
        if (device.isValid) device.SendHapticImpulse(0u, amplitude, duration);
    }

    // ---------- Dựng bảng ----------

    private void Build()
    {
        int rows = Mathf.Max(DoorEntries.Length, OtherEntries.Length);
        float height = Padding + TitleHeight + rows * (ItemHeight + ItemGap) + FooterHeight + Padding;

        var rootGo = new GameObject("TeammateCommandMenu (runtime)", typeof(RectTransform));
        var canvas = rootGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;
        var rootRect = (RectTransform)rootGo.transform;
        rootRect.sizeDelta = new Vector2(Width, height);
        rootRect.localScale = Vector3.one * 0.001f;
        _root = rootGo.transform;

        Image bg = CreateImage(rootRect, "Background", _panelColor);
        Stretch(bg.rectTransform);

        _title = CreateText(rootRect, "Title", 26f, TextAlignmentOptions.Center);
        Place(_title.rectTransform, -Padding, TitleHeight, Width - 2f * Padding);

        float top = -Padding - TitleHeight;
        float half = (ColumnWidth + ColumnGap) * 0.5f;
        void Column(Entry[] entries, float x)
        {
            float y = top;
            foreach (Entry entry in entries)
            {
                Image itemBg = CreateImage(rootRect, entry.Label, _itemColor);
                Place(itemBg.rectTransform, y, ItemHeight, ColumnWidth, x);
                TextMeshProUGUI text = CreateText(itemBg.rectTransform, "Label", 22f, TextAlignmentOptions.Center);
                Stretch(text.rectTransform);
                text.text = entry.Label;
                _items.Add(new Item { Entry = entry, Rect = itemBg.rectTransform, Background = itemBg, Text = text, Enabled = true });
                y -= ItemHeight + ItemGap;
            }
        }
        Column(DoorEntries, -half);
        Column(OtherEntries, half);
        float footerY = top - rows * (ItemHeight + ItemGap);

        _footer = CreateText(rootRect, "Footer", 18f, TextAlignmentOptions.Center);
        Place(_footer.rectTransform, footerY, FooterHeight, Width - 2f * Padding);

        Image reticle = CreateImage(rootRect, "Reticle", Color.white);
        _reticle = reticle.rectTransform;
        _reticle.anchorMin = _reticle.anchorMax = _reticle.pivot = new Vector2(0.5f, 0.5f);
        _reticle.sizeDelta = new Vector2(10f, 10f);

        BuildLine();
        rootGo.SetActive(false);
    }

    private void BuildLine()
    {
        Material mat = _lineMaterial;
        if (mat == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return; // không có shader: chỉ dùng chấm trỏ trên bảng
            mat = new Material(shader);
        }
        var go = new GameObject("TeammateCommandMenu Ray");
        _line = go.AddComponent<LineRenderer>();
        _line.sharedMaterial = mat;
        _line.useWorldSpace = true;
        _line.positionCount = 2;
        _line.startWidth = _lineWidth;
        _line.endWidth = _lineWidth * 0.5f;
        _line.startColor = _lineColor;
        _line.endColor = new Color(_lineColor.r, _lineColor.g, _lineColor.b, _lineColor.a * 0.3f);
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows = false;
        _line.enabled = false;
    }

    private static Image CreateImage(RectTransform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private TextMeshProUGUI CreateText(RectTransform parent, string name, float size, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        if (_font != null) text.font = _font;
        text.fontSize = size;
        text.alignment = align;
        text.color = _textColor;
        text.raycastTarget = false;
        text.richText = true;
        return text;
    }

    /// <summary>Đặt rect neo mép trên giữa bảng, tại y (âm = xuống dưới), lệch ngang x.</summary>
    private static void Place(RectTransform rect, float y, float height, float width, float x = 0f)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
