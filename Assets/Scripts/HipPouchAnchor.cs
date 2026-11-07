using Unity.XR.CoreUtils;
using UnityEngine;

public class HipPouchAnchor : MonoBehaviour
{
    [Header("Reference")]
    [SerializeField] private Transform head;

    [Header("Body Offset")]
    [SerializeField] private Vector3 bodyOffset = new Vector3(-0.22f, -0.5f, 0.05f);

    [Header("Yaw Follow")]
    [SerializeField] private float yawDeadzoneDegrees = 50f;
    [SerializeField] private float yawFollowSpeed = 4f;

    private float bodyYaw;

    private void Awake()
    {
        if (head == null)
        {
            XROrigin origin = GetComponentInParent<XROrigin>();

            if (origin != null && origin.Camera != null)
                head = origin.Camera.transform;
        }
    }

    private void OnEnable()
    {
        if (head != null)
            bodyYaw = head.eulerAngles.y;
    }

    private void LateUpdate()
    {
        if (head == null)
            return;

        float headYaw = head.eulerAngles.y;
        float delta = Mathf.DeltaAngle(bodyYaw, headYaw);

        if (Mathf.Abs(delta) > yawDeadzoneDegrees)
        {
            float targetYaw = headYaw - Mathf.Sign(delta) * yawDeadzoneDegrees;
            bodyYaw = Mathf.LerpAngle(bodyYaw, targetYaw, yawFollowSpeed * Time.deltaTime);
        }

        Quaternion bodyRotation = Quaternion.Euler(0f, bodyYaw, 0f);
        transform.SetPositionAndRotation(head.position + bodyRotation * bodyOffset, bodyRotation);
    }
}
