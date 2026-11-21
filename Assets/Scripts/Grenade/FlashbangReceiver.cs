using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRSwat.Grenades
{
    /// <summary>
    /// Phần "bị chói" của flashbang, gắn lên mắt người chơi (camera của XR Origin).
    ///
    /// Component này không tự biết quả lựu nổ ở đâu. <see cref="Flashbang"/> tính ra cường độ
    /// 0..1 rồi gọi <see cref="ApplyFlash"/>, ở đây chỉ lo dựng màn trắng và tiếng rít tai.
    ///
    /// Màn trắng là một tấm quad sinh ra lúc chạy, làm con của camera, nằm sát mặt phẳng near.
    /// Không dùng Canvas Screen Space Overlay vì kiểu đó không hiện trên kính VR, cũng không
    /// dùng post-process vì Volume override cần dựng cả profile mà vẫn không chắc vẽ sau cùng.
    ///
    /// Nhiều quả nổ liên tiếp thì cộng dồn kiểu "lấy cái nặng nhất": độ chói và thời gian đều
    /// lấy max, không nối đuôi nhau, nên không bao giờ bị chói lâu hơn một quả duy nhất đáng ra.
    /// </summary>
    [AddComponentMenu("VR SWAT/Flashbang Receiver")]
    [DisallowMultipleComponent]
    public class FlashbangReceiver : MonoBehaviour
    {
        private static readonly List<FlashbangReceiver> Receivers = new List<FlashbangReceiver>();
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public const string OverlayShaderName = "VR SWAT/Flashbang Overlay";

        [Header("Mắt")]
        [Tooltip("Camera của người chơi. Để trống thì lấy Camera trên object này, không có nữa " +
                 "thì lấy Camera.main.")]
        [SerializeField] private Transform eye;

        [Header("Màn chói")]
        [Tooltip("Shader vẽ màn trắng. Để trống sẽ tự tìm theo tên, nhưng nên gán sẵn: shader " +
                 "không được asset nào tham chiếu có thể bị loại khỏi build.")]
        [SerializeField] private Shader overlayShader;

        [SerializeField] private Color flashColor = Color.white;

        [Tooltip("Độ đục tối đa. 1 = trắng xóa hoàn toàn.")]
        [Range(0f, 1f)]
        [SerializeField] private float maxOpacity = 1f;

        [Tooltip("Giây giữ nguyên độ chói đỉnh, ứng với cường độ 1. Cường độ thấp hơn thì ngắn " +
                 "lại theo tỉ lệ.")]
        [SerializeField] private float peakHold = 1.2f;

        [Tooltip("Giây mờ dần sau khi hết giữ, ứng với cường độ 1.")]
        [SerializeField] private float fadeDuration = 4f;

        [Tooltip("Độ chói theo thời gian mờ dần: trục X là 0..1 của quãng mờ, trục Y là phần " +
                 "độ chói còn lại.")]
        [SerializeField]
        private AnimationCurve fadeCurve = new AnimationCurve(
            new Keyframe(0f, 1f, 0f, -0.6f),
            new Keyframe(1f, 0f, -1.2f, 0f));

        [Header("Tiếng rít tai")]
        [Tooltip("Clip rít tai, nên là loop. Không bắt buộc.")]
        [SerializeField] private AudioClip ringingClip;

        [Range(0f, 1f)]
        [SerializeField] private float ringingVolume = 0.7f;

        [Tooltip("Hạ âm lượng mọi tiếng khác khi đang bị choáng (kiểu bị ù tai).")]
        [SerializeField] private bool muffleOtherSounds = true;

        [Tooltip("Âm lượng còn lại của các tiếng khác lúc choáng nặng nhất.")]
        [Range(0f, 1f)]
        [SerializeField] private float muffledVolume = 0.15f;

        private MeshRenderer overlayRenderer;
        private Material overlayMaterial;
        private Mesh overlayMesh;
        private AudioSource ringingSource;

        private float peakAlpha;
        private float holdLeft;
        private float fadeLeft;
        private float fadeTotal;
        private float currentAlpha;
        private float baseListenerVolume = 1f;
        private bool flashing;

        /// <summary>Mọi người chơi đang có thể bị chói. Flashbang duyệt danh sách này.</summary>
        public static IReadOnlyList<FlashbangReceiver> Active => Receivers;

        /// <summary>Mốc mắt dùng để tính khoảng cách và hướng nhìn.</summary>
        public Transform Eye => eye != null ? eye : transform;

        /// <summary>Đang trong lúc bị chói hay không.</summary>
        public bool IsFlashed => flashing;

        /// <summary>Phần độ chói còn lại, 0..1. Dùng cho AI hoặc HUD nếu cần.</summary>
        public float CurrentIntensity => flashing && maxOpacity > 0f
            ? Mathf.Clamp01(currentAlpha / maxOpacity)
            : 0f;

        /// <summary>
        /// Tìm receiver đang sống; chưa có thì tự dựng một cái trên camera chính.
        /// Đỡ phải setup tay, nhưng bản dựng sẵn (qua menu Tools) mới chắc chắn còn shader
        /// trong build.
        /// </summary>
        public static FlashbangReceiver EnsureDefault()
        {
            for (int i = Receivers.Count - 1; i >= 0; i--)
            {
                if (Receivers[i] != null)
                    return Receivers[i];
            }

            Camera cam = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();

            if (cam == null)
                return null;

            // Có thể camera đã có receiver nhưng đang disable nên không nằm trong danh sách.
            // AddComponent lúc đó sẽ fail vì DisallowMultipleComponent, nên phải tìm trước.
            FlashbangReceiver existing = cam.GetComponent<FlashbangReceiver>();

            if (existing != null)
                return existing;

            return cam.gameObject.AddComponent<FlashbangReceiver>();
        }

        private void Awake()
        {
            if (eye == null)
            {
                Camera own = GetComponent<Camera>();
                eye = own != null
                    ? own.transform
                    : (Camera.main != null ? Camera.main.transform : transform);
            }

            baseListenerVolume = AudioListener.volume;
        }

        private void OnEnable()
        {
            if (!Receivers.Contains(this))
                Receivers.Add(this);
        }

        private void OnDisable()
        {
            Receivers.Remove(this);
            StopFlash();
        }

        private void OnDestroy()
        {
            if (overlayMaterial != null)
                Destroy(overlayMaterial);

            if (overlayMesh != null)
                Destroy(overlayMesh);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Gán sẵn shader lúc ở Editor để nó được scene/prefab tham chiếu, nhờ vậy không bị
            // loại khỏi build. Shader.Find lúc chạy chỉ là lưới an toàn cuối.
            if (overlayShader == null)
                overlayShader = Shader.Find(OverlayShaderName);

            peakHold = Mathf.Max(0f, peakHold);
            fadeDuration = Mathf.Max(0.01f, fadeDuration);
        }
#endif

        /// <summary>
        /// Chói mắt người chơi với cường độ <paramref name="intensity01"/> (0..1).
        /// Gọi nhiều lần thì lấy cái nặng nhất chứ không cộng dồn.
        /// </summary>
        public void ApplyFlash(float intensity01)
        {
            intensity01 = Mathf.Clamp01(intensity01);

            if (intensity01 <= 0f || maxOpacity <= 0f)
                return;

            EnsureOverlay();

            if (overlayRenderer == null)
                return;

            peakAlpha = Mathf.Max(peakAlpha, maxOpacity * intensity01);
            holdLeft = Mathf.Max(holdLeft, peakHold * intensity01);
            fadeTotal = Mathf.Max(fadeTotal, fadeDuration * intensity01);
            fadeLeft = fadeTotal;

            if (!flashing)
            {
                flashing = true;
                baseListenerVolume = AudioListener.volume;
                overlayRenderer.enabled = true;
                StartRinging();
            }

            Advance(0f);
        }

        /// <summary>Tắt hiệu ứng ngay, dùng khi hồi sinh hoặc đổi scene.</summary>
        public void StopFlash()
        {
            flashing = false;
            peakAlpha = 0f;
            holdLeft = 0f;
            fadeLeft = 0f;
            fadeTotal = 0f;
            currentAlpha = 0f;

            if (overlayRenderer != null)
                overlayRenderer.enabled = false;

            if (ringingSource != null && ringingSource.isPlaying)
                ringingSource.Stop();

            if (muffleOtherSounds)
                AudioListener.volume = baseListenerVolume;
        }

        private void Update()
        {
            if (flashing)
                Advance(Time.deltaTime);
        }

        private void Advance(float dt)
        {
            if (holdLeft > 0f)
            {
                holdLeft -= dt;
                currentAlpha = peakAlpha;
            }
            else if (fadeLeft > 0f)
            {
                fadeLeft = Mathf.Max(0f, fadeLeft - dt);
                float t = fadeTotal > 0f ? 1f - fadeLeft / fadeTotal : 1f;
                currentAlpha = peakAlpha * Mathf.Clamp01(fadeCurve.Evaluate(t));
            }
            else
            {
                StopFlash();
                return;
            }

            float envelope = peakAlpha > 0f ? Mathf.Clamp01(currentAlpha / peakAlpha) : 0f;

            if (overlayMaterial != null)
            {
                Color c = flashColor;
                c.a = currentAlpha;
                overlayMaterial.SetColor(ColorId, c);
            }

            if (ringingSource != null && ringingSource.isPlaying)
                ringingSource.volume = ringingVolume * envelope;

            if (muffleOtherSounds)
                AudioListener.volume = Mathf.Lerp(baseListenerVolume,
                    baseListenerVolume * muffledVolume, envelope);
        }

        private void StartRinging()
        {
            if (ringingClip == null)
                return;

            if (ringingSource == null)
            {
                ringingSource = gameObject.AddComponent<AudioSource>();
                ringingSource.playOnAwake = false;
                ringingSource.loop = true;
                ringingSource.spatialBlend = 0f;           // 2D: tiếng rít ở trong đầu
                ringingSource.ignoreListenerVolume = true;  // khỏi bị chính cái muffle dìm xuống
                ringingSource.ignoreListenerPause = true;
            }

            ringingSource.clip = ringingClip;
            ringingSource.volume = ringingVolume;

            if (!ringingSource.isPlaying)
                ringingSource.Play();
        }

        private void EnsureOverlay()
        {
            if (overlayRenderer != null)
                return;

            Transform target = Eye;

            if (target == null)
                return;

            Shader shader = overlayShader != null ? overlayShader : Shader.Find(OverlayShaderName);

            if (shader == null)
            {
                Debug.LogError("[Flashbang] Không tìm thấy shader " + OverlayShaderName +
                               ". Gán tay vào ô Overlay Shader của Flashbang Receiver.", this);
                return;
            }

            Camera cam = target.GetComponent<Camera>();
            float near = cam != null ? cam.nearClipPlane : 0.05f;
            float distance = Mathf.Max(near * 2f, 0.03f);

            var go = new GameObject("FlashbangOverlay");
            go.transform.SetParent(target, false);
            go.transform.SetLocalPositionAndRotation(new Vector3(0f, 0f, distance),
                Quaternion.identity);

            // Quad rộng gấp 40 lần khoảng cách tới mắt: phủ khoảng 174 độ, kín cả hai mắt kể cả
            // khi mắt lệch sang hai bên so với gốc camera.
            go.transform.localScale = Vector3.one * distance * 40f;

            overlayMesh = CreateQuad();
            go.AddComponent<MeshFilter>().sharedMesh = overlayMesh;

            overlayMaterial = new Material(shader) { name = "FlashbangOverlay (runtime)" };
            overlayMaterial.SetColor(ColorId,
                new Color(flashColor.r, flashColor.g, flashColor.b, 0f));

            overlayRenderer = go.AddComponent<MeshRenderer>();
            overlayRenderer.sharedMaterial = overlayMaterial;
            overlayRenderer.shadowCastingMode = ShadowCastingMode.Off;
            overlayRenderer.receiveShadows = false;
            overlayRenderer.lightProbeUsage = LightProbeUsage.Off;
            overlayRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            overlayRenderer.allowOcclusionWhenDynamic = false;
            overlayRenderer.enabled = false;
        }

        private static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "FlashbangOverlayQuad" };

            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
            });

            mesh.SetTriangles(new List<int> { 0, 2, 1, 2, 3, 1 }, 0);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1f, 1f, 0.01f));

            return mesh;
        }
    }
}
