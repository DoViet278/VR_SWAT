using UnityEngine;
using System.Collections;
using System;

namespace KnifePlayerController
{
    public class RiflePickupItem : PickupableItem
    {
        protected override BaseItem getItem()
        {
            RifleItem item = new RifleItem("Item_Rifle");
            item.Add(1);
            return item;
        }

        public override Type GetItemType()
        {
            return typeof(RifleItem);
        }
    }
}