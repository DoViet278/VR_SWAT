using UnityEngine;

/// <summary>
/// Hiệu ứng nổ của bom (lửa, khói, chớp sáng, vết cháy, tiếng nổ). Sinh ra tại tâm nổ, tự huỷ sau LifeTime.
/// Vết cháy (Decal) được đặt xuống mặt đất bên dưới, nằm theo pháp tuyến mặt đất; không có đất thì ẩn.
/// Tiếng nổ phát đúng frame sinh ra (cùng lúc với lửa và sát thương).
/// </summary>
public class NPCExplosion : MonoBehaviour
{
    [SerializeField] private float _lifeTime = 8f;
    [Tooltip("Vết cháy trên mặt đất. Trục Up của transform này là pháp tuyến của vết.")]
    [SerializeField] private Transform _decal;
    [SerializeField] private float _decalLifeTime = 20f;
    [SerializeField] private float _groundSearch = 2.5f;
    [SerializeField] private AudioSource _audio;
    [SerializeField] private AudioClip[] _explosionClips;
    [SerializeField, Range(0f, 0.3f)] private float _pitchJitter = 0.08f;
    [SerializeField] private Light _flash;
    [SerializeField] private float _flashTime = 0.35f;

    private float _flashIntensity;
    private float _spawnTime;

    private void Awake()
    {
        _spawnTime = Time.time;
        if (_flash != null) _flashIntensity = _flash.intensity;

        if (_audio != null)
        {
            AudioClip clip = _explosionClips != null && _explosionClips.Length > 0
                ? _explosionClips[Random.Range(0, _explosionClips.Length)] : _audio.clip;
            if (clip != null)
            {
                _audio.pitch = 1f + Random.Range(-_pitchJitter, _pitchJitter);
                _audio.PlayOneShot(clip);
            }
        }

        PlaceDecal();
        Destroy(gameObject, _lifeTime);
    }

    private void Update()
    {
        if (_flash == null) return;
        float k = Mathf.Clamp01((Time.time - _spawnTime) / Mathf.Max(0.01f, _flashTime));
        _flash.intensity = _flashIntensity * (1f - k) * (1f - k);
        if (k >= 1f) _flash.enabled = false;
    }

    private void PlaceDecal()
    {
        if (_decal == null) return;
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, _groundSearch + 0.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            || (hit.rigidbody != null && !hit.rigidbody.isKinematic))
        {
            _decal.gameObject.SetActive(false);
            return;
        }

        _decal.SetParent(hit.collider.gameObject.isStatic ? null : hit.collider.transform, true);
        _decal.position = hit.point + hit.normal * 0.01f;
        _decal.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * Quaternion.AngleAxis(Random.value * 360f, Vector3.up);
        Destroy(_decal.gameObject, _decalLifeTime);
    }
}
