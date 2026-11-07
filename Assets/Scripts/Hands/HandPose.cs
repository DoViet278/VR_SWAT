using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Một dáng tay đã tạo sẵn: 16 góc xoay local của khớp ngón, cộng dáng riêng cho ba đốt ngón
/// trỏ lúc bóp cò.
///
/// Chỉ lưu góc xoay LOCAL của xương, không lưu vị trí: xương ngón không bao giờ dịch, và góc
/// local thì dùng được cho mọi instance của model tay bất kể tay đang ở đâu trong scene. Vị trí
/// của cả bàn tay so với vật thì do điểm bám (attach transform) của vật quyết định, chứ không
/// nằm trong file này.
///
/// Mảng đánh chỉ số theo <see cref="HandJointId"/>, nên đừng đổi thứ tự enum đó.
/// </summary>
[CreateAssetMenu(fileName = "HandPose", menuName = "VR SWAT/Hand Pose", order = 100)]
public class HandPose : ScriptableObject
{
    [Tooltip("Dáng này dành cho tay nào. Dùng để chọn đúng bàn tay lúc chơi.")]
    [SerializeField] private HandSide handedness = HandSide.Right;

    [Tooltip("Đây là dáng cho bàn tay thứ hai (tay đỡ nòng / foregrip), không phải tay cầm chính.")]
    [SerializeField] private bool supportHand;

    [Tooltip("Góc xoay local của 16 khớp, xếp theo HandJointId.")]
    [SerializeField] private Quaternion[] jointRotations = NewIdentityArray(HandJoints.Count);

    [Tooltip("Đã ghi dáng ngón trỏ lúc bóp cò chưa. Chưa thì ngón trỏ giữ nguyên dáng cầm.")]
    [SerializeField] private bool hasTriggerPose;

    [Tooltip("Góc xoay local của ba đốt ngón trỏ khi cò được bóp hết.")]
    [SerializeField] private Quaternion[] triggerIndexRotations = NewIdentityArray(3);

    public HandSide Handedness
    {
        get => handedness;
        set => handedness = value;
    }

    public bool SupportHand
    {
        get => supportHand;
        set => supportHand = value;
    }

    public bool HasTriggerPose => hasTriggerPose;

    public Quaternion GetJointRotation(HandJointId joint)
    {
        EnsureArrays();
        return jointRotations[(int)joint];
    }

    public void SetJointRotation(HandJointId joint, Quaternion rotation)
    {
        EnsureArrays();
        jointRotations[(int)joint] = Normalize(rotation);
    }

    /// <summary>Ghi cả 16 khớp một lượt. <paramref name="rotations"/> phải đủ 16 phần tử.</summary>
    public void SetJointRotations(IList<Quaternion> rotations)
    {
        if (rotations == null || rotations.Count < HandJoints.Count)
        {
            Debug.LogError(nameof(HandPose) + ": cần đủ " + HandJoints.Count + " góc xoay.", this);
            return;
        }

        EnsureArrays();

        for (int i = 0; i < HandJoints.Count; i++)
            jointRotations[i] = Normalize(rotations[i]);
    }

    /// <summary>Góc xoay của đốt ngón trỏ thứ <paramref name="slot"/> (0..2) lúc bóp hết cò.</summary>
    public Quaternion GetTriggerIndexRotation(int slot)
    {
        EnsureArrays();

        if (slot < 0 || slot >= triggerIndexRotations.Length)
            return Quaternion.identity;

        return triggerIndexRotations[slot];
    }

    /// <summary>Ghi dáng ngón trỏ lúc bóp cò. <paramref name="rotations"/> theo thứ tự <see cref="HandJoints.IndexJoints"/>.</summary>
    public void SetTriggerPose(IList<Quaternion> rotations)
    {
        if (rotations == null || rotations.Count < HandJoints.IndexJoints.Length)
        {
            Debug.LogError(nameof(HandPose) + ": cần đủ " + HandJoints.IndexJoints.Length +
                           " góc xoay cho ngón trỏ.", this);
            return;
        }

        EnsureArrays();

        for (int i = 0; i < HandJoints.IndexJoints.Length; i++)
            triggerIndexRotations[i] = Normalize(rotations[i]);

        hasTriggerPose = true;
    }

    public void ClearTriggerPose()
    {
        hasTriggerPose = false;
    }

    /// <summary>
    /// Chép dáng từ pose của tay kia.
    ///
    /// Chép THẲNG góc xoay, không lật dấu trục nào: hai model tay trái/phải của Oculus dùng
    /// chung một bind pose (xương trái và xương phải cùng hướng trong local space của chính nó),
    /// nên cùng một góc local cho ra cùng hình dáng ở cả hai bên. Giả định đó được
    /// <c>HandPoseBindPoseCheck</c> kiểm tra và sẽ cảnh báo nếu model tay đổi.
    /// </summary>
    public void MirrorFrom(HandPose other)
    {
        if (other == null)
            return;

        EnsureArrays();
        other.EnsureArrays();

        for (int i = 0; i < HandJoints.Count; i++)
            jointRotations[i] = other.jointRotations[i];

        hasTriggerPose = other.hasTriggerPose;

        for (int i = 0; i < triggerIndexRotations.Length; i++)
            triggerIndexRotations[i] = other.triggerIndexRotations[i];
    }

    private void OnValidate()
    {
        EnsureArrays();
    }

    private void EnsureArrays()
    {
        if (jointRotations == null || jointRotations.Length != HandJoints.Count)
            jointRotations = Resize(jointRotations, HandJoints.Count);

        if (triggerIndexRotations == null || triggerIndexRotations.Length != HandJoints.IndexJoints.Length)
            triggerIndexRotations = Resize(triggerIndexRotations, HandJoints.IndexJoints.Length);
    }

    private static Quaternion[] Resize(Quaternion[] source, int length)
    {
        var resized = NewIdentityArray(length);

        if (source != null)
        {
            int shared = Mathf.Min(source.Length, length);

            for (int i = 0; i < shared; i++)
                resized[i] = Normalize(source[i]);
        }

        return resized;
    }

    private static Quaternion[] NewIdentityArray(int length)
    {
        var array = new Quaternion[length];

        for (int i = 0; i < length; i++)
            array[i] = Quaternion.identity;

        return array;
    }

    // Quaternion mặc định của mảng mới là (0,0,0,0) - không phải identity, và gán thẳng vào
    // localRotation sẽ cho ra lỗi. Chặn luôn ở đây.
    private static Quaternion Normalize(Quaternion rotation)
    {
        float lengthSquared = rotation.x * rotation.x + rotation.y * rotation.y +
                              rotation.z * rotation.z + rotation.w * rotation.w;

        return lengthSquared < 1e-6f ? Quaternion.identity : Quaternion.Normalize(rotation);
    }
}
