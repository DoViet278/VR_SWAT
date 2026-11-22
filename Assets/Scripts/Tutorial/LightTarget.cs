using System;
using UnityEngine;
using UnityEngine.Events;
using static CONST;

// Tấm bia chỉ tính điểm khi bị bắn bằng đúng loại súng.
// Đủ số phát thì đổi sang material sáng và phát TARGET_COMPLETED.
// Cần: Collider (nằm trong hittableLayers của Gun) trên object này hoặc object con.
public class LightTarget : MonoBehaviour
{
    [Header("Đèn báo số phát (kéo 3 đèn vào theo thứ tự)")]
    [SerializeField] private Renderer[] hitLights = new Renderer[3];
    [SerializeField] private Material lightOffMaterial;
    [SerializeField] private Material lightOnMaterial;

    [Header("Yêu cầu")]
    [SerializeField] private WeaponType requiredWeapon = WeaponType.Pistol;
    [Tooltip("Số phát cần bắn. Nếu đã gắn đèn thì lấy theo số đèn.")]
    [SerializeField, Min(1)] private int hitsRequired = 3;

    [Header("Sự kiện (tùy chọn, nối trong Inspector)")]
    public UnityEvent<int> onCorrectHit;     // số phát đúng hiện tại
    public UnityEvent onWrongWeapon;         // trúng nhưng sai súng
    public UnityEvent onCompleted;

    public WeaponType RequiredWeapon => requiredWeapon;
    public int Hits { get; private set; }
    public int HitsRequired => LightCount > 0 ? LightCount : hitsRequired;
    public bool IsCompleted { get; private set; }

    private int LightCount => hitLights != null ? hitLights.Length : 0;

    private void Start()
    {
        ResetTarget();
    }

    private void OnEnable()
    {
        ObserverManager.Register(PLAYER_SHOOT_TARGET, (Action<Gun, GameObject, RaycastHit>)OnPlayerShoot);
    }

    private void OnDisable()
    {
        ObserverManager.Unregister(PLAYER_SHOOT_TARGET, (Action<Gun, GameObject, RaycastHit>)OnPlayerShoot);
    }

    private void OnPlayerShoot(Gun gun, GameObject hitObject, RaycastHit hit)
    {
        if (IsCompleted || gun == null || hitObject == null) return;

        // Mọi phát bắn đều phát sự kiện, nên mỗi bia tự lọc xem có phải mình bị trúng không
        if (hitObject != gameObject && !hitObject.transform.IsChildOf(transform)) return;

        // if (gun.Type != requiredWeapon)
        // {
        //     onWrongWeapon?.Invoke();
        //     return;
        // }

        Hits++;
        SetHitLight(Hits - 1, true);   // phát 1 -> đèn 0, phát 2 -> đèn 1...
        onCorrectHit?.Invoke(Hits);

        if (Hits >= HitsRequired)
        {
            IsCompleted = true;
            onCompleted?.Invoke();
            ObserverManager.InvokeEvent(TARGET_COMPLETED, this);
        }
    }

    public void ResetTarget()
    {
        Hits = 0;
        IsCompleted = false;
        for (int i = 0; i < LightCount; i++)
            SetHitLight(i, false);
    }

    private void SetHitLight(int index, bool on)
    {
        if (index < 0 || index >= LightCount || hitLights[index] == null) return;
        Material m = on ? lightOnMaterial : lightOffMaterial;
        if (m != null)
            hitLights[index].sharedMaterial = m;
    }
}
