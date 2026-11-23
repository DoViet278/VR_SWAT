using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace VRSwat.Grenades
{
    /// <summary>
    /// Cục sáng lóa tại chỗ flashbang nổ, tự tắt dần rồi tự hủy.
    ///
    /// Phải là object riêng chứ không nằm trên quả lựu, vì <see cref="VRGrenade"/> hủy quả lựu
    /// ngay khi nổ, coroutine trên đó sẽ chết theo.
    ///
    /// Đây chỉ là ánh sáng ngoài cảnh để người khác thấy "có cái gì vừa lóe lên". Phần chói mắt
    /// người chơi do <see cref="FlashbangReceiver"/> làm, hoàn toàn độc lập với đèn này.
    /// </summary>
    [AddComponentMenu("")]
    public class FlashbangLightBurst : MonoBehaviour
    {
        private Light burst;
        private float startIntensity;
        private float duration;
        private float elapsed;

        /// <summary>Dựng một cục sáng tại <paramref name="position"/> rồi để nó tự lo.</summary>
        public static FlashbangLightBurst Spawn(Vector3 position, Color color, float intensity,
            float range, float duration)
        {
            var go = new GameObject("FlashbangLightBurst");
            go.transform.position = position;

            var pointLight = go.AddComponent<Light>();
            pointLight.type = LightType.Point;
            pointLight.color = color;
            pointLight.intensity = intensity;
            pointLight.range = range;
            pointLight.shadows = LightShadows.None;   // bóng của một đèn 0.3 giây chỉ tốn tiền vô ích
            pointLight.renderMode = LightRenderMode.ForcePixel;

            // URP đọc thêm dữ liệu từ component này; thiếu thì chạy bằng giá trị mặc định.
            go.AddComponent<UniversalAdditionalLightData>();

            var fade = go.AddComponent<FlashbangLightBurst>();
            fade.burst = pointLight;
            fade.startIntensity = intensity;
            fade.duration = Mathf.Max(0.01f, duration);

            return fade;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            if (burst != null)
            {
                // Bình phương để lóe sáng gắt rồi rụng nhanh, thay vì tắt đều đều.
                float k = 1f - t;
                burst.intensity = startIntensity * k * k;
            }

            if (t >= 1f)
                Destroy(gameObject);
        }
    }
}
