using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Quản lý tập trung lỗ đạn và tiếng va chạm: tạo sẵn pool, giới hạn số lượng,
/// vượt giới hạn thì thu hồi lỗ cũ nhất (ring buffer). Đặt 1 object duy nhất trong scene.
/// </summary>
[DisallowMultipleComponent]
public class BulletDecalManager : MonoBehaviour
{
    [Header("Surfaces")]
    [Tooltip("Cấu hình dùng khi vật bị bắn không có component ImpactSurface.")]
    [SerializeField] private ImpactSurfaceEntry defaultSurface = new ImpactSurfaceEntry();
    [Tooltip("Bảng tra theo loại vật liệu. Loại không có trong bảng sẽ rơi về Default Surface.")]
    [SerializeField] private List<ImpactSurfaceEntry> surfaces = new List<ImpactSurfaceEntry>();

    [Header("Pool")]
    [Tooltip("Số lỗ đạn tối đa tồn tại cùng lúc. Vượt quá thì lỗ cũ nhất bị tái sử dụng.")]
    [SerializeField] private int maxDecals = 96;
    [Tooltip("Số decal tạo sẵn cho mỗi loại bề mặt lúc khởi động, tránh khựng ở phát bắn đầu.")]
    [SerializeField] private int prewarmPerSurface = 8;

    [Header("Placement")]
    [Tooltip("Đẩy decal ra khỏi mặt tường theo pháp tuyến (mét) để không bị z-fighting.")]
    [SerializeField] private float surfaceOffset = 0.004f;
    [Tooltip("Xoay ngẫu nhiên quanh pháp tuyến để các lỗ đạn không giống hệt nhau.")]
    [SerializeField] private bool randomRoll = true;
    [Tooltip("Khoảng dao động kích thước ngẫu nhiên của mỗi lỗ đạn.")]
    [SerializeField] private Vector2 randomScaleRange = new Vector2(0.9f, 1.1f);
    [Tooltip("Chỉ cho decal bám vào vật khi scale của vật gần đều. Lệch quá tỉ lệ này thì decal đứng yên trong world để không bị méo.")]
    [SerializeField] private float maxParentScaleRatio = 1.05f;

    [Header("Impact Audio")]
    [Tooltip("Số AudioSource quay vòng dùng chung cho tiếng va chạm.")]
    [SerializeField] private int audioSourceCount = 4;
    [Tooltip("Âm lượng chung của tiếng va chạm.")]
    [Range(0f, 1f)] [SerializeField] private float impactVolume = 0.8f;
    [Tooltip("Khoảng cao độ ngẫu nhiên mỗi phát, tránh nghe lặp.")]
    [SerializeField] private Vector2 impactPitchRange = new Vector2(0.92f, 1.08f);
    [Tooltip("Bán kính nghe rõ (mét).")]
    [SerializeField] private float audioMinDistance = 1f;
    [Tooltip("Khoảng cách tắt hẳn tiếng (mét).")]
    [SerializeField] private float audioMaxDistance = 25f;

    private static BulletDecalManager instance;

    /// <summary>Một ô trong ring buffer: giữ instance decal đang hiển thị và prefab gốc của nó.</summary>
    private sealed class DecalSlot
    {
        public GameObject instance;
        public Transform cachedTransform;
        public GameObject sourcePrefab;
    }

    private readonly List<DecalSlot> slots = new List<DecalSlot>();
    private readonly Dictionary<GameObject, Stack<GameObject>> idlePool =
        new Dictionary<GameObject, Stack<GameObject>>();

    private AudioSource[] audioSources;
    private Transform decalRoot;
    private int nextSlot;
    private int nextAudioSource;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            // Chỉ bỏ component trùng, không destroy object của người khác.
            Destroy(this);
            return;
        }

        instance = this;

        decalRoot = new GameObject("Decals").transform;
        decalRoot.SetParent(transform, false);

        BuildAudioSources();
        Prewarm();
    }

    private void OnDestroy()
    {
        // Object Unity đã destroy không null với toán tử ?. của C#, phải tự xoá tham chiếu static.
        if (instance == this)
            instance = null;
    }

    /// <summary>Điểm vào duy nhất cho súng: tự bỏ qua khi scene không có manager.</summary>
    public static void SpawnImpact(RaycastHit hit)
    {
        if (instance != null)
            instance.Spawn(hit);
    }

    /// <summary>Xoá toàn bộ lỗ đạn đang hiển thị. Dùng khi reset màn chơi.</summary>
    public void ClearAll()
    {
        for (int i = 0; i < slots.Count; i++)
            ReleaseToPool(slots[i]);

        slots.Clear();
        nextSlot = 0;
    }

    private void Spawn(RaycastHit hit)
    {
        if (hit.collider == null)
            return;

        ImpactSurface surface = hit.collider.GetComponentInParent<ImpactSurface>();
        ImpactSurfaceEntry entry = ResolveEntry(surface);

        TrySpawnDecal(hit, surface, entry);
        TryPlayImpactSound(hit.point, surface, entry);
    }

    private ImpactSurfaceEntry ResolveEntry(ImpactSurface surface)
    {
        if (surface == null)
            return defaultSurface;

        for (int i = 0; i < surfaces.Count; i++)
        {
            if (surfaces[i] != null && surfaces[i].surfaceType == surface.SurfaceType)
                return surfaces[i];
        }

        return defaultSurface;
    }

    private void TrySpawnDecal(RaycastHit hit, ImpactSurface surface, ImpactSurfaceEntry entry)
    {
        if (surface != null && !surface.ReceiveDecals)
            return;

        GameObject prefab = surface != null && surface.DecalOverride != null
            ? surface.DecalOverride
            : entry.decalPrefab;

        if (prefab == null || maxDecals <= 0)
            return;

        DecalSlot slot = AcquireSlot(prefab);
        if (slot.cachedTransform == null)
            return;

        // Unity tạo element mới trong List với mọi field = 0, bỏ qua giá trị khởi tạo trong C#,
        // nên coi decalScale <= 0 là "chưa set" và về 1, thay vì ra lỗ đạn bé tí gần như vô hình.
        float entryScale = entry.decalScale > 0f ? entry.decalScale : 1f;

        float scale = entryScale
                      * (surface != null ? surface.DecalScaleMultiplier : 1f)
                      * Random.Range(randomScaleRange.x, randomScaleRange.y);

        bool attach = hit.collider.attachedRigidbody != null
                      && (surface == null || surface.AttachDecals);

        PlaceDecal(slot.cachedTransform, hit, scale, attach);
    }

    /// <summary>
    /// Lấy ô để dùng: chưa đầy thì thêm ô mới, đầy rồi thì tái sử dụng ô cũ nhất.
    /// Các ô giữ nguyên index, chỉ con trỏ nextSlot chạy vòng, nên ô tại nextSlot luôn là ô cũ nhất.
    /// </summary>
    private DecalSlot AcquireSlot(GameObject prefab)
    {
        DecalSlot slot;

        if (slots.Count < maxDecals)
        {
            slot = new DecalSlot();
            slots.Add(slot);
        }
        else
        {
            slot = slots[nextSlot];
            nextSlot = (nextSlot + 1) % maxDecals;
        }

        // Instance cũ có thể đã bị huỷ cùng vật cha -> coi như ô trống.
        if (slot.instance == null)
        {
            slot.instance = null;
            slot.cachedTransform = null;
            slot.sourcePrefab = null;
        }

        if (slot.sourcePrefab != prefab)
        {
            ReleaseToPool(slot);

            slot.instance = Rent(prefab);
            slot.cachedTransform = slot.instance.transform;
            slot.sourcePrefab = prefab;
        }

        if (!slot.instance.activeSelf)
            slot.instance.SetActive(true);

        return slot;
    }

    private GameObject Rent(GameObject prefab)
    {
        if (idlePool.TryGetValue(prefab, out Stack<GameObject> stack))
        {
            while (stack.Count > 0)
            {
                GameObject pooled = stack.Pop();
                if (pooled != null)
                    return pooled;
            }
        }

        GameObject created = Instantiate(prefab, decalRoot);
        created.SetActive(false);
        return created;
    }

    private void ReleaseToPool(DecalSlot slot)
    {
        GameObject released = slot.instance;
        GameObject sourcePrefab = slot.sourcePrefab;

        slot.instance = null;
        slot.cachedTransform = null;
        slot.sourcePrefab = null;

        if (released == null)
            return;

        released.SetActive(false);
        released.transform.SetParent(decalRoot, false);

        if (sourcePrefab == null)
        {
            Destroy(released);
            return;
        }

        if (!idlePool.TryGetValue(sourcePrefab, out Stack<GameObject> stack))
        {
            stack = new Stack<GameObject>();
            idlePool[sourcePrefab] = stack;
        }

        stack.Push(released);
    }

    private void PlaceDecal(Transform decal, RaycastHit hit, float scale, bool attach)
    {
        Vector3 normal = hit.normal;

        // FromToRotation thay cho LookRotation: không suy biến khi bắn thẳng vào sàn/trần
        // (pháp tuyến song song Vector3.up). Prefab đã lật quad 180 độ nên forward = pháp tuyến.
        Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, normal);

        if (randomRoll)
            rotation = Quaternion.AngleAxis(Random.Range(0f, 360f), normal) * rotation;

        Transform parent = attach && IsUniformEnough(hit.collider.transform)
            ? hit.collider.transform
            : decalRoot;

        decal.SetParent(parent, false);
        decal.SetPositionAndRotation(hit.point + normal * surfaceOffset, rotation);
        decal.localScale = CompensateScale(parent, scale);
    }

    /// <summary>Scale của vật có đủ đều để parent decal vào mà không bị méo (shear) hay không.</summary>
    private bool IsUniformEnough(Transform target)
    {
        Vector3 lossy = target.lossyScale;
        float x = Mathf.Abs(lossy.x);
        float y = Mathf.Abs(lossy.y);
        float z = Mathf.Abs(lossy.z);

        float min = Mathf.Min(x, Mathf.Min(y, z));
        float max = Mathf.Max(x, Mathf.Max(y, z));

        if (min <= 0.0001f)
            return false;

        return max / min <= Mathf.Max(1f, maxParentScaleRatio);
    }

    /// <summary>Bù lại scale của parent để lỗ đạn giữ đúng kích thước trong world.</summary>
    private Vector3 CompensateScale(Transform parent, float scale)
    {
        if (parent == decalRoot)
            return Vector3.one * scale;

        Vector3 lossy = parent.lossyScale;
        return new Vector3(
            scale / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
            scale / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
            scale / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));
    }

    private void TryPlayImpactSound(Vector3 point, ImpactSurface surface, ImpactSurfaceEntry entry)
    {
        AudioClip clip = surface != null ? surface.PickOverrideClip() : null;

        if (clip == null)
            clip = entry.PickClip();

        if (clip == null || audioSources == null || audioSources.Length == 0)
            return;

        AudioSource source = audioSources[nextAudioSource];
        nextAudioSource = (nextAudioSource + 1) % audioSources.Length;

        // volume <= 0 cũng là ca "element mới trong List": coi như chưa set.
        // Muốn tắt tiếng cho bề mặt nào thì để trống Impact Clips của bề mặt đó.
        float entryVolume = entry.volume > 0f ? entry.volume : 1f;

        source.transform.position = point;
        source.pitch = Random.Range(impactPitchRange.x, impactPitchRange.y);
        source.PlayOneShot(clip, impactVolume * Mathf.Clamp01(entryVolume));
    }

    /// <summary>
    /// Dựng sẵn vài AudioSource quay vòng. Không dùng AudioSource.PlayClipAtPoint vì hàm đó
    /// tạo và huỷ 1 GameObject mỗi phát bắn.
    /// </summary>
    private void BuildAudioSources()
    {
        int count = Mathf.Max(1, audioSourceCount);
        audioSources = new AudioSource[count];

        for (int i = 0; i < count; i++)
        {
            GameObject holder = new GameObject("ImpactAudio " + i);
            holder.transform.SetParent(transform, false);

            AudioSource source = holder.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = audioMinDistance;
            source.maxDistance = audioMaxDistance;
            source.dopplerLevel = 0f;
            // Thấp hơn tiếng súng (mặc định 128) để tiếng va chạm bị drop trước khi hết voice.
            source.priority = 160;

            audioSources[i] = source;
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Unity tạo element mới trong List với mọi field = 0 (bỏ qua giá trị khởi tạo của C#),
    /// nên tự chỉnh về 1 để Inspector hiện đúng ngay sau khi thêm dòng mới.
    /// </summary>
    private void OnValidate()
    {
        NormalizeEntry(defaultSurface);

        for (int i = 0; i < surfaces.Count; i++)
            NormalizeEntry(surfaces[i]);
    }

    private static void NormalizeEntry(ImpactSurfaceEntry entry)
    {
        if (entry == null)
            return;

        if (entry.decalScale <= 0f)
            entry.decalScale = 1f;

        if (entry.volume <= 0f)
            entry.volume = 1f;
    }
#endif

    private void Prewarm()
    {
        if (prewarmPerSurface <= 0)
            return;

        PrewarmEntry(defaultSurface);

        for (int i = 0; i < surfaces.Count; i++)
            PrewarmEntry(surfaces[i]);
    }

    private void PrewarmEntry(ImpactSurfaceEntry entry)
    {
        GameObject prefab = entry != null ? entry.decalPrefab : null;
        if (prefab == null)
            return;

        if (!idlePool.TryGetValue(prefab, out Stack<GameObject> stack))
        {
            stack = new Stack<GameObject>();
            idlePool[prefab] = stack;
        }
        else if (stack.Count > 0)
        {
            // Prefab này đã được prewarm bởi một entry khác.
            return;
        }

        for (int i = 0; i < prewarmPerSurface; i++)
        {
            GameObject created = Instantiate(prefab, decalRoot);
            created.SetActive(false);
            stack.Push(created);
        }
    }
}
