using UnityEngine;

/// <summary>
/// IK tay + đầu cho NPC (gắn tự động lên object có Animator bởi NPCCuffable).
/// OnAnimatorIK chỉ được gọi trên script cùng object với Animator và khi layer bật IK Pass.
/// </summary>
public class NPCHandsIK : MonoBehaviour
{
    public float Weight;
    public Vector3 LeftHandPosition, RightHandPosition, LookAt;
    public Quaternion LeftHandRotation = Quaternion.identity, RightHandRotation = Quaternion.identity;

    private Animator _animator;

    private void Awake() => _animator = GetComponent<Animator>();

    private void OnAnimatorIK(int layerIndex)
    {
        if (_animator == null || layerIndex != 0) return;
        float w = Weight <= 0f ? 0f : Mathf.SmoothStep(0f, 1f, Weight);

        _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, w);
        _animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, w);
        _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, w);
        _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, w);
        _animator.SetLookAtWeight(w * 0.8f, 0.2f, 0.9f, 0f, 0.5f);
        if (w <= 0f) return;

        _animator.SetIKPosition(AvatarIKGoal.LeftHand, LeftHandPosition);
        _animator.SetIKRotation(AvatarIKGoal.LeftHand, LeftHandRotation);
        _animator.SetIKPosition(AvatarIKGoal.RightHand, RightHandPosition);
        _animator.SetIKRotation(AvatarIKGoal.RightHand, RightHandRotation);
        _animator.SetLookAtPosition(LookAt);
    }
}
