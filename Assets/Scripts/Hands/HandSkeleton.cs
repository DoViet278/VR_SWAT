using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dò và nhớ 16 xương ngón tay trên một model tay. Gắn lên GỐC model tay (chỗ có Animator).
///
/// Dò một lần rồi lưu vào mảng chứ không tìm theo tên mỗi frame: tìm theo tên trong
/// <c>LateUpdate</c> là 16 lần quét hierarchy mỗi frame mỗi tay.
///
/// Chỗ dễ sai nhất là model tay Oculus có HAI bộ xương cùng đuôi tên:
/// - <c>hands:b_l_index1</c> - xương thật của da (nằm trong <c>SkinnedMeshRenderer.bones</c>),
/// - <c>coll_hands:b_l_index1</c> - một bộ song song treo ngay dưới xương trên, chỉ để chứa
///   capsule collider.
/// Gán nhầm bộ <c>coll_</c> thì mình tạo dáng cho collider còn mesh không nhúc nhích. Nên
/// <see cref="Resolve"/> loại tên có <c>coll_</c>, loại đầu mút <c>*_ignore</c> (xương rỗng để
/// đo chiều dài đốt cuối), và ưu tiên xương nằm trong danh sách <c>bones</c> của skinned mesh.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("VR SWAT/Hand Skeleton")]
public class HandSkeleton : MonoBehaviour
{
    [Tooltip("Model này là tay nào. Quyết định dò xương b_l_* hay b_r_*.")]
    [SerializeField] private HandSide handedness = HandSide.Left;

    [Tooltip("Xương cổ tay (b_?_hand). Gốc của 16 khớp ngón.")]
    [SerializeField] private Transform wrist;

    [Tooltip("16 khớp ngón, xếp theo HandJointId. Bấm Dò lại xương nếu còn trống.")]
    [SerializeField] private Transform[] joints = new Transform[HandJoints.Count];

    private Collider[] fingerColliders;

    public HandSide Handedness
    {
        get => handedness;
        set => handedness = value;
    }

    public Transform Wrist => wrist;

    public IReadOnlyList<Transform> Joints
    {
        get
        {
            EnsureArray();
            return joints;
        }
    }

    /// <summary>Đã dò đủ 16 xương chưa.</summary>
    public bool IsResolved => MissingJointCount == 0;

    public int MissingJointCount
    {
        get
        {
            EnsureArray();
            int missing = 0;

            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] == null)
                    missing++;
            }

            return missing;
        }
    }

    public Transform GetJoint(HandJointId joint)
    {
        EnsureArray();
        return joints[(int)joint];
    }

    public void SetJoint(HandJointId joint, Transform bone)
    {
        EnsureArray();
        joints[(int)joint] = bone;
        fingerColliders = null;
    }

    private void Awake()
    {
        EnsureArray();

        if (!IsResolved)
            Resolve();
    }

    /// <summary>
    /// Dò lại 16 xương theo tên. Trả về true nếu tìm đủ.
    /// </summary>
    public bool Resolve()
    {
        EnsureArray();
        fingerColliders = null;

        wrist = ResolveWrist(transform, handedness);
        Transform[] resolved = ResolveJoints(transform, handedness);

        for (int i = 0; i < HandJoints.Count; i++)
            joints[i] = resolved[i];

        return IsResolved;
    }

    /// <summary>
    /// Dò 16 xương trên một hierarchy bất kỳ, không cần có component này.
    ///
    /// Dùng được cả trên prefab asset (công cụ kiểm tra bind pose đọc thẳng prefab của hai tay
    /// thay vì phải nhân bản chúng vào scene).
    /// </summary>
    public static Transform[] ResolveJoints(Transform root, HandSide side)
    {
        var resolved = new Transform[HandJoints.Count];

        if (root == null)
            return resolved;

        HashSet<Transform> skinBones = CollectSkinBones(root);
        Transform[] candidates = root.GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < HandJoints.Count; i++)
        {
            string boneName = HandJoints.BoneName((HandJointId)i, side);
            resolved[i] = FindBone(candidates, skinBones, boneName);
        }

        return resolved;
    }

    /// <summary>Dò xương cổ tay trên một hierarchy bất kỳ.</summary>
    public static Transform ResolveWrist(Transform root, HandSide side)
    {
        if (root == null)
            return null;

        return FindBone(root.GetComponentsInChildren<Transform>(true), CollectSkinBones(root),
            HandJoints.WristBoneName(side));
    }

    private static HashSet<Transform> CollectSkinBones(Transform root)
    {
        var skinBones = new HashSet<Transform>();

        foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Transform[] bones = renderer.bones;

            if (bones == null)
                continue;

            foreach (Transform bone in bones)
            {
                if (bone != null)
                    skinBones.Add(bone);
            }
        }

        return skinBones;
    }

    private static Transform FindBone(Transform[] candidates, HashSet<Transform> skinBones, string boneName)
    {
        Transform best = null;
        int bestScore = int.MinValue;

        foreach (Transform candidate in candidates)
        {
            string name = candidate.name;

            // Bộ collider song song: cùng đuôi tên nhưng không phải xương của da.
            if (name.Contains("coll_"))
                continue;

            // Đầu mút rỗng: b_l_index_ignore nằm sau index3 chỉ để đo chiều dài đốt cuối.
            if (name.EndsWith("_ignore"))
                continue;

            // FBX nào cũng mang namespace riêng ("hands:b_l_index1"), bỏ phần trước dấu ':'.
            if (StripNamespace(name) != boneName)
                continue;

            int score = skinBones.Contains(candidate) ? 1 : 0;

            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private static string StripNamespace(string name)
    {
        int colon = name.LastIndexOf(':');
        return colon >= 0 ? name.Substring(colon + 1) : name;
    }

    /// <summary>Đọc góc xoay local hiện tại của 16 khớp. Khớp trống trả về identity.</summary>
    public Quaternion[] CaptureLocalRotations()
    {
        EnsureArray();
        var rotations = new Quaternion[HandJoints.Count];

        for (int i = 0; i < HandJoints.Count; i++)
            rotations[i] = joints[i] != null ? joints[i].localRotation : Quaternion.identity;

        return rotations;
    }

    /// <summary>Ghi góc xoay local lên 16 khớp.</summary>
    public void ApplyLocalRotations(IList<Quaternion> rotations)
    {
        if (rotations == null)
            return;

        EnsureArray();

        int count = Mathf.Min(rotations.Count, HandJoints.Count);

        for (int i = 0; i < count; i++)
        {
            if (joints[i] != null)
                joints[i].localRotation = rotations[i];
        }
    }

    /// <summary>
    /// Các collider nằm trên xương NGÓN (16 capsule), không gồm 8 collider lòng bàn tay.
    ///
    /// <see cref="HandPoseDriver"/> chỉ được tắt đúng nhóm này: 8 capsule trên <c>b_?_hand</c>
    /// là phần thân thể của bàn tay, <c>PhysicsHands</c> dựa vào chúng để tay bị tường chặn lại.
    /// </summary>
    public Collider[] GetFingerColliders()
    {
        if (fingerColliders != null)
            return fingerColliders;

        EnsureArray();

        var jointSet = new HashSet<Transform>();

        foreach (Transform joint in joints)
        {
            if (joint != null)
                jointSet.Add(joint);
        }

        var found = new List<Collider>();

        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
        {
            if (collider == null || collider is CharacterController)
                continue;

            if (IsUnderAnyJoint(collider.transform, jointSet))
                found.Add(collider);
        }

        fingerColliders = found.ToArray();
        return fingerColliders;
    }

    private static bool IsUnderAnyJoint(Transform transform, HashSet<Transform> joints)
    {
        for (Transform current = transform; current != null; current = current.parent)
        {
            if (joints.Contains(current))
                return true;
        }

        return false;
    }

    private void EnsureArray()
    {
        if (joints == null || joints.Length != HandJoints.Count)
        {
            var resized = new Transform[HandJoints.Count];

            if (joints != null)
            {
                int shared = Mathf.Min(joints.Length, HandJoints.Count);

                for (int i = 0; i < shared; i++)
                    resized[i] = joints[i];
            }

            joints = resized;
        }
    }
}
