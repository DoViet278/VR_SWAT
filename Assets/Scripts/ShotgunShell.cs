using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Một viên đạn shotgun rời, cầm được bằng tay và nhét được vào cửa nạp của súng.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class ShotgunShell : MonoBehaviour
{
    [Tooltip("Đầu viên đạn: trục +Z phải chỉ ra ngoài theo hướng nhét vào súng. Để trống sẽ dùng chính transform này.")]
    [SerializeField] private Transform noseTip;
    [Tooltip("Đạn thật (nạp được). Tắt với vỏ đạn đã bắn.")]
    [SerializeField] private bool isLive = true;

    public bool IsLive => isLive;
    public bool IsConsumed { get; private set; }

    public Transform NoseTip => noseTip != null ? noseTip : transform;
    public Rigidbody Body { get; private set; }
    public XRGrabInteractable Grab { get; private set; }

    public bool IsHeld => Grab != null && Grab.isSelected;

    private void Awake()
    {
        Body = GetComponent<Rigidbody>();
        Grab = GetComponent<XRGrabInteractable>();
    }

    /// <summary>Chốt lại là viên đạn này đã được nhận, không cửa nạp nào nhận lại lần nữa.</summary>
    public void MarkConsumed()
    {
        IsConsumed = true;
    }

    /// <summary>Viên đạn đã trượt vào trong súng: biến mất khỏi thế giới.</summary>
    public void Consume()
    {
        MarkConsumed();

        if (Grab != null)
            Grab.enabled = false;

        Destroy(gameObject);
    }

    /// <summary>Chuẩn bị cho animation trượt vào súng: tắt vật lý và va chạm.</summary>
    public void PrepareForInsertion()
    {
        if (Grab != null)
            Grab.enabled = false;

        if (Body != null)
        {
            Body.isKinematic = true;
            Body.detectCollisions = false;
        }

        foreach (Collider shellCollider in GetComponentsInChildren<Collider>())
            shellCollider.enabled = false;
    }
}
