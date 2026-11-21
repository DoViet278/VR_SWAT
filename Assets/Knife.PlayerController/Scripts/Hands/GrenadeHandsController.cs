using UnityEngine;
using UnityEngine.UI;

namespace KnifePlayerController
{
    [System.Serializable]
    public class GrenadeHandsController : HandsController, IWeaponAmmo, IWeaponShootMode
    {
        public string ThrowStartAnim;
        public string ThrowAnim;
        public string DamageAnim;
        public string CrouchAnim = "GrenadeCrouch";
        public string StandupAnim = "GrenadeStandUp";

        public GameObject HandsProps;
        public PlayerController Controller;

        public Rigidbody PropsBody;
        public Collider[] PropsCollision;
        public float DetachPropsForce;
        public Transform DetachPropsDirection;

        public Grenade GrenadeTemplate;
        public int GrenadesCount
        {
            get
            {
                return ammoItem.GetCurrentCount();
            }
        }

        public Transform GrenadeThrowDirection;

        [SerializeField]
        string toUpgradeAnim = "GrenadeToUpgrade";
        [SerializeField]
        string fromUpgradeAnim = "GrenadeFromUpgrade";
        [SerializeField]
        MeshRenderer sphere;
        [SerializeField]
        WeaponCustomization customization;

        TransformStateSetupper propsTransformState;
        Rigidbody clonedPropsBody;
        bool inLoad = false;
        bool isUpgrading = false;

        GrenadeItem ammoItem;

        public override void Init(GameObject root)
        {
            base.Init(root);

            propsTransformState = PropsBody.GetComponent<TransformStateSetupper>();

            Controller.JumpStartEvent += jumpStart;
            Controller.JumpEndEvent += jumpEnd;

            Controller.CrouchEvent += crouch;
            Controller.StandUpEvent += standUp;

            Controller.DamageHandler.DamagedEvent.AddListener(damaged);
            Controller.DamageHandler.ResurrectEvent.AddListener(resurrectEvent);


            if (!propsTransformState.CalculateVelocity)
            {
                Debug.LogWarning("Weapon velocity calculation disabled, it's enables automatically", PropsBody);
                propsTransformState.CalculateVelocity = true;
            }
            ammoItem = HandsItem as GrenadeItem;
        }

        void crouch()
        {
            if (!isDeployed || inLoad || !canControl || !HandsProps.activeSelf)
                return;

            AnimatorStateInfo state = handsAnimator.GetCurrentAnimatorStateInfo(0);

            if (state.IsName(ThrowAnim) || state.IsName(ThrowStartAnim))
                return;
            handsAnimator.Play(CrouchAnim);
        }

        void standUp()
        {
            if (!isDeployed || inLoad || !canControl || !HandsProps.activeSelf)
                return;

            AnimatorStateInfo state = handsAnimator.GetCurrentAnimatorStateInfo(0);

            if (state.IsName(ThrowAnim) || state.IsName(ThrowStartAnim))
                return;
            handsAnimator.Play(StandupAnim);
        }

        private void resurrectEvent()
        {
            if (IsDeployed)
            {
                ShowProps();
            }

            if (clonedPropsBody != null)
            {
                GameObject targetDestroy = clonedPropsBody.gameObject;
                GameObject.Destroy(targetDestroy);
            }
        }

        public void DetachProps()
        {
            HideProps();
            if (GrenadesCount > 0)
            {
                clonedPropsBody = GameObject.Instantiate(PropsBody, PropsBody.transform.parent);
                clonedPropsBody.transform.SetParent(null);
                clonedPropsBody.gameObject.SetActive(true);
                clonedPropsBody.isKinematic = false;
                clonedPropsBody.linearVelocity = propsTransformState.Velocity;
                clonedPropsBody.angularVelocity = propsTransformState.AngularVelocity;
                clonedPropsBody.AddForce(DetachPropsDirection.forward * DetachPropsForce);
            }
        }

        private void damaged(DamageData damage)
        {
            if (!isDeployed || inLoad || !canControl || GrenadesCount == 0 || !HandsProps.activeSelf)
                return;

            AnimatorStateInfo state = handsAnimator.GetCurrentAnimatorStateInfo(0);

            if (state.IsName(ThrowAnim) || state.IsName(ThrowStartAnim))
                return;

            handsAnimator.Play(DamageAnim);
        }

        protected virtual void jumpStart()
        {
            if (!IsDeployed || inLoad || !canControl || GrenadesCount == 0 || !HandsProps.activeSelf)
                return;

            AnimatorStateInfo state = handsAnimator.GetCurrentAnimatorStateInfo(0);

            if (state.IsName(ThrowAnim) || state.IsName(ThrowStartAnim))
                return;

            handsAnimator.SetBool("JumpFall", false);
            handsAnimator.SetTrigger("Jump");
        }

        protected virtual void jumpEnd()
        {
            if (!IsDeployed || inLoad || !canControl || GrenadesCount == 0 || !HandsProps.activeSelf)
                return;

            AnimatorStateInfo state = handsAnimator.GetCurrentAnimatorStateInfo(0);

            if (state.IsName(ThrowAnim) || state.IsName(ThrowStartAnim))
                return;

            handsAnimator.SetBool("JumpFall", true);
            handsAnimator.ResetTrigger("Jump");
        }

        public void DeployedAnimationEvent()
        {
            ShowProps();
        }

        public override void Deploy()
        {
            base.Deploy();
            ShowProps();

            inLoad = false;
        }

        public override void HideProps()
        {
            HandsProps.SetActive(false);
        }

        public override void ShowProps()
        {
            if (GrenadesCount > 0)
                HandsProps.SetActive(true);
            else
                HandsProps.SetActive(false);
        }

        protected override void inputEvent(InputEventType et)
        {
            base.inputEvent(et);
            if (!HandsProps.activeSelf || !canControl)
                return;

            if (et == InputEventType.MouseDown)
            {
                attack();
            }

            if (et == InputEventType.Upgrade)
            {
                if (isUpgrading)
                {
                    fromUpgrade(true);
                }
                else
                {
                    toUpgrade();
                }
            }
        }

        protected virtual void toUpgrade()
        {
            if (inLoad || !HandsProps.activeSelf)
                return;

            sphere.enabled = true;
            customization.ShowModifications();
            isUpgrading = true;
            handsAnimator.CrossFadeInFixedTime(toUpgradeAnim, 0.25f, 0);
            Controller.Freeze(true);
        }

        protected virtual void fromUpgrade(bool withAnim = true)
        {
            sphere.enabled = false;
            customization.CloseModifications();
            isUpgrading = false;

            if (withAnim)
            {
                handsAnimator.CrossFadeInFixedTime(fromUpgradeAnim, 0.25f, 0);
            }

            Controller.Freeze(false);
        }

        protected virtual void attack()
        {
            if (inLoad || GrenadesCount <= 0)
                return;

            AnimatorStateInfo state = handsAnimator.GetCurrentAnimatorStateInfo(0);

            if (state.IsName(ThrowAnim) || state.IsName(ThrowStartAnim))
                return;

            //Debug.Log("Attack grenade");
            handsAnimator.SetBool("ThrowGrenade", false);
            handsAnimator.Play(ThrowStartAnim);
            inLoad = true;
        }

        protected virtual void throwGrenade()
        {
            handsAnimator.Play(ThrowAnim);
            inLoad = false;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            handsAnimator.SetInteger("GrenadesCount", GrenadesCount);

            if (inLoad && !lastInputData.MouseHold)
            {
                AnimatorStateInfo state = handsAnimator.GetCurrentAnimatorStateInfo(0);
                if (state.IsName(ThrowStartAnim) && state.normalizedTime >= 0.9f)
                    throwGrenade();
            }
        }

        protected override BaseItem createHandsItem()
        {
            return new GrenadeItem("Item_Grenade");
        }

        public virtual void SpawnGrenade()
        {
            ammoItem.Consume(1);
            HideProps();
            Grenade g = GameObject.Instantiate(GrenadeTemplate, GrenadeTemplate.transform.parent);
            g.gameObject.SetActive(true);
            g.Throw(GrenadeThrowDirection.forward);
        }

        public override void Hide()
        {
            base.Hide();
        }

        public Sprite GetCurrentModeSprite()
        {
            return null;
        }

        public int GetCurrentAmmo()
        {
            if (GrenadesCount > 0)
                return 1;
            else
                return 0;
        }

        public int GetMaxAmmo()
        {
            return 1;
        }

        public string GetCurrentAmmoString()
        {
            if (GrenadesCount > 0)
                return "1";
            else
                return "0";
        }

        public string GetCapacityAmmoString()
        {
            if (GrenadesCount > 0)
                return (GrenadesCount - 1).ToString();
            else
                return "0";
        }
    }

    [System.Serializable]
    public class GrenadeItem : BaseItem
    {
        public GrenadeItem(string itemName) : base(itemName)
        {
            MaxCapacity = 6;
            IconName = "Grenade";
        }
    }
}