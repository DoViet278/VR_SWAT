using System.Collections.Generic;

/// <summary>Bên tay. Dùng chung cho pose, skeleton và driver.</summary>
public enum HandSide
{
    Left = 0,
    Right = 1,
}

/// <summary>Năm ngón, để nhóm khớp lại cho slider cong ngón.</summary>
public enum HandFinger
{
    Thumb = 0,
    Index = 1,
    Middle = 2,
    Ring = 3,
    Pinky = 4,
}

/// <summary>
/// Thứ tự cố định của 16 khớp ngón tay trên model tay Oculus.
///
/// KHÔNG ĐỔI THỨ TỰ và không chèn thêm giá trị vào giữa: <see cref="HandPose"/> lưu dáng thành
/// một mảng 16 quaternion đánh chỉ số đúng theo enum này, nên đổi thứ tự là mọi pose đã lưu
/// nhảy khớp (ngón trỏ mang góc của ngón giữa...). Thêm khớp mới thì thêm vào CUỐI.
///
/// Ngón cái/trỏ/giữa/áp út có 3 đốt, ngón út có thêm một đốt bàn (pinky0) nên tổng là 16.
/// </summary>
public enum HandJointId
{
    ThumbProximal = 0,
    ThumbIntermediate = 1,
    ThumbDistal = 2,

    IndexProximal = 3,
    IndexIntermediate = 4,
    IndexDistal = 5,

    MiddleProximal = 6,
    MiddleIntermediate = 7,
    MiddleDistal = 8,

    RingProximal = 9,
    RingIntermediate = 10,
    RingDistal = 11,

    PinkyMetacarpal = 12,
    PinkyProximal = 13,
    PinkyIntermediate = 14,
    PinkyDistal = 15,
}

/// <summary>Tra cứu quanh <see cref="HandJointId"/>: tên xương, ngón nào, nhãn hiển thị.</summary>
public static class HandJoints
{
    /// <summary>Số khớp một bàn tay. Dùng làm độ dài mảng ở mọi nơi.</summary>
    public const int Count = 16;

    /// <summary>Đuôi tên xương cổ tay, ghép sau tiền tố <c>b_l_</c> / <c>b_r_</c>.</summary>
    public const string WristSuffix = "hand";

    // Đuôi tên xương trên model tay Oculus, xếp đúng thứ tự enum.
    private static readonly string[] BoneSuffixes =
    {
        "thumb1", "thumb2", "thumb3",
        "index1", "index2", "index3",
        "middle1", "middle2", "middle3",
        "ring1", "ring2", "ring3",
        "pinky0", "pinky1", "pinky2", "pinky3",
    };

    private static readonly HandFinger[] Fingers =
    {
        HandFinger.Thumb, HandFinger.Thumb, HandFinger.Thumb,
        HandFinger.Index, HandFinger.Index, HandFinger.Index,
        HandFinger.Middle, HandFinger.Middle, HandFinger.Middle,
        HandFinger.Ring, HandFinger.Ring, HandFinger.Ring,
        HandFinger.Pinky, HandFinger.Pinky, HandFinger.Pinky, HandFinger.Pinky,
    };

    private static readonly string[] Labels =
    {
        "Ngón cái - đốt 1", "Ngón cái - đốt 2", "Ngón cái - đốt 3",
        "Ngón trỏ - đốt 1", "Ngón trỏ - đốt 2", "Ngón trỏ - đốt 3",
        "Ngón giữa - đốt 1", "Ngón giữa - đốt 2", "Ngón giữa - đốt 3",
        "Ngón áp út - đốt 1", "Ngón áp út - đốt 2", "Ngón áp út - đốt 3",
        "Ngón út - đốt bàn", "Ngón út - đốt 1", "Ngón út - đốt 2", "Ngón út - đốt 3",
    };

    private static readonly string[] FingerLabels =
    {
        "Ngón cái", "Ngón trỏ", "Ngón giữa", "Ngón áp út", "Ngón út",
    };

    private static readonly HandJointId[][] FingerJoints =
    {
        new[] { HandJointId.ThumbProximal, HandJointId.ThumbIntermediate, HandJointId.ThumbDistal },
        new[] { HandJointId.IndexProximal, HandJointId.IndexIntermediate, HandJointId.IndexDistal },
        new[] { HandJointId.MiddleProximal, HandJointId.MiddleIntermediate, HandJointId.MiddleDistal },
        new[] { HandJointId.RingProximal, HandJointId.RingIntermediate, HandJointId.RingDistal },
        new[]
        {
            HandJointId.PinkyMetacarpal, HandJointId.PinkyProximal,
            HandJointId.PinkyIntermediate, HandJointId.PinkyDistal,
        },
    };

    /// <summary>Ba đốt ngón trỏ, theo đúng thứ tự lưu trong dáng ngón trỏ lúc bóp cò.</summary>
    public static readonly HandJointId[] IndexJoints =
    {
        HandJointId.IndexProximal, HandJointId.IndexIntermediate, HandJointId.IndexDistal,
    };

    /// <summary>Mọi khớp, đúng thứ tự enum.</summary>
    public static readonly HandJointId[] All = BuildAll();

    private static HandJointId[] BuildAll()
    {
        var all = new HandJointId[Count];

        for (int i = 0; i < Count; i++)
            all[i] = (HandJointId)i;

        return all;
    }

    /// <summary>Tiền tố tên xương của một bên tay: <c>b_l_</c> hoặc <c>b_r_</c>.</summary>
    public static string BonePrefix(HandSide side)
    {
        return side == HandSide.Left ? "b_l_" : "b_r_";
    }

    /// <summary>Đuôi tên xương, ví dụ <c>index2</c>.</summary>
    public static string BoneSuffix(HandJointId joint)
    {
        return BoneSuffixes[(int)joint];
    }

    /// <summary>Tên xương đầy đủ (chưa kèm namespace FBX), ví dụ <c>b_l_index2</c>.</summary>
    public static string BoneName(HandJointId joint, HandSide side)
    {
        return BonePrefix(side) + BoneSuffixes[(int)joint];
    }

    /// <summary>Tên xương cổ tay, ví dụ <c>b_r_hand</c>.</summary>
    public static string WristBoneName(HandSide side)
    {
        return BonePrefix(side) + WristSuffix;
    }

    public static HandFinger FingerOf(HandJointId joint)
    {
        return Fingers[(int)joint];
    }

    public static IReadOnlyList<HandJointId> JointsOf(HandFinger finger)
    {
        return FingerJoints[(int)finger];
    }

    public static string Label(HandJointId joint)
    {
        return Labels[(int)joint];
    }

    public static string Label(HandFinger finger)
    {
        return FingerLabels[(int)finger];
    }

    public static string Label(HandSide side)
    {
        return side == HandSide.Left ? "Tay trái" : "Tay phải";
    }

    public static HandSide Opposite(HandSide side)
    {
        return side == HandSide.Left ? HandSide.Right : HandSide.Left;
    }

    /// <summary>Chỉ số của khớp trong mảng <see cref="IndexJoints"/>, -1 nếu không phải ngón trỏ.</summary>
    public static int IndexSlotOf(HandJointId joint)
    {
        for (int i = 0; i < IndexJoints.Length; i++)
        {
            if (IndexJoints[i] == joint)
                return i;
        }

        return -1;
    }
}
