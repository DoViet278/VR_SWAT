using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Cửa nạp đạn dưới hộp khoá nòng shotgun. Người chơi cầm viên đạn, đẩy đầu đạn vào đây:
/// cánh cửa (Object001) bật mở theo độ sâu, đủ sâu thì viên đạn rời khỏi tay và trượt vào ống đạn.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ShellLoadPort : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private GunAmmoManager ammoManager;
    [Tooltip("Trục nhét đạn: +Z chỉ vào trong ống đạn. Để trống sẽ dùng chính transform này.")]
    [SerializeField] private Transform insertionAxis;
    [Tooltip("Điểm viên đạn trượt tới rồi biến mất, nằm sâu trong ống đạn.")]
    [SerializeField] private Transform tubeEnd;

    [Header("Gate Door (Object001)")]
    [Tooltip("Cánh cửa nạp. Để trống thì không có animation cửa.")]
    [SerializeField] private Transform gateDoor;
    [Tooltip("Trục bản lề, tính trong local space của cánh cửa.")]
    [SerializeField] private Vector3 gateHingeLocalAxis = Vector3.right;
    [Tooltip("Góc mở tối đa. Đổi dấu nếu cửa mở sai chiều.")]
    [SerializeField] private float gateOpenAngle = 35f;
    [Tooltip("Dịch cánh cửa thêm một đoạn khi mở (dùng khi pivot của mesh không nằm ở bản lề).")]
    [SerializeField] private Vector3 gateOpenLocalOffset = Vector3.zero;
    [SerializeField] private float gateCloseDelay = 0.15f;
    [SerializeField] private float gateCloseTime = 0.12f;

    [Header("Insertion")]
    [Tooltip("Độ thẳng hàng tối thiểu giữa đầu đạn và trục nạp. 0.6 ≈ lệch dưới 53 độ.")]
    [Range(0f, 1f)] [SerializeField] private float minAlignmentDot = 0.6f;
    [Tooltip("Đầu đạn phải lọt qua mặt cửa bao nhiêu mét (world) thì tính là nhét vào.")]
    [SerializeField] private float requiredDepth = 0.015f;
    [Tooltip("Thời gian viên đạn trượt vào ống.")]
    [SerializeField] private float insertTweenTime = 0.12f;
    [Tooltip("Nghỉ giữa 2 viên, chặn việc quét tay một cái nạp cả ống.")]
    [SerializeField] private float loadCooldown = 0.35f;

    [Header("Feedback")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip loadClip;
    [Tooltip("Ống đạn đã đầy.")]
    [SerializeField] private AudioClip rejectClip;
    [SerializeField] private GunHapticFeedback hapticFeedback;

    [Header("Events")]
    public UnityEvent OnShellLoaded;
    public UnityEvent OnLoadRejected;

    private Transform InsertionAxis => insertionAxis != null ? insertionAxis : transform;

    private Quaternion gateClosedRotation;
    private Vector3 gateClosedPosition;
    private float nextLoadTime;
    private Coroutine gateRoutine;

    private void Awake()
    {
        if (ammoManager == null)
            ammoManager = GetComponentInParent<GunAmmoManager>();

        if (hapticFeedback == null)
            hapticFeedback = GetComponent<GunHapticFeedback>();

        if (gateDoor != null)
        {
            gateClosedRotation = gateDoor.localRotation;
            gateClosedPosition = gateDoor.localPosition;
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (ammoManager == null)
            return;

        ShotgunShell shell = other.GetComponentInParent<ShotgunShell>();

        if (shell == null || shell.IsConsumed || !shell.IsLive)
            return;

        // Đạn lăn lóc trên sàn hay nằm trong thùng thì không được tự nạp.
        if (!shell.IsHeld)
            return;

        if (Time.time < nextLoadTime)
            return;

        Transform axis = InsertionAxis;

        // Cầm ngược đầu đạn thì không vào được: lặng lẽ bỏ qua, không kêu tiếng gì.
        if (Vector3.Dot(shell.NoseTip.forward, axis.forward) < minAlignmentDot)
            return;

        float depth = Vector3.Dot(shell.NoseTip.position - axis.position, axis.forward);

        // Cửa hé mở dần theo độ sâu, cho cảm giác đẩy đạn vào.
        SetGateOpenAmount(Mathf.Clamp01(depth / Mathf.Max(0.0001f, requiredDepth)));

        if (depth < requiredDepth)
            return;

        nextLoadTime = Time.time + loadCooldown;

        if (ammoManager.IsMagazineFull)
        {
            PlayClip(rejectClip);
            hapticFeedback?.PlayShellRejected(FirstInteractor(shell));
            OnLoadRejected?.Invoke();
            return;
        }

        object interactor = FirstInteractor(shell);
        shell.MarkConsumed();
        StartCoroutine(InsertRoutine(shell, interactor));
    }

    private static object FirstInteractor(ShotgunShell shell)
    {
        if (shell.Grab == null || shell.Grab.interactorsSelecting.Count == 0)
            return null;

        return shell.Grab.interactorsSelecting[0];
    }

    private IEnumerator InsertRoutine(ShotgunShell shell, object interactor)
    {
        XRGrabInteractable grab = shell.Grab;

        if (grab != null && grab.isSelected && grab.interactionManager != null)
            grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);

        shell.PrepareForInsertion();

        // XRI ghi Rigidbody khi nhả tay; chờ 1 frame rồi mới tự điều khiển transform.
        yield return null;

        Transform shellTransform = shell.transform;
        Vector3 startPosition = shellTransform.position;
        Quaternion startRotation = shellTransform.rotation;
        Transform target = tubeEnd != null ? tubeEnd : InsertionAxis;

        for (float elapsed = 0f; elapsed < insertTweenTime; elapsed += Time.deltaTime)
        {
            if (shellTransform == null)
                yield break;

            float k = Mathf.Clamp01(elapsed / insertTweenTime);
            shellTransform.SetPositionAndRotation(
                Vector3.Lerp(startPosition, target.position, k),
                Quaternion.Slerp(startRotation, target.rotation, k));

            yield return null;
        }

        shell.Consume();

        ammoManager.TryAddRoundToMagazine();
        PlayClip(loadClip);
        hapticFeedback?.PlayShellInsert(interactor);
        OnShellLoaded?.Invoke();

        if (gateDoor != null)
        {
            if (gateRoutine != null)
                StopCoroutine(gateRoutine);

            gateRoutine = StartCoroutine(CloseGateRoutine());
        }
    }

    private IEnumerator CloseGateRoutine()
    {
        yield return new WaitForSeconds(gateCloseDelay);

        float startAmount = currentGateAmount;

        for (float elapsed = 0f; elapsed < gateCloseTime; elapsed += Time.deltaTime)
        {
            SetGateOpenAmount(Mathf.Lerp(startAmount, 0f, Mathf.Clamp01(elapsed / gateCloseTime)));
            yield return null;
        }

        SetGateOpenAmount(0f);
        gateRoutine = null;
    }

    private float currentGateAmount;

    private void SetGateOpenAmount(float amount)
    {
        currentGateAmount = amount;

        if (gateDoor == null)
            return;

        Vector3 hinge = gateHingeLocalAxis.sqrMagnitude > 1e-6f ? gateHingeLocalAxis.normalized : Vector3.right;
        gateDoor.localRotation = gateClosedRotation * Quaternion.AngleAxis(gateOpenAngle * amount, hinge);
        gateDoor.localPosition = gateClosedPosition + gateOpenLocalOffset * amount;
    }

    private void PlayClip(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }

    private void OnDrawGizmosSelected()
    {
        Transform axis = InsertionAxis;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(axis.position, axis.position + axis.forward * requiredDepth);
        Gizmos.DrawSphere(axis.position + axis.forward * requiredDepth, 0.004f);
    }
}
