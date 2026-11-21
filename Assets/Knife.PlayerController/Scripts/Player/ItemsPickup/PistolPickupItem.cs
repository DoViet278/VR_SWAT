using UnityEngine;
using System.Collections;
using System;

namespace KnifePlayerController
{ 
    public class PistolPickupItem : PickupableItem
    {
        protected override BaseItem getItem()
        {
            PistolItem item = new PistolItem("Item_Pistol");
            item.Add(1);
            return item;
        }

        public override Type GetItemType()
        {
            return typeof(PistolItem);
        }
    }
}