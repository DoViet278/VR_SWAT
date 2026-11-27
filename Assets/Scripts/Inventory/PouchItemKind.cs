using UnityEngine;
using VRSwat.Grenades;

/// <summary>
/// Loại vật phẩm, để từng ô túi đồ khai nó nhận cái gì.
///
/// Là bitmask nên một ô tick được nhiều loại: ô súng dài nhận Rifle + Shotgun chẳng hạn.
/// </summary>
[System.Flags]
public enum PouchItemKind
{
    None = 0,

    Pistol = 1 << 0,
    Rifle = 1 << 1,
    Shotgun = 1 << 2,

    PistolMagazine = 1 << 3,
    RifleMagazine = 1 << 4,
    ShotgunMagazine = 1 << 5,

    Grenade = 1 << 6,
    Flashbang = 1 << 7,

    /// <summary>Thứ nhét được vào túi nhưng không thuộc loại nào ở trên (có component PouchItem).</summary>
    Other = 1 << 8,
}

/// <summary>Xếp loại một vật phẩm để so với danh sách loại mà ô túi nhận.</summary>
public static class PouchItemKinds
{
    /// <summary>
    /// Vật này thuộc loại nào. Đọc trên prefab asset cũng được (editor dùng để vẽ preview),
    /// vì chỉ đi tìm component chứ không cần trạng thái runtime.
    /// </summary>
    public static PouchItemKind Classify(Transform itemTransform)
    {
        if (itemTransform == null)
            return PouchItemKind.None;

        // Lựu đạn choáng phải xét TRƯỚC lựu đạn thường: prefab FlashGrenade có cả hai component
        // (VRGrenade lo phần chốt/kíp nổ, Flashbang lo phần gây chói).
        if (itemTransform.GetComponent<Flashbang>() != null)
            return PouchItemKind.Flashbang;

        if (itemTransform.GetComponent<VRGrenade>() != null)
            return PouchItemKind.Grenade;

        Gun gun = itemTransform.GetComponent<Gun>();

        if (gun != null)
            return FromWeaponType(gun.Type, false);

        Magazine magazine = itemTransform.GetComponent<Magazine>();

        if (magazine != null)
            return FromWeaponType(magazine.MagazineType, true);

        if (itemTransform.GetComponentInChildren<PouchItem>() != null)
            return PouchItemKind.Other;

        return PouchItemKind.None;
    }

    private static PouchItemKind FromWeaponType(WeaponType weaponType, bool isMagazine)
    {
        switch (weaponType)
        {
            case WeaponType.Pistol:
                return isMagazine ? PouchItemKind.PistolMagazine : PouchItemKind.Pistol;

            case WeaponType.Rifle:
                return isMagazine ? PouchItemKind.RifleMagazine : PouchItemKind.Rifle;

            case WeaponType.Shotgun:
                return isMagazine ? PouchItemKind.ShotgunMagazine : PouchItemKind.Shotgun;

            default:
                return PouchItemKind.Other;
        }
    }
}
