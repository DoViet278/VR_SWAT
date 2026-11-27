using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Vẽ đúng cái đồ trong Starting Items tại chỗ nó sẽ nằm lúc chạy, ngay trên Scene view.
///
/// Chạy tĩnh (InitializeOnLoad) chứ không nằm trong ItemPouchEditor, vì muốn kéo một anchor thì
/// phải chọn chính anchor đó — lúc đó ItemPouch không còn được select nữa và inspector của nó
/// đã đóng. Preview phải sống độc lập với selection mới dùng được.
///
/// Chỉ VẼ mesh (Graphics.DrawMeshNow), không Instantiate GameObject nào: không có nguy cơ lọt
/// object rác vào prefab, không làm prefab dirty, đóng Unity là hết.
/// </summary>
[InitializeOnLoad]
public static class ItemPouchPreview
{
    private const string ShowPreviewKey = "VRSwat.ItemPouch.ShowPreview";

    private static readonly List<ItemPouch> Pouches = new List<ItemPouch>();

    private static Material fallbackMaterial;

    public static bool Enabled
    {
        get => EditorPrefs.GetBool(ShowPreviewKey, true);
        set
        {
            EditorPrefs.SetBool(ShowPreviewKey, value);
            SceneView.RepaintAll();
        }
    }

    static ItemPouchPreview()
    {
        SceneView.duringSceneGui -= OnSceneGui;
        SceneView.duringSceneGui += OnSceneGui;
    }

    private static void OnSceneGui(SceneView view)
    {
        if (!Enabled || Event.current.type != EventType.Repaint)
            return;

        if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
            return;

        Pouches.Clear();
        // Trong Prefab Mode thì cái này quét đúng stage của prefab đang mở, nên túi đồ nằm
        // trong prefab (không scene nào chứa nó) vẫn thấy được.
        Pouches.AddRange(Object.FindObjectsByType<ItemPouch>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));

        for (int i = 0; i < Pouches.Count; i++)
            DrawPouch(Pouches[i]);
    }

    private static void DrawPouch(ItemPouch pouch)
    {
        if (pouch == null || !pouch.gameObject.activeInHierarchy)
            return;

        IReadOnlyList<ItemPouch.LoadoutEntry> items = pouch.StartingItems;

        if (items == null)
            return;

        bool[] taken = new bool[Mathf.Max(1, pouch.ResolvedSlotCount)];

        for (int i = 0; i < items.Count; i++)
        {
            ItemPouch.LoadoutEntry entry = items[i];

            if (entry == null || entry.prefab == null)
                continue;

            int slot = ResolveSlot(pouch, entry.prefab, entry.slotIndex, taken, out bool mismatch);

            if (slot < 0)
                continue;

            if (!TryGetItemRootPose(pouch, entry.prefab, slot, out Vector3 rootPosition,
                    out Quaternion rootRotation, out Vector3 rootScale, out Vector3 socketPosition))
                continue;

            DrawPrefabMeshes(entry.prefab, Matrix4x4.TRS(rootPosition, rootRotation, rootScale));

            Handles.Label(socketPosition, mismatch
                ? $"[!] {slot}: {entry.prefab.name} — ô này không nhận loại này"
                : $"{slot}: {entry.prefab.name}");
        }
    }

    /// <summary>
    /// Cùng luật với ResolveSlot lúc chạy, kể cả phần xét loại của từng ô.
    ///
    /// Khác một chỗ: nếu không ô nào nhận thì vẫn trả về ô người dùng chỉ định và bật mismatch,
    /// để nhìn preview là thấy ngay chỗ nào sai — thay vì vật lặng lẽ biến mất và không hiểu tại sao.
    /// </summary>
    internal static int ResolveSlot(ItemPouch pouch, GameObject prefab, int wanted, bool[] taken, out bool mismatch)
    {
        mismatch = false;
        Transform item = prefab.transform;

        if (wanted >= 0 && wanted < taken.Length && !taken[wanted] && pouch.CanStoreInSlot(wanted, item))
        {
            taken[wanted] = true;
            return wanted;
        }

        for (int i = 0; i < taken.Length; i++)
        {
            if (taken[i] || !pouch.CanStoreInSlot(i, item))
                continue;

            taken[i] = true;
            return i;
        }

        mismatch = true;

        if (wanted >= 0 && wanted < taken.Length && !taken[wanted])
        {
            taken[wanted] = true;
            return wanted;
        }

        for (int i = 0; i < taken.Length; i++)
        {
            if (taken[i])
                continue;

            taken[i] = true;
            return i;
        }

        return -1;
    }

    /// <summary>
    /// Tư thế mà gốc của prefab sẽ có khi nằm trong ô.
    ///
    /// XRI không đặt gốc vật vào tâm ô: nó khớp Attach Transform của vật vào điểm neo của ô.
    /// Nên preview phải lùi gốc vật lại đúng bằng nghịch đảo của phép biến đổi gốc -> attach,
    /// y như XRI làm. Bỏ bước này là preview lệch hẳn so với lúc chạy, rõ nhất ở băng đạn.
    /// </summary>
    private static bool TryGetItemRootPose(ItemPouch pouch, GameObject prefab, int slot,
        out Vector3 rootPosition, out Quaternion rootRotation, out Vector3 rootScale,
        out Vector3 socketPosition)
    {
        rootPosition = Vector3.zero;
        rootRotation = Quaternion.identity;
        rootScale = Vector3.one;
        socketPosition = Vector3.zero;

        Transform itemRoot = prefab.transform;
        Transform attach = ResolveSocketAttach(prefab);

        if (attach == null)
            return false;

        pouch.GetSlotLocalPose(slot, out Vector3 slotLocalPosition, out Quaternion slotLocalRotation);

        // Điểm neo của ô: PouchItem trên vật ghi đè được, đúng như ApplyItemPose lúc chạy.
        Vector3 attachSlotPosition = Vector3.zero;
        Quaternion attachSlotRotation = Quaternion.identity;
        PouchItem pouchItem = prefab.GetComponentInChildren<PouchItem>();

        if (pouchItem != null && pouchItem.OverrideSocketPose)
        {
            attachSlotPosition = pouchItem.PositionOffset;
            attachSlotRotation = pouchItem.RotationOffset;
        }

        Transform pouchTransform = pouch.transform;
        socketPosition = pouchTransform.TransformPoint(slotLocalPosition + slotLocalRotation * attachSlotPosition);
        Quaternion socketRotation = pouchTransform.rotation * slotLocalRotation * attachSlotRotation;

        rootScale = pouch.ScaleStoredItems ? pouch.StoredItemScale : itemRoot.localScale;

        Vector3 attachLocalPosition = itemRoot.InverseTransformPoint(attach.position);
        Quaternion attachLocalRotation = Quaternion.Inverse(itemRoot.rotation) * attach.rotation;

        rootRotation = socketRotation * Quaternion.Inverse(attachLocalRotation);
        rootPosition = socketPosition - rootRotation * Vector3.Scale(attachLocalPosition, rootScale);

        return true;
    }

    /// <summary>
    /// Điểm neo mà socket sẽ khớp vào, theo đúng thứ tự ưu tiên của lúc chạy.
    /// </summary>
    private static Transform ResolveSocketAttach(GameObject prefab)
    {
        // Băng đạn tự quyết định neo của nó khi nằm trong socket: Magazine.RefreshAttachTransform ghi
        // thẳng vào grab.attachTransform. Phải tin SocketAttachPoint và KHÔNG được rơi về Attach
        // Transform của tay: hai prefab băng đạn đang bật Separate Socket Attach nhưng để trống
        // Socket Attach Transform, nên neo của socket chính là GỐC băng, lệch hẳn so với neo tay.
        Magazine magazine = prefab.GetComponent<Magazine>();

        if (magazine != null)
            return magazine.SocketAttachPoint;

        XRGrabInteractable grab = prefab.GetComponent<XRGrabInteractable>();

        if (grab != null && grab.attachTransform != null)
            return grab.attachTransform;

        return prefab.transform;
    }

    private static void DrawPrefabMeshes(GameObject prefab, Matrix4x4 rootMatrix)
    {
        Matrix4x4 worldToPrefabRoot = prefab.transform.worldToLocalMatrix;
        MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);

        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            Mesh mesh = filter.sharedMesh;

            if (mesh == null)
                continue;

            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();

            if (renderer == null || !renderer.enabled)
                continue;

            Matrix4x4 matrix = rootMatrix * worldToPrefabRoot * filter.transform.localToWorldMatrix;
            Material[] materials = renderer.sharedMaterials;

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                Material material = sub < materials.Length ? materials[sub] : null;

                // Shader của vật không chịu SetPass (hay chưa gán material): vẫn vẽ bằng material
                // tạm để còn thấy hình khối mà xếp chỗ.
                if (material == null || !material.SetPass(0))
                {
                    if (!GetFallbackMaterial().SetPass(0))
                        continue;
                }

                Graphics.DrawMeshNow(mesh, matrix, sub);
            }
        }
    }

    private static Material GetFallbackMaterial()
    {
        if (fallbackMaterial == null)
        {
            fallbackMaterial = new Material(Shader.Find("Hidden/Internal-Colored"))
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            fallbackMaterial.SetColor("_Color", new Color(0.4f, 0.8f, 1f, 1f));
        }

        return fallbackMaterial;
    }
}

/// <summary>
/// Inspector của ItemPouch: chuyển các ô thành GameObject con để kéo tay trên Scene view, và
/// bảng tóm tắt ô nào nhận loại gì / đang có món nào.
/// </summary>
[CustomEditor(typeof(ItemPouch))]
public class ItemPouchEditor : Editor
{
    private const string AnchorNamePrefix = "Slot ";
    private const string UndoLabel = "Dung anchor tui do";

    private SerializedProperty layoutProp;
    private SerializedProperty slotAnchorsProp;

    private void OnEnable()
    {
        layoutProp = serializedObject.FindProperty("layout");
        slotAnchorsProp = serializedObject.FindProperty("slotAnchors");
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        ItemPouch pouch = target as ItemPouch;

        if (pouch == null || layoutProp == null || slotAnchorsProp == null)
            return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Ô nào nhận loại gì", EditorStyles.boldLabel);
        DrawSlotSummary(pouch);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Chỉnh ô ngay trên Scene view", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "1. Bấm \"Dựng anchor từ layout hiện tại\": mỗi ô thành một GameObject con, Layout chuyển sang Anchors.\n" +
            "2. Chọn từng anchor rồi kéo/xoay bằng gizmo thường. Preview chạy theo ngay.\n" +
            "3. Lưu prefab. Vào Play thì đồ nằm đúng chỗ preview đứng.",
            MessageType.None);

        bool usingAnchors = layoutProp.enumValueIndex == (int)ItemPouch.SlotLayout.Anchors;
        int validAnchors = CountValidAnchors();

        EditorGUILayout.LabelField("Số ô thực tế", usingAnchors
            ? $"{pouch.ResolvedSlotCount}  (Anchors: {validAnchors}/{slotAnchorsProp.arraySize} anchor có gán)"
            : $"{pouch.ResolvedSlotCount}  ({(ItemPouch.SlotLayout)layoutProp.enumValueIndex})");

        if (GUILayout.Button("Dựng anchor từ layout hiện tại"))
            BuildAnchors(pouch);

        bool show = EditorGUILayout.Toggle("Hiện preview vật phẩm", ItemPouchPreview.Enabled);

        if (show != ItemPouchPreview.Enabled)
            ItemPouchPreview.Enabled = show;

        using (new EditorGUI.DisabledScope(!usingAnchors))
        {
            if (GUILayout.Button("Bỏ dùng anchor (giữ lại các GameObject)"))
            {
                layoutProp.enumValueIndex = (int)ItemPouch.SlotLayout.Arc;
                slotAnchorsProp.ClearArray();
                serializedObject.ApplyModifiedProperties();
                SceneView.RepaintAll();
            }
        }

        if (usingAnchors && validAnchors < slotAnchorsProp.arraySize)
        {
            EditorGUILayout.HelpBox(
                $"Có {slotAnchorsProp.arraySize - validAnchors} anchor để trống: những ô đó rơi về vị trí tự tính, " +
                "không theo anchor. Bấm \"Dựng anchor từ layout hiện tại\" để tạo và gán cho đủ.",
                MessageType.Warning);
        }
    }

    /// <summary>
    /// Bảng "ô nào nhận gì, đang có món nào" — tính bằng đúng luật của lúc chạy, nên nó cũng là
    /// chỗ phát hiện sớm việc xếp một món vào ô không nhận loại đó.
    /// </summary>
    private void DrawSlotSummary(ItemPouch pouch)
    {
        int count = Mathf.Max(1, pouch.ResolvedSlotCount);
        IReadOnlyList<ItemPouch.LoadoutEntry> items = pouch.StartingItems;
        bool[] taken = new bool[count];
        string[] occupant = new string[count];
        List<string> rejected = new List<string>();

        if (items != null)
        {
            for (int i = 0; i < items.Count; i++)
            {
                ItemPouch.LoadoutEntry entry = items[i];

                if (entry == null || entry.prefab == null)
                    continue;

                int slot = ItemPouchPreview.ResolveSlot(pouch, entry.prefab, entry.slotIndex, taken, out bool mismatch);

                if (slot < 0 || mismatch)
                {
                    rejected.Add(entry.prefab.name);
                    continue;
                }

                occupant[slot] = entry.prefab.name;
            }
        }

        for (int i = 0; i < count; i++)
        {
            PouchItemKind allowed = pouch.GetAllowedKinds(i);
            string kinds = allowed == PouchItemKind.None ? "(luật chung)" : allowed.ToString();
            string item = string.IsNullOrEmpty(occupant[i]) ? "trống" : occupant[i];

            EditorGUILayout.LabelField($"Ô {i}", $"{kinds}   →   {item}");
        }

        if (rejected.Count > 0)
        {
            EditorGUILayout.HelpBox(
                "Không ô nào nhận: " + string.Join(", ", rejected) +
                ".\nSửa Slot Allowed Kinds của ô, hoặc bỏ món đó khỏi Starting Items.",
                MessageType.Warning);
        }
    }

    private int CountValidAnchors()
    {
        int count = 0;

        for (int i = 0; i < slotAnchorsProp.arraySize; i++)
        {
            if (slotAnchorsProp.GetArrayElementAtIndex(i).objectReferenceValue != null)
                count++;
        }

        return count;
    }

    /// <summary>
    /// Tạo một GameObject con cho mỗi ô, đặt đúng chỗ layout đang tính, rồi chuyển sang Anchors.
    /// Nhờ vậy bạn bắt đầu từ đúng cái đang có chứ không phải xếp lại từ đầu.
    /// </summary>
    private void BuildAnchors(ItemPouch pouch)
    {
        int count = pouch.ResolvedSlotCount;

        // Phải đọc tư thế TRƯỚC khi đổi Layout sang Anchors: sau khi đổi thì GetSlotLocalPose
        // đọc lại chính các anchor, và vị trí gốc mất luôn.
        Vector3[] positions = new Vector3[count];
        Quaternion[] rotations = new Quaternion[count];

        for (int i = 0; i < count; i++)
            pouch.GetSlotLocalPose(i, out positions[i], out rotations[i]);

        List<Transform> anchors = new List<Transform>();

        for (int i = 0; i < slotAnchorsProp.arraySize; i++)
            anchors.Add(slotAnchorsProp.GetArrayElementAtIndex(i).objectReferenceValue as Transform);

        while (anchors.Count < count)
            anchors.Add(null);

        Undo.SetCurrentGroupName(UndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        for (int i = 0; i < count; i++)
        {
            Transform anchor = anchors[i];

            if (anchor == null)
            {
                GameObject anchorObject = new GameObject(AnchorNamePrefix + i);
                Undo.RegisterCreatedObjectUndo(anchorObject, UndoLabel);
                anchorObject.transform.SetParent(pouch.transform, false);
                anchor = anchorObject.transform;
                anchors[i] = anchor;
            }
            else
            {
                Undo.RecordObject(anchor, UndoLabel);
            }

            anchor.localPosition = positions[i];
            anchor.localRotation = rotations[i];
            anchor.localScale = Vector3.one;
        }

        slotAnchorsProp.arraySize = count;

        for (int i = 0; i < count; i++)
            slotAnchorsProp.GetArrayElementAtIndex(i).objectReferenceValue = anchors[i];

        layoutProp.enumValueIndex = (int)ItemPouch.SlotLayout.Anchors;
        serializedObject.ApplyModifiedProperties();

        Undo.CollapseUndoOperations(undoGroup);
        SceneView.RepaintAll();
    }
}
