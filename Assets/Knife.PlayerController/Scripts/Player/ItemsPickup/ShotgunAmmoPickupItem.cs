using UnityEngine;
using System.Collections;
using System;

namespace KnifePlayerController
{
    public class ShotgunAmmoPickupItem : PickupableItem
    {
        public int Count;

        protected override BaseItem getItem()
        {
            ShotgunAmmoItem item = new ShotgunAmmoItem("Item_Shotgun_Ammo");
            item.Add(Count);
            return item;
        }

        public override Type GetItemType()
        {
            return typeof(ShotgunAmmoItem);
        }
    }
}