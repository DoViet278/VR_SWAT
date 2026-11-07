using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Hai mốc cho slider cong ngón: dáng MỞ và dáng NẮM của từng khớp.
///
/// Mốc lấy từ chính clip animation của model tay - <c>l_hand_default_anim</c> cho đầu mở và
/// <c>l_hand_fist_anim</c> cho đầu nắm (tương tự <c>r_</c>). Lấy từ clip vì dáng nắm do người làm
/// model nặn ra, nên mỗi khớp quay quanh đúng trục của nó; suy trục từ hình học thì ngón hay bị
/// xoè ngang hoặc vặn.
///
/// Không tìm thấy clip (đổi sang model tay khác) thì rơi về đường dự phòng: suy trục gập từ hình
/// học - trục vuông góc với thân xương và với pháp tuyến lòng bàn tay, chiều thì thử cả hai rồi
/// chọn chiều nào đưa đầu ngón lại gần lòng bàn tay hơn. Thô hơn, và Inspector nói rõ là đang ở
/// đường dự phòng.
/// </summary>
public static class HandPoseCurl
{
    public class CurlData
    {
        public Quaternion[] Open = new Quaternion[HandJoints.Count];
        public Quaternion[] Closed = new Quaternion[HandJoints.Count];

        /// <summary>true = lấy được từ clip, false = đang dùng đường dự phòng hình học.</summary>
        public bool FromClips;

        public string Source = string.Empty;
    }

    private static readonly Dictionary<HandSide, CurlData> Cache = new Dictionary<HandSide, CurlData>();

    /// <summary>Hai mốc cho một bên tay. Tính một lần rồi nhớ lại.</summary>
    public static CurlData Get(HandSkeleton skeleton)
    {
        if (skeleton == null || !skeleton.IsResolved)
            return null;

        HandSide side = skeleton.Handedness;

        if (Cache.TryGetValue(side, out CurlData cached) && cached != null)
            return cached;

        CurlData data = BuildFromClips(skeleton) ?? BuildFromGeometry(skeleton);
        Cache[side] = data;

        return data;
    }

    public static void ClearCache()
    {
        Cache.Clear();
    }

    /// <summary>Góc xoay local của một khớp ở độ cong <paramref name="t"/> (0 = mở, 1 = nắm).</summary>
    public static Quaternion Evaluate(CurlData data, HandJointId joint, float t)
    {
        if (data == null)
            return Quaternion.identity;

        int i = (int)joint;
        return Quaternion.Slerp(data.Open[i], data.Closed[i], Mathf.Clamp01(t));
    }

    // ------------------------------------------------------------------ clip

    private static CurlData BuildFromClips(HandSkeleton skeleton)
    {
        string prefix = skeleton.Handedness == HandSide.Left ? "l" : "r";

        AnimationClip openClip = FindClip(prefix + "_hand_default_anim");
        AnimationClip closedClip = FindClip(prefix + "_hand_fist_anim");

        if (openClip == null || closedClip == null)
            return null;

        var animator = skeleton.GetComponent<Animator>();
        bool animatorWasEnabled = animator != null && animator.enabled;

        if (animator != null)
            animator.enabled = true;

        Quaternion[] saved = skeleton.CaptureLocalRotations();

        Quaternion[] open = Sample(skeleton, openClip, 0f);
        Quaternion[] closed = Sample(skeleton, closedClip, closedClip.length);

        skeleton.ApplyLocalRotations(saved);

        if (animator != null)
            animator.enabled = animatorWasEnabled;

        // Đường dẫn curve trong clip không khớp hierarchy (đổi model tay, đổi namespace FBX) thì
        // SampleAnimation không ghi được gì và hai mốc trùng nhau. Coi như không có clip.
        if (!Differs(open, closed))
            return null;

        return new CurlData
        {
            Open = open,
            Closed = closed,
            FromClips = true,
            Source = openClip.name + " / " + closedClip.name,
        };
    }

    private static Quaternion[] Sample(HandSkeleton skeleton, AnimationClip clip, float time)
    {
        clip.SampleAnimation(skeleton.gameObject, Mathf.Max(0f, time));
        return skeleton.CaptureLocalRotations();
    }

    private static bool Differs(Quaternion[] a, Quaternion[] b)
    {
        for (int i = 0; i < HandJoints.Count; i++)
        {
            if (Quaternion.Angle(a[i], b[i]) > 1f)
                return true;
        }

        return false;
    }

    private static AnimationClip FindClip(string assetName)
    {
        foreach (string guid in AssetDatabase.FindAssets(assetName))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (!string.Equals(Path.GetFileNameWithoutExtension(path), assetName,
                    System.StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    return clip;
            }
        }

        return null;
    }

    // -------------------------------------------------------------- hình học

    private static CurlData BuildFromGeometry(HandSkeleton skeleton)
    {
        var data = new CurlData
        {
            Open = skeleton.CaptureLocalRotations(),
            FromClips = false,
            Source = "suy từ hình học (không tìm thấy clip tay)",
        };

        data.Open.CopyTo(data.Closed, 0);

        Vector3 palmNormal = PalmNormal(skeleton);
        Vector3 palmCenter = PalmCenter(skeleton);

        Quaternion[] saved = skeleton.CaptureLocalRotations();

        foreach (HandFinger finger in new[]
                 {
                     HandFinger.Thumb, HandFinger.Index, HandFinger.Middle,
                     HandFinger.Ring, HandFinger.Pinky,
                 })
        {
            CurlFinger(skeleton, finger, palmNormal, palmCenter, data);
        }

        skeleton.ApplyLocalRotations(saved);

        return data;
    }

    private static void CurlFinger(HandSkeleton skeleton, HandFinger finger, Vector3 palmNormal,
        Vector3 palmCenter, CurlData data)
    {
        IReadOnlyList<HandJointId> joints = HandJoints.JointsOf(finger);
        Transform tip = FingerTip(skeleton, joints);

        if (tip == null)
            return;

        float bestDistance = float.MaxValue;
        Quaternion[] bestRotations = null;

        // Trục gập thì suy được, nhưng CHIỀU thì không: thử cả hai rồi lấy chiều nào đưa đầu ngón
        // lại gần lòng bàn tay hơn.
        foreach (float sign in new[] { 1f, -1f })
        {
            skeleton.ApplyLocalRotations(data.Open);

            var rotations = new Quaternion[joints.Count];

            for (int i = 0; i < joints.Count; i++)
            {
                HandJointId joint = joints[i];
                Transform bone = skeleton.GetJoint(joint);

                if (bone == null)
                {
                    rotations[i] = Quaternion.identity;
                    continue;
                }

                Vector3 boneDirection = BoneDirection(skeleton, joints, i);

                if (boneDirection == Vector3.zero)
                {
                    rotations[i] = bone.localRotation;
                    continue;
                }

                Vector3 axisWorld = Vector3.Cross(boneDirection, palmNormal);

                if (axisWorld.sqrMagnitude < 1e-8f)
                {
                    rotations[i] = bone.localRotation;
                    continue;
                }

                Vector3 axisLocal = Quaternion.Inverse(bone.rotation) * axisWorld.normalized;
                float angle = sign * CurlAngle(joint);

                bone.localRotation = bone.localRotation * Quaternion.AngleAxis(angle, axisLocal);
                rotations[i] = bone.localRotation;
            }

            float distance = Vector3.Distance(tip.position, palmCenter);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestRotations = rotations;
            }
        }

        if (bestRotations == null)
            return;

        for (int i = 0; i < joints.Count; i++)
            data.Closed[(int)joints[i]] = bestRotations[i];
    }

    /// <summary>Hướng thân xương trong world: từ khớp này tới khớp sau, hoặc tới đầu mút.</summary>
    private static Vector3 BoneDirection(HandSkeleton skeleton, IReadOnlyList<HandJointId> joints, int index)
    {
        Transform bone = skeleton.GetJoint(joints[index]);

        if (bone == null)
            return Vector3.zero;

        Transform next = index + 1 < joints.Count ? skeleton.GetJoint(joints[index + 1]) : FirstBoneChild(bone);

        if (next == null)
            return Vector3.zero;

        Vector3 direction = next.position - bone.position;
        return direction.sqrMagnitude < 1e-10f ? Vector3.zero : direction.normalized;
    }

    private static Transform FingerTip(HandSkeleton skeleton, IReadOnlyList<HandJointId> joints)
    {
        Transform last = skeleton.GetJoint(joints[joints.Count - 1]);

        if (last == null)
            return null;

        return FirstBoneChild(last) ?? last;
    }

    /// <summary>Con đầu tiên không thuộc bộ collider song song (<c>coll_</c>).</summary>
    private static Transform FirstBoneChild(Transform bone)
    {
        for (int i = 0; i < bone.childCount; i++)
        {
            Transform child = bone.GetChild(i);

            if (!child.name.Contains("coll_"))
                return child;
        }

        return null;
    }

    private static float CurlAngle(HandJointId joint)
    {
        switch (joint)
        {
            case HandJointId.ThumbProximal:
            case HandJointId.ThumbIntermediate:
            case HandJointId.ThumbDistal:
                return 35f;

            // Đốt bàn ngón út gần như không gập, chỉ hơi khép lòng bàn tay.
            case HandJointId.PinkyMetacarpal:
                return 12f;

            default:
                return 70f;
        }
    }

    private static Vector3 PalmNormal(HandSkeleton skeleton)
    {
        Transform wrist = skeleton.Wrist;
        Transform index = skeleton.GetJoint(HandJointId.IndexProximal);
        Transform pinky = skeleton.GetJoint(HandJointId.PinkyProximal);

        if (wrist == null || index == null || pinky == null)
            return wrist != null ? wrist.up : Vector3.up;

        Vector3 normal = Vector3.Cross(index.position - wrist.position, pinky.position - wrist.position);

        return normal.sqrMagnitude < 1e-10f ? wrist.up : normal.normalized;
    }

    private static Vector3 PalmCenter(HandSkeleton skeleton)
    {
        Transform wrist = skeleton.Wrist;
        Transform middle = skeleton.GetJoint(HandJointId.MiddleProximal);

        if (wrist == null)
            return middle != null ? middle.position : Vector3.zero;

        if (middle == null)
            return wrist.position;

        return Vector3.Lerp(wrist.position, middle.position, 0.5f);
    }
}
