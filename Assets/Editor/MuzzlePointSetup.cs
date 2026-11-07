using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tạo/chỉnh object MuzzlePoint cho từng khẩu súng trong scene đang mở và gán vào field
/// muzzlePoint của component Gun, sao cho +Z (forward) của MuzzlePoint trùng hướng nòng.
///
/// Vì sao cần: Gun.FireRaycast bắn theo origin.forward (+Z), nhưng model súng trong project
/// có trục nòng là -Y (Rifle) và -X (Pistol). Trỏ muzzlePoint vào object mesh của nòng sẽ
/// cho forward = +Z và lệch 90 độ. Shotgun đã có MuzzlePoint xoay đúng nên không bị.
///
/// Chạy qua menu Tools > VR SWAT > Setup Muzzle Points.
/// </summary>
public static class MuzzlePointSetup
{
    private const string MuzzlePointName = "MuzzlePoint";

    /// <summary>Thông số muzzle point của 1 khẩu, trong local space của chính gốc khẩu súng đó.</summary>
    private struct MuzzleSetup
    {
        public string gunObjectName;
        public Vector3 localPosition;
        public Vector3 localEuler;
        public string note;
    }

    private static readonly MuzzleSetup[] Setups =
    {
        new MuzzleSetup
        {
            gunObjectName = "Rifle h",
            // Trùng vị trí object muzzle flash "GunShot" - điểm xa nhất về phía trước trên trục nòng.
            localPosition = new Vector3(0.000119f, -0.3813f, 0.0781f),
            // X = +90 biến forward (+Z) thành -Y = hướng nòng.
            localEuler = new Vector3(90f, 0f, 0f),
            note = "truc nong -Y (bang/dau ruoi sau/tay cam/dau ruoi truoc/nong don dieu theo -Y)"
        },
        new MuzzleSetup
        {
            gunObjectName = "Pistol",
            // Hơi quá đầu quy lát (Object012 ở x = -0.0286); z = 0.022 là cao độ trục nòng.
            localPosition = new Vector3(-0.035f, 0f, 0.022f),
            // Y = -90 biến forward (+Z) thành -X = hướng nòng.
            localEuler = new Vector3(0f, -90f, 0f),
            note = "truc nong -X (quy lat keo lui ve +X)"
        }
    };

    [MenuItem("Tools/VR SWAT/Setup Muzzle Points")]
    private static void SetupAll()
    {
        Gun[] guns = Object.FindObjectsByType<Gun>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (guns.Length == 0)
        {
            Debug.LogWarning("[MuzzlePointSetup] Không tìm thấy component Gun nào trong scene đang mở.");
            return;
        }

        int changed = 0;

        foreach (MuzzleSetup setup in Setups)
        {
            Gun gun = FindGunByName(guns, setup.gunObjectName);

            if (gun == null)
            {
                Debug.LogWarning($"[MuzzlePointSetup] Không thấy khẩu '{setup.gunObjectName}' trong scene, bỏ qua.");
                continue;
            }

            if (Apply(gun, setup))
                changed++;
        }

        ReportOtherGuns(guns);

        if (changed > 0)
        {
            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log($"[MuzzlePointSetup] Xong {changed} khẩu. Nhớ Ctrl+S để lưu scene.");
        }
    }

    private static Gun FindGunByName(Gun[] guns, string gunObjectName)
    {
        foreach (Gun gun in guns)
        {
            if (gun.gameObject.name == gunObjectName)
                return gun;
        }

        return null;
    }

    private static bool Apply(Gun gun, MuzzleSetup setup)
    {
        Transform gunTransform = gun.transform;
        Transform muzzle = gunTransform.Find(MuzzlePointName);

        if (muzzle == null)
        {
            GameObject created = new GameObject(MuzzlePointName);
            Undo.RegisterCreatedObjectUndo(created, "Create MuzzlePoint");
            muzzle = created.transform;
            muzzle.SetParent(gunTransform, false);
        }
        else
        {
            Undo.RecordObject(muzzle, "Update MuzzlePoint");
        }

        muzzle.localPosition = setup.localPosition;
        muzzle.localRotation = Quaternion.Euler(setup.localEuler);
        muzzle.localScale = Vector3.one;

        // muzzlePoint là [SerializeField] private nên phải gán qua SerializedObject.
        var serializedGun = new SerializedObject(gun);
        SerializedProperty property = serializedGun.FindProperty("muzzlePoint");

        if (property == null)
        {
            Debug.LogError($"[MuzzlePointSetup] Không tìm thấy field 'muzzlePoint' trên Gun của '{gun.name}'.");
            return false;
        }

        property.objectReferenceValue = muzzle;
        serializedGun.ApplyModifiedProperties();

        Vector3 worldDirection = muzzle.forward;
        Debug.Log(
            $"[MuzzlePointSetup] {gun.name}: MuzzlePoint tại local {setup.localPosition}, " +
            $"rotation {setup.localEuler} -> forward world {worldDirection} ({setup.note}).",
            muzzle.gameObject);

        return true;
    }

    /// <summary>Cảnh báo những khẩu không nằm trong bảng mà cũng chưa gán muzzlePoint.</summary>
    private static void ReportOtherGuns(Gun[] guns)
    {
        foreach (Gun gun in guns)
        {
            bool handled = false;

            foreach (MuzzleSetup setup in Setups)
            {
                if (gun.gameObject.name == setup.gunObjectName)
                {
                    handled = true;
                    break;
                }
            }

            if (handled)
                continue;

            var serializedGun = new SerializedObject(gun);
            SerializedProperty property = serializedGun.FindProperty("muzzlePoint");
            Object assigned = property != null ? property.objectReferenceValue : null;

            if (assigned == null)
            {
                Debug.LogWarning(
                    $"[MuzzlePointSetup] '{gun.name}' chưa gán muzzlePoint và không có trong bảng setup. " +
                    "Khẩu này sẽ bắn theo forward của chính gốc súng.",
                    gun.gameObject);
            }
            else
            {
                Debug.Log($"[MuzzlePointSetup] '{gun.name}' đã có muzzlePoint ('{assigned.name}'), không chỉnh.", gun.gameObject);
            }
        }
    }
}
