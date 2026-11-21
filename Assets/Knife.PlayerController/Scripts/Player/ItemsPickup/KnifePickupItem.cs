using UnityEngine;
using System.Collections;
using System;

namespace KnifePlayerController
{
    public class KnifePickupItem : PickupableItem
    {
        protected override BaseItem getItem()
        {
            KnifeItem item = new KnifeItem("Item_Knife");
            item.Add(1);
            return item;
        }

        public override Type GetItemType()
        {
            return typeof(KnifeItem);
        }
    }
}