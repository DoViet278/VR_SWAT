using UnityEngine;

/// <summary>
/// Gắn lên vật bị bắn để khai báo vật liệu, decal và tiếng va chạm riêng.
/// Không bắt buộc: vật không có component này sẽ dùng cấu hình mặc định của BulletDecalManager.
/// </summary>
[DisallowMultipleComponent]
public class ImpactSurface : MonoBehaviour
{
    [Header("Surface")]
    [Tooltip("Loại vật liệu, dùng để tra decal và tiếng va chạm trong BulletDecalManager.")]
    [SerializeField] private ImpactSurfaceType surfaceType = ImpactSurfaceType.Default;

    [Header("Decal")]
    [Tooltip("Tắt nếu vật này không được để lại lỗ đạn (kính, màn hình, nhân vật). Tiếng va chạm vẫn phát.")]
    [SerializeField] private bool receiveDecals = true;
    [Tooltip("Decal riêng cho vật này. Để trống sẽ dùng decal theo loại vật liệu.")]
    [SerializeField] private GameObject decalOverride;
    [Tooltip("Hệ số phóng to/thu nhỏ lỗ đạn trên vật này. Vật nhỏ nên để nhỏ hơn 1.")]
    [SerializeField] private float decalScaleMultiplier = 1f;
    [Tooltip("Cho decal bám theo vật khi vật di chuyển. Tắt nếu vật hay bị xoá hoặc biến hình.")]
    [SerializeField] private bool attachDecals = true;

    [Header("Audio")]
    [Tooltip("Tiếng va chạm riêng cho vật này. Để trống sẽ dùng clip theo loại vật liệu.")]
    [SerializeField] private AudioClip[] impactClipsOverride;

    public ImpactSurfaceType SurfaceType => surfaceType;
    public bool ReceiveDecals => receiveDecals;
    public GameObject DecalOverride => decalOverride;
    public float DecalScaleMultiplier => Mathf.Max(0.01f, decalScaleMultiplier);
    public bool AttachDecals => attachDecals;

    /// <summary>Clip ghi đè cho vật này, null nếu dùng clip theo loại vật liệu.</summary>
    public AudioClip PickOverrideClip() =>
        impactClipsOverride == null || impactClipsOverride.Length == 0
            ? null
            : impactClipsOverride[Random.Range(0, impactClipsOverride.Length)];
}
