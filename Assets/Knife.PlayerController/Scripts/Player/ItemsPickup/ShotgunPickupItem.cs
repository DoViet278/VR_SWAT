using UnityEngine;
using System.Collections;
using System;

namespace KnifePlayerController
{
    public class ShotgunPickupItem : PickupableItem
    {
        protected override BaseItem getItem()
        {
            ShotgunItem item = new ShotgunItem("Item_Shotgun");
            item.Add(1);
            return item;
        }

        public override Type GetItemType()
        {
            return typeof(ShotgunItem);
        }
    }
}