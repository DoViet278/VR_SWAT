using UnityEngine;
using System.Collections;
using System;
using UnityEngine.Events;

namespace KnifePlayerController
{
    [RequireComponent(typeof(Collider))]
    public class PickupableItem : MonoBehaviour, IPlayerAction
    {
        public bool AutoDestroyAfterPickup = true;
        public bool Infinity = false;

        public UnityEvent ItemPickedUp = new UnityEvent();

        public virtual bool CanPickup
        {
            get
            {
                return true;
            }
        }

        public bool IsPickedUp
        {
            get
            {
                return isPickedUp;
            }
        }

        bool isPickedUp;

        public BaseItem PickupItem()
        {
            ItemPickedUp.Invoke();
            pickUpEvent();
            if (isPickedUp && !Infinity)
                return null;

            if(AutoDestroyAfterPickup)
                Destroy(gameObject);

            isPickedUp = true;
            return getItem();
        }

        public virtual Type GetItemType()
        {
            return typeof(BaseItem);
        }

        protected virtual void pickUpEvent()
        {

        }

        public void UseEnd()
        {

        }

        public void UseStart()
        {
            PickupItem();
        }

        public void UseUpdate()
        {

        }

        protected virtual BaseItem getItem()
        {
            return new BaseItem("Item_Null");
        }
    }
}