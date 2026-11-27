#if UNITY_EDITOR || VRSWAT_SIM_TEST
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRSwat.Doors;
using VRSwat.Grenades;
using Object = UnityEngine.Object;

/// <summary>
/// Công cụ test trên XR Device Simulator: phím tắt đưa đồ vào tay, ném lựu đạn, nạp đạn, còng.
///
/// CHỈ có trong Editor (hoặc khi thêm define VRSWAT_SIM_TEST): cả file nằm trong
/// <c>#if UNITY_EDITOR || VRSWAT_SIM_TEST</c>, nên bản build thật không có dòng nào của nó.
/// Không cần đặt vào scene: vào Play là tự tạo. Muốn chỉnh phím / prefab thì Add Component vào một
/// GameObject trong scene (bản tự tạo sẽ không sinh ra nữa); muốn tắt hẳn thì để component đó disabled.
/// Khi trong scene không có XR Device Simulator đang chạy (đeo kính thật) thì helper đứng yên.
///
/// Mọi thao tác cầm / thả đi qua XRI thật (StartManualInteraction / SelectExit trên interactor của
/// tay), nên các sự kiện game vẫn chạy y như khi chơi bằng kính: Sig.GrabWeapon, cò (activated),
/// socket túi đồ, HandleReleased của lựu đạn...
///
/// Không dùng phím Shift: Shift trái là "điều khiển tay trái" của simulator.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("VR SWAT/Dev/Simulator Test Helper")]
public class SimulatorTestHelper : MonoBehaviour
{
    public enum ItemKind
    {
        Pistol,
        Rifle,
        Shotgun,
        FragGrenade,
        Flashbang,
        Lockpick,
        BreachCharge,
        Detonator
    }

    public enum Hand
    {
        Left,
        Right
    }

    [Serializable]
    public class ItemBinding
    {
        public ItemKind kind;
        public Key key;
        [Tooltip("Prefab spawn ra khi trong scene không có sẵn vật này. Trống = tự lấy prefab mặc định trong Assets/Prefabs (chỉ Editor).")]
        public GameObject prefab;

        public ItemBinding(ItemKind kind, Key key)
        {
            this.kind = kind;
            this.key = key;
        }
    }

    [Header("Bật / tắt")]
    [Tooltip("Chỉ hoạt động khi trong scene có XR Device Simulator đang chạy. Tắt = luôn hoạt động.")]
    [SerializeField] private bool onlyWithDeviceSimulator = true;
    [SerializeField] private bool showHud = true;

    [Header("Đồ vào tay")]
    [SerializeField] private List<ItemBinding> items = new List<ItemBinding>
    {
        new ItemBinding(ItemKind.Pistol, Key.F1),
        new ItemBinding(ItemKind.Rifle, Key.F2),
        new ItemBinding(ItemKind.Shotgun, Key.F3),
        new ItemBinding(ItemKind.FragGrenade, Key.F4),
        new ItemBinding(ItemKind.Flashbang, Key.F5),
        new ItemBinding(ItemKind.Lockpick, Key.F6),
        new ItemBinding(ItemKind.BreachCharge, Key.F7),
        new ItemBinding(ItemKind.Detonator, Key.Insert),
    };

    [Tooltip("Bật: lấy vật có sẵn trong scene gần người chơi nhất trước (vd. 3 khẩu súng phòng 2), không có mới spawn. " +
             "Tắt: luôn spawn cái mới.")]
    [SerializeField] private bool preferSceneItems = true;
    [Tooltip("Chỉ lấy vật trong scene cách đầu người chơi tối đa bao nhiêu mét.")]
    [SerializeField] private float sceneSearchRadius = 20f;
    [Tooltip("Tầm với của phím cầm vật đang nhìn vào (m).")]
    [SerializeField] private float grabLookRange = 5f;

    [Header("Phím")]
    [SerializeField] private Key grabLookKey = Key.Numpad0;
    [SerializeField] private Key dropKey = Key.Backspace;
    [SerializeField] private Key throwKey = Key.F8;
    [SerializeField] private Key pullPinKey = Key.End;
    [SerializeField] private Key toggleHandKey = Key.F9;
    [SerializeField] private Key reloadKey = Key.F10;
    [SerializeField] private Key cycleKey = Key.F11;
    [SerializeField] private Key infiniteAmmoKey = Key.Home;
    [SerializeField] private Key hudKey = Key.F12;
    [SerializeField] private Key cuffKey = Key.NumpadEnter;
    [SerializeField] private Key sourceModeKey = Key.NumpadPeriod;
    [Tooltip("Mở / đóng cửa gần nhất. Cửa đang khóa thì lắc tay nắm (như người chơi kéo cửa khóa).")]
    [SerializeField] private Key doorKey = Key.PageUp;
    [Tooltip("Mở khóa / khóa lại cửa gần nhất (bỏ qua lockpick, chỉ để test).")]
    [SerializeField] private Key doorLockKey = Key.PageDown;

    [Header("Cửa")]
    [Tooltip("Chỉ tác động cửa có ổ khóa cách đầu người chơi tối đa bao nhiêu mét.")]
    [SerializeField] private float doorRange = 3f;
    [SerializeField] private float doorOpenAngle = 90f;
    [SerializeField] private float doorMoveTime = 0.6f;

    [Header("Chuột trái (dùng đồ đang cầm)")]
    [Tooltip("Chuột trái: cầm Lockpick thì giữ để phá khóa, cầm bom thì bấm để dán lên cửa gần nhất, " +
             "cầm kíp nổ thì bấm để nổ. Súng không bị ảnh hưởng (cò súng vẫn do simulator lo).")]
    [SerializeField] private bool leftMouseUsesItem = true;
    [Tooltip("Nới tầm với của Lockpick lúc đang cầm (m), để không phải đưa đầu kit sát ổ khóa bằng simulator. 0 = giữ nguyên.")]
    [SerializeField] private float lockpickAssistRange = 3f;

    [Header("Ném")]
    [Tooltip("Bấm nhả nhanh = tốc độ này (m/s).")]
    [SerializeField] private float minThrowSpeed = 6f;
    [Tooltip("Giữ đủ Full Charge Time = tốc độ này (m/s).")]
    [SerializeField] private float maxThrowSpeed = 18f;
    [SerializeField] private float fullChargeTime = 1f;
    [Tooltip("Ném chếch lên trên so với hướng nhìn (độ).")]
    [SerializeField] private float throwUpAngle = 10f;

    [Header("Súng")]
    [SerializeField] private GameObject pistolMagazinePrefab;
    [SerializeField] private GameObject rifleMagazinePrefab;
    [Tooltip("Băng / ống đạn của súng đang cầm luôn được nạp đầy lại.")]
    [SerializeField] private bool infiniteAmmo;

    private static readonly FieldInfo ManualFlagField =
        typeof(XRBaseInteractor).GetField("m_IsPerformingManualInteraction", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo ManualTargetField =
        typeof(XRBaseInteractor).GetField("m_ManualInteractionInteractable", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo BuiltInMagazineField =
        typeof(GunAmmoManager).GetField("builtInMagazine", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo MagazineSocketField =
        typeof(GunAmmoManager).GetField("magazineSocket", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo HandleLockedOpenField =
        typeof(ChargingHandle).GetField("lockedOpen", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo LockpickRangeField =
        typeof(LockpickKit).GetField("range", BindingFlags.Instance | BindingFlags.NonPublic);

    private XRGrabInteractable activatedItem;
    private XRBaseInputInteractor activatedBy;

    private readonly XRBaseInputInteractor[] handInteractors = new XRBaseInputInteractor[2];
    private Hand chosenHand = Hand.Right;
    private float throwChargeStart = -1f;
    private string message;
    private float messageUntil;
    private GUIStyle hudStyle;

    private bool IsActive
    {
        get
        {
            if (!onlyWithDeviceSimulator)
                return true;

            XRDeviceSimulator sim = XRDeviceSimulator.instance;
            return sim != null && sim.isActiveAndEnabled;
        }
    }

    // ---------- Tự tạo ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindAnyObjectByType<SimulatorTestHelper>(FindObjectsInactive.Include) != null)
            return;

        var go = new GameObject("SimulatorTestHelper (auto)");
        DontDestroyOnLoad(go);
        go.AddComponent<SimulatorTestHelper>();
    }

    private void Awake()
    {
        LoadDefaultPrefabs();
    }

    private void OnEnable()
    {
        PlayerCuffInput.SimulatedButtonHeld = IsCuffButtonHeld;
    }

    private void OnDisable()
    {
        if (PlayerCuffInput.SimulatedButtonHeld == (Func<int, bool>)IsCuffButtonHeld)
            PlayerCuffInput.SimulatedButtonHeld = null;
    }

    private void Reset()
    {
        LoadDefaultPrefabs();
    }

    private void LoadDefaultPrefabs()
    {
#if UNITY_EDITOR
        foreach (ItemBinding b in items)
        {
            if (b.prefab == null)
                b.prefab = LoadPrefab(DefaultPrefabPath(b.kind));
        }

        if (pistolMagazinePrefab == null)
            pistolMagazinePrefab = LoadPrefab("Assets/Prefabs/MagPistol.prefab");

        if (rifleMagazinePrefab == null)
            rifleMagazinePrefab = LoadPrefab("Assets/Prefabs/RifleMagazine.prefab");
#endif
    }

#if UNITY_EDITOR
    private static GameObject LoadPrefab(string path)
    {
        return string.IsNullOrEmpty(path) ? null : UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private static string DefaultPrefabPath(ItemKind kind) => kind switch
    {
        ItemKind.Pistol => "Assets/Prefabs/Pistol.prefab",
        ItemKind.Rifle => "Assets/Prefabs/Rifle h .prefab",
        ItemKind.Shotgun => "Assets/Prefabs/Shotgun.prefab",
        ItemKind.FragGrenade => "Assets/Prefabs/VRGrenade.prefab",
        ItemKind.Flashbang => "Assets/Prefabs/FlashGrenade.prefab",
        ItemKind.Lockpick => "Assets/Prefabs/Lockpick.prefab",
        ItemKind.BreachCharge => "Assets/Prefabs/explosive.prefab",
        ItemKind.Detonator => "Assets/Prefabs/Detonator.prefab",
        _ => null
    };
#endif

    // ---------- Vòng lặp ----------

    private void Update()
    {
        CleanUpManualInteractions();

        if (!IsActive)
            return;

        Keyboard kb = Keyboard.current;
        if (kb == null)
            return;

        UpdateChosenHand();

        if (Pressed(kb, toggleHandKey))
        {
            chosenHand = chosenHand == Hand.Left ? Hand.Right : Hand.Left;
            Say("Tay: " + HandName(chosenHand));
        }

        if (Pressed(kb, hudKey))
            showHud = !showHud;

        if (Pressed(kb, sourceModeKey))
        {
            preferSceneItems = !preferSceneItems;
            Say(preferSceneItems ? "Lấy đồ có sẵn trong scene trước" : "Luôn spawn đồ mới");
        }

        if (Pressed(kb, infiniteAmmoKey))
        {
            infiniteAmmo = !infiniteAmmo;
            Say(infiniteAmmo ? "Đạn vô hạn: BẬT" : "Đạn vô hạn: TẮT");
        }

        foreach (ItemBinding b in items)
        {
            if (Pressed(kb, b.key))
                Equip(b);
        }

        if (Pressed(kb, grabLookKey))
            GrabLookedAt();

        if (Pressed(kb, dropKey))
            Drop();

        if (Pressed(kb, pullPinKey))
            PullPinOnly();

        UpdateThrow(kb);

        if (Pressed(kb, reloadKey))
            Reload();

        if (Pressed(kb, cycleKey))
            CycleAction();

        if (Pressed(kb, doorKey))
            ToggleNearestDoor();

        if (Pressed(kb, doorLockKey))
            ToggleNearestDoorLock();

        UpdateLeftMouse();
        AssistLockpickRange();

        if (infiniteAmmo)
            KeepAmmoFull();
    }

    private static bool Pressed(Keyboard kb, Key key)
    {
        return key != Key.None && kb[key].wasPressedThisFrame;
    }

    // ---------- Tay ----------

    /// <summary>Simulator đang điều khiển đúng một tay (T / Y / Shift / Space) thì theo tay đó.</summary>
    private void UpdateChosenHand()
    {
        XRDeviceSimulator sim = XRDeviceSimulator.instance;
        if (sim == null)
            return;

        bool left = sim.manipulatingLeftDevice;
        bool right = sim.manipulatingRightDevice;

        if (left && !right)
            chosenHand = Hand.Left;
        else if (right && !left)
            chosenHand = Hand.Right;
    }

    private XRBaseInputInteractor GetInteractor(Hand hand)
    {
        int i = (int)hand;
        XRBaseInputInteractor cached = handInteractors[i];

        if (cached != null && cached.isActiveAndEnabled)
            return cached;

        InteractorHandedness wanted = hand == Hand.Left ? InteractorHandedness.Left : InteractorHandedness.Right;
        XRBaseInputInteractor best = null;
        int bestScore = -1;

        foreach (XRBaseInputInteractor it in FindObjectsByType<XRBaseInputInteractor>(FindObjectsSortMode.None))
        {
            if (!it.isActiveAndEnabled || it.handedness != wanted || it is XRSocketInteractor)
                continue;

            int score = it is NearFarInteractor ? 3 : it is XRDirectInteractor ? 2 : it is XRRayInteractor ? 1 : 0;
            if (score > bestScore)
            {
                best = it;
                bestScore = score;
            }
        }

        handInteractors[i] = best;
        return best;
    }

    private XRGrabInteractable GetHeld(Hand hand)
    {
        XRBaseInputInteractor it = GetInteractor(hand);
        if (it == null || !it.hasSelection)
            return null;

        return it.firstInteractableSelected as XRGrabInteractable;
    }

    /// <summary>
    /// Manual interaction của XRI giữ cờ "đang bóp grip" tới khi gọi EndManualInteraction. Nếu vật bị lấy
    /// mất kiểu khác (nổ, bị tay kia / socket giật), cờ vẫn còn và tay sẽ tự chộp vật kế tiếp nó chạm vào.
    /// Không gọi EndManualInteraction lúc đó được vì nó bắn selectExited giả, nên xoá cờ trực tiếp.
    /// </summary>
    private void CleanUpManualInteractions()
    {
        foreach (XRBaseInputInteractor it in handInteractors)
        {
            if (it == null || !it.isPerformingManualInteraction)
                continue;

            var target = ManualTargetField?.GetValue(it) as IXRSelectInteractable;
            bool lost = target == null || (target as Object) == null || !it.IsSelecting(target);

            if (lost)
            {
                ManualFlagField?.SetValue(it, false);
                ManualTargetField?.SetValue(it, null);
            }
        }
    }

    private IXRSelectInteractable Release(Hand hand)
    {
        XRBaseInputInteractor it = GetInteractor(hand);
        if (it == null || !it.hasSelection)
            return null;

        IXRSelectInteractable target = it.firstInteractableSelected;

        if (it.isPerformingManualInteraction && it.IsSelecting(ManualTargetField?.GetValue(it) as IXRSelectInteractable))
            it.EndManualInteraction();
        else
            it.interactionManager.SelectExit(it, target);

        return target;
    }

    // ---------- Đưa đồ vào tay ----------

    private void Equip(ItemBinding binding)
    {
        Hand hand = chosenHand;
        XRBaseInputInteractor it = GetInteractor(hand);
        if (it == null)
        {
            Say("Không tìm thấy interactor tay " + HandName(hand));
            return;
        }

        XRGrabInteractable grab = preferSceneItems ? FindSceneItem(binding.kind) : null;

        if (grab != null)
        {
            EquipToHand(grab, hand);
            Say(binding.kind + " (có sẵn trong scene) -> tay " + HandName(hand));
            return;
        }

        if (binding.prefab == null)
        {
            Say("Chưa gán prefab cho " + binding.kind);
            return;
        }

        Transform at = it.transform;
        GameObject go = Instantiate(binding.prefab, at.position + at.forward * 0.1f, at.rotation);
        grab = go.GetComponent<XRGrabInteractable>();
        if (grab == null)
            grab = go.GetComponentInChildren<XRGrabInteractable>();

        if (grab == null)
        {
            Say(binding.prefab.name + " không có XRGrabInteractable");
            return;
        }

        // Đợi 1 frame để vật mới đăng ký xong với XRInteractionManager và chạy Awake/Start.
        StartCoroutine(EquipNextFrame(grab, hand));
        Say(binding.kind + " (spawn mới) -> tay " + HandName(hand));
    }

    private IEnumerator EquipNextFrame(XRGrabInteractable grab, Hand hand)
    {
        yield return null;

        if (grab != null)
            EquipToHand(grab, hand);
    }

    private void EquipToHand(XRGrabInteractable grab, Hand hand)
    {
        XRBaseInputInteractor it = GetInteractor(hand);
        if (it == null || grab == null)
            return;

        if (it.IsSelecting(grab))
            return;

        Release(hand);

        // Đang nằm ở tay kia thì thả tay kia ra trước (socket túi đồ thì XRI tự nhả).
        Hand other = hand == Hand.Left ? Hand.Right : Hand.Left;
        XRBaseInputInteractor otherIt = GetInteractor(other);
        if (otherIt != null && otherIt.IsSelecting(grab))
            Release(other);

        // Dịch vật về sát tay trước, để velocity tracking không kéo nó xuyên tường bay về.
        grab.transform.SetPositionAndRotation(it.transform.position, it.transform.rotation);

        it.StartManualInteraction((IXRSelectInteractable)grab);
    }

    private XRGrabInteractable FindSceneItem(ItemKind kind)
    {
        Vector3 head = HeadPosition();
        XRGrabInteractable best = null;
        float bestDist = sceneSearchRadius;

        foreach (Component c in Candidates(kind))
        {
            if (c == null)
                continue;

            XRGrabInteractable grab = c.GetComponent<XRGrabInteractable>();
            if (grab == null || !grab.isActiveAndEnabled || IsHeldByHand(grab))
                continue;

            float d = Vector3.Distance(head, grab.transform.position);
            if (d <= bestDist)
            {
                best = grab;
                bestDist = d;
            }
        }

        return best;
    }

    private static IEnumerable<Component> Candidates(ItemKind kind)
    {
        switch (kind)
        {
            case ItemKind.Pistol:
            case ItemKind.Rifle:
            case ItemKind.Shotgun:
                WeaponType wanted = kind == ItemKind.Pistol ? WeaponType.Pistol
                    : kind == ItemKind.Rifle ? WeaponType.Rifle : WeaponType.Shotgun;
                foreach (Gun g in FindObjectsByType<Gun>(FindObjectsSortMode.None))
                    if (g.Type == wanted)
                        yield return g;
                break;

            case ItemKind.FragGrenade:
            case ItemKind.Flashbang:
                bool wantFlash = kind == ItemKind.Flashbang;
                foreach (VRGrenade g in FindObjectsByType<VRGrenade>(FindObjectsSortMode.None))
                    if (!g.PinPulled && (g.GetComponent<Flashbang>() != null) == wantFlash)
                        yield return g;
                break;

            case ItemKind.Lockpick:
                foreach (LockpickKit k in FindObjectsByType<LockpickKit>(FindObjectsSortMode.None))
                    yield return k;
                break;

            case ItemKind.BreachCharge:
                foreach (BreachCharge b in FindObjectsByType<BreachCharge>(FindObjectsSortMode.None))
                    if (!b.IsPlaced)
                        yield return b;
                break;

            case ItemKind.Detonator:
                foreach (Detonator d in FindObjectsByType<Detonator>(FindObjectsSortMode.None))
                    yield return d;
                break;
        }
    }

    private static bool IsHeldByHand(XRGrabInteractable grab)
    {
        foreach (IXRSelectInteractor i in grab.interactorsSelecting)
            if (!(i is XRSocketInteractor))
                return true;

        return false;
    }

    private void GrabLookedAt()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        RaycastHit[] hits = Physics.RaycastAll(cam.transform.position, cam.transform.forward, grabLookRange, ~0, QueryTriggerInteraction.Collide);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            XRGrabInteractable grab = hit.collider.GetComponentInParent<XRGrabInteractable>();
            if (grab == null)
                continue;

            // Trúng chốt lựu đạn thì cầm cả quả.
            if (grab.GetComponent<GrenadePin>() != null && grab.transform.parent != null)
            {
                XRGrabInteractable parent = grab.transform.parent.GetComponentInParent<XRGrabInteractable>();
                if (parent != null)
                    grab = parent;
            }

            if (IsHeldByHand(grab))
                continue;

            EquipToHand(grab, chosenHand);
            Say(grab.name + " -> tay " + HandName(chosenHand));
            return;
        }

        Say("Không nhìn thấy vật cầm được trong " + grabLookRange + " m");
    }

    private void Drop()
    {
        IXRSelectInteractable dropped = Release(chosenHand);
        Say(dropped != null ? "Thả " + dropped.transform.name : "Tay " + HandName(chosenHand) + " không cầm gì");
    }

    // ---------- Lựu đạn / ném ----------

    private void PullPinOnly()
    {
        VRGrenade grenade = GetHeld(chosenHand)?.GetComponent<VRGrenade>();
        if (grenade == null)
        {
            Say("Tay " + HandName(chosenHand) + " không cầm lựu đạn");
            return;
        }

        PullPin(grenade);
        Say("Đã rút chốt, thả tay (" + dropKey + ") hoặc ném (" + throwKey + ") là cháy ngòi");
    }

    private static void PullPin(VRGrenade grenade)
    {
        if (grenade.PinPulled)
            return;

        GrenadePin pin = grenade.GetComponentInChildren<GrenadePin>();
        if (pin != null)
            pin.ForcePull();
        else
            grenade.OnPinPulled();
    }

    private void UpdateThrow(Keyboard kb)
    {
        if (throwKey == Key.None)
            return;

        if (kb[throwKey].wasPressedThisFrame)
        {
            if (GetHeld(chosenHand) == null)
                Say("Tay " + HandName(chosenHand) + " không cầm gì để ném");
            else
                throwChargeStart = Time.unscaledTime;
        }

        if (throwChargeStart >= 0f && kb[throwKey].wasReleasedThisFrame)
        {
            float t = Mathf.Clamp01(ThrowCharge());
            throwChargeStart = -1f;
            ThrowHeld(chosenHand, Mathf.Lerp(minThrowSpeed, maxThrowSpeed, t));
        }
    }

    private float ThrowCharge()
    {
        return throwChargeStart < 0f ? 0f : (Time.unscaledTime - throwChargeStart) / Mathf.Max(0.01f, fullChargeTime);
    }

    private void ThrowHeld(Hand hand, float speed)
    {
        XRGrabInteractable grab = GetHeld(hand);
        if (grab == null)
            return;

        VRGrenade grenade = grab.GetComponent<VRGrenade>();
        if (grenade != null)
            PullPin(grenade);

        Camera cam = Camera.main;
        Transform aim = cam != null ? cam.transform : GetInteractor(hand).transform;
        Vector3 dir = Quaternion.AngleAxis(-throwUpAngle, aim.right) * aim.forward;

        // Tắt throw của XRI cho lần thả này: vận tốc tay trong simulator gần như bằng 0 và sẽ ghi đè lên.
        bool throwOnDetach = grab.throwOnDetach;
        grab.throwOnDetach = false;

        // Thả bằng đường XRI thật: VRGrenade.HandleReleased tự cháy ngòi vì chốt đã rút.
        Release(hand);
        StartCoroutine(ApplyThrow(grab, dir * speed, throwOnDetach));

        Say("Ném " + grab.name + " " + speed.ToString("0") + " m/s");
    }

    private static IEnumerator ApplyThrow(XRGrabInteractable grab, Vector3 velocity, bool restoreThrowOnDetach)
    {
        // XRI tách vật khỏi tay ở pha Late của frame thả; đợi qua đó rồi mới đặt vận tốc.
        yield return null;

        if (grab == null)
            yield break;

        grab.throwOnDetach = restoreThrowOnDetach;

        Rigidbody body = grab.GetComponent<Rigidbody>();
        if (body == null || body.isKinematic)
            yield break;

        body.linearVelocity = velocity;
        body.angularVelocity = UnityEngine.Random.insideUnitSphere * 5f;
    }

    // ---------- Súng ----------

    private bool TryGetHeldGun(out Gun gun, out GunAmmoManager ammo)
    {
        XRGrabInteractable grab = GetHeld(chosenHand);
        gun = grab != null ? grab.GetComponent<Gun>() : null;
        ammo = gun != null ? gun.GetComponent<GunAmmoManager>() : null;

        if (gun == null || ammo == null)
        {
            Say("Tay " + HandName(chosenHand) + " không cầm súng");
            return false;
        }

        return true;
    }

    private static Magazine CurrentMagazine(GunAmmoManager ammo, out XRSocketInteractor socket)
    {
        socket = null;

        if (BuiltInMagazineField?.GetValue(ammo) is Magazine builtIn && builtIn != null)
            return builtIn;

        socket = MagazineSocketField?.GetValue(ammo) as XRSocketInteractor;
        if (socket == null)
            socket = ammo.GetComponentInChildren<XRSocketInteractor>();

        if (socket != null && socket.hasSelection)
            return socket.firstInteractableSelected.transform.GetComponent<Magazine>();

        return null;
    }

    private void Reload()
    {
        if (!TryGetHeldGun(out Gun gun, out GunAmmoManager ammo))
            return;

        Magazine mag = CurrentMagazine(ammo, out XRSocketInteractor socket);

        if (mag != null)
        {
            mag.Refill();
            ChamberIfNeeded(gun, ammo);
            Say("Nạp đầy " + gun.name + ": " + ammo.TotalAmmo + " viên");
            return;
        }

        if (socket == null)
        {
            Say(gun.name + " không có khe băng đạn");
            return;
        }

        GameObject prefab = gun.Type == WeaponType.Pistol ? pistolMagazinePrefab : rifleMagazinePrefab;
        if (prefab == null)
        {
            Say("Chưa gán prefab băng đạn cho " + gun.Type);
            return;
        }

        Transform at = socket.attachTransform != null ? socket.attachTransform : socket.transform;
        GameObject go = Instantiate(prefab, at.position, at.rotation);
        StartCoroutine(InsertMagazineNextFrame(go, socket, gun, ammo));
        Say("Gắn băng mới vào " + gun.name);
    }

    private IEnumerator InsertMagazineNextFrame(GameObject magObject, XRSocketInteractor socket, Gun gun, GunAmmoManager ammo)
    {
        yield return null;

        if (magObject == null || socket == null)
            yield break;

        var grab = magObject.GetComponent<XRBaseInteractable>();
        if (grab == null || socket.interactionManager == null)
            yield break;

        socket.interactionManager.SelectEnter((IXRSelectInteractor)socket, (IXRSelectInteractable)grab);

        Magazine mag = magObject.GetComponent<Magazine>();
        if (mag != null)
            mag.Refill();

        ChamberIfNeeded(gun, ammo);
    }

    private static void ChamberIfNeeded(Gun gun, GunAmmoManager ammo)
    {
        if (!ammo.IsChambered)
        {
            if (ammo.HasSpentCase)
                ammo.ExtractChamber();

            ammo.Chamber();
        }

        UnlockSlide(gun);
        ammo.OnAmmoChanged?.Invoke();
    }

    /// <summary>Hết đạn thì tay cò / khóa nòng bị khóa mở; nạp xong thì nhả cho nó trượt về.</summary>
    private static void UnlockSlide(Gun gun)
    {
        ChargingHandle handle = gun.GetComponentInChildren<ChargingHandle>(true);
        if (handle != null && handle.IsLockedOpen)
            HandleLockedOpenField?.SetValue(handle, false);
    }

    private void CycleAction()
    {
        if (!TryGetHeldGun(out Gun gun, out GunAmmoManager ammo))
            return;

        UnlockSlide(gun);
        ammo.CycleAction();

        ChargingHandle handle = gun.GetComponentInChildren<ChargingHandle>(true);
        if (handle != null)
            handle.PlayCycle();

        Say(ammo.IsChambered ? "Đã lên nòng" : "Kéo pump nhưng hết đạn");
    }

    private void KeepAmmoFull()
    {
        for (int h = 0; h < 2; h++)
        {
            XRGrabInteractable grab = GetHeld((Hand)h);
            GunAmmoManager ammo = grab != null ? grab.GetComponent<GunAmmoManager>() : null;
            if (ammo == null)
                continue;

            Magazine mag = CurrentMagazine(ammo, out _);
            if (mag != null && !mag.IsFull)
            {
                mag.Refill();
                ammo.OnAmmoChanged?.Invoke();
            }
        }
    }

    // ---------- Cửa ----------

    private DoorController FindNearestDoor()
    {
        Vector3 head = HeadPosition();
        DoorController best = null;
        float bestDist = doorRange;

        foreach (DoorController door in DoorController.All)
        {
            if (door == null || !door.isActiveAndEnabled)
                continue;

            float d = Vector3.Distance(head, door.LockPoint.position);
            if (d <= bestDist)
            {
                best = door;
                bestDist = d;
            }
        }

        if (best == null)
            Say("Không có cửa nào trong " + doorRange + " m");

        return best;
    }

    private void ToggleNearestDoor()
    {
        DoorController door = FindNearestDoor();
        if (door == null)
            return;

        switch (door.State)
        {
            case DoorState.Breached:
                Say(door.name + " đã bị phá");
                return;

            case DoorState.Locked:
                // Đi đúng đường tay nắm: kéo cửa khóa thì cửa bắn LockedRattle (tutorial phòng 5, 6 chờ cái này).
                if (door.BeginGrab(this))
                {
                    door.ProposeAngle(this, 10f);
                    door.EndGrab(this);
                }
                Say(door.name + " đang khóa (" + doorLockKey + " để mở khóa)");
                return;

            case DoorState.Open:
                StartCoroutine(MoveDoorRoutine(door, 0f, false));
                Say("Đóng " + door.name);
                return;

            default:
                // Mở ra phía xa người chơi.
                bool fromFront = door.transform.InverseTransformPoint(HeadPosition()).z >= 0f;
                StartCoroutine(MoveDoorRoutine(door, fromFront ? doorOpenAngle : -doorOpenAngle, true));
                Say("Mở " + door.name);
                return;
        }
    }

    /// <summary>
    /// Kéo cửa như tay nắm của người chơi (BeginGrab / ProposeAngle / EndGrab), nên cửa tự đổi
    /// trạng thái Closed / Open và báo StateChanged như thật.
    /// </summary>
    private IEnumerator MoveDoorRoutine(DoorController door, float target, bool tryOtherSideIfBlocked)
    {
        for (int pass = 0; pass < (tryOtherSideIfBlocked ? 2 : 1); pass++)
        {
            if (door == null || !door.BeginGrab(this))
            {
                Say("Cửa đang bận (có người / AI đang giữ)");
                yield break;
            }

            float start = door.Angle;
            float time = 0f;
            float duration = Mathf.Max(0.05f, doorMoveTime);

            while (time < duration && door != null)
            {
                time += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, time / duration);
                if (!door.ProposeAngle(this, Mathf.Lerp(start, target, t)))
                    break;
                yield return null;
            }

            if (door == null)
                yield break;

            door.EndGrab(this);

            // Phía đó bị tường chặn (giới hạn góc = 0): thử mở về phía kia.
            if (Mathf.Abs(door.Angle) > 5f || !tryOtherSideIfBlocked)
                yield break;

            target = -target;
        }
    }

    private void ToggleNearestDoorLock()
    {
        DoorController door = FindNearestDoor();
        if (door == null)
            return;

        if (door.State == DoorState.Locked)
        {
            door.Unlock();
            Say("Đã mở khóa " + door.name);
        }
        else if (door.State == DoorState.Closed)
        {
            door.Lock();
            Say("Đã khóa " + door.name);
        }
        else
        {
            Say(door.name + " đang " + door.State + ", chỉ khóa được khi cửa đóng");
        }
    }

    // ---------- Chuột trái: dùng đồ đang cầm ----------

    private void UpdateLeftMouse()
    {
        // Đồ đang "bóp cò" giả bị thả / đổi tay / nổ mất thì nhả cò.
        if (activatedItem != null && (activatedBy == null || !activatedBy.IsSelecting(activatedItem)))
            SendDeactivate();

        Mouse mouse = Mouse.current;
        if (!leftMouseUsesItem || mouse == null)
            return;

        if (mouse.leftButton.wasReleasedThisFrame)
            SendDeactivate();

        if (!mouse.leftButton.wasPressedThisFrame)
            return;

        XRGrabInteractable held = GetHeld(chosenHand);
        if (held == null)
            return;

        BreachCharge charge = held.GetComponent<BreachCharge>();
        if (charge != null)
        {
            PlaceChargeOnNearestDoor(held);
            return;
        }

        if (held.GetComponent<LockpickKit>() == null && held.GetComponent<Detonator>() == null)
            return;

        // Simulator đang điều khiển tay này thì chuột trái đã là cò thật rồi, không bóp thêm lần nữa.
        if (SimulatorDrivesHand(chosenHand))
            return;

        SendActivate(GetInteractor(chosenHand), held);
    }

    private static bool SimulatorDrivesHand(Hand hand)
    {
        XRDeviceSimulator sim = XRDeviceSimulator.instance;
        if (sim == null)
            return false;

        return hand == Hand.Left ? sim.manipulatingLeftDevice : sim.manipulatingRightDevice;
    }

    /// <summary>Bóp cò qua đúng đường XRI (IXRActivateInteractable.OnActivated), nên activated của vật chạy như thật.</summary>
    private void SendActivate(XRBaseInputInteractor it, XRGrabInteractable grab)
    {
        if (it == null || grab == null)
            return;

        SendDeactivate();

        var args = new ActivateEventArgs
        {
            interactorObject = it,
            interactableObject = grab
        };
        ((IXRActivateInteractable)grab).OnActivated(args);

        activatedItem = grab;
        activatedBy = it;
    }

    private void SendDeactivate()
    {
        if (activatedItem == null)
        {
            activatedBy = null;
            return;
        }

        XRGrabInteractable grab = activatedItem;
        XRBaseInputInteractor it = activatedBy;
        activatedItem = null;
        activatedBy = null;

        if (grab == null)
            return;

        var args = new DeactivateEventArgs
        {
            interactorObject = it,
            interactableObject = grab
        };
        ((IXRActivateInteractable)grab).OnDeactivated(args);
    }

    private void PlaceChargeOnNearestDoor(XRGrabInteractable chargeGrab)
    {
        Vector3 head = HeadPosition();
        ChargeMount best = null;
        float bestDist = doorRange;

        foreach (ChargeMount mount in FindObjectsByType<ChargeMount>(FindObjectsSortMode.None))
        {
            if (!mount.isActiveAndEnabled || mount.hasSelection || !mount.socketActive)
                continue;

            if (mount.Door != null && mount.Door.State == DoorState.Breached)
                continue;

            float d = Vector3.Distance(head, mount.transform.position);
            if (d <= bestDist)
            {
                best = mount;
                bestDist = d;
            }
        }

        if (best == null)
        {
            Say("Không có ổ dán bom (ChargeMount) trống nào trong " + doorRange + " m");
            return;
        }

        // Thả khỏi tay trước (BreachCharge đã nhớ LastHeldBy lúc cầm, DetonatorSpawner sẽ đưa kíp nổ vào tay này),
        // rồi cho ổ trên cửa select quả bom như khi người chơi thả lại gần.
        Release(chosenHand);
        best.interactionManager.SelectEnter((IXRSelectInteractor)best, (IXRSelectInteractable)chargeGrab);

        Say("Dán bom lên " + (best.Door != null ? best.Door.name : best.name));
    }

    private void AssistLockpickRange()
    {
        if (lockpickAssistRange <= 0f || LockpickRangeField == null)
            return;

        for (int h = 0; h < 2; h++)
        {
            XRGrabInteractable grab = GetHeld((Hand)h);
            LockpickKit kit = grab != null ? grab.GetComponent<LockpickKit>() : null;
            if (kit == null)
                continue;

            if (LockpickRangeField.GetValue(kit) is float range && range < lockpickAssistRange)
                LockpickRangeField.SetValue(kit, lockpickAssistRange);
        }
    }

    // ---------- Còng ----------

    private bool IsCuffButtonHeld(int hand)
    {
        if (!isActiveAndEnabled || !IsActive || cuffKey == Key.None)
            return false;

        Keyboard kb = Keyboard.current;
        return kb != null && kb[cuffKey].isPressed && hand == (int)chosenHand;
    }

    // ---------- HUD ----------

    private Vector3 HeadPosition()
    {
        Camera cam = Camera.main;
        return cam != null ? cam.transform.position : transform.position;
    }

    private static string HandName(Hand hand) => hand == Hand.Left ? "TRÁI" : "PHẢI";

    private void Say(string text)
    {
        message = text;
        messageUntil = Time.unscaledTime + 3f;
        Debug.Log("[SimTest] " + text);
    }

    private void OnGUI()
    {
        if (!showHud || !IsActive)
            return;

        if (hudStyle == null)
        {
            hudStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 13,
                richText = true,
                wordWrap = false,
                padding = new RectOffset(8, 8, 6, 6)
            };
            hudStyle.normal.textColor = Color.white;
        }

        var sb = new StringBuilder();
        sb.Append("<b>SIM TEST</b>   tay: <b>").Append(HandName(chosenHand)).Append("</b> (").Append(toggleHandKey).Append(")\n");

        XRGrabInteractable held = GetHeld(chosenHand);
        sb.Append("Đang cầm: ").Append(held != null ? held.name : "-").Append('\n');

        GunAmmoManager ammo = held != null ? held.GetComponent<GunAmmoManager>() : null;
        if (ammo != null)
            sb.Append("Đạn: ").Append(ammo.CurrentAmmo).Append('/').Append(ammo.Capacity)
              .Append(ammo.IsChambered ? " +1" : "").Append("   buồng: ").Append(ammo.ChamberStatus).Append('\n');

        VRGrenade grenade = held != null ? held.GetComponent<VRGrenade>() : null;
        if (grenade != null)
            sb.Append("Chốt: ").Append(grenade.PinPulled ? "<color=#ff6060>ĐÃ RÚT</color>" : "còn").Append('\n');

        if (throwChargeStart >= 0f)
        {
            int bars = Mathf.RoundToInt(Mathf.Clamp01(ThrowCharge()) * 20f);
            sb.Append("Lực ném: [").Append(new string('|', bars)).Append(new string('.', 20 - bars)).Append("]\n");
        }

        sb.Append("Nguồn đồ: ").Append(preferSceneItems ? "scene trước" : "spawn mới").Append(" (").Append(sourceModeKey).Append(")")
          .Append("   Đạn vô hạn: ").Append(infiniteAmmo ? "BẬT" : "tắt").Append(" (").Append(infiniteAmmoKey).Append(")\n");

        sb.Append("<color=#a0d0ff>");
        foreach (ItemBinding b in items)
            sb.Append(b.key).Append(' ').Append(b.kind).Append("  ");
        sb.Append('\n')
          .Append(grabLookKey).Append(" cầm vật đang nhìn  ").Append(dropKey).Append(" thả\n")
          .Append(throwKey).Append(" ném (giữ = xa hơn)  ").Append(pullPinKey).Append(" rút chốt\n")
          .Append(reloadKey).Append(" nạp đạn  ").Append(cycleKey).Append(" lên nòng / pump\n")
          .Append("Chuột trái: giữ = phá khóa, bấm = dán bom / kích nổ\n")
          .Append(doorKey).Append(" mở / đóng cửa  ").Append(doorLockKey).Append(" mở khóa / khóa cửa\n")
          .Append(cuffKey).Append(" (giữ) nút còng  ").Append(hudKey).Append(" ẩn HUD</color>");

        if (message != null && Time.unscaledTime < messageUntil)
            sb.Append("\n<color=#ffe080>").Append(message).Append("</color>");

        var content = new GUIContent(sb.ToString());
        Vector2 size = hudStyle.CalcSize(content);
        GUI.Box(new Rect(10f, 10f, size.x, size.y), content, hudStyle);
    }
}
#endif
