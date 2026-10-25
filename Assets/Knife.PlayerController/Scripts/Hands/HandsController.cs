using System;
using UnityEngine;
using UnityEngine.Events;

namespace KnifePlayerController
{
    [System.Serializable]
    public class HandsController
    {
        [System.Serializable]
        public struct HandsControllerInput
        {
            public bool MouseDown;
            public bool MouseHold;
            public bool MouseUp;

            public bool MouseSecondDown;
            public bool MouseSecondHold;
            public bool MouseSecondUp;

            public bool Reload;
            public bool Upgrade;
            public bool Flashlight;
        }

        public bool IsDeployed
        {
            get
            {
                return isDeployed;
            }
        }

        public bool CanControl
        {
            get
            {
                return canControl;
            }
        }

        public UnityEvent PickedUp
        {
            get
            {
                return pickedUp;
            }

            private set
            {
                pickedUp = value;
            }
        }

        public KeyCode DeployHideKey;
        public UnityEvent DeployedEvent;
        public bool LockControlOnDeploy = true;

        public BaseItem HandsItem;

        [SerializeField]
        protected string deployedParameter = "Pistol";

        [SerializeField]
        protected string deployAnim = "Deploy";
        [SerializeField]
        protected string hideAnim = "Hide";

        [SerializeField]
        protected float hideCrossFade = 0.1f;

        protected GameObject handsRoot;
        protected Animator handsAnimator;

        protected bool isDeployed;
        protected bool canControl = true;

        protected HandsControllerInput lastInputData;
        protected HandsControllerInput currentInputData;

        protected PlayerInventory playerInventory;

        private UnityEvent pickedUp = new UnityEvent();

        bool hasItem = false;

        public virtual void Init(GameObject root)
        {
            handsRoot = root;
            handsAnimator = handsRoot.GetComponent<Animator>();
            playerInventory = handsRoot.GetComponent<PlayerInventory>();
            HandsItem = createHandsItem();
            HandsItem.ItemChanged.AddListener(itemChanged);

            hasItem = HandsItem.GetCurrentCount() > 0;
            playerInventory.AddItem(HandsItem);
            HideProps();
        }

        private void itemChanged()
        {
            bool newHasItem = HandsItem.GetCurrentCount() > 0;

            if(hasItem != newHasItem)
            {
                hasItem = newHasItem;
                if(hasItem)
                {
                    pickedUp.Invoke();
                }
            }
        }

        protected virtual BaseItem createHandsItem()
        {
            return new BaseItem("Item_Hands");
        }

        public virtual void Deploy()
        {
            if (handsAnimator == null)
            {
                throw new System.Exception("Hands animator null reference exception");
            }

            if (isDeployed)
            {
                return;
            }

            if (LockControlOnDeploy)
            {
                canControl = false;
                //Debug.Log("SET NO CONTROL " + DeployHideKey);
            }
            handsAnimator.Play(deployAnim);
            SetDeployed(true);
        }
        
        public virtual void SetFullyDeployed()
        {
            canControl = true;
            //Debug.Log("SET CONTROL " + DeployHideKey);
        }

        public virtual void SetDeployed(bool value)
        {
            isDeployed = value;
            handsAnimator.SetBool(deployedParameter, isDeployed);

            if (LockControlOnDeploy)
                canControl = false;

            if (value)
            {
                if (DeployedEvent != null)
                {
                    DeployedEvent.Invoke();
                }
            }
        }

        public virtual void Hide()
        {
            if (!isDeployed)
                return;

            handsAnimator.CrossFadeInFixedTime(hideAnim, 0.1f, 0);
            SetDeployed(false);
        }

        public virtual void HideImmediately()
        {
            handsAnimator.Play(hideAnim, 0, 1f);
            SetDeployed(false);
        }

        public virtual void Update(float deltaTime)
        {

        }

        public virtual void ApplyInput(HandsControllerInput inputData)
        {
            currentInputData = inputData;

            if (inputData.MouseDown)
                inputEvent(InputEventType.MouseDown);
            if (inputData.MouseHold)
                inputEvent(InputEventType.MouseHold);
            if (inputData.MouseUp)
                inputEvent(InputEventType.MouseUp);
            if (inputData.MouseSecondDown)
                inputEvent(InputEventType.MouseSecondDown);
            if (inputData.MouseSecondHold)
                inputEvent(InputEventType.MouseSecondHold);
            if (inputData.MouseSecondUp)
                inputEvent(InputEventType.MouseSecondUp);
            if (inputData.Upgrade)
                inputEvent(InputEventType.Upgrade);
            if (inputData.Reload)
                inputEvent(InputEventType.Reload);
            if (inputData.Flashlight)
                inputEvent(InputEventType.Flashlight);

            lastInputData = inputData;
        }

        protected virtual void inputEvent(InputEventType et)
        {

        }

        public enum InputEventType
        {
            MouseDown,
            MouseHold,
            MouseUp,
            MouseSecondDown,
            MouseSecondHold,
            MouseSecondUp,
            Reload,
            Upgrade,
            Flashlight
        }

        public virtual void ShowProps()
        {

        }

        public virtual void HideProps()
        {

        }
    }

    public interface IAmmoItemGetter
    {
        BaseItem GetAmmoItem();
    }
}