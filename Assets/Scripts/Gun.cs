using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class Gun : MonoBehaviour
{
    [Header("Gun Shot Effect")]
    [SerializeField] private GameObject gunShotEffect;
    [SerializeField] private float gunShotLiveTime = 0.1f;

    [Header("Shell")]
    [SerializeField] private Rigidbody usedShellTemplate;
    [SerializeField] private Transform muzzlePoint;
    [SerializeField] private float shellSpeedMin = 20f;
    [SerializeField] private float shellSpeedMax = 30f;

    [Header("Ammo")]
    [SerializeField] private GunAmmoManager ammoManager;
    [SerializeField] private AudioSource dryFireAudioSource;

    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabInteractable;

    private void Awake()
    {
        grabInteractable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();

        if (ammoManager == null)
            ammoManager = GetComponent<GunAmmoManager>();
    }

    void Start()
    {
        grabInteractable.activated.AddListener(Shoot);
    }

    private void Shoot(ActivateEventArgs args)
    {
        if (ammoManager != null && !ammoManager.TryFireRound())
        {
            PlayDryFireEffect();
            return;
        }

        PlayGunShotEffect();
        // EjectShell();
    }

    private void PlayDryFireEffect()
    {
        if (dryFireAudioSource != null)
            dryFireAudioSource.Play();
    }

    private void PlayGunShotEffect()
    {
        if (gunShotEffect == null)
            return;

        GameObject instance = Instantiate(gunShotEffect, gunShotEffect.transform.position, gunShotEffect.transform.rotation, gunShotEffect.transform.parent);
        instance.SetActive(true);
        Destroy(instance, gunShotLiveTime);
    }

    private void EjectShell()
    {
        if (usedShellTemplate == null)
            return;

        Transform origin = muzzlePoint != null ? muzzlePoint : transform;
        Rigidbody shellInstance = Instantiate(usedShellTemplate, origin.position, origin.rotation);

        shellInstance.gameObject.SetActive(true);
        shellInstance.transform.SetParent(null);
        shellInstance.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        shellInstance.linearVelocity = origin.forward * Random.Range(shellSpeedMin, shellSpeedMax);
    }

    private void OnDestroy()
    {
        if (grabInteractable != null)
        {
            grabInteractable.activated.RemoveListener(Shoot);
        }
    }
}
