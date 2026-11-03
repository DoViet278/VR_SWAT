using UnityEngine;

/// <summary>
/// Nhả vỏ đạn ra khỏi cửa nhả khi kéo pump. Nối vào GunAmmoManager.OnSpentCaseEjected /
/// OnLiveRoundEjected trong Inspector.
/// </summary>
public class ShellEjector : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Cửa nhả vỏ: +Z chỉ ra ngoài theo hướng vỏ bay.")]
    [SerializeField] private Transform ejectPoint;
    [Tooltip("Mẫu vỏ đạn. Dùng được object 'Shell' có sẵn trên súng (đang inactive).")]
    [SerializeField] private Rigidbody shellTemplate;

    [Header("Throw")]
    [SerializeField] private float speedMin = 1.5f;
    [SerializeField] private float speedMax = 3f;
    [SerializeField] private float spinMin = 4f;
    [SerializeField] private float spinMax = 12f;
    [Tooltip("Cộng thêm vận tốc của chính khẩu súng để vỏ bay đúng khi đang di chuyển.")]
    [SerializeField] private bool inheritGunVelocity = true;

    [Header("Misc")]
    [Tooltip("Bù lại scale của súng: Instantiate ra ngoài hierarchy sẽ mất scale của root.")]
    [SerializeField] private bool matchGunScale = true;
    [Tooltip("Tự huỷ vỏ sau bao lâu. 0 = để nguyên TimedDestructor của mẫu.")]
    [SerializeField] private float shellLifetime = 6f;
    [Tooltip("Tắt khói cho viên đạn còn nguyên bị nhả ra.")]
    [SerializeField] private bool disableSmokeOnLiveRound = true;

    private Rigidbody gunBody;
    private Collider[] gunColliders;

    private void Awake()
    {
        gunBody = GetComponentInParent<Rigidbody>();
        gunColliders = GetComponentsInChildren<Collider>(true);
    }

    /// <summary>Nhả vỏ đạn đã bắn (có khói).</summary>
    public void EjectSpent()
    {
        Eject(true);
    }

    /// <summary>Nhả viên đạn còn nguyên do kéo pump khi chưa bắn.</summary>
    public void EjectLive()
    {
        Eject(false);
    }

    private void Eject(bool spent)
    {
        if (shellTemplate == null || ejectPoint == null)
            return;

        Rigidbody shell = Instantiate(shellTemplate, ejectPoint.position, ejectPoint.rotation);
        shell.gameObject.SetActive(true);
        shell.transform.SetParent(null);

        if (matchGunScale)
        {
            Vector3 gunScale = transform.lossyScale;
            Vector3 templateScale = shellTemplate.transform.localScale;
            shell.transform.localScale = new Vector3(
                templateScale.x * gunScale.x,
                templateScale.y * gunScale.y,
                templateScale.z * gunScale.z);
        }

        shell.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        shell.linearVelocity = ejectPoint.forward * Random.Range(speedMin, speedMax)
            + (inheritGunVelocity && gunBody != null ? gunBody.linearVelocity : Vector3.zero);
        shell.angularVelocity = Random.insideUnitSphere * Random.Range(spinMin, spinMax);

        IgnoreGunCollisions(shell);

        if (shellLifetime > 0f)
            Destroy(shell.gameObject, shellLifetime);

        if (!spent && disableSmokeOnLiveRound)
            DisableParticles(shell.gameObject);
    }

    // Vỏ spawn ngay sát thân súng: không cho nó đẩy khẩu súng bay đi.
    private void IgnoreGunCollisions(Rigidbody shell)
    {
        if (gunColliders == null)
            return;

        Collider[] shellColliders = shell.GetComponentsInChildren<Collider>(true);

        foreach (Collider shellCollider in shellColliders)
        {
            foreach (Collider gunCollider in gunColliders)
            {
                if (gunCollider != null && gunCollider != shellCollider && gunCollider.enabled)
                    Physics.IgnoreCollision(shellCollider, gunCollider, true);
            }
        }
    }

    private static void DisableParticles(GameObject shell)
    {
        foreach (ParticleSystem particles in shell.GetComponentsInChildren<ParticleSystem>(true))
            particles.gameObject.SetActive(false);
    }
}
