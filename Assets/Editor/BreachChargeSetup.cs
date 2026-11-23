using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRSwat.Doors;

/// <summary>
/// Dựng sẵn ba thứ cho luồng phá cửa bằng thuốc nổ: ổ dán bom trên cửa, quả bom, và kíp nổ.
///
/// Làm tay thì vướng toàn thứ lắt nhắt dễ quên: ổ dán phải là collider trigger chứ không phải
/// collider thường, phải nhô ra đúng mặt ngoài cánh cửa không thì cửa văng ngược vào mặt người
/// chơi, XR Grab Interactable phải để Instantaneous, và BoxCollider phải fit theo bounds của
/// mesh con rồi chia cho lossyScale — hai model đang để scale 2 và 3 nên bước chia đó mà quên
/// là collider to gấp mấy lần quả bom.
///
/// Chạy qua menu Tools &gt; VR SWAT &gt; Breach Charge.
/// </summary>
public static class BreachChargeSetup
{
    private const string ChargeModelPath = "Assets/Model/explosive.fbx";
    private const string DetonatorModelPath = "Assets/Model/Detonator/Detonator.fbx";

    // Cạnh ổ khóa, ngang ngực, nhô ra phía -Z vì tay nắm cửa nằm ở z = -0.098, tức đó là phía
    // người chơi đứng thao tác. DoorController.AwayAngle() đọc dấu Z này để chọn chiều văng.
    private static readonly Vector3 MountLocalPosition = new Vector3(-0.8f, 1f, -0.05f);
    private static readonly Vector3 MountTriggerSize = new Vector3(0.25f, 0.25f, 0.2f);

    [MenuItem("Tools/VR SWAT/Breach Charge/Setup Mount On Selected Door")]
    private static void SetupMount()
    {
        DoorController door = FindDoor(Selection.activeGameObject);

        if (door == null)
        {
            EditorUtility.DisplayDialog("Breach Charge",
                "Chọn một cửa (object có DoorController) trong Hierarchy rồi chạy lại.\n\n" +
                "Muốn cả 9 cửa trong scene có ổ dán bom thì mở prefab Door_0_Base rồi chạy trong " +
                "Prefab Mode, chứ đừng chạy trên từng instance.", "OK");
            return;
        }

        Transform mountPoint = FindDeep(door.transform, "BreachZone");

        if (mountPoint == null)
        {
            var created = new GameObject("BreachZone");
            Undo.RegisterCreatedObjectUndo(created, "Create Breach Zone");
            created.transform.SetParent(FindLeaf(door.transform), false);
            mountPoint = created.transform;
        }

        Undo.RecordObject(mountPoint, "Setup Charge Mount");
        mountPoint.localPosition = MountLocalPosition;
        mountPoint.localRotation = Quaternion.identity;

        BoxCollider trigger = mountPoint.GetComponent<BoxCollider>();

        if (trigger == null)
            trigger = Undo.AddComponent<BoxCollider>(mountPoint.gameObject);

        Undo.RecordObject(trigger, "Setup Charge Mount");
        trigger.isTrigger = true;
        trigger.size = MountTriggerSize;
        trigger.center = Vector3.zero;

        ChargeMount mount = mountPoint.GetComponent<ChargeMount>();

        if (mount == null)
            mount = Undo.AddComponent<ChargeMount>(mountPoint.gameObject);

        Undo.RecordObject(mount, "Setup Charge Mount");
        mount.showInteractableHoverMeshes = false;
        mount.recycleDelayTime = 0f;
        mount.socketScaleMode = SocketScaleMode.None;

        if (door.GetComponent<DoorBreachEffects>() == null)
            Undo.AddComponent<DoorBreachEffects>(door.gameObject);

        EditorUtility.SetDirty(mountPoint.gameObject);
        EditorUtility.SetDirty(door.gameObject);
        Selection.activeGameObject = mountPoint.gameObject;

        Debug.Log(
            $"Đã dựng ổ dán bom trên '{door.name}'. Còn phải gán hiệu ứng nổ và tiếng nổ vào " +
            "Door Breach Effects ở object cửa (dùng Blow.prefab + grenade explosion.wav).",
            mountPoint.gameObject);
    }

    [MenuItem("Tools/VR SWAT/Breach Charge/Create Charge (explosive.fbx)")]
    private static void CreateCharge()
    {
        GameObject root = BuildHandheld(ChargeModelPath, "BreachCharge", 0.4f);

        if (root == null)
            return;

        root.AddComponent<BreachCharge>();

        Debug.Log(
            "Đã dựng quả bom. Gán hiệu ứng nổ + tiếng nổ vào Breach Charge (hoặc để trống nếu " +
            "đã gán bên Door Breach Effects), rồi kéo nó vào Assets/Prefabs thành prefab.", root);
    }

    [MenuItem("Tools/VR SWAT/Breach Charge/Create Detonator (Detonator.fbx)")]
    private static void CreateDetonator()
    {
        GameObject root = BuildHandheld(DetonatorModelPath, "Detonator", 0.2f);

        if (root == null)
            return;

        root.AddComponent<Detonator>();

        Debug.Log(
            "Đã dựng kíp nổ. Gán AudioSource cho Fire Sfx và Empty Click nếu muốn có tiếng, rồi " +
            "kéo vào Assets/Prefabs thành prefab.", root);
    }

    /// <summary>
    /// Phần chung của bom và kíp nổ: bọc model trong một root sạch, fit collider, gắn vật lý và
    /// grab interactable.
    /// </summary>
    private static GameObject BuildHandheld(string modelPath, string rootName, float mass)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);

        if (model == null)
        {
            EditorUtility.DisplayDialog("Breach Charge", $"Không thấy model ở {modelPath}.", "OK");
            return null;
        }

        // Model bọc trong một root riêng thay vì gắn script thẳng lên node FBX: hai model đang
        // để scale 2 và 3 trong scene, mesh thì nằm ở node con, gắn thẳng là dính theo cái scale
        // đó và lệch cả pose tay lẫn hướng dán.
        var root = new GameObject(rootName);
        Undo.RegisterCreatedObjectUndo(root, $"Create {rootName}");

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.transform.SetParent(root.transform, false);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;

        BoxCollider box = root.AddComponent<BoxCollider>();
        FitBoxToRenderers(box);

        Rigidbody body = root.AddComponent<Rigidbody>();
        body.mass = mass;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var grab = root.AddComponent<XRGrabInteractable>();
        grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
        grab.throwOnDetach = false;   // đồ để dán và để bấm, không phải để ném

        Selection.activeGameObject = root;
        EditorUtility.SetDirty(root);

        return root;
    }

    private static DoorController FindDoor(GameObject selected)
    {
        if (selected == null)
            return null;

        DoorController door = selected.GetComponent<DoorController>();

        if (door == null)
            door = selected.GetComponentInParent<DoorController>();

        if (door == null)
            door = selected.GetComponentInChildren<DoorController>();

        return door;
    }

    // Cánh cửa là object xoay quanh bản lề; ổ dán bom phải nằm trên cánh chứ không trên khung,
    // không thì cửa mở ra mà quả bom đứng im giữa không khí.
    private static Transform FindLeaf(Transform doorRoot)
    {
        Transform leaf = FindDeep(doorRoot, "Leaf");

        if (leaf != null)
            return leaf;

        Transform hinge = FindDeep(doorRoot, "Hinge");

        if (hinge != null)
            return hinge.childCount > 0 ? hinge.GetChild(0) : hinge;

        Debug.LogWarning(
            $"{doorRoot.name}: không thấy 'Leaf' hay 'Hinge', đặt tạm ổ dán bom ngay dưới gốc cửa. " +
            "Nhớ kéo nó vào đúng cánh cửa.", doorRoot);

        return doorRoot;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name)
                return child;
        }

        return null;
    }

    private static void FitBoxToRenderers(BoxCollider box)
    {
        Renderer[] renderers = box.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
            return;

        Bounds bounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        Transform t = box.transform;
        Vector3 scale = t.lossyScale;

        box.center = t.InverseTransformPoint(bounds.center);
        box.size = new Vector3(
            bounds.size.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
            bounds.size.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
            bounds.size.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
    }
}
