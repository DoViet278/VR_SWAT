using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KnifePlayerController
{
    [System.Serializable]
    public class KnifeHandsController : HandsController, IWeaponAmmo, IWeaponShootMode
    {
        public string[] Attacks;
        public string[] CameraAnimations;
        public string ToIdle;
        public string DamageAnim;
        public GameObject HandsProps;
        public PlayerController Controller;
        //public CountableProgressbar AmmoProgressBar;
        public string InfinitySymbol = "";
        public Rigidbody PropsBody;
        public Collider[] PropsCollision;
        public float DetachPropsForce;
        public Transform DetachPropsDirection;
        public LayerMask HitLayer;
        public Transform HitSphere;
        public float Damage = 5f;
        public float HitRadius = 0.2f;
        [SerializeField]
        string toUpgradeAnim = "KnifeToUpgrade";
        [SerializeField]
        string fromUpgradeAnim = "KnifeFromUpgrade";
        [SerializeField]
        MeshRenderer sphere;
        [SerializeField]
        WeaponCustomization customization;
        [SerializeField]
        PlayerController controller;
        TransformStateSetupper propsTransformState;
        Rigidbody clonedPropsBody;

        bool needNextAttack = false;
        bool isAttacking = false;
        bool isUpgrading = false;

        int currentAttack = 0;

        List<IHittableObject> hittedTargets = new List<IHittableObject>();

        public override void Init(GameObject root)
        {
            base.Init(root);

            propsTransformState = PropsBody.GetComponent<TransformStateSetupper>();

            Controller.JumpStartEvent += jumpStart;
            Controller.JumpEndEvent += jumpEnd;

            Controller.DamageHandler.DamagedEvent.AddListener(damaged);
            Controller.DamageHandler.ResurrectEvent.AddListener(resurrectEvent);


            if (!propsTransformState.CalculateVelocity)
            {
                Debug.LogWarning("Weapon velocity calculation disabled, it's enables automatically", PropsBody);
                propsTransformState.CalculateVelocity = true;
            }

            Controller.PlayerFreezeChanged.AddListener(playerFreezeChanged);
        }

        private void playerFreezeChanged(bool isFreezed)
        {
            if(!isFreezed)
            {
                if(isUpgrading)
                {
                    Controller.Freeze(true);
                }
            }
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            float animationIsAttack = handsAnimator.GetFloat("IsKnifeAttacking");
            if(animationIsAttack >= 0.9f)
            {
                List<IHittableObject> hitted = getTargetsInSphere(HitSphere, HitRadius, HitLayer);
                foreach(IHittableObject h in hitted)
                {
                    if (hittedTargets.Contains(h))
                        continue;

                    DamageData damage = new DamageData();
                    damage.DamageAmount = Damage;
                    damage.HitDirection = HitSphere.forward;
                    damage.HitPosition = HitSphere.position;
                    damage.Receiver = h;
                    damage.HitType = DamageData.DamageType.KnifeHit;
                    h.TakeDamage(damage);

                    hittedTargets.Add(h);
                }
            }
            else
            {
                hittedTargets.Clear();
            }
        }

        private List<IHittableObject> getTargetsInSphere(Transform sphere, float radius, LayerMask mask)
        {
            List<IHittableObject> targets = new List<IHittableObject>();

            Collider[] colliders = Physics.OverlapSphere(sphere.position, HitRadius, HitLayer);

            foreach(Collider c in colliders)
            {
                IHittableObject target = c.GetComponent<IHittableObject>();

                if(target != null)
                {
                    targets.Add(target);
                }
            }

            return targets;
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
            clonedPropsBody = GameObject.Instantiate(PropsBody, PropsBody.transform.parent);
            clonedPropsBody.transform.SetParent(null);
            clonedPropsBody.gameObject.SetActive(true);
            clonedPropsBody.isKinematic = false;
            clonedPropsBody.linearVelocity = propsTransformState.Velocity;
            clonedPropsBody.angularVelocity = propsTransformState.AngularVelocity;
            clonedPropsBody.AddForce(DetachPropsDirection.forward * DetachPropsForce);
        }

        private void damaged(DamageData damage)
        {
            if (!isDeployed)
                return;

            handsAnimator.Play(DamageAnim);
        }

        protected virtual void jumpStart()
        {
            if (!IsDeployed || !canControl)
                return;

            handsAnimator.SetBool("JumpFall", false);
            handsAnimator.SetTrigger("Jump");
        }

        protected virtual void jumpEnd()
        {
            if (!IsDeployed || !canControl)
                return;

            handsAnimator.SetBool("JumpFall", true);
            handsAnimator.ResetTrigger("Jump");
        }

        public override void Deploy()
        {
            base.Deploy();
            HandsProps.SetActive(true);

            //AmmoProgressBar.SetMaxCount(0);
        }

        public override void HideProps()
        {
            HandsProps.SetActive(false);
        }

        public override void ShowProps()
        {
            HandsProps.SetActive(true);
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
            if (isAttacking)
                return;

            sphere.enabled = true;
            customization.ShowModifications();
            isUpgrading = true;
            handsAnimator.CrossFadeInFixedTime(toUpgradeAnim, 0.25f, 0);
            controller.Freeze(true);
        }

        protected virtual void fromUpgrade(bool withAnim = true)
        {
            sphere.enabled = false;
            customization.CloseModifications();
            isUpgrading = false;

            if (withAnim)
            {
                handsAnimator.Play(fromUpgradeAnim, 0, 0);
            }

            controller.Freeze(false);
        }

        public virtual void NextAttack()
        {
            if (!IsDeployed || !CanControl)
                return;

            hittedTargets.Clear();
            if (needNextAttack)
            {
                needNextAttack = false;
                if (currentAttack == 0)
                    currentAttack = 1;
                playAttack(true);
            }
            else
            {
                isAttacking = false;
                handsAnimator.Play(ToIdle, 0, 0);
            }
        }

        protected virtual void playAttack(bool isNextAttack)
        {
            if (isNextAttack)
            {
                handsAnimator.CrossFadeInFixedTime(CameraAnimations[currentAttack], 0.1f, 3, 0);
                handsAnimator.CrossFadeInFixedTime(Attacks[currentAttack], 0.1f, 0, 0);
            }
            else
            {
                handsAnimator.Play(CameraAnimations[currentAttack], 3, 0);
                handsAnimator.Play(Attacks[currentAttack], 0, 0);
            }
            currentAttack++;
            if (currentAttack >= Attacks.Length)
            {
                currentAttack = 0;
            }
        }

        protected virtual void attack()
        {
            if (isAttacking)
            {
                if (!needNextAttack)
                {
                    needNextAttack = true;
                }
            }
            else
            {
                hittedTargets.Clear();
                currentAttack = 0;
                playAttack(false);
                isAttacking = true;
            }
        }

        public override void Hide()
        {
            base.Hide();
            isAttacking = false;
            needNextAttack = false;
        }

        public int GetCurrentAmmo()
        {
            return 0;
        }

        public int GetMaxAmmo()
        {
            return 0;
        }

        public string GetCurrentAmmoString()
        {
            return InfinitySymbol;
        }

        public string GetCapacityAmmoString()
        {
            return InfinitySymbol;
        }

        public Sprite GetCurrentModeSprite()
        {
            return null;
        }

        protected override BaseItem createHandsItem()
        {
            return new KnifeItem("Item_Knife");
        }
    }

    public class KnifeItem : BaseItem
    {
        public KnifeItem(string itemName) : base(itemName)
        {
            MaxCapacity = 1;
        }
    }

}