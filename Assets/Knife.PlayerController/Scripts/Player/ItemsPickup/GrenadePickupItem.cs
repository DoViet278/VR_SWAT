using UnityEngine;
using System.Collections;
using System;

namespace KnifePlayerController
{
    public class GrenadePickupItem : PickupableItem
    {
        protected override BaseItem getItem()
        {
            GrenadeItem item = new GrenadeItem("Item_Grenade");
            item.Add(1);
            return item;
        }

        public override Type GetItemType()
        {
            return typeof(GrenadeItem);
        }
    }
}