using TMPro;
using UnityEngine;

public class AmmoDisplay : MonoBehaviour
{
    [SerializeField] private GunAmmoManager ammoManager;
    [SerializeField] private TMP_Text ammoText;
    [SerializeField] private string noMagazineText = "--";

    private void OnEnable()
    {
        if (ammoManager == null)
            return;

        ammoManager.OnAmmoChanged.AddListener(Refresh);
        ammoManager.OnMagazineInserted.AddListener(Refresh);
        ammoManager.OnMagazineRemoved.AddListener(Refresh);
        Refresh();
    }

    private void OnDisable()
    {
        if (ammoManager == null)
            return;

        ammoManager.OnAmmoChanged.RemoveListener(Refresh);
        ammoManager.OnMagazineInserted.RemoveListener(Refresh);
        ammoManager.OnMagazineRemoved.RemoveListener(Refresh);
    }

    private void Refresh()
    {
        if (ammoText == null)
            return;

        ammoText.text = ammoManager.HasMagazine
            ? $"{ammoManager.CurrentAmmo}/{ammoManager.Capacity}"
            : noMagazineText;
    }
}
