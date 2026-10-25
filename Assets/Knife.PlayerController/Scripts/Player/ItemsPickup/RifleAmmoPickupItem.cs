using UnityEngine;
using System.Collections;
using System;

namespace KnifePlayerController
{
    public class RifleAmmoPickupItem : PickupableItem
    {
        public int Count = 100;
        public Animator BoxAnimator;

        public override bool CanPickup
        {
            get
            {
                return BoxAnimator.GetInteger("State") < 2;
            }
        }

        protected override BaseItem getItem()
        {
            RifleAmmoItem item = new RifleAmmoItem("Item_Rifle_Ammo");
            item.Add(Count);
            return item;
        }

        public override Type GetItemType()
        {
            return typeof(RifleAmmoItem);
        }

        protected override void pickUpEvent()
        {
            base.pickUpEvent();

            if (IsPickedUp)
            {
                BoxAnimator.SetInteger("State", 2);
            }
            else
            {
                BoxAnimator.SetInteger("State", 1);
            }
        }
    }
}