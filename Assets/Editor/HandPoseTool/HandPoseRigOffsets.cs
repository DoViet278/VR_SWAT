using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Nhớ độ lệch giữa GỐC MODEL TAY và ĐIỂM BÁM của interactor, cho mỗi bên tay.
///
/// Vì sao cần: lúc chơi, XRI kéo vật sao cho điểm bám của vật trùng với điểm bám của interactor.
/// Bàn tay thì không nằm ngay tại điểm bám đó - trong dự án này model tay đứng lệch 90° quanh
/// trục Z so với nó. Nên muốn bàn tay XEM TRƯỚC trong Editor đứng đúng như lúc chơi, phải đo
/// trước độ lệch ấy một lần rồi đặt bàn tay xem trước theo đúng độ lệch đó so với điểm bám của
/// vật. Không đo thì bàn tay xem trước lệch đúng chừng ấy so với lúc chơi, và dáng tay tạo ra sẽ
/// sai y như vậy.
///
/// Asset đi kèm nằm cùng thư mục. Thư mục Editor nên không vào build.
/// </summary>
public class HandPoseRigOffsets : ScriptableObject
{
    public const string AssetPath = "Assets/Editor/HandPoseTool/HandPoseRigOffsets.asset";

    [Serializable]
    public class SideOffset
    {
        public bool captured;

        [Tooltip("Vị trí gốc model tay trong local space của điểm bám của interactor.")]
        public Vector3 localPosition;

        [Tooltip("Góc xoay gốc model tay so với điểm bám của interactor.")]
        public Quaternion localRotation = Quaternion.identity;

        [Tooltip("Prefab model tay, để dựng bàn tay xem trước mà không cần scene có sẵn tay.")]
        public GameObject handModelPrefab;
    }

    [SerializeField] private SideOffset left = new SideOffset();
    [SerializeField] private SideOffset right = new SideOffset();

    public SideOffset Get(HandSide side)
    {
        return side == HandSide.Left ? left : right;
    }

    public bool IsCaptured(HandSide side)
    {
        SideOffset offset = Get(side);
        return offset != null && offset.captured;
    }

    /// <summary>Tìm asset, tạo mới nếu chưa có.</summary>
    public static HandPoseRigOffsets GetOrCreate()
    {
        var offsets = AssetDatabase.LoadAssetAtPath<HandPoseRigOffsets>(AssetPath);

        if (offsets != null)
            return offsets;

        offsets = CreateInstance<HandPoseRigOffsets>();

        // AssetDatabase dùng dấu '/', còn Path.GetDirectoryName trên Windows trả về dấu '\' nên
        // IsValidFolder luôn báo false. Cắt tay cho chắc.
        string folder = AssetPath.Substring(0, AssetPath.LastIndexOf('/'));

        if (!AssetDatabase.IsValidFolder(folder))
        {
            System.IO.Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
        }

        AssetDatabase.CreateAsset(offsets, AssetPath);
        AssetDatabase.SaveAssets();

        return offsets;
    }

    /// <summary>
    /// Đo lại độ lệch cho cả hai tay từ scene đang mở. Trả về true nếu đo được ít nhất một bên.
    /// </summary>
    public static bool CalibrateBoth(out string report)
    {
        var text = new StringBuilder();

        // Lúc Play thì PhysicsHands đã tháo bàn tay ra khỏi Controller, số đo vô nghĩa.
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            report = "Đang ở Play mode. PhysicsHands đã tháo bàn tay khỏi Controller nên số đo " +
                     "sẽ sai. Thoát Play rồi căn lại.";
            return false;
        }

        HandPoseRigOffsets offsets = GetOrCreate();
        bool any = false;

        foreach (HandSide side in new[] { HandSide.Left, HandSide.Right })
            any |= Calibrate(offsets, side, text);

        if (any)
        {
            EditorUtility.SetDirty(offsets);
            AssetDatabase.SaveAssets();
        }

        report = text.ToString().TrimEnd();
        return any;
    }

    private static bool Calibrate(HandPoseRigOffsets offsets, HandSide side, StringBuilder text)
    {
        string label = HandJoints.Label(side);

        XRBaseInteractor interactor = FindInteractor(side);

        if (interactor == null)
        {
            text.AppendLine(label + ": không tìm thấy interactor nào đặt Handedness = " +
                            (side == HandSide.Left ? "Left" : "Right") +
                            ". Mở NearFarInteractor của bên đó và đặt Handedness (đừng để None).");
            return false;
        }

        HandSkeleton skeleton = FindSkeleton(side);

        if (skeleton == null)
        {
            text.AppendLine(label + ": không tìm thấy model tay có HandSkeleton. Chạy " +
                            "Tools > VR SWAT > Setup Hand Pose trước.");
            return false;
        }

        Transform attach = interactor.attachTransform != null ? interactor.attachTransform : interactor.transform;
        Transform hand = skeleton.transform;

        SideOffset offset = offsets.Get(side);
        offset.localPosition = attach.InverseTransformPoint(hand.position);
        offset.localRotation = Quaternion.Inverse(attach.rotation) * hand.rotation;
        offset.handModelPrefab = ResolvePrefab(skeleton.gameObject);
        offset.captured = true;

        offset.localRotation.ToAngleAxis(out float angle, out Vector3 axis);

        if (float.IsNaN(angle) || float.IsInfinity(axis.x))
        {
            angle = 0f;
            axis = Vector3.up;
        }

        text.AppendLine($"{label}: lệch {angle:0.#}° quanh ({axis.x:0.##}, {axis.y:0.##}, {axis.z:0.##}), " +
                        $"cách {offset.localPosition.magnitude * 100f:0.#}cm so với '{attach.name}'.");

        if (offset.handModelPrefab == null)
        {
            text.AppendLine($"  Lưu ý: '{hand.name}' không phải instance của prefab nào, nên không " +
                            "lưu được prefab model tay. Bàn tay xem trước sẽ phải nhân bản từ tay " +
                            "trong scene.");
        }

        return true;
    }

    /// <summary>Interactor có Handedness đúng bên. Ưu tiên cái có attach transform riêng.</summary>
    public static XRBaseInteractor FindInteractor(HandSide side)
    {
        InteractorHandedness wanted = side == HandSide.Left
            ? InteractorHandedness.Left
            : InteractorHandedness.Right;

        InteractorPick best = default;

        foreach (XRBaseInteractor interactor in UnityEngine.Object
                     .FindObjectsByType<XRBaseInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (interactor == null || interactor.handedness != wanted)
                continue;

            // NearFarInteractor là cái thật sự cầm vật; các interactor khác (poke, UI ray) có thể
            // cũng đặt handedness nhưng attach transform của chúng không phải chỗ tay bám.
            int score = 0;

            if (interactor is NearFarInteractor)
                score += 2;

            if (interactor.attachTransform != null)
                score += 1;

            if (best.Interactor == null || score > best.Score)
                best = new InteractorPick { Interactor = interactor, Score = score };
        }

        return best.Interactor;
    }

    private struct InteractorPick
    {
        public XRBaseInteractor Interactor;
        public int Score;
    }

    /// <summary>Model tay trong scene đang mở có HandSkeleton đúng bên.</summary>
    public static HandSkeleton FindSkeleton(HandSide side)
    {
        foreach (HandSkeleton skeleton in UnityEngine.Object
                     .FindObjectsByType<HandSkeleton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (skeleton != null && skeleton.Handedness == side)
                return skeleton;
        }

        return null;
    }

    /// <summary>Prefab gốc của một instance trong scene, null nếu nó không phải prefab instance.</summary>
    public static GameObject ResolvePrefab(GameObject instance)
    {
        if (instance == null)
            return null;

        if (!PrefabUtility.IsPartOfPrefabInstance(instance))
            return null;

        GameObject root = PrefabUtility.GetNearestPrefabInstanceRoot(instance);

        if (root == null)
            root = instance;

        return PrefabUtility.GetCorrespondingObjectFromOriginalSource(root);
    }
}
