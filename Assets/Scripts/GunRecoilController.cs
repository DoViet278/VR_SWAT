using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

[RequireComponent(typeof(XRGrabInteractable))]
public class GunRecoilController : XRBaseGrabTransformer
{
    [Header("Recoil Kick")]
    [SerializeField] private Vector3 positionKickMin = new Vector3(0f, 0f, -0.01f);
    [SerializeField] private Vector3 positionKickMax = new Vector3(0f, 0f, -0.025f);
    [SerializeField] private Vector3 rotationKickMin = new Vector3(-7f, -1.5f, -1f);
    [SerializeField] private Vector3 rotationKickMax = new Vector3(-4f, 1.5f, 1f);
    [SerializeField] private float positionRecoverTime = 0.12f;
    [SerializeField] private float rotationRecoverTime = 0.1f;

    [Header("Spread (Heat)")]
    [SerializeField] private float heatPerShot = 0.15f;
    [SerializeField] private float heatDecayPerSecond = 0.6f;
    [SerializeField] private float minSpreadDegrees = 0.15f;
    [SerializeField] private float maxSpreadDegrees = 4f;

    [Header("Two-Handed Stability")]
    [SerializeField] [Range(0f, 1f)] private float twoHandedRecoilMultiplier = 0.45f;
    [SerializeField] [Range(0f, 1f)] private float twoHandedSpreadMultiplier = 0.5f;
    [SerializeField] [Range(0f, 1f)] private float twoHandedStabilizationBoost = 0.6f;

    [Header("Tuning (Jitter Stabilization)")]
    [SerializeField] private bool stabilizeJitter = true;
    [SerializeField] private float jitterAngleThreshold = 1.2f;
    [SerializeField] [Range(0f, 1f)] private float minFollowFactor = 0.25f;
    [SerializeField] [Range(0f, 1f)] private float maxFollowFactor = 1f;

    protected override RegistrationMode registrationMode => RegistrationMode.SingleAndMultiple;

    private XRGrabInteractable grabInteractable;

    private Vector3 positionOffset;
    private Vector3 positionOffsetVelocity;
    private Vector3 rotationOffsetEuler;
    private Vector3 rotationOffsetVelocity;

    private float currentHeat;

    private Quaternion filteredRotation;
    private bool hasFilteredRotation;

    public bool IsTwoHanded => grabInteractable != null && grabInteractable.interactorsSelecting.Count >= 2;
    public float CurrentSpreadDegrees { get; private set; }

    private void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
    }

    public override void OnLink(XRGrabInteractable grabInteractableArg)
    {
        base.OnLink(grabInteractableArg);

        int singleIndex = grabInteractableArg.singleGrabTransformersCount - 1;
        if (singleIndex >= 0)
            grabInteractableArg.MoveSingleGrabTransformerTo(this, singleIndex);

        int multipleIndex = grabInteractableArg.multipleGrabTransformersCount - 1;
        if (multipleIndex >= 0)
            grabInteractableArg.MoveMultipleGrabTransformerTo(this, multipleIndex);
    }

    private void Update()
    {
        if (grabInteractable == null || !grabInteractable.isSelected)
        {
            ResetState();
            return;
        }

        currentHeat = Mathf.Max(0f, currentHeat - heatDecayPerSecond * Time.deltaTime);
        float stabilityMultiplier = IsTwoHanded ? twoHandedSpreadMultiplier : 1f;
        CurrentSpreadDegrees = Mathf.Lerp(minSpreadDegrees, maxSpreadDegrees, currentHeat) * stabilityMultiplier;

        positionOffset = Vector3.SmoothDamp(positionOffset, Vector3.zero, ref positionOffsetVelocity, positionRecoverTime);
        rotationOffsetEuler = Vector3.SmoothDamp(rotationOffsetEuler, Vector3.zero, ref rotationOffsetVelocity, rotationRecoverTime);
    }

    public override void Process(XRGrabInteractable grabInteractableArg, XRInteractionUpdateOrder.UpdatePhase updatePhase, ref Pose targetPose, ref Vector3 localScale)
    {
        if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic &&
            updatePhase != XRInteractionUpdateOrder.UpdatePhase.OnBeforeRender)
            return;

        ApplyJitterStabilization(grabInteractableArg, ref targetPose);

        targetPose.position += targetPose.rotation * positionOffset;
        targetPose.rotation *= Quaternion.Euler(rotationOffsetEuler);
    }

    public void RegisterShot()
    {
        float recoilMultiplier = IsTwoHanded ? twoHandedRecoilMultiplier : 1f;

        Vector3 positionKick = new Vector3(
            Random.Range(positionKickMin.x, positionKickMax.x),
            Random.Range(positionKickMin.y, positionKickMax.y),
            Random.Range(positionKickMin.z, positionKickMax.z)) * recoilMultiplier;

        Vector3 rotationKick = new Vector3(
            Random.Range(rotationKickMin.x, rotationKickMax.x),
            Random.Range(rotationKickMin.y, rotationKickMax.y),
            Random.Range(rotationKickMin.z, rotationKickMax.z)) * recoilMultiplier;

        positionOffset += positionKick;
        rotationOffsetEuler += rotationKick;

        currentHeat = Mathf.Clamp01(currentHeat + heatPerShot);
    }

    private void ApplyJitterStabilization(XRGrabInteractable grabInteractableArg, ref Pose targetPose)
    {
        Quaternion rawRotation = targetPose.rotation;

        if (!stabilizeJitter || !hasFilteredRotation)
        {
            filteredRotation = rawRotation;
            hasFilteredRotation = true;
            targetPose.rotation = filteredRotation;
            return;
        }

        float angularSpeed = Quaternion.Angle(filteredRotation, rawRotation) / Mathf.Max(Time.deltaTime, 0.0001f);

        float t = Mathf.InverseLerp(0f, jitterAngleThreshold, angularSpeed);
        float followFactor = Mathf.Lerp(minFollowFactor, maxFollowFactor, t);

        if (grabInteractableArg.interactorsSelecting.Count >= 2)
            followFactor *= 1f - twoHandedStabilizationBoost;

        followFactor = Mathf.Clamp(followFactor, 0.01f, 1f);

        filteredRotation = Quaternion.Slerp(filteredRotation, rawRotation, followFactor);
        targetPose.rotation = filteredRotation;
    }

    private void ResetState()
    {
        positionOffset = Vector3.zero;
        positionOffsetVelocity = Vector3.zero;
        rotationOffsetEuler = Vector3.zero;
        rotationOffsetVelocity = Vector3.zero;
        hasFilteredRotation = false;
        currentHeat = 0f;
        CurrentSpreadDegrees = minSpreadDegrees;
    }
}
