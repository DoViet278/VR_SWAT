using System.Collections.Generic;
using UnityEngine;

namespace VRSwat.Doors
{
    /// <summary>
    /// Đánh dấu "cửa này có panel chọn cách mở", và giữ phần cấu hình riêng của từng cửa: danh
    /// sách option, mốc đặt panel, và cái đèn báo lúc người chơi lại gần.
    ///
    /// Component này KHÔNG đọc nút và KHÔNG vẽ panel. Phần đó nằm ở
    /// <see cref="DoorActionMenuInput"/> — một cái duy nhất trên XR Origin — để cả màn chỉ có một
    /// chỗ poll input thay vì mỗi cửa một cái <c>Update</c>.
    ///
    /// Gắn lên cùng object với <see cref="DoorController"/> (hoặc object con, script tự tìm ngược
    /// lên cha).
    /// </summary>
    [AddComponentMenu("VR SWAT/Door Action Menu")]
    [DisallowMultipleComponent]
    public class DoorActionMenu : MonoBehaviour
    {
        [Header("Cửa")]
        [Tooltip("Để trống sẽ tự tìm DoorController ở cấp cha.")]
        [SerializeField] private DoorController door;

        [Header("Option")]
        [Tooltip("Các cách mở cửa hiện trên panel, theo đúng thứ tự này. Để TRỐNG thì cửa dùng " +
                 "danh sách mặc định khai trên Door Action Menu Input — chỉ cửa nào đặc biệt " +
                 "mới cần điền riêng ở đây.")]
        [SerializeField] private List<DoorActionOption> options = new List<DoorActionOption>();

        [Header("Chỗ đặt panel")]
        [Tooltip("Mốc để panel hiện ra cạnh đó. Để trống thì lấy ổ khóa của cửa (Lock Point).")]
        [SerializeField] private Transform anchor;

        [Tooltip("Dịch panel so với mốc, tính theo world space (m). Y dương là nhổng lên cao.")]
        [SerializeField] private Vector3 panelOffset = new Vector3(0f, 0.25f, 0f);

        [Tooltip("Kéo panel về phía người chơi bấy nhiêu mét, để nó không cắm vào cánh cửa.")]
        [SerializeField] private float pullTowardPlayer = 0.15f;

        [Header("Đèn báo khi lại gần")]
        [Tooltip("Object phát sáng ở ổ khóa (quad có material emissive, halo...). Bật/tắt theo " +
                 "khoảng cách. Không bắt buộc.")]
        [SerializeField] private GameObject highlightObject;

        [Tooltip("Đèn point light nhỏ đặt ở ổ khóa. Không bắt buộc.")]
        [SerializeField] private Light highlightLight;

        [Tooltip("Độ sáng tối đa của đèn khi người chơi đứng sát.")]
        [SerializeField] private float lightIntensity = 1.5f;

        [Tooltip("Số nhịp nhấp nháy mỗi giây. 0 = sáng đều, không nhấp nháy.")]
        [SerializeField] private float pulsesPerSecond = 1.2f;

        [Tooltip("Lúc nhấp nháy thì tối nhất còn bao nhiêu phần độ sáng.")]
        [Range(0f, 1f)]
        [SerializeField] private float pulseFloor = 0.45f;

        [Tooltip("Renderer đổi material khi sáng lên. CẢNH BÁO: đừng kéo cái lamp mà " +
                 "DoorLockLight đang dùng vào đây, hai script sẽ giành nhau ghi material. Để " +
                 "trống nếu đã có Highlight Object hoặc Highlight Light.")]
        [SerializeField] private Renderer highlightRenderer;

        [SerializeField] private Material highlightMaterial;

        [Tooltip("Tốc độ sáng lên / tắt đi.")]
        [SerializeField] private float fadeSpeed = 8f;

        private static readonly List<DoorActionMenu> all = new List<DoorActionMenu>();

        /// <summary>Mọi cửa có panel đang sống trong scene, để khỏi quét toàn scene mỗi frame.</summary>
        public static IReadOnlyList<DoorActionMenu> All => all;

        /// <summary>Panel đang mở, hoặc null. Dùng để chặn nắm tay nắm trong lúc đang chọn.</summary>
        public static DoorActionMenu Open { get; internal set; }

        public DoorController Door => door;

        public Transform Anchor => anchor != null ? anchor : door != null ? door.LockPoint : transform;

        private Material originalMaterial;
        private float glow;
        private bool wantGlow;

        /// <summary>Panel này đang giữ chỗ cửa (xem <see cref="RequestDoor"/>).</summary>
        private bool holdingDoor;

        private void Awake()
        {
            if (door == null)
                door = GetComponentInParent<DoorController>();

            if (door == null)
                Debug.LogWarning(
                    $"{name}: không tìm thấy DoorController ở cấp cha nên panel không biết mở " +
                    "cửa nào. Gán ô Door, hoặc đặt component này lên chính object cửa.", this);

            // Gán nhầm chính object này (hoặc một object cha) làm đèn báo là tự sát: tắt đèn
            // nghĩa là SetActive(false) lên chính mình, OnEnable không bao giờ chạy, cửa không
            // vào được danh sách All, và panel im lặng không hiện gì mà cũng không báo lỗi.
            if (highlightObject != null &&
                (highlightObject == gameObject || transform.IsChildOf(highlightObject.transform)))
            {
                Debug.LogWarning(
                    $"{name}: ô Highlight Object đang trỏ vào \"{highlightObject.name}\" — chính " +
                    "object chứa Door Action Menu (hoặc object cha của nó). Tắt đèn là tắt luôn " +
                    "component này nên panel sẽ không bao giờ hiện. Tạo một object CON riêng để " +
                    "phát sáng ở ổ khóa rồi gán vào đây, hoặc để trống ô này và dùng Highlight " +
                    "Light. Tạm thời bỏ qua ô Highlight Object.", this);
                highlightObject = null;
            }

            if (highlightRenderer != null)
                originalMaterial = highlightRenderer.sharedMaterial;

            ApplyGlow(0f);
        }

        private void OnEnable()
        {
            if (!all.Contains(this))
                all.Add(this);
        }

        private void OnDisable()
        {
            all.Remove(this);

            if (Open == this)
                Open = null;

            wantGlow = false;
            ApplyGlow(0f);
        }

        private void Update()
        {
            // Trả chỗ ngay khi cánh cửa đứng yên. DoorController.Request giữ Occupant cho tới
            // khi actor tự Release, không tự nhả — quên bước này thì cửa vừa mở bằng panel một
            // lần là từ đó AI lẫn panel đều nhận Busy vĩnh viễn.
            if (holdingDoor && (door == null || !door.IsMoving))
            {
                if (door != null)
                    door.Release(this);

                holdingDoor = false;
            }

            float target = wantGlow ? 1f : 0f;

            if (!Mathf.Approximately(glow, target))
            {
                glow = fadeSpeed > 0f
                    ? Mathf.MoveTowards(glow, target, fadeSpeed * Time.deltaTime)
                    : target;
            }
            else if (!wantGlow)
            {
                return;     // đã tắt hẳn, khỏi ghi lại mỗi frame
            }

            ApplyGlow(glow);
        }

        /// <summary>Chỗ panel hiện ra, đã kéo về phía <paramref name="viewer"/> một chút.</summary>
        public Vector3 PanelPoint(Vector3 viewer)
        {
            Vector3 point = Anchor.position + panelOffset;

            if (pullTowardPlayer <= 0f)
                return point;

            Vector3 toViewer = viewer - point;
            toViewer.y = 0f;

            return toViewer.sqrMagnitude > 0.0001f
                ? point + toViewer.normalized * pullTowardPlayer
                : point;
        }

        /// <summary>
        /// Xin cửa làm một việc, nhân danh panel này. Dùng cái này thay vì gọi thẳng
        /// <see cref="DoorController.Request"/>: panel sẽ tự trả chỗ lúc cánh cửa dừng, nên AI
        /// vẫn dùng được cửa sau đó.
        /// </summary>
        public DoorRequestResult RequestDoor(DoorAction action, Vector3 from)
        {
            if (door == null)
                return DoorRequestResult.Refused;

            DoorRequestResult result = door.Request(action, this, from);

            if (result == DoorRequestResult.Accepted)
                holdingDoor = true;

            return result;
        }

        /// <summary>Cửa đang rảnh, hoặc đang do chính panel này giữ.</summary>
        public bool DoorIsFree => door != null && (door.Occupant == null || ReferenceEquals(door.Occupant, this));

        /// <summary>Bật/tắt đèn báo "cửa này tương tác được". Do Door Action Menu Input gọi.</summary>
        public void SetPrompt(bool on)
        {
            wantGlow = on;
        }

        /// <summary>
        /// Danh sách option của cửa này. Cửa để trống thì dùng <paramref name="fallback"/> khai
        /// trên Door Action Menu Input.
        /// </summary>
        public IReadOnlyList<DoorActionOption> ResolveOptions(IReadOnlyList<DoorActionOption> fallback)
        {
            return options != null && options.Count > 0 ? options : fallback;
        }

        private void ApplyGlow(float amount)
        {
            bool on = amount > 0.001f;

            if (highlightObject != null && highlightObject.activeSelf != on)
                highlightObject.SetActive(on);

            if (highlightLight != null)
            {
                highlightLight.enabled = on;
                highlightLight.intensity = lightIntensity * amount * Pulse();
            }

            if (highlightRenderer != null && highlightMaterial != null)
            {
                Material wanted = on ? highlightMaterial : originalMaterial;

                if (wanted != null && highlightRenderer.sharedMaterial != wanted)
                    highlightRenderer.sharedMaterial = wanted;
            }
        }

        private float Pulse()
        {
            if (pulsesPerSecond <= 0f)
                return 1f;

            float wave = 0.5f + 0.5f * Mathf.Sin(Time.time * pulsesPerSecond * Mathf.PI * 2f);
            return Mathf.Lerp(pulseFloor, 1f, wave);
        }

        private void OnDrawGizmosSelected()
        {
            Transform a = anchor != null ? anchor : transform;
            Gizmos.color = new Color(0.95f, 0.78f, 0.17f);
            Gizmos.DrawWireSphere(a.position + panelOffset, 0.08f);
        }
    }
}
