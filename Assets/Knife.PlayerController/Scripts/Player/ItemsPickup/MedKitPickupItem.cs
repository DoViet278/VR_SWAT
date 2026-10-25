using UnityEngine;
using System.Collections;
using System;

namespace KnifePlayerController
{
    public class MedKitPickupItem : PickupableItem
    {
        public int Count;

        protected override BaseItem getItem()
        {
            MedkitItem item = new MedkitItem("MedKit");
            item.Add(Count);
            return item;
        }

        public override Type GetItemType()
        {
            return typeof(MedkitItem);
        }
    }
}