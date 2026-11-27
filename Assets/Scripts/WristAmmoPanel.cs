using System.Collections.Generic;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Panel đeo ở cổ tay hiển thị thông số đạn của khẩu súng đang cầm: số đạn còn trong băng, sức
/// chứa của băng, thanh vạch đạn, trạng thái buồng đạn và số băng dự trữ trong túi đạn.
///
/// Panel KHÔNG gắn chết vào khẩu nào: mặc định nó bám theo khẩu đang nằm trên tay người chơi
/// (xem <see cref="GunAmmoManager.ActiveGuns"/>), nên đổi súng là panel đổi theo, không phải nối
/// dây lại. Muốn ghim cứng vào một khẩu thì tắt Track Held Gun rồi kéo khẩu đó vào ô Ammo Manager.
///
/// Dựng panel bằng menu Tools &gt; VR SWAT &gt; Create Wrist Ammo Panel, đừng dựng tay: canvas phải
/// World Space và phải thu nhỏ cỡ 0.0005, không thì panel to bằng cả căn phòng.
/// </summary>
[DisallowMultipleComponent]
public class WristAmmoPanel : MonoBehaviour
{
    /// <summary>Trục cục bộ của bàn tay, dùng để chỉ ra hướng lòng bàn tay.</summary>
    public enum HandAxis
    {
        Up,
        Down,
        Left,
        Right,
        Forward,
        Back
    }

    [Header("Nguồn dữ liệu")]
    [Tooltip("Bám theo khẩu súng đang nằm trên tay người chơi. Tắt đi thì panel chỉ đọc đúng khẩu " +
             "gán ở ô Ammo Manager bên dưới.")]
    [SerializeField] private bool trackHeldGun = true;

    [Tooltip("Khẩu súng cố định panel này đọc. Bật Track Held Gun thì ô này chỉ là khẩu mặc định " +
             "lúc chưa cầm gì.")]
    [SerializeField] private GunAmmoManager ammoManager;

    [Tooltip("Túi đạn lấy số băng dự trữ. Để trống + bật Auto Find Pouch thì tự tìm trong scene.")]
    [SerializeField] private MagazinePouch pouch;
    [SerializeField] private bool autoFindPouch = true;

    [Header("Thành phần UI")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMP_Text weaponLabel;
    [SerializeField] private TMP_Text chamberLabel;
    [SerializeField] private Image chamberDot;
    [SerializeField] private TMP_Text currentText;
    [SerializeField] private TMP_Text capacityText;
    [Tooltip("Hàng vạch đạn. Các vạch do script tự sinh theo sức chứa của băng đang cắm.")]
    [SerializeField] private RectTransform bulletStrip;
    [Tooltip("Hàng ô băng dự trữ. Các ô cũng do script tự sinh.")]
    [SerializeField] private RectTransform magPips;
    [SerializeField] private TMP_Text spareCountText;

    [Header("Màu")]
    [SerializeField] private Color normalColor = new Color(0.31f, 0.82f, 0.91f);
    [Tooltip("Màu lúc đạn còn ít (dưới Low Ammo Ratio).")]
    [SerializeField] private Color lowColor = new Color(0.94f, 0.64f, 0.23f);
    [Tooltip("Màu lúc hết đạn hoặc chưa cắm băng.")]
    [SerializeField] private Color emptyColor = new Color(0.89f, 0.38f, 0.37f);
    [SerializeField] private Color dimColor = new Color(0.50f, 0.59f, 0.65f);
    [Range(0f, 1f)]
    [SerializeField] private float lowAmmoRatio = 0.25f;

    [Header("Vạch đạn")]
    [Tooltip("Băng chứa nhiều hơn số này thì giấu hàng vạch đi, vì vẽ ra cũng không đọc nổi.")]
    [SerializeField] private int maxTicks = 40;
    [Tooltip("Số ô băng dự trữ vẽ tối đa. Dư ra thì chỉ hiện bằng con số bên cạnh.")]
    [SerializeField] private int maxMagPips = 6;

    [Header("Chữ hiện trên panel")]
    [SerializeField] private string noMagazineText = "--";
    [SerializeField] private string chamberedText = "ĐÃ LÊN ĐẠN";
    [SerializeField] private string chamberEmptyText = "BUỒNG RỖNG";
    [SerializeField] private string spentCaseText = "CÒN VỎ ĐẠN";
    [SerializeField] private string noGunText = "CHƯA CẦM SÚNG";

    [Header("Lúc nào thì hiện")]
    [Tooltip("Bật: chỉ hiện panel khi trên tay đang có súng, buông súng ra là tắt. Tắt: panel vẫn " +
             "hiện với chữ ở ô No Gun Text.")]
    [SerializeField] private bool onlyWhenHoldingGun = true;

    [Tooltip("Buông súng bao lâu rồi mới coi là không cầm (giây). Chừa một nhịp nhỏ cho đỡ nhấp " +
             "nháy lúc chuyền súng từ tay này sang tay kia, vì giữa hai cái nắm có vài frame " +
             "không tay nào giữ súng cả.")]
    [SerializeField] private float releaseGrace = 0.3f;

    [Tooltip("Bật: panel chỉ sáng lên khi người chơi lật cổ tay về phía mặt mình, giống xem đồng " +
             "hồ đeo tay. Tắt: luôn hiện, tiện lúc test trên màn hình.")]
    [SerializeField] private bool requireWristFlip = true;

    [Tooltip("Tự quay mặt panel về phía đầu người chơi lúc chạy, nên không bao giờ nhìn phải lưng " +
             "canvas (chữ lộn ngược như soi gương).\n\n" +
             "Vì sao cần: tư thế bàn tay lúc chạy là do kính theo dõi tay thật, khác hẳn tư thế " +
             "bày trong Scene view, nên canh mắt trong Editor không bảo đảm được gì. Bật cái này " +
             "thì chỉ cần đặt panel nằm phẳng trên cổ tay, úp hay ngửa cũng được.\n\n" +
             "Panel chỉ quay lúc đang mờ, nên người chơi không thấy nó lật.")]
    [SerializeField] private bool autoFaceHead = true;

    [Tooltip("Đầu người chơi. Để trống thì tự tìm camera của XR Origin (hoặc Camera.main) lúc chạy.")]
    [SerializeField] private Transform head;

    [Tooltip("Bật: chỉ hiện khi người chơi ngửa bàn tay lên, hạ tay về tư thế cầm súng là ẩn.\n\n" +
             "Khác với Require Wrist Flip ở chỗ điều kiện này đọc tư thế BÀN TAY chứ không đọc " +
             "hướng mặt panel, nên panel gắn bên hông bàn tay vẫn ăn đúng động tác ngửa tay.")]
    [SerializeField] private bool requirePalmUp = true;

    [Tooltip("Gốc bàn tay (Left / Right Controller). Để trống thì lấy ngay cha của panel.")]
    [SerializeField] private Transform handAnchor;

    [Tooltip("Trục của bàn tay chĩa về phía LÒNG bàn tay. Tay cầm XR thường là Down (-Y), tức " +
             "lòng bàn tay úp xuống khi cầm súng. Chọn sai thì panel hiện đúng lúc đáng lẽ phải " +
             "ẩn: chọn trục ngược lại là xong. Chọn panel trong Scene view sẽ thấy tia vàng vẽ " +
             "theo trục này.")]
    [SerializeField] private HandAxis palmAxis = HandAxis.Down;

    [Tooltip("Lòng bàn tay chếch lên trời trong khoảng bao nhiêu độ thì coi là đã ngửa tay.")]
    [Range(10f, 90f)]
    [SerializeField] private float palmUpAngle = 60f;

    [Tooltip("Góc tối đa giữa mặt panel và hướng về phía đầu thì coi là đang lật cổ tay lên xem (độ).")]
    [Range(10f, 90f)]
    [SerializeField] private float visibleAngle = 55f;

    [Tooltip("Tốc độ hiện ra / mờ đi.")]
    [SerializeField] private float fadeSpeed = 10f;

    private readonly List<Image> ticks = new List<Image>();
    private readonly List<Image> pips = new List<Image>();

    private GunAmmoManager boundGun;
    private int builtTickCount = -1;
    private int builtPipCount = -1;
    private float lastHeldTime = float.NegativeInfinity;
    private float nextHeadSearch;

    /// <summary>Khẩu súng panel đang hiển thị. Null = chưa cầm khẩu nào.</summary>
    public GunAmmoManager BoundGun => boundGun;

    private void Awake()
    {
        ResolveHead();

        if (pouch == null && autoFindPouch)
            pouch = FindFirstObjectByType<MagazinePouch>();

        if (canvasGroup != null && (requireWristFlip || onlyWhenHoldingGun))
            canvasGroup.alpha = 0f;
    }

    private void OnEnable()
    {
        SubscribePouch(true);

        GunAmmoManager held = FindCurrentGun();

        if (held != null)
            lastHeldTime = Time.time;

        Bind(held != null ? held : (onlyWhenHoldingGun ? null : ammoManager));
        Refresh();
    }

    private void OnDisable()
    {
        SubscribePouch(false);
        Bind(null);
    }

    private void Update()
    {
        GunAmmoManager held = FindCurrentGun();

        if (held != null)
        {
            lastHeldTime = Time.time;

            if (held != boundGun)
            {
                Bind(held);
                Refresh();
            }
        }
        else if (boundGun != null && Time.time - lastHeldTime > Mathf.Max(0f, releaseGrace))
        {
            // Buông súng đủ lâu: nhả khẩu cũ ra để panel tắt (hoặc về chữ chưa cầm súng).
            Bind(null);
            Refresh();
        }

        // Xoay trước rồi mới tính độ mờ: làm ngược lại thì ngay frame panel bắt đầu hiện, alpha đã
        // kịp nhích lên và cú lật bị hoãn lại, người chơi đọc phải chữ ngược.
        ResolveHead();
        FaceHead();
        UpdateVisibility();
    }

    /// <summary>Gán tay một khẩu súng cho panel (gọi được từ UnityEvent).</summary>
    public void SetGun(GunAmmoManager gun)
    {
        Bind(gun);
        Refresh();
    }

    /// <summary>
    /// Khẩu súng đang nằm trên tay người chơi lúc này. Ghim cứng một khẩu (tắt Track Held Gun)
    /// thì chỉ khẩu đó mới tính, và vẫn phải đang cầm trên tay.
    /// </summary>
    private GunAmmoManager FindCurrentGun()
    {
        if (trackHeldGun)
            return FindHeldGun();

        if (ammoManager == null)
            return null;

        return !onlyWhenHoldingGun || ammoManager.IsHeldByHand ? ammoManager : null;
    }

    private static GunAmmoManager FindHeldGun()
    {
        IReadOnlyList<GunAmmoManager> guns = GunAmmoManager.ActiveGuns;

        for (int i = 0; i < guns.Count; i++)
        {
            if (guns[i] != null && guns[i].IsHeldByHand)
                return guns[i];
        }

        return null;
    }

    private void Bind(GunAmmoManager gun)
    {
        if (boundGun == gun)
            return;

        if (boundGun != null)
        {
            boundGun.OnAmmoChanged.RemoveListener(Refresh);
            boundGun.OnMagazineInserted.RemoveListener(Refresh);
            boundGun.OnMagazineRemoved.RemoveListener(Refresh);
        }

        boundGun = gun;

        if (boundGun != null)
        {
            boundGun.OnAmmoChanged.AddListener(Refresh);
            boundGun.OnMagazineInserted.AddListener(Refresh);
            boundGun.OnMagazineRemoved.AddListener(Refresh);
        }
    }

    private void SubscribePouch(bool subscribe)
    {
        if (pouch == null)
            return;

        if (subscribe)
        {
            pouch.OnMagazineTaken.AddListener(Refresh);
            pouch.OnMagazineSpawned.AddListener(Refresh);
            pouch.OnPouchEmpty.AddListener(Refresh);
            return;
        }

        pouch.OnMagazineTaken.RemoveListener(Refresh);
        pouch.OnMagazineSpawned.RemoveListener(Refresh);
        pouch.OnPouchEmpty.RemoveListener(Refresh);
    }

    /// <summary>Vẽ lại toàn bộ panel. UnityEvent của súng và của túi đạn gọi thẳng vào đây.</summary>
    public void Refresh()
    {
        bool hasGun = boundGun != null;
        bool hasMag = hasGun && boundGun.HasMagazine;
        int current = hasMag ? boundGun.CurrentAmmo : 0;
        int capacity = hasMag ? boundGun.Capacity : 0;
        Color tone = Tone(hasMag, current, capacity);

        if (weaponLabel != null)
        {
            weaponLabel.text = hasGun ? WeaponTitle(boundGun) : noGunText;
            weaponLabel.color = dimColor;
        }

        if (currentText != null)
        {
            currentText.text = hasMag ? current.ToString("00") : noMagazineText;
            currentText.color = tone;
        }

        if (capacityText != null)
        {
            capacityText.text = hasMag ? "/ " + capacity : string.Empty;
            capacityText.color = dimColor;
        }

        RefreshChamber(hasGun);
        RefreshTicks(hasMag, current, capacity, tone);
        RefreshSpare();
    }

    private Color Tone(bool hasMag, int current, int capacity)
    {
        if (!hasMag || current <= 0)
            return emptyColor;

        if (capacity > 0 && (float)current / capacity <= lowAmmoRatio)
            return lowColor;

        return normalColor;
    }

    private static string WeaponTitle(GunAmmoManager gun)
    {
        Gun weapon = gun.GetComponentInParent<Gun>();

        if (weapon == null)
            return gun.name.ToUpperInvariant();

        return weapon.Type.ToString().ToUpperInvariant() + " · " +
               weapon.Mode.ToString().ToUpperInvariant();
    }

    private void RefreshChamber(bool hasGun)
    {
        if (chamberLabel == null && chamberDot == null)
            return;

        string label = string.Empty;
        Color color = dimColor;

        if (hasGun)
        {
            switch (boundGun.ChamberStatus)
            {
                case GunAmmoManager.ChamberState.Live:
                    label = chamberedText;
                    color = normalColor;
                    break;

                case GunAmmoManager.ChamberState.Spent:
                    label = spentCaseText;
                    color = lowColor;
                    break;

                default:
                    label = chamberEmptyText;
                    color = emptyColor;
                    break;
            }
        }

        if (chamberLabel != null)
        {
            chamberLabel.text = label;
            chamberLabel.color = color;
        }

        if (chamberDot != null)
        {
            chamberDot.color = color;
            chamberDot.enabled = hasGun;
        }
    }

    private void RefreshTicks(bool hasMag, int current, int capacity, Color tone)
    {
        if (bulletStrip == null)
            return;

        bool show = hasMag && capacity > 0 && capacity <= maxTicks;

        if (bulletStrip.gameObject.activeSelf != show)
            bulletStrip.gameObject.SetActive(show);

        if (!show)
            return;

        BuildTicks(capacity);

        Color off = dimColor;
        off.a = 0.22f;

        for (int i = 0; i < ticks.Count; i++)
            ticks[i].color = i < current ? tone : off;
    }

    private void BuildTicks(int capacity)
    {
        if (builtTickCount == capacity && ticks.Count == capacity)
            return;

        ClearChildren(bulletStrip, ticks);

        for (int i = 0; i < capacity; i++)
            ticks.Add(CreateBar(bulletStrip, "Tick" + i.ToString("00")));

        builtTickCount = capacity;
    }

    private void RefreshSpare()
    {
        int spare = pouch != null ? pouch.TotalMagazinesLeft : 0;

        // Túi đạn vô hạn trả về int.MaxValue, in ra màn hình thì thành một dãy số vô nghĩa.
        bool infinite = spare > 999;

        if (spareCountText != null)
        {
            spareCountText.text = pouch == null ? "-" : (infinite ? "∞" : spare.ToString());
            spareCountText.color = spare > 0 ? Color.white : emptyColor;
        }

        if (magPips == null)
            return;

        int pipCount = Mathf.Clamp(infinite ? maxMagPips : spare, 1, maxMagPips);
        BuildPips(pipCount);

        Color off = dimColor;
        off.a = 0.22f;

        for (int i = 0; i < pips.Count; i++)
            pips[i].color = infinite || i < spare ? Color.white : off;
    }

    private void BuildPips(int count)
    {
        if (builtPipCount == count && pips.Count == count)
            return;

        ClearChildren(magPips, pips);

        for (int i = 0; i < count; i++)
            pips.Add(CreateBar(magPips, "Pip" + i.ToString("00")));

        builtPipCount = count;
    }

    private static Image CreateBar(RectTransform parent, string barName)
    {
        var go = new GameObject(barName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        Image image = go.GetComponent<Image>();
        image.raycastTarget = false;

        return image;
    }

    private static void ClearChildren(RectTransform parent, List<Image> cache)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            GameObject child = parent.GetChild(i).gameObject;

            if (Application.isPlaying)
                Destroy(child);
            else
                DestroyImmediate(child);
        }

        cache.Clear();
    }

    // Lật cổ tay lên nhìn thì panel hiện, hạ tay xuống thì tắt: để panel sáng suốt trận sẽ vướng
    // tầm nhìn lúc hai tay đang cầm súng.
    private void UpdateVisibility()
    {
        if (canvasGroup == null)
            return;

        // Tay không cầm súng thì chẳng có gì để đọc: tắt hẳn panel đi cho đỡ vướng mắt.
        if (onlyWhenHoldingGun && boundGun == null)
        {
            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, 0f, fadeSpeed * Time.deltaTime);
            return;
        }

        float target = 1f;
        Vector3 toHead = head != null ? head.position - transform.position : Vector3.zero;

        if (requireWristFlip && head != null)
        {
            // Mặt đọc được của Canvas là mặt -Z chứ không phải +Z: đặt một canvas xoay 0 độ trước
            // camera mặc định thì chữ đọc xuôi, mà lúc đó +Z của nó chĩa ra xa camera. Nên "đang
            // quay về phía người chơi" nghĩa là -Z hướng về đầu.
            float angle = Vector3.Angle(-transform.forward, toHead);

            // Bật Auto Face Head thì mặt kia cũng tính, vì lát nữa panel sẽ tự quay cho đúng chiều.
            bool facing = angle <= visibleAngle || (autoFaceHead && 180f - angle <= visibleAngle);
            target = facing ? 1f : 0f;
        }

        // Chưa ngửa bàn tay thì ẩn, kể cả khi mặt panel đang chếch về phía đầu.
        if (target > 0f && requirePalmUp && !IsPalmUp())
            target = 0f;

        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, target, fadeSpeed * Time.deltaTime);
    }

    /// <summary>
    /// Lòng bàn tay đang chếch lên trời hay không. Đọc thẳng tư thế bàn tay chứ không đọc hướng
    /// mặt panel, nên panel gắn kiểu gì, ở đâu trên tay cũng không ảnh hưởng tới điều kiện này.
    /// </summary>
    private bool IsPalmUp()
    {
        Transform hand = handAnchor != null ? handAnchor : transform.parent;

        // Panel không nằm trong tay ai (đang test rời) thì đừng chặn, kẻo không bao giờ thấy nó.
        if (hand == null)
            return true;

        return Vector3.Angle(PalmDirection(hand), Vector3.up) <= palmUpAngle;
    }

    private Vector3 PalmDirection(Transform hand)
    {
        switch (palmAxis)
        {
            case HandAxis.Up: return hand.up;
            case HandAxis.Down: return -hand.up;
            case HandAxis.Left: return -hand.right;
            case HandAxis.Right: return hand.right;
            case HandAxis.Forward: return hand.forward;
            default: return -hand.forward;
        }
    }

    // Vẽ trục lòng bàn tay trong Scene view: chọn trục cho đúng dễ hơn nhiều khi nhìn thấy nó.
    private void OnDrawGizmosSelected()
    {
        if (!requirePalmUp)
            return;

        Transform hand = handAnchor != null ? handAnchor : transform.parent;

        if (hand == null)
            return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(hand.position, PalmDirection(hand) * 0.15f);
    }

    /// <summary>
    /// Tìm đầu người chơi khi ô Head bỏ trống. Không có đầu thì panel không biết mình đang quay
    /// về phía nào: mất luôn cả điều kiện lật cổ tay lẫn khả năng tự xoay cho chữ đọc xuôi, nên
    /// tìm lại đều đặn phòng khi XR Origin được spawn sau.
    /// </summary>
    private void ResolveHead()
    {
        if (head != null || Time.unscaledTime < nextHeadSearch)
            return;

        nextHeadSearch = Time.unscaledTime + 1f;

        XROrigin origin = FindFirstObjectByType<XROrigin>();

        if (origin != null && origin.Camera != null)
        {
            head = origin.Camera.transform;
            return;
        }

        if (Camera.main != null)
            head = Camera.main.transform;
    }

    /// <summary>
    /// Quay panel nửa vòng khi người chơi đang nhìn vào mặt sau của nó, tức là chữ đang lộn ngược
    /// như soi gương. Mặt đọc được của Canvas là mặt -Z, nên nhìn thấy mặt sau nghĩa là +Z đang
    /// chĩa về phía đầu.
    ///
    /// Tư thế bàn tay lúc chạy do kính theo dõi tay thật quyết định chứ không phải tư thế bày sẵn
    /// trong Scene view, nên cứ để nó tự nắn cho chắc.
    /// </summary>
    private void FaceHead()
    {
        if (!autoFaceHead || head == null)
            return;

        // Chừa một khoảng chết quanh lúc nhìn nghiêng, kẻo panel lật qua lật lại.
        if (Vector3.Dot(transform.forward, (head.position - transform.position).normalized) < 0.1f)
            return;

        transform.Rotate(0f, 180f, 0f, Space.Self);

        // Kéo panel về trong suốt rồi cho hiện lại: cú lật diễn ra lúc không nhìn thấy gì, nên
        // người chơi không kịp thấy chữ ngược lẫn cảnh panel quay.
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
    }
}
