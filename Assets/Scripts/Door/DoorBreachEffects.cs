using UnityEngine;

namespace VRSwat.Doors
{
    /// <summary>
    /// Khói, lửa và tiếng nổ khi cánh cửa bị phá.
    ///
    /// Gắn lên chính object có <see cref="DoorController"/>. Nó nghe sự kiện
    /// <see cref="DoorController.Breached"/> — sự kiện này trước giờ không có ai đăng ký, nên phá
    /// cửa xong cánh văng ra trong im lặng tuyệt đối.
    ///
    /// Tách riêng khỏi <see cref="BreachCharge"/> để cửa bị phá bằng đường nào cũng có cùng một
    /// phản ứng: hôm nay là thuốc nổ, mai có thể là đạn hoa cải qua <see cref="DoorBreachable"/>.
    ///
    /// LƯU Ý: <see cref="BreachCharge"/> cũng có ô hiệu ứng nổ của riêng nó. Chỉ điền MỘT trong
    /// hai chỗ, không thì một vụ nổ sinh ra hai lần khói và hai lần tiếng.
    /// </summary>
    [AddComponentMenu("VR SWAT/Door Breach Effects")]
    [DisallowMultipleComponent]
    public class DoorBreachEffects : MonoBehaviour
    {
        [Header("Cửa")]
        [Tooltip("Để trống sẽ tự tìm trên object này rồi ngược lên cha.")]
        [SerializeField] private DoorController door;

        [Header("Hiệu ứng")]
        [Tooltip("Prefab nổ, sinh ra tại điểm trúng. Blow.prefab có sẵn khói, lửa, ánh sáng và vệt cháy.")]
        [SerializeField] private GameObject breachEffect;

        [SerializeField] private float effectLiveTime = 5f;

        [Header("Âm thanh")]
        [SerializeField] private AudioClip breachClip;

        [Tooltip("AudioSource phát tiếng nổ. Để trống sẽ dùng PlayClipAtPoint tại điểm trúng.")]
        [SerializeField] private AudioSource audioSource;

        private void Awake()
        {
            if (door == null)
                door = GetComponentInParent<DoorController>();

            if (door == null)
                Debug.LogWarning($"{name}: không tìm thấy DoorController nên sẽ không có hiệu ứng phá cửa.", this);
        }

        private void OnEnable()
        {
            if (door != null)
                door.Breached += OnBreached;
        }

        private void OnDisable()
        {
            if (door != null)
                door.Breached -= OnBreached;
        }

        private void OnBreached(DoorController breached, Vector3 hitPoint)
        {
            if (breachEffect != null)
            {
                GameObject effect = Instantiate(breachEffect, hitPoint, Quaternion.identity);
                effect.SetActive(true);
                Destroy(effect, effectLiveTime);
            }

            if (breachClip == null)
                return;

            if (audioSource != null)
                audioSource.PlayOneShot(breachClip);
            else
                AudioSource.PlayClipAtPoint(breachClip, hitPoint);
        }
    }
}
