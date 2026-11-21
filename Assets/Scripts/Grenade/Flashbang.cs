using UnityEngine;

namespace VRSwat.Grenades
{
    /// <summary>
    /// Phần "gây chói" của flashbang, gắn chung object với <see cref="VRGrenade"/>.
    /// <see cref="VRGrenade"/> gọi <see cref="Detonate"/> đúng lúc nổ.
    ///
    /// Việc của nó là trả lời một câu: ở chỗ người chơi đang đứng và đang nhìn, quả này chói
    /// bao nhiêu phần? Ba thứ nhân vào nhau:
    ///   - khoảng cách: trong <see cref="fullBlindRadius"/> là chói tối đa, ra tới
    ///     <see cref="blindRadius"/> thì về 0 theo <see cref="distanceFalloff"/>;
    ///   - hướng nhìn: nhìn thẳng vào chỗ nổ thì đủ 100%, quay lưng chỉ còn
    ///     <see cref="peripheralMultiplier"/> (mắt vẫn bắt được ánh sáng dội từ tường);
    ///   - vật cản: có tường chắn giữa thì nhân <see cref="blockedMultiplier"/>.
    ///
    /// Kết quả đẩy sang <see cref="FlashbangReceiver"/> trên đầu người chơi, component đó mới
    /// lo màn trắng và tiếng rít.
    /// </summary>
    [AddComponentMenu("VR SWAT/Flashbang")]
    [DisallowMultipleComponent]
    public class Flashbang : MonoBehaviour
    {
        [Header("Tầm chói")]
        [Tooltip("Bán kính xa nhất còn bị chói (m). Ngoài khoảng này coi như không ảnh hưởng.")]
        [SerializeField] private float blindRadius = 12f;

        [Tooltip("Trong bán kính này thì luôn chói tối đa (m), bất kể xa gần.")]
        [SerializeField] private float fullBlindRadius = 3f;

        [Tooltip("Độ chói theo khoảng cách: X là 0..1 từ Full Blind Radius ra tới Blind Radius.")]
        [SerializeField]
        private AnimationCurve distanceFalloff = AnimationCurve.Linear(0f, 1f, 1f, 0f);

        [Header("Hướng nhìn")]
        [Tooltip("Lệch tâm mắt dưới góc này (độ) thì vẫn ăn đủ 100%.")]
        [Range(0f, 180f)]
        [SerializeField] private float fullEffectAngle = 50f;

        [Tooltip("Lệch quá góc này (độ) thì chỉ còn mức Peripheral Multiplier.")]
        [Range(0f, 180f)]
        [SerializeField] private float peripheralAngle = 120f;

        [Tooltip("Phần độ chói còn lại khi quay lưng hẳn với chỗ nổ. 0 = quay lưng thì miễn " +
                 "nhiễm hoàn toàn.")]
        [Range(0f, 1f)]
        [SerializeField] private float peripheralMultiplier = 0.15f;

        [Header("Vật cản")]
        [Tooltip("Có tường chắn giữa chỗ nổ và mắt thì giảm độ chói.")]
        [SerializeField] private bool requireLineOfSight = true;

        [Tooltip("Lớp được tính là vật cản.")]
        [SerializeField] private LayerMask occluderLayers = ~0;

        [Tooltip("Phần độ chói còn lại khi bị chắn. 0 = núp sau tường là an toàn tuyệt đối.")]
        [Range(0f, 1f)]
        [SerializeField] private float blockedMultiplier = 0f;

        [Tooltip("Chừa trống quanh đầu người chơi khi bắn tia kiểm tra (m), để collider của " +
                 "chính người chơi không bị tính thành tường.")]
        [SerializeField] private float headClearance = 0.3f;

        [Header("Ngưỡng")]
        [Tooltip("Dưới mức này thì bỏ qua, khỏi làm nháy màn hình vô nghĩa.")]
        [Range(0f, 1f)]
        [SerializeField] private float minIntensity = 0.05f;

        [Header("Hiệu ứng ngoài cảnh")]
        [Tooltip("Prefab nổ tùy chọn (khói, tia lửa...). Để trống cũng được.")]
        [SerializeField] private GameObject flashEffect;

        [SerializeField] private float flashEffectLifetime = 3f;

        [Tooltip("Tự sinh một đèn point sáng lóa tại chỗ nổ, soi sáng cả phòng trong tích tắc.")]
        [SerializeField] private bool spawnLightBurst = true;

        [SerializeField] private float lightIntensity = 150f;
        [SerializeField] private float lightRange = 25f;
        [SerializeField] private float lightDuration = 0.35f;
        [SerializeField] private Color lightColor = new Color(1f, 0.98f, 0.92f);

        [Tooltip("Tiếng nổ của flashbang.")]
        [SerializeField] private AudioClip bangClip;

        [SerializeField] private float bangVolume = 1f;

        [Header("Khác")]
        [Tooltip("Chưa thấy Flashbang Receiver nào trong scene thì tự dựng một cái trên camera " +
                 "chính. Tiện lúc thử, nhưng bản dựng sẵn qua menu Tools mới chắc chắn trong build.")]
        [SerializeField] private bool autoCreateReceiver = true;

        /// <summary>
        /// Kích nổ ở <paramref name="center"/>: chạy hiệu ứng ngoài cảnh rồi chói từng người chơi.
        /// Gọi được cả khi object này sắp bị hủy, vì phần chói chạy bên phía người chơi.
        /// </summary>
        public void Detonate(Vector3 center)
        {
            SpawnEffects(center);

            if (autoCreateReceiver && FlashbangReceiver.Active.Count == 0)
                FlashbangReceiver.EnsureDefault();

            var receivers = FlashbangReceiver.Active;

            for (int i = receivers.Count - 1; i >= 0; i--)
            {
                FlashbangReceiver receiver = receivers[i];

                if (receiver == null)
                    continue;

                float strength = EvaluateIntensity(center, receiver);

                if (strength > minIntensity)
                    receiver.ApplyFlash(strength);
            }
        }

        /// <summary>Cường độ chói 0..1 mà <paramref name="receiver"/> phải chịu.</summary>
        public float EvaluateIntensity(Vector3 center, FlashbangReceiver receiver)
        {
            if (receiver == null)
                return 0f;

            Transform eye = receiver.Eye;

            if (eye == null)
                return 0f;

            Vector3 toBlast = center - eye.position;
            float distance = toBlast.magnitude;

            if (distance > blindRadius)
                return 0f;

            float distanceFactor = 1f;

            if (distance > fullBlindRadius && blindRadius > fullBlindRadius)
            {
                float t = Mathf.InverseLerp(fullBlindRadius, blindRadius, distance);
                distanceFactor = Mathf.Clamp01(distanceFalloff.Evaluate(t));
            }

            if (distanceFactor <= 0f)
                return 0f;

            float viewFactor = EvaluateViewFactor(eye, toBlast, distance);

            if (viewFactor <= 0f)
                return 0f;

            float sightFactor = requireLineOfSight && IsBlocked(center, eye, toBlast, distance)
                ? blockedMultiplier
                : 1f;

            return Mathf.Clamp01(distanceFactor * viewFactor * sightFactor);
        }

        private float EvaluateViewFactor(Transform eye, Vector3 toBlast, float distance)
        {
            // Đứng ngay trên quả lựu thì không có hướng để so, cho ăn đủ.
            if (distance < 0.01f)
                return 1f;

            float angle = Vector3.Angle(eye.forward, toBlast / distance);

            if (angle <= fullEffectAngle)
                return 1f;

            if (angle >= peripheralAngle || peripheralAngle <= fullEffectAngle)
                return peripheralMultiplier;

            float t = Mathf.InverseLerp(fullEffectAngle, peripheralAngle, angle);
            return Mathf.Lerp(1f, peripheralMultiplier, t);
        }

        private bool IsBlocked(Vector3 center, Transform eye, Vector3 toBlast, float distance)
        {
            float checkDistance = distance - Mathf.Max(0f, headClearance);

            if (checkDistance <= 0.01f)
                return false;

            Vector3 direction = -toBlast / distance;   // từ chỗ nổ hướng về mắt

            if (!Physics.Raycast(center, direction, out RaycastHit hit, checkDistance,
                    occluderLayers, QueryTriggerInteraction.Ignore))
                return false;

            // Trúng chính người chơi thì không coi là bị chắn.
            return !hit.transform.IsChildOf(eye.root);
        }

        private void SpawnEffects(Vector3 center)
        {
            if (flashEffect != null)
            {
                GameObject effect = Instantiate(flashEffect, center, Quaternion.identity);
                effect.SetActive(true);
                Destroy(effect, flashEffectLifetime);
            }

            if (spawnLightBurst)
                FlashbangLightBurst.Spawn(center, lightColor, lightIntensity, lightRange,
                    lightDuration);

            if (bangClip != null)
                AudioSource.PlayClipAtPoint(bangClip, center, bangVolume);
        }

        private void OnValidate()
        {
            blindRadius = Mathf.Max(0f, blindRadius);
            fullBlindRadius = Mathf.Clamp(fullBlindRadius, 0f, blindRadius);
            peripheralAngle = Mathf.Max(peripheralAngle, fullEffectAngle);
            lightDuration = Mathf.Max(0.01f, lightDuration);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 1f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, blindRadius);

            Gizmos.color = new Color(1f, 1f, 1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, fullBlindRadius);
        }
    }
}
