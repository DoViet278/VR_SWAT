using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Ghi dáng tay lên xương. Gắn cạnh <see cref="HandSkeleton"/> trên gốc model tay.
///
/// Ghi trong <c>LateUpdate</c> chứ không phải <c>Update</c>: Animator chạy sau Update và trước
/// LateUpdate, nên ghi ở Update là bị Animator ghi đè ngay trong frame đó. Ở LateUpdate thì
/// <c>localRotation</c> đọc ra chính là output của Animator frame này, dùng luôn làm điểm bắt đầu
/// để blend sang dáng cầm - vào mượt, ra mượt, không cần lưu lại dáng cũ.
///
/// Trong lúc cầm, ngón trỏ vẫn nhấp nhả theo cò: dáng cầm là điểm 0, dáng ngón trỏ lúc bóp cò
/// (lưu trong <see cref="HandPose"/>) là điểm 1, giá trị cò kéo qua lại giữa hai đầu.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(HandSkeleton))]
[AddComponentMenu("VR SWAT/Hand Pose Driver")]
public class HandPoseDriver : MonoBehaviour
{
    [Header("Cò")]
    [Tooltip("Action đọc giá trị cò (0..1) để nhấp nhả ngón trỏ. Để trống sẽ tự lấy từ " +
             "AnimateHandOnInput trên chính GameObject này hoặc trên cha.")]
    [SerializeField] private InputActionProperty triggerAction;

    [Header("Blend")]
    [Tooltip("Thời gian vào dáng khi vừa cầm (giây). 0 = snap ngay.")]
    [SerializeField] private float blendInTime = 0.08f;

    [Tooltip("Thời gian ra khỏi dáng khi thả (giây). 0 = snap ngay.")]
    [SerializeField] private float blendOutTime = 0.08f;

    [Header("Collider ngón tay")]
    [Tooltip("Tắt 16 capsule trên xương ngón trong lúc đang áp dáng.\n\n" +
             "Vì sao: capsule nằm thẳng trên xương ngón, nên khi ngón quặp quanh báng chúng lún " +
             "vào thân súng mỗi frame. Súng là kinematic lúc cầm nên chưa thấy gì, nhưng vừa thả " +
             "là nó thành dynamic trong lúc còn lún sâu và bị hất bay.\n\n" +
             "8 capsule lòng bàn tay KHÔNG bị tắt - PhysicsHands dựa vào chúng để tay bị tường chặn.")]
    [SerializeField] private bool disableCollidersWhilePosed = true;

    [Tooltip("Thả ra rồi chờ bao lâu mới bật lại collider ngón (giây). Chờ để vật kịp rời khỏi " +
             "lòng bàn tay, kẻo bật lại lúc còn lún là bị hất.")]
    [SerializeField] private float colliderRestoreDelay = 0.2f;

    [Header("Animator")]
    [Tooltip("Ép Animator sang Always Animate.\n\n" +
             "Prefab tay để Cull Update Transforms: khi mesh bị cull, Animator ngừng ghi xương, " +
             "nên driver sẽ đọc lại chính output của mình ở frame trước và blend vào đó - thành " +
             "vòng lặp khiến tay kẹt dáng vĩnh viễn.")]
    [SerializeField] private bool forceAlwaysAnimate = true;

    private static readonly List<HandPoseDriver> Drivers = new List<HandPoseDriver>();

    private HandSkeleton skeleton;
    private Animator animator;
    private AnimateHandOnInput handInput;

    private HandPose pose;
    private float weight;
    private float targetWeight;
    private float blendRate;

    private readonly List<Collider> disabledColliders = new List<Collider>();
    private float colliderRestoreTime = float.PositiveInfinity;
    private PhysicsHands physicsHands;
    private bool searchedPhysicsHands;

    public HandSkeleton Skeleton => skeleton;

    public HandSide Handedness => skeleton != null ? skeleton.Handedness : HandSide.Left;

    /// <summary>Dáng đang áp, null nếu tay đang tự do.</summary>
    public HandPose CurrentPose => targetWeight > 0f ? pose : null;

    /// <summary>Tìm driver của một bên tay. Trả về null nếu scene không có.</summary>
    public static HandPoseDriver Find(HandSide side)
    {
        for (int i = Drivers.Count - 1; i >= 0; i--)
        {
            HandPoseDriver driver = Drivers[i];

            if (driver == null)
            {
                Drivers.RemoveAt(i);
                continue;
            }

            if (driver.Handedness == side)
                return driver;
        }

        return null;
    }

    private void Awake()
    {
        skeleton = GetComponent<HandSkeleton>();
        animator = GetComponent<Animator>();

        if (animator == null)
            animator = GetComponentInParent<Animator>();

        // Phải tìm trong Awake: PhysicsHands tháo model tay khỏi Controller trong Start, sau đó
        // AnimateHandOnInput không còn là cha nữa.
        handInput = GetComponentInParent<AnimateHandOnInput>();

        if (handInput == null)
            handInput = FindInputByAnimator();
    }

    private AnimateHandOnInput FindInputByAnimator()
    {
        if (animator == null)
            return null;

        foreach (AnimateHandOnInput candidate in FindObjectsByType<AnimateHandOnInput>(FindObjectsSortMode.None))
        {
            if (candidate != null && candidate.handAnimator == animator)
                return candidate;
        }

        return null;
    }

    private void OnEnable()
    {
        if (!Drivers.Contains(this))
            Drivers.Add(this);

        if (forceAlwaysAnimate && animator != null && animator.cullingMode != AnimatorCullingMode.AlwaysAnimate)
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }

    private void OnDisable()
    {
        Drivers.Remove(this);

        // Tắt giữa lúc đang cầm (đổi controller <-> hand tracking): trả collider ngay, kẻo tay
        // bật lại với 16 capsule tắt vĩnh viễn.
        RestoreColliders();

        pose = null;
        weight = 0f;
        targetWeight = 0f;
    }

    /// <summary>Áp một dáng lên bàn tay, blend vào theo Blend In Time.</summary>
    public void ApplyPose(HandPose newPose)
    {
        ApplyPose(newPose, blendInTime);
    }

    /// <summary>Áp một dáng lên bàn tay. blendTime &lt;= 0 là snap ngay.</summary>
    public void ApplyPose(HandPose newPose, float blendTime)
    {
        if (newPose == null)
        {
            ClearPose();
            return;
        }

        pose = newPose;
        targetWeight = 1f;
        blendRate = blendTime > 0f ? 1f / blendTime : float.PositiveInfinity;

        if (float.IsPositiveInfinity(blendRate))
            weight = 1f;

        colliderRestoreTime = float.PositiveInfinity;
        DisableFingerColliders();
    }

    /// <summary>Thả dáng, blend ra theo Blend Out Time.</summary>
    public void ClearPose()
    {
        ClearPose(blendOutTime);
    }

    /// <summary>Thả dáng. blendTime &lt;= 0 là bỏ ngay.</summary>
    public void ClearPose(float blendTime)
    {
        if (targetWeight <= 0f && weight <= 0f)
        {
            // Chưa từng áp dáng mà vẫn bị thả: vẫn phải chốt lịch trả collider cho chắc.
            ScheduleColliderRestore();
            return;
        }

        targetWeight = 0f;
        blendRate = blendTime > 0f ? 1f / blendTime : float.PositiveInfinity;

        if (float.IsPositiveInfinity(blendRate))
            weight = 0f;

        ScheduleColliderRestore();
    }

    private void LateUpdate()
    {
        UpdateWeight();
        UpdateColliderRestore();

        if (pose == null || weight <= 0.0001f || skeleton == null)
            return;

        float trigger = pose.HasTriggerPose ? ReadTrigger() : 0f;

        for (int i = 0; i < HandJoints.Count; i++)
        {
            var joint = (HandJointId)i;
            Transform bone = skeleton.GetJoint(joint);

            if (bone == null)
                continue;

            Quaternion target = pose.GetJointRotation(joint);

            if (trigger > 0f)
            {
                int slot = HandJoints.IndexSlotOf(joint);

                if (slot >= 0)
                    target = Quaternion.Slerp(target, pose.GetTriggerIndexRotation(slot), trigger);
            }

            // localRotation lúc này là output của Animator frame này, nên blend từ đó là blend từ
            // dáng tay tự do sang dáng cầm.
            bone.localRotation = weight >= 0.9999f
                ? target
                : Quaternion.Slerp(bone.localRotation, target, weight);
        }
    }

    private void UpdateWeight()
    {
        if (Mathf.Approximately(weight, targetWeight))
        {
            if (targetWeight <= 0f)
                pose = null;

            return;
        }

        weight = float.IsPositiveInfinity(blendRate)
            ? targetWeight
            : Mathf.MoveTowards(weight, targetWeight, blendRate * Time.deltaTime);

        if (weight <= 0f && targetWeight <= 0f)
            pose = null;
    }

    private float ReadTrigger()
    {
        InputAction action = triggerAction.action;

        if (action == null && handInput != null)
            action = handInput.triggerValue.action;

        if (action == null || !action.enabled)
            return 0f;

        return Mathf.Clamp01(action.ReadValue<float>());
    }

    private void DisableFingerColliders()
    {
        if (!disableCollidersWhilePosed || skeleton == null || disabledColliders.Count > 0)
            return;

        foreach (Collider collider in skeleton.GetFingerColliders())
        {
            if (collider == null || !collider.enabled)
                continue;

            collider.enabled = false;
            disabledColliders.Add(collider);
        }
    }

    private void ScheduleColliderRestore()
    {
        if (disabledColliders.Count == 0)
            return;

        colliderRestoreTime = Time.time + Mathf.Max(0f, colliderRestoreDelay);
    }

    private void UpdateColliderRestore()
    {
        if (Time.time < colliderRestoreTime)
            return;

        colliderRestoreTime = float.PositiveInfinity;
        RestoreColliders();
    }

    private void RestoreColliders()
    {
        colliderRestoreTime = float.PositiveInfinity;

        if (disabledColliders.Count == 0)
            return;

        foreach (Collider collider in disabledColliders)
        {
            if (collider != null)
                collider.enabled = true;
        }

        disabledColliders.Clear();

        // Physics.IgnoreCollision KHÔNG bền: Unity xoá cờ của một collider ngay khi collider đó
        // bị disable rồi enable lại. Không gọi lại thì ngay sau lần cầm súng đầu tiên, ngón tay
        // bắt đầu đập vào CharacterController của chính người chơi và đập vào bàn tay kia.
        if (!searchedPhysicsHands)
        {
            searchedPhysicsHands = true;
            physicsHands = FindFirstObjectByType<PhysicsHands>();
        }

        if (physicsHands != null)
            physicsHands.RefreshSelfCollisionIgnores();
    }
}
