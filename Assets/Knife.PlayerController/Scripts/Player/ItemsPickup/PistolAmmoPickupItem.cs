using UnityEngine;
using System.Collections;
using System;

namespace KnifePlayerController
{
    public class PistolAmmoPickupItem : PickupableItem
    {
        public int Count;

        protected override BaseItem getItem()
        {
            PistolAmmoItem item = new PistolAmmoItem("Pistol_Ammo");
            item.Add(Count);
            return item;
        }

        public override Type GetItemType()
        {
            return typeof(PistolAmmoItem);
        }
    }
}