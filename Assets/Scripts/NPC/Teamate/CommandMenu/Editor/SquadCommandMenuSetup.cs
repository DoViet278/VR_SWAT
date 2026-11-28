using System.IO;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using VRSwat.Squad;

/// <summary>
/// Dựng sẵn panel menu lệnh VR cho đội và gắn luôn vào XR Origin trong scene đang mở.
///
/// Dựng tay thì phải nhớ đủ thứ lắt nhắt: Canvas phải World Space, RectTransform phải thu nhỏ cỡ
/// 0.001 không thì panel to bằng cả căn phòng, dòng mẫu phải nằm sẵn trong Rows Root và phải
/// TẮT, rồi còn phải gán chéo một mớ tham chiếu. Menu này làm hết.
///
/// Font: TMP mặc định (LiberationSans SDF) bake atlas TĨNH chỉ có 250 glyph Latin-1, nên chữ
/// tiếng Việt có dấu móc hiện ra ô vuông ("CỬA ĐÓNG" thành "C□A □ÓNG"). Tool tự tạo một font
/// asset chế độ ĐỘNG từ Inter-Regular.ttf ở <see cref="FontAssetPath"/> — chữ nào chưa có thì
/// TMP tự render thêm vào atlas lúc chạy, nên đủ mọi dấu. File TTF trong project đã có sẵn
/// bảng mã tiếng Việt đầy đủ, vấn đề chỉ nằm ở atlas.
///
/// Chạy qua Tools &gt; VR SWAT &gt; Teammate &gt; Create Squad Command Menu. Chạy lại nhiều lần
/// cũng được: panel cũ bị dỡ đi và dựng lại theo kiểu mới nhất, tham chiếu trên XR Origin tự nối
/// lại.
/// </summary>
public static class SquadCommandMenuSetup
{
    private const string PanelName = "Squad Command Menu";

    private const string FontFolder = "Assets/Fonts";
    private const string FontAssetPath = FontFolder + "/VRSwat UI SDF.asset";
    private const string InterPath = "Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular.ttf";
    private const string LiberationPath = "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";

    /// <summary>Bề ngang panel tính theo đơn vị UI. 540 * 0.001 = 54 cm ngang.</summary>
    private const float PanelWidth = 540f;

    private const float PanelScale = 0.001f;
    private const float BorderThickness = 2f;
    private const float RowHeight = 62f;
    private const float TitleHeight = 38f;
    private const float DividerHeight = 2f;

    // Bảng màu: xám than + xanh thép, kiểu bảng điều khiển chiến thuật.
    private static readonly Color BorderColor = new Color(0.20f, 0.42f, 0.54f, 0.95f);
    private static readonly Color PanelColor = new Color(0.035f, 0.045f, 0.052f, 0.96f);
    private static readonly Color DividerColor = new Color(0.20f, 0.45f, 0.58f, 0.75f);
    private static readonly Color TitleColor = new Color(0.52f, 0.70f, 0.80f);
    private static readonly Color RowColor = new Color(0.09f, 0.10f, 0.11f, 0.94f);
    private static readonly Color LabelColor = new Color(0.82f, 0.86f, 0.88f);
    private static readonly Color MarkerColor = new Color(0.35f, 0.72f, 0.95f);

    [MenuItem("Tools/VR SWAT/Teammate/Create Squad Command Menu")]
    public static void Create()
    {
        TMP_FontAsset font = EnsureFontAsset();

        // Dựng lại từ đầu thay vì dùng lại panel cũ: chạy tool lần nữa là để lấy kiểu mới nhất.
        SquadCommandMenuView old = Object.FindFirstObjectByType<SquadCommandMenuView>();

        if (old != null)
            Undo.DestroyObjectImmediate(old.gameObject);

        SquadCommandMenuView view = BuildPanel(font);
        int wired = WireToRig(view);

        // Không có đội thì menu mở ra cũng không ra lệnh được cho ai.
        TeammateSetupTool.AddSquadToScene();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = view.gameObject;
        EditorGUIUtility.PingObject(view.gameObject);

        Debug.Log(wired > 0
            ? $"[Squad Menu] Đã dựng panel \"{PanelName}\" (font {(font != null ? font.name : "mặc định")}) " +
              $"và gắn vào {wired} Squad Command Menu Input. Giữ nút X (hoặc A) trong lúc chơi để mở menu lệnh."
            : $"[Squad Menu] Đã dựng panel \"{PanelName}\" nhưng chưa thấy XR Origin trong scene. " +
              "Tự thêm component Squad Command Menu Input lên XR Origin rồi kéo panel vào ô View.",
            view.gameObject);
    }

    // ---------------- font ----------------

    /// <summary>
    /// Font asset chế độ động cho UI tiếng Việt. Có sẵn thì dùng lại, chưa có thì tạo từ
    /// Inter-Regular.ttf (không có Inter thì lùi về LiberationSans.ttf).
    /// </summary>
    private static TMP_FontAsset EnsureFontAsset()
    {
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);

        if (existing != null)
            return existing;

        Font source = AssetDatabase.LoadAssetAtPath<Font>(InterPath)
                      ?? AssetDatabase.LoadAssetAtPath<Font>(LiberationPath);

        if (source == null)
        {
            Debug.LogWarning(
                $"[Squad Menu] Không tìm thấy {InterPath} lẫn {LiberationPath} nên panel dùng " +
                "font TMP mặc định. Chữ tiếng Việt có dấu móc sẽ hiện ra ô vuông.");
            return null;
        }

        if (!AssetDatabase.IsValidFolder(FontFolder))
            AssetDatabase.CreateFolder(Path.GetDirectoryName(FontFolder), Path.GetFileName(FontFolder));

        // Atlas động: chữ nào chưa có thì TMP render thêm lúc chạy, nên không lo thiếu dấu.
        TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(
            source, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);

        font.name = Path.GetFileNameWithoutExtension(FontAssetPath);
        AssetDatabase.CreateAsset(font, FontAssetPath);

        // Atlas và material phải nằm trong cùng asset, không thì mở lại project là mất.
        if (font.atlasTextures != null && font.atlasTextures.Length > 0 && font.atlasTextures[0] != null)
        {
            font.atlasTextures[0].name = font.name + " Atlas";
            AssetDatabase.AddObjectToAsset(font.atlasTextures[0], font);
        }

        if (font.material != null)
        {
            font.material.name = font.name + " Material";
            AssetDatabase.AddObjectToAsset(font.material, font);
        }

        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(FontAssetPath);

        Debug.Log($"[Squad Menu] Đã tạo font động {FontAssetPath} từ {AssetDatabase.GetAssetPath(source)} " +
                  "— dùng lại được cho mọi UI tiếng Việt khác trong game.");

        return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
    }

    // ---------------- panel ----------------

    private static SquadCommandMenuView BuildPanel(TMP_FontAsset font)
    {
        // Khung ngoài = viền. Nền thật nằm ở "Body" bên trong, thụt vào BorderThickness.
        var root = new GameObject(PanelName,
            typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        Undo.RegisterCreatedObjectUndo(root, "Create Squad Command Menu");

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var rect = (RectTransform)root.transform;
        rect.sizeDelta = new Vector2(PanelWidth, 200f);
        rect.localScale = Vector3.one * PanelScale;

        Image border = root.AddComponent<Image>();
        border.color = BorderColor;
        border.raycastTarget = false;

        Column(root, new RectOffset(
            (int)BorderThickness, (int)BorderThickness, (int)BorderThickness, (int)BorderThickness), 0f);

        GameObject body = Child(root.transform, "Body");
        Image bodyImage = body.AddComponent<Image>();
        bodyImage.color = PanelColor;
        bodyImage.raycastTarget = false;
        Column(body, new RectOffset(18, 18, 14, 16), 9f);

        TMP_Text title = BuildTitle(body.transform, font);
        BuildDivider(body.transform);
        RectTransform rowsRoot = BuildRowsRoot(body.transform);
        SquadCommandMenuRow template = BuildRowTemplate(rowsRoot, font);

        var view = root.AddComponent<SquadCommandMenuView>();
        var so = new SerializedObject(view);
        so.FindProperty("canvas").objectReferenceValue = canvas;
        so.FindProperty("canvasGroup").objectReferenceValue = root.GetComponent<CanvasGroup>();
        so.FindProperty("rowsRoot").objectReferenceValue = rowsRoot;
        so.FindProperty("rowTemplate").objectReferenceValue = template;
        so.FindProperty("title").objectReferenceValue = title;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Dòng mẫu phải tắt: view nhân bản nó ra chứ không dùng trực tiếp.
        template.gameObject.SetActive(false);

        return view;
    }

    /// <summary>Xếp con theo cột, cao tới đâu tùy nội dung.</summary>
    private static void Column(GameObject go, RectOffset padding, float spacing)
    {
        VerticalLayoutGroup column = go.AddComponent<VerticalLayoutGroup>();
        column.padding = padding;
        column.spacing = spacing;
        column.childControlWidth = true;
        column.childForceExpandWidth = true;
        column.childControlHeight = true;
        column.childForceExpandHeight = false;

        ContentSizeFitter fitter = go.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private static TMP_Text BuildTitle(Transform parent, TMP_FontAsset font)
    {
        GameObject go = Child(parent, "Title");
        var text = go.AddComponent<TextMeshProUGUI>();

        Apply(text, font, 25f, TitleColor);
        text.fontStyle = FontStyles.Bold;
        text.characterSpacing = 9f;
        text.alignment = TextAlignmentOptions.Left;
        text.text = "LỆNH ĐỘI";

        Height(go, TitleHeight);
        return text;
    }

    /// <summary>Vạch kẻ mảnh dưới tiêu đề, tách phần đầu panel khỏi danh sách lệnh.</summary>
    private static void BuildDivider(Transform parent)
    {
        GameObject go = Child(parent, "Divider");

        Image line = go.AddComponent<Image>();
        line.color = DividerColor;
        line.raycastTarget = false;

        Height(go, DividerHeight);
    }

    private static RectTransform BuildRowsRoot(Transform parent)
    {
        GameObject go = Child(parent, "Rows");

        VerticalLayoutGroup column = go.AddComponent<VerticalLayoutGroup>();
        column.spacing = 6f;
        column.childControlWidth = true;
        column.childForceExpandWidth = true;
        column.childControlHeight = true;
        column.childForceExpandHeight = false;

        ContentSizeFitter fitter = go.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return (RectTransform)go.transform;
    }

    private static SquadCommandMenuRow BuildRowTemplate(Transform parent, TMP_FontAsset font)
    {
        GameObject go = Child(parent, "Row Template");

        // Không gán sprite: Image vẽ hình chữ nhật sắc cạnh. Gán UISprite bo góc rồi kéo giãn
        // thì dòng phình ra như viên thuốc, nhìn rất không ra dáng bảng điều khiển.
        Image background = go.AddComponent<Image>();
        background.color = RowColor;
        background.raycastTarget = false;

        Height(go, RowHeight);

        // Vạch màu dọc bên trái: xanh = lệnh đội, vàng = lệnh gắn với một cánh cửa.
        GameObject markerGo = Child(go.transform, "Marker");
        var markerRect = (RectTransform)markerGo.transform;
        markerRect.anchorMin = new Vector2(0f, 0f);
        markerRect.anchorMax = new Vector2(0f, 1f);
        markerRect.pivot = new Vector2(0f, 0.5f);
        markerRect.offsetMin = new Vector2(12f, 12f);
        markerRect.offsetMax = new Vector2(18f, -12f);

        Image marker = markerGo.AddComponent<Image>();
        marker.color = MarkerColor;
        marker.raycastTarget = false;

        GameObject labelGo = Child(go.transform, "Label");
        var labelRect = (RectTransform)labelGo.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(34f, 0f);
        labelRect.offsetMax = new Vector2(-18f, 0f);

        var label = labelGo.AddComponent<TextMeshProUGUI>();
        Apply(label, font, 30f, LabelColor);
        label.fontStyle = FontStyles.Bold;
        label.characterSpacing = 4f;
        label.alignment = TextAlignmentOptions.Left;
        label.text = "LỆNH";

        var row = go.AddComponent<SquadCommandMenuRow>();
        var so = new SerializedObject(row);
        so.FindProperty("background").objectReferenceValue = background;
        so.FindProperty("marker").objectReferenceValue = marker;
        so.FindProperty("label").objectReferenceValue = label;
        so.ApplyModifiedPropertiesWithoutUndo();

        return row;
    }

    // ---------------- tiện ích dựng UI ----------------

    private static GameObject Child(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void Height(GameObject go, float height)
    {
        LayoutElement layout = go.AddComponent<LayoutElement>();
        layout.minHeight = height;
        layout.preferredHeight = height;
    }

    private static void Apply(TMP_Text text, TMP_FontAsset font, float size, Color color)
    {
        if (font != null)
            text.font = font;

        text.raycastTarget = false;
        text.enableAutoSizing = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.fontSize = size;
        text.color = color;
    }

    // ---------------- rig ----------------

    /// <summary>Gắn Squad Command Menu Input lên XR Origin và kéo panel vào ô View.</summary>
    private static int WireToRig(SquadCommandMenuView view)
    {
        var inputs = Object.FindObjectsByType<SquadCommandMenuInput>(FindObjectsSortMode.None);

        if (inputs.Length == 0)
        {
            XROrigin origin = Object.FindFirstObjectByType<XROrigin>();

            if (origin == null)
                return 0;

            Undo.RecordObject(origin.gameObject, "Add Squad Command Menu Input");
            inputs = new[] { origin.gameObject.AddComponent<SquadCommandMenuInput>() };
        }

        foreach (SquadCommandMenuInput input in inputs)
        {
            // Luôn nối lại: panel cũ vừa bị dỡ nên ô View đang trỏ vào chỗ trống.
            var so = new SerializedObject(input);
            so.FindProperty("view").objectReferenceValue = view;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(input);
        }

        return inputs.Length;
    }
}
