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

    // Bia có đang nhận đạn không. Mặc định true (dùng độc lập, ví dụ ShootingChallenge).
    // Room02 tắt hết lúc đầu rồi chỉ Arm() bia của bước hiện tại.
    public bool IsArmed { get; private set; } = true;
    private WeaponType? allowedType;   // != null: chỉ đếm phát bắn từ đúng loại súng này (khẩu nào cùng loại cũng được, kể cả bản Clone)

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
        if (gun == null || hitObject == null) return;

        // Mọi phát bắn đều phát sự kiện, nên mỗi bia tự lọc xem có phải mình bị trúng không
        if (hitObject != gameObject && !hitObject.transform.IsChildOf(transform)) return;

        // [DEBUG] Bia này bị trúng: in trạng thái để xem dừng ở đâu
        Debug.Log($"[LightTarget] {name} trúng bởi '{gun.name}' (collider '{hitObject.name}') | armed={IsArmed} completed={IsCompleted} " +
                  $"loại súng bắn={gun.Type} loại được phép={(allowedType.HasValue ? allowedType.Value.ToString() : "bất kỳ")} hits={Hits}/{HitsRequired}", this);

        if (!IsArmed)
        {
            Debug.LogWarning($"[LightTarget] {name}: BỎ QUA vì bia chưa được Arm (Room02 chưa chạy Script hoặc đã Disarm)", this);
            return;
        }
        if (IsCompleted)
        {
            Debug.Log($"[LightTarget] {name}: BỎ QUA vì bia đã xong", this);
            return;
        }

        // if (gun.Type != requiredWeapon)
        // {
        //     onWrongWeapon?.Invoke();
        //     return;
        // }

        // Room02 đã chỉ định loại súng: bắn bằng loại khác thì không tính
        if (allowedType.HasValue && gun.Type != allowedType.Value)
        {
            Debug.LogWarning($"[LightTarget] {name}: BỎ QUA vì sai loại súng. Bắn bằng '{gun.name}' loại {gun.Type} (path {GetPath(gun.transform)}), " +
                             $"bia chỉ nhận loại {allowedType.Value}. Nếu khẩu này đúng là {allowedType.Value} thì kiểm tra Weapon Type trên component Gun", this);
            onWrongWeapon?.Invoke();
            return;
        }

        Hits++;
        Debug.Log($"[LightTarget] {name}: TÍNH phát {Hits}/{HitsRequired}, bật đèn {Hits - 1}", this);
        SetHitLight(Hits - 1, true);   // phát 1 -> đèn 0, phát 2 -> đèn 1...
        onCorrectHit?.Invoke(Hits);

        if (Hits >= HitsRequired)
        {
            IsCompleted = true;
            onCompleted?.Invoke();
            ObserverManager.InvokeEvent(TARGET_COMPLETED, this);
        }
    }

    // Script phòng gọi: bia bắt đầu nhận đạn (type = loại súng được phép, null = loại nào cũng được)
    public void Arm(WeaponType? type = null)
    {
        allowedType = type;
        IsArmed = true;
        Debug.Log($"[LightTarget] {name}: Arm, loại súng được phép = {(type.HasValue ? type.Value.ToString() : "bất kỳ")}", this);
    }

    // Ngừng nhận đạn, đèn giữ nguyên
    public void Disarm()
    {
        IsArmed = false;
        Debug.Log($"[LightTarget] {name}: Disarm", this);
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
        if (index < 0 || index >= LightCount || hitLights[index] == null)
        {
            if (on) Debug.LogWarning($"[LightTarget] {name}: không có đèn ở ô {index} (hitLights chưa gán?)", this);
            return;
        }
        Material m = on ? lightOnMaterial : lightOffMaterial;
        if (m != null)
            hitLights[index].sharedMaterial = m;
        else if (on)
            Debug.LogWarning($"[LightTarget] {name}: lightOnMaterial chưa gán", this);
        if (on) Debug.Log($"[LightTarget] {name}: đèn '{hitLights[index].name}' -> '{(m ? m.name : "null")}'", this);
    }

    static string GetPath(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
