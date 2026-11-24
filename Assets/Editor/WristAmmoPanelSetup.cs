using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Unity.XR.CoreUtils;

/// <summary>
/// Dựng sẵn panel đạn đeo cổ tay cho <see cref="WristAmmoPanel"/>.
///
/// Dựng tay thì phải nhớ đủ thứ lắt nhắt: Canvas phải World Space, RectTransform phải thu nhỏ cỡ
/// 0.0005 không thì panel to bằng cả căn phòng, hàng vạch đạn phải Horizontal Layout Group bật
/// Force Expand Width mới chia đều được 30 vạch, và Canvas Group phải tắt Blocks Raycasts kẻo
/// panel ăn mất tia bấm của tay.
///
/// Menu này làm hết, rồi gắn luôn vào tay của XR Origin đang có trong scene.
///
/// Chạy qua menu Tools &gt; VR SWAT &gt; Create Wrist Ammo Panel (Right / Left Hand). Panel đã có
/// sẵn mà bị xoay sai (nhìn thấy mặt sau nên chữ lộn ngược) thì chọn nó rồi chạy Fix Wrist Ammo
/// Panel Orientation.
/// </summary>
public static class WristAmmoPanelSetup
{
    private const string BuiltinSprite = "UI/Skin/UISprite.psd";
    private const string KnobSprite = "UI/Skin/Knob.psd";

    private static readonly Color PanelColor = new Color(0.04f, 0.086f, 0.118f, 0.92f);
    private static readonly Color DimColor = new Color(0.50f, 0.59f, 0.65f);
    private static readonly Color CyanColor = new Color(0.31f, 0.82f, 0.91f);

    // Panel nằm trên mu bàn tay, hơi lùi về phía cổ tay. Số đo lấy theo tay cầm Quest, chỉnh lại
    // trong Inspector cho vừa tay người chơi là chuyện bình thường.
    private static readonly Vector3 WristOffset = new Vector3(0f, 0.035f, -0.07f);

    // Canvas của Unity chỉ đọc xuôi khi NHÌN TỪ PHÍA +Z của nó; nhìn từ sau lưng là chữ lộn
    // ngược như soi gương. Nên trục +Z bắt buộc phải ngửa ra khỏi mu bàn tay, về phía mặt người
    // chơi khi lật cổ tay lên.
    //
    // -90 quanh X: mặt panel (+Z) ngửa lên trời, đúng kiểu đồng hồ đeo tay.
    // 180 quanh Z: đầu chữ quay về phía bàn tay, lật cổ tay lên là đọc xuôi.
    //
    // Hai tay dùng chung số đo: trục +Y của tay cầm đều chĩa ra khỏi mu bàn tay, trái phải chỉ
    // khác nhau ở hướng lật cổ tay chứ không khác hệ trục.
    private static readonly Vector3 WristEuler = new Vector3(-90f, 0f, 180f);

    [MenuItem("Tools/VR SWAT/Create Wrist Ammo Panel (Right Hand)")]
    private static void CreateRight()
    {
        Create(true);
    }

    [MenuItem("Tools/VR SWAT/Create Wrist Ammo Panel (Left Hand)")]
    private static void CreateLeft()
    {
        Create(false);
    }

    /// <summary>
    /// Nắn lại panel đang chọn: gắn đúng tay, đặt lại vị trí và góc xoay chuẩn. Dùng khi panel đã
    /// bị kéo tay thành ra nhìn vào mặt sau, chữ lộn ngược.
    /// </summary>
    [MenuItem("Tools/VR SWAT/Fix Wrist Ammo Panel Orientation")]
    private static void FixOrientation()
    {
        GameObject selected = Selection.activeGameObject;
        WristAmmoPanel panel = selected != null ? selected.GetComponentInParent<WristAmmoPanel>() : null;

        if (panel == null)
        {
            EditorUtility.DisplayDialog("Wrist Ammo Panel",
                "Chọn WristAmmoPanel trong Hierarchy trước đã.", "OK");
            return;
        }

        Transform root = panel.transform;
        Transform hand = root.parent;

        // Đang nằm trong tay nào thì giữ nguyên tay đó, chỉ nắn lại tư thế.
        bool right = hand == null || hand.name.ToLowerInvariant().Contains("right");

        if (hand == null || !IsHand(hand))
        {
            hand = FindHand(right, out _);

            if (hand == null)
            {
                EditorUtility.DisplayDialog("Wrist Ammo Panel",
                    "Không thấy Left / Right Controller trong scene để gắn vào.", "OK");
                return;
            }

            Undo.SetTransformParent(root, hand, "Fix Wrist Ammo Panel");
        }

        Undo.RecordObject(root, "Fix Wrist Ammo Panel");
        root.localPosition = WristOffset;
        root.localEulerAngles = WristEuler;
        EditorUtility.SetDirty(root);

        Debug.Log($"Đã nắn lại WristAmmoPanel trên \"{hand.name}\": mặt panel (+Z) ngửa ra khỏi mu " +
                  "bàn tay nên chữ đọc xuôi. Scale giữ nguyên.", root.gameObject);
    }

    private static void Create(bool rightHand)
    {
        Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(BuiltinSprite);
        Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>(KnobSprite);

        var root = new GameObject("WristAmmoPanel",
            typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        Undo.RegisterCreatedObjectUndo(root, "Create Wrist Ammo Panel");

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        CanvasGroup group = root.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        var rect = (RectTransform)root.transform;
        rect.sizeDelta = new Vector2(340f, 150f);
        rect.localScale = Vector3.one * 0.0005f;   // 340 * 0.0005 = 17 cm ngang

        Image background = AddImage(root.transform, "Background", sprite, PanelColor);
        Stretch((RectTransform)background.transform, 0f);
        background.type = Image.Type.Sliced;

        // Hàng trên: tên súng bên trái, trạng thái buồng đạn bên phải.
        RectTransform header = AddRow(root.transform, "Header", 22f, -14f);

        TMP_Text weaponLabel = AddText(header, "WeaponLabel", "RIFLE · AUTO", 15f,
            TextAlignmentOptions.MidlineLeft, DimColor);
        var weaponRect = (RectTransform)weaponLabel.transform;
        weaponRect.anchorMin = Vector2.zero;
        weaponRect.anchorMax = new Vector2(0.6f, 1f);
        weaponRect.offsetMin = Vector2.zero;
        weaponRect.offsetMax = Vector2.zero;
        weaponLabel.characterSpacing = 6f;

        RectTransform chamberGroup = AddAutoRow(header, "ChamberGroup", 6f);

        Image chamberDot = AddImage(chamberGroup, "ChamberDot", knob, CyanColor);
        AddLayoutSize(chamberDot.gameObject, 9f, 9f);

        TMP_Text chamberLabel = AddText(chamberGroup, "ChamberLabel", "ĐÃ LÊN ĐẠN", 12f,
            TextAlignmentOptions.MidlineRight, CyanColor);
        chamberLabel.characterSpacing = 4f;

        // Hàng giữa: số đạn to, sức chứa nhỏ bên cạnh, vạch đạn dồn về bên phải.
        RectTransform ammoRow = AddRow(root.transform, "AmmoRow", 58f, -42f);

        TMP_Text currentText = AddText(ammoRow, "CurrentAmmo", "28", 52f,
            TextAlignmentOptions.BottomLeft, CyanColor);
        var currentRect = (RectTransform)currentText.transform;
        currentRect.anchorMin = new Vector2(0f, 0f);
        currentRect.anchorMax = new Vector2(0f, 1f);
        currentRect.pivot = new Vector2(0f, 0.5f);
        currentRect.sizeDelta = new Vector2(96f, 0f);
        currentRect.anchoredPosition = Vector2.zero;

        TMP_Text capacityText = AddText(ammoRow, "Capacity", "/ 30", 18f,
            TextAlignmentOptions.BottomLeft, DimColor);
        var capacityRect = (RectTransform)capacityText.transform;
        capacityRect.anchorMin = new Vector2(0f, 0f);
        capacityRect.anchorMax = new Vector2(0f, 1f);
        capacityRect.pivot = new Vector2(0f, 0.5f);
        capacityRect.sizeDelta = new Vector2(70f, -6f);
        capacityRect.anchoredPosition = new Vector2(98f, 0f);

        RectTransform bulletStrip = AddFixedRow(ammoRow, "BulletStrip", 170f, 16f, 2f);
        bulletStrip.anchorMin = new Vector2(1f, 0.5f);
        bulletStrip.anchorMax = new Vector2(1f, 0.5f);
        bulletStrip.pivot = new Vector2(1f, 0.5f);
        bulletStrip.anchoredPosition = Vector2.zero;

        Image divider = AddImage(root.transform, "Divider", null, new Color(0.47f, 0.63f, 0.71f, 0.3f));
        var dividerRect = (RectTransform)divider.transform;
        dividerRect.anchorMin = new Vector2(0f, 1f);
        dividerRect.anchorMax = new Vector2(1f, 1f);
        dividerRect.pivot = new Vector2(0.5f, 1f);
        dividerRect.sizeDelta = new Vector2(-32f, 1f);
        dividerRect.anchoredPosition = new Vector2(0f, -112f);

        // Hàng dưới: băng dự trữ trong túi đạn.
        RectTransform footer = AddRow(root.transform, "Footer", 20f, 0f);
        footer.anchorMin = new Vector2(0f, 0f);
        footer.anchorMax = new Vector2(1f, 0f);
        footer.pivot = new Vector2(0.5f, 0f);
        footer.anchoredPosition = new Vector2(0f, 14f);

        TMP_Text spareLabel = AddText(footer, "SpareLabel", "BĂNG DỰ TRỮ", 12f,
            TextAlignmentOptions.MidlineLeft, DimColor);
        var spareLabelRect = (RectTransform)spareLabel.transform;
        spareLabelRect.anchorMin = Vector2.zero;
        spareLabelRect.anchorMax = new Vector2(0.6f, 1f);
        spareLabelRect.offsetMin = Vector2.zero;
        spareLabelRect.offsetMax = Vector2.zero;
        spareLabel.characterSpacing = 4f;

        RectTransform spareGroup = AddAutoRow(footer, "SpareGroup", 8f);

        RectTransform magPips = AddFixedRow(spareGroup, "MagPips", 62f, 16f, 3f);
        AddLayoutSize(magPips.gameObject, 62f, 16f);

        TMP_Text spareCount = AddText(spareGroup, "SpareCount", "4", 15f,
            TextAlignmentOptions.MidlineRight, Color.white);

        WristAmmoPanel panel = root.AddComponent<WristAmmoPanel>();
        var so = new SerializedObject(panel);
        so.FindProperty("canvasGroup").objectReferenceValue = group;
        so.FindProperty("weaponLabel").objectReferenceValue = weaponLabel;
        so.FindProperty("chamberLabel").objectReferenceValue = chamberLabel;
        so.FindProperty("chamberDot").objectReferenceValue = chamberDot;
        so.FindProperty("currentText").objectReferenceValue = currentText;
        so.FindProperty("capacityText").objectReferenceValue = capacityText;
        so.FindProperty("bulletStrip").objectReferenceValue = bulletStrip;
        so.FindProperty("magPips").objectReferenceValue = magPips;
        so.FindProperty("spareCountText").objectReferenceValue = spareCount;

        Transform wrist = FindHand(rightHand, out string wristName);

        if (wrist != null)
        {
            Undo.SetTransformParent(root.transform, wrist, "Attach Wrist Ammo Panel");
            root.transform.localPosition = WristOffset;
            root.transform.localEulerAngles = WristEuler;

            XROrigin origin = wrist.GetComponentInParent<XROrigin>();

            if (origin != null && origin.Camera != null)
            {
                so.FindProperty("head").objectReferenceValue = origin.Camera.transform;

                // Canvas World Space không có Event Camera thì Unity kêu cảnh báo mỗi lần mở scene.
                canvas.worldCamera = origin.Camera;
            }
        }

        so.ApplyModifiedPropertiesWithoutUndo();

        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);

        Debug.Log(wrist != null
            ? $"Đã tạo WristAmmoPanel và gắn vào \"{wristName}\". Vào Scene view kéo panel cho vừa " +
              "mu bàn tay, nhưng ĐỪNG xoay lung tung: xoay quá nửa vòng là nhìn vào mặt sau canvas, " +
              "chữ lộn ngược. Lỡ xoay hỏng thì chạy Tools > VR SWAT > Fix Wrist Ammo Panel Orientation."
            : $"Đã tạo WristAmmoPanel nhưng chưa thấy {(rightHand ? "Right" : "Left")} Controller " +
              "trong scene: tự kéo panel vào tay, xoay mặt panel (+Z) ngửa ra khỏi mu bàn tay, rồi " +
              "gán Head = Main Camera.", root);
    }

    /// <summary>Tìm tay trong scene: ưu tiên Left / Right Controller của XR Origin.</summary>
    private static Transform FindHand(bool rightHand, out string foundName)
    {
        foundName = null;
        XROrigin origin = Object.FindFirstObjectByType<XROrigin>();

        if (origin == null)
            return null;

        string side = rightHand ? "right" : "left";

        foreach (Transform child in origin.GetComponentsInChildren<Transform>(true))
        {
            if (!child.name.ToLowerInvariant().Contains(side) || !IsHand(child))
                continue;

            foundName = child.name;
            return child;
        }

        return null;
    }

    /// <summary>Node này là gốc bàn tay, không phải mấy node con lắt nhắt bên trong nó.</summary>
    private static bool IsHand(Transform candidate)
    {
        string lower = candidate.name.ToLowerInvariant();

        if (!lower.Contains("controller") && !lower.Contains("hand"))
            return false;

        return !lower.Contains("attach") && !lower.Contains("stabilized") && !lower.Contains("ray")
               && !lower.Contains("model") && !lower.Contains("interactor");
    }

    /// <summary>Hàng neo mép trên của panel, cao cố định, trừ lề trái phải 16.</summary>
    private static RectTransform AddRow(Transform parent, string name, float height, float top)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(-32f, height);
        rect.anchoredPosition = new Vector2(0f, top);

        return rect;
    }

    /// <summary>Nhóm dồn về mép phải, rộng theo đúng nội dung bên trong.</summary>
    private static RectTransform AddAutoRow(Transform parent, string name, float spacing)
    {
        var go = new GameObject(name, typeof(RectTransform),
            typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(0f, 0f);
        rect.anchoredPosition = Vector2.zero;

        HorizontalLayoutGroup layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        return rect;
    }

    /// <summary>
    /// Hàng rộng cố định, chia đều cho các ô con. Vạch đạn và ô băng dự trữ do script sinh lúc
    /// chạy nên không có kích thước riêng: phải Force Expand thì chúng mới chia nhau chỗ trống.
    /// </summary>
    private static RectTransform AddFixedRow(Transform parent, string name, float width,
        float height, float spacing)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.sizeDelta = new Vector2(width, height);

        HorizontalLayoutGroup layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        return rect;
    }

    private static Image AddImage(Transform parent, string name, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(Image));
        go.transform.SetParent(parent, false);

        Image image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;

        return image;
    }

    private static TMP_Text AddText(Transform parent, string name, string content, float size,
        TextAlignmentOptions alignment, Color color)
    {
        var go = new GameObject(name, typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        var text = go.GetComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;

        return text;
    }

    private static void AddLayoutSize(GameObject go, float width, float height)
    {
        LayoutElement element = go.AddComponent<LayoutElement>();
        element.preferredWidth = width;
        element.preferredHeight = height;
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }
}
