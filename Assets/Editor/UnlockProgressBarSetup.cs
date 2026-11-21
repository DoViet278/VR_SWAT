using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VRSwat.Doors;

/// <summary>
/// Dựng sẵn thanh tiến trình phá khóa cho <see cref="LockpickKit"/>.
///
/// Dựng tay thì phải nhớ đủ thứ lắt nhắt: Canvas phải World Space, RectTransform phải thu nhỏ
/// cỡ 0.0006 không thì thanh to bằng cả căn phòng, Image phần đầy phải Filled + Horizontal +
/// gốc bên trái mới chạy được fillAmount. Menu này làm hết rồi gắn luôn vào kit đang có trong
/// scene.
///
/// Chạy qua menu Tools &gt; VR SWAT &gt; Create Unlock Progress Bar.
/// </summary>
public static class UnlockProgressBarSetup
{
    private const string BuiltinSprite = "UI/Skin/UISprite.psd";

    [MenuItem("Tools/VR SWAT/Create Unlock Progress Bar")]
    private static void Create()
    {
        Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(BuiltinSprite);

        var root = new GameObject("UnlockProgressBar", typeof(Canvas), typeof(CanvasScaler));
        Undo.RegisterCreatedObjectUndo(root, "Create Unlock Progress Bar");

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var rect = (RectTransform)root.transform;
        rect.sizeDelta = new Vector2(300f, 36f);
        rect.localScale = Vector3.one * 0.0006f;   // 300 * 0.0006 = 18 cm ngang

        Image background = AddImage(root.transform, "Background", sprite,
            new Color(0f, 0f, 0f, 0.7f), 0f);
        background.raycastTarget = false;

        Image fill = AddImage(root.transform, "Fill", sprite,
            new Color(0.25f, 0.85f, 0.4f, 1f), 4f);
        fill.raycastTarget = false;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 0f;

        TMP_Text label = AddLabel(root.transform);

        UnlockProgressBar bar = root.AddComponent<UnlockProgressBar>();
        var so = new SerializedObject(bar);
        so.FindProperty("canvas").objectReferenceValue = canvas;
        so.FindProperty("fill").objectReferenceValue = fill;
        so.FindProperty("label").objectReferenceValue = label;
        so.ApplyModifiedPropertiesWithoutUndo();

        int wired = WireToKits(bar);

        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);

        Debug.Log(wired > 0
            ? $"Đã tạo UnlockProgressBar và gắn vào {wired} Lockpick Kit trong scene."
            : "Đã tạo UnlockProgressBar. Chưa thấy Lockpick Kit nào trong scene, nhớ tự kéo vào " +
              "ô Progress Bar của kit.", root);
    }

    private static Image AddImage(Transform parent, string name, Sprite sprite, Color color,
        float inset)
    {
        var go = new GameObject(name, typeof(Image));
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);

        Image image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;

        return image;
    }

    private static TMP_Text AddLabel(Transform parent)
    {
        var go = new GameObject("Label", typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var text = go.GetComponent<TextMeshProUGUI>();
        text.raycastTarget = false;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = false;
        text.fontSize = 20f;
        text.color = Color.white;
        text.text = "0%";

        return text;
    }

    private static int WireToKits(UnlockProgressBar bar)
    {
        LockpickKit[] kits = Object.FindObjectsByType<LockpickKit>(FindObjectsSortMode.None);
        int wired = 0;

        foreach (LockpickKit kit in kits)
        {
            if (kit.ProgressBar != null)
                continue;

            Undo.RecordObject(kit, "Wire Unlock Progress Bar");
            kit.ProgressBar = bar;
            EditorUtility.SetDirty(kit);
            wired++;
        }

        return wired;
    }
}
