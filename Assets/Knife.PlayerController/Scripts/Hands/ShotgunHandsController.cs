using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KnifePlayerController
{
    [System.Serializable]
    public class ShotgunHandsController : HandsController, IWeaponAmmo, IWeaponShootMode, IAmmoItemGetter
    {
        #region params
        public float CameraFov
        {
            get
            {
                return cameraFov;
            }
        }

        [Header("Ammo")]
        public int AmmoCurrent = 7;
        public int AmmoMax = 7;
        public int AmmoMaxDefault = 7;
        public int AmmoMaxAdd = 9;
        public int AmmoCapacity
        {
            get
            {
                return ammoItem.GetCurrentCount();
            }
        }
        public int BulletsCount = 10;
        public float BulletAngleMin = -4f;
        public float BulletAngleMax = 4f;
        public float DamageAmount = 10;

        [Header("Options")]
        public bool CentralAim = false;
        public bool LowPoseInAiming = false;
        public bool Silence = false;
        public bool Auto = false;
        public bool Flashlight = false;
        public bool AddAmmo = false;

        public float LowPoseWeightTarget = 0.08f;
        public float LowPoseWeightBlendSpeed = 0.2f;
        public LayerMask FireCollisionLayer;
        public Transform FirePoint;
        public GameObject GunHitDecalPrefab;
        public LerpToRoot HandsLerp;
        public AnimationCurve RecoilCurve;
        public Vector2 RecoilAmountMin = new Vector2(0.3f, 1f);
        public Vector2 RecoilAmountMax = new Vector2(-0.3f, 1f);
        public float RecoilFadeOutTime = 0.3f;
        public float AimRecoilMultiplier = 0.3f;
        public float AimingNoise;
        public TransformNoise CameraNoise;
        public Rigidbody PropsBody;
        public Collider[] PropsCollision;
        public float DetachPropsForce;
        public Transform DetachPropsDirection;
        public Animator BarrelSmokeAnimator;
        public Transform BarrelSmokeRotationReference;
        public float BarrelSmokeDelay = 2f;
        TransformStateSetupper propsTransformState;

        public GameObject BulletInHand;
        public GameObject BulletToGun;

        public string ReloadEndTrigger = "SReloadEnd";

        float recoilTime = 0;
        Vector2 currentRecoil = new Vector2();

        [Header("Animations")]
        public string FromAimDefaultAnim = "FromAimDefault";
        [SerializeField]
        float fovSmoothSpeed = 1f;
        [SerializeField]
        float fromAimDelay = 7f;
        [SerializeField]
        float defaultFov = 60f;
        [SerializeField]
        float aimFov = 35f;
        [SerializeField]
        float aimFovCentral = 30f;
        [SerializeField]
        string fovParameter = "FOV";
        [SerializeField]
        string firstTimeAnim = "FirstTime";
        [SerializeField]
        string lowposeAnim = "SGLowPose";
        [SerializeField]
        string reloadFromIdleAnim = "ReloadFromIdleStart";
        [SerializeField]
        string reloadFromAimAnim = "ReloadFromAimStart";
        [SerializeField]
        string aimAnim = "Aim";
        [SerializeField]
        string fromAimAnim = "FromAim";
        [SerializeField]
        string aimInAimAnim = "AimInAim";
        [SerializeField]
        string centralAim = "CentralAim";
        [SerializeField]
        string centralAimFromIdle = "CentralAimFromIdle";
        [SerializeField]
        string centralFromAim = "CentralFromAim";
        [SerializeField]
        string centralFromAimToIdle = "CentralFromAimToIdle";
        [SerializeField]
        string toUpgradeAnim = "UpgradeDeploy";
        [SerializeField]
        string fromUpgradeAnim = "UpgradeHide";

        public string UpgradeIdleAnim = "UpgradeIdle";

        [SerializeField]
        string shotAnim = "Shot";
        [SerializeField]
        string shotCentralAnim = "SGCentralShot";
        [SerializeField]
        string emptyShotAnim = "ShotEmpty";
        [SerializeField]
        string emptyCentralShotAnim = "SGCentralShotEmpty";
        [SerializeField]
        string shotWithoutAimAnim = "ShotWithoutAim";
        [SerializeField]
        string emptyShotWithoutAimAnim = "ShotEmptyWithoutAim";
        [SerializeField]
        string jumpStartAimAnim = "JumpStartAim";
        [SerializeField]
        string jumpEndAimAnim = "JumpEndAim";
        [SerializeField]
        string jumpStartIdleAnim = "JumpStartIdle";
        [SerializeField]
        string jumpEndIdleAnim = "JumpEndIdle";
        [SerializeField]
        string jumpStartCentralAimAnim = "JumpStartAimCentral";
        [SerializeField]
        string jumpEndCentralAimAnim = "JumpEndAimCentral";

        [Header("Crouch")]
        [SerializeField]
        string crouchAimAnim = "ShotgunBasicAimSitDown";
        [SerializeField]
        string standUpAimAnim = "ShotgunBasicAimGetUp";
        [SerializeField]
        string crouchIdleAnim = "ShotgunIdleSitDown";
        [SerializeField]
        string standUpIdleAnim = "ShotgunIdleGetUp";
        [SerializeField]
        string crouchCentralAimAnim = "ShotgunCentralAimSitDown";
        [SerializeField]
        string standUpCentralAimAnim = "ShotgunCentralAimGetUp";

        [Header("Damage")]
        [SerializeField]
        string damagedBasicAimAnim = "SGDamageBasicAim";
        string damagedIdleAnim = "SGDamageIdle";
        string damagedCentralAimAnim = "SGDamageCentral";

        [Header("Common")]
        public Sprite ShootModeSprite;
        [SerializeField]
        GameObject gunshot;
        [SerializeField]
        GameObject gunshotSilencer;
        [SerializeField]
        float GunShotLiveTime = 0.1f;
        [SerializeField]
        PlayerController controller;
        [SerializeField]
        WeaponCustomization customization;
        [SerializeField]
        MeshRenderer sphere;
        public RuntimeAnimatorController ControllerDefault;
        public AnimatorOverrideController ControllerCentral;
        [SerializeField]
        Rigidbody shell;
        [SerializeField]
        Transform shellForceDir;
        [SerializeField]
        float shellForceMin;
        [SerializeField]
        float shellForceMax;
        [SerializeField]
        Transform shellTorqueDir;
        [SerializeField]
        float shellTorque;

        float smokeElapsedTime = 0f;

        [Header("Sounds")]
        public string DefaultShoot = "ShotgunShoot";
        public string SilenceShoot = "ShotgunShootSilence";
        public string DefaultCentralAimShoot = "ShotgunShoot";
        public string SilenceCentralAimShoot = "ShotgunShootSilence";

        public bool IsAiming
        {
            get
            {
                return isAiming;
            }
        }

        float cameraFov = 60f;

        [SerializeField]
        GameObject propsRoot;

        protected bool firstTimeDeploy;

        bool isAiming = false;
        bool centralAimFromDefaultAim = false;
        bool isReloading = false;
        bool isUpgrading = false;

        bool needSmoothInAim = false;
        bool needLowPose = false;

        float toIdleElapsedTime = 0;
        float fromAimTime = 0;

        float currentLowPoseWeight = 0;

        [SerializeField]
        float autoFireRate = 0.75f;
        float nextFireTime;

        bool flashLightIsOn = false;
        Light flashlightLight;
        MeshRenderer flashLightVolumetric;

        Queue<GameObject> gunHitDecalPool = new Queue<GameObject>();
        int poolSize = 200;
        float hitDecalLifetime = 20f;

        Rigidbody clonedPropsBody;

        ShotgunAmmoItem ammoItem;

        public float currentAimFov
        {
            get
            {
                return CentralAim ? aimFovCentral : aimFov;
            }
        }

        #endregion
        public override void Init(GameObject root)
        {
            base.Init(root);
            firstTimeDeploy = true;

            propsTransformState = PropsBody.GetComponent<TransformStateSetupper>();

            initPool();
            controller.JumpStartEvent += jumpStart;
            controller.JumpEndEvent += jumpEnd;

            controller.CrouchEvent += crouch;
            controller.StandUpEvent += standup;

            controller.DamageHandler.DamagedEvent.AddListener(damaged);
            controller.DamageHandler.ResurrectEvent.AddListener(resurrectEvent);


            if (!propsTransformState.CalculateVelocity)
            {
                Debug.LogWarning("Weapon velocity calculation disabled, it's enables automatically", PropsBody);
                propsTransformState.CalculateVelocity = true;
            }

            ammoItem = new ShotgunAmmoItem("Item_Shotgun_Ammo");
            playerInventory.AddItem(ammoItem);
            controller.PlayerFreezeChanged.AddListener(playerFreezeChanged);
        }

        private void playerFreezeChanged(bool isFreezed)
        {
            if (!isFreezed)
            {
                if (isUpgrading)
                {
                    controller.Freeze(true);
                }
            }
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
            if (!IsDeployed)
                return;

            if (isReloading)
                return;

            if (!canControl)
                return;

            AnimatorStateInfo state = handsAnimator.GetCurrentAnimatorStateInfo(0);
            if(state.IsName("SGCentralAimIdle"))
            {
                handsAnimator.Play(damagedCentralAimAnim);
            } else if(state.IsName("SGAimInAim"))
            {
                handsAnimator.Play(damagedBasicAimAnim);
            }
            else if(state.IsName("SGIdle"))
            {
                handsAnimator.Play(damagedIdleAnim);
            }
        }

        protected virtual void crouch()
        {
            if (!IsDeployed)
                return;

            if (isReloading)
                return;

            if (!canControl)
                return;

            if (isAiming)
            {
                if (CentralAim)
                {
                    handsAnimator.CrossFadeInFixedTime(crouchCentralAimAnim, 0.05f, 0, 0);
                }
                else
                {
                    handsAnimator.CrossFadeInFixedTime(crouchAimAnim, 0.05f, 0, 0);
                }
            }
            else
            {
                if(fromAimTime > 0)
                {
                    handsAnimator.CrossFadeInFixedTime(crouchAimAnim, 0.05f, 0, 0);
                }
                else
                {
                    handsAnimator.CrossFadeInFixedTime(crouchIdleAnim, 0.05f, 0, 0);
                }
            }
        }

        protected virtual void standup()
        {
            if (!IsDeployed)
                return;

            if (isReloading)
                return;

            if (!canControl)
                return;


            if (isAiming)
            {
                if (CentralAim)
                {
                    handsAnimator.CrossFadeInFixedTime(standUpCentralAimAnim, 0.05f, 0, 0);
                }
                else
                {
                    handsAnimator.CrossFadeInFixedTime(standUpAimAnim, 0.05f, 0, 0);
                }
            }
            else
            {
                if (fromAimTime > 0)
                {
                    handsAnimator.CrossFadeInFixedTime(standUpAimAnim, 0.05f, 0, 0);
                }
                else
                {
                    handsAnimator.CrossFadeInFixedTime(standUpIdleAnim, 0.05f, 0, 0);
                }
            }

        }

        protected virtual void jumpStart()
        {
            if (!IsDeployed || !canControl)
                return;

            if (!canControl)
                return;

            if (!isReloading)
            {
                handsAnimator.SetBool("JumpFall", false);
                handsAnimator.SetTrigger("Jump");
            }
        }

        protected virtual void jumpEnd()
        {
            if (!IsDeployed || !canControl)
                return;
            if (isReloading)
                return;

            if (!canControl)
                return;

            handsAnimator.SetBool("JumpFall", true);
            handsAnimator.ResetTrigger("Jump");
        }

        protected virtual void initPool()
        {
            for (int i = 0; i < poolSize; i++)
            {
                GameObject instance = GameObject.Instantiate(GunHitDecalPrefab);
                instance.SetActive(false);

                gunHitDecalPool.Enqueue(instance);
            }
        }

        protected virtual GameObject SpawnHitDecal()
        {
            if (gunHitDecalPool.Count == 0)
            {
                GameObject instance = GameObject.Instantiate(GunHitDecalPrefab);
                instance.SetActive(false);

                gunHitDecalPool.Enqueue(instance);
            }

            return gunHitDecalPool.Dequeue();
        }

        public string ShotSound
        {
            get
            {
                if(CentralAim && isAiming && lastInputData.MouseSecondHold)
                {
                    if (Silence)
                    {
                        return SilenceCentralAimShoot;
                    }
                    else
                    {
                        return DefaultCentralAimShoot;
                    }
                } else
                {
                    if (Silence)
                    {
                        return SilenceShoot;
                    }
                    else
                    {
                        return DefaultShoot;
                    }
                }
            }
        }

        public bool IsReloading
        {
            get
            {
                return isReloading;
            }
        }

        public void SetCameraFov(float fov)
        {
            cameraFov = fov;
        }

        Vector2 getRandomVector(Vector2 min, Vector2 max)
        {
            return new Vector2(Random.Range(min.x, max.x), Random.Range(min.y, max.y));
        }

        public override void Update(float deltaTime)
        {
            if (controller.IsFreezed || !canControl)// || !controller.IsGrounded)
                return;

            Vector3 upDirection = Vector3.up;
            Vector3 forwardDirection = BarrelSmokeRotationReference.forward;
            BarrelSmokeAnimator.transform.rotation = Quaternion.LookRotation(forwardDirection, upDirection);
            if (isReloading)
            {
                fromAimTime = Time.time + fromAimDelay;
            }

            base.Update(deltaTime);

            if (isAiming)
            {
                if (lastInputData.MouseSecondHold)
                {
                    CameraNoise.NoiseAmount = Mathf.MoveTowards(CameraNoise.NoiseAmount, AimingNoise, deltaTime * 5f);
                    fromAimTime = Time.time + fromAimDelay;
                    if (needSmoothInAim)
                    {
                        cameraFov = Mathf.Lerp(cameraFov, Mathf.Lerp(defaultFov, currentAimFov, handsAnimator.GetFloat(fovParameter)), deltaTime * fovSmoothSpeed);
                    }
                    else
                    {
                        cameraFov = Mathf.Lerp(defaultFov, currentAimFov, handsAnimator.GetFloat(fovParameter));
                    }
                }
                else
                {
                    if (Input.GetKey(KeyCode.LeftShift))
                    {
                        fromAimTime = 0;
                    }
                    toIdleElapsedTime = 0;
                    if (Time.time >= fromAimTime && fromAimTime >= 0)
                    {
                        fromAim();
                    }
                    cameraFov = Mathf.Lerp(cameraFov, defaultFov, Time.deltaTime * fovSmoothSpeed);
                }
            }
            else
            {

                cameraFov = Mathf.Lerp(cameraFov, defaultFov, Time.deltaTime * fovSmoothSpeed);
            }

            nextFireTime += Time.deltaTime;
            if (lastInputData.MouseHold && AmmoCurrent > 0)
            {
                if (nextFireTime >= autoFireRate)
                {
                    fire();
                }
            }

            if (smokeElapsedTime >= 0)
            {
                smokeElapsedTime += Time.deltaTime;
                if (smokeElapsedTime >= BarrelSmokeDelay)
                {
                    BarrelSmokeAnimator.Play("BarrelSmokeAnimation", 0, 0);
                    smokeElapsedTime = -1;
                }
            }

            recoilTime += Time.deltaTime;
            float recoilFraction = recoilTime / RecoilFadeOutTime;
            float recoilValue = RecoilCurve.Evaluate(recoilFraction);

            currentRecoil = Vector2.Lerp(Vector2.zero, currentRecoil, recoilValue);

            Vector2 recoilResult = currentRecoil;
            controller.Look.LookRotation(recoilResult.x, recoilResult.y, deltaTime);

            if (IsDeployed)
            {
                currentLowPoseWeight = Mathf.MoveTowards(currentLowPoseWeight, (needLowPose && LowPoseInAiming && isDeployed) ? LowPoseWeightTarget : 0, LowPoseWeightBlendSpeed * deltaTime);
                handsAnimator.SetLayerWeight(1, currentLowPoseWeight);
            }
        }

        public override void Deploy()
        {
            if (controller.IsFreezed)
                return;

            if (firstTimeDeploy)
            {
                if (isDeployed)
                    return;

                if (LockControlOnDeploy)
                    canControl = false;

                handsAnimator.Play(firstTimeAnim);
                SetDeployed(true);

                propsRoot.SetActive(true);
            }
            else
            {
                base.Deploy();

                propsRoot.SetActive(true);
            }
        }

        public override void HideProps()
        {
            propsRoot.SetActive(false);
        }

        public override void ShowProps()
        {
            propsRoot.SetActive(true);
        }

        public override void SetDeployed(bool value)
        {
            base.SetDeployed(value);
            handsAnimator.Play(lowposeAnim, 1);
        }

        public override void Hide()
        {
            if (!isDeployed || controller.IsFreezed)
                return;

            base.Hide();

            if (flashLightIsOn)
                toggleFlashlight();

            firstTimeDeploy = false;

            toIdleElapsedTime = 0;
            isAiming = false;
            isReloading = false;
            HideBullets();
        }

        protected override void inputEvent(InputEventType et)
        {
            //if (!controller.IsGrounded)
            //    return;
            if (!propsRoot.activeSelf || !canControl)
                return;

            base.inputEvent(et);

            if (et == InputEventType.Reload)
            {
                reload();
            }

            if (et == InputEventType.MouseDown)
            {
                fire();
            }

            if (et == InputEventType.MouseHold)
            {

            }

            if (et == InputEventType.MouseSecondDown)
            {
                aim();
            }

            if (et == InputEventType.MouseSecondHold)
            {
                aimUpdate();
            }

            if (et == InputEventType.MouseSecondUp)
            {
                fromAim();
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

            if (et == InputEventType.Flashlight)
            {
                toggleFlashlight();
            }
        }

        protected virtual void toggleFlashlight()
        {
            if (flashlightLight == null || flashLightVolumetric == null)
            {
                throw new System.Exception("No flashlight setupped");
            }

            flashLightIsOn = !flashLightIsOn;

            flashlightLight.enabled = flashLightIsOn;
            flashLightVolumetric.enabled = flashLightIsOn;
        }

        public virtual void PutFlashLight(GameObject root)
        {
            flashlightLight = root.GetComponent<Light>();
            flashLightVolumetric = root.transform.GetChild(0).GetComponent<MeshRenderer>();
        }

        protected virtual void reload()
        {
            if (isReloading || !isDeployed || controller.IsFreezed)
                return;

            if (AmmoCurrent == AmmoMax || AmmoCapacity == 0)
                return;

            if (isAiming)
            {
                handsAnimator.CrossFadeInFixedTime(reloadFromAimAnim, 0.1f, 0);
            }
            else
            {
                handsAnimator.CrossFadeInFixedTime(reloadFromIdleAnim, 0.1f, 0);
            }

            fromUpgrade(false);
            fromAim(false);
            isReloading = true;
        }

        protected virtual void fire()
        {
            if (isReloading)
            {
                reloadEnding();
                return;
            }

            if (!isDeployed || controller.IsFreezed || nextFireTime < autoFireRate)
                return;

            if (AmmoCurrent == 0)
            {
                if (isAiming && (centralAimFromDefaultAim || currentInputData.MouseSecondHold))
                {
                    if (CentralAim)
                    {
                        handsAnimator.Play(emptyCentralShotAnim, 0, 0);
                    }
                    else
                    {
                        handsAnimator.Play(emptyShotAnim, 0, 0);
                    }
                }
                else
                {
                    handsAnimator.CrossFadeInFixedTime(emptyShotWithoutAimAnim, 0.05f, 0);
                }
            }
            else
            {
                if (isAiming && (centralAimFromDefaultAim || currentInputData.MouseSecondHold))
                {
                    if (CentralAim)
                    {
                        handsAnimator.Play(shotCentralAnim, 0, 0);
                    }
                    else
                    {
                        handsAnimator.Play(shotAnim, 0, 0);
                    }
                }
                else
                {
                    handsAnimator.CrossFadeInFixedTime(shotWithoutAimAnim, 0.05f, 0);
                }

                currentRecoil += getRandomVector(RecoilAmountMin, RecoilAmountMax) * (currentInputData.MouseHold ? AimRecoilMultiplier : 1f);
                recoilTime = 0;
                gunShotInstantiate();
                //EmitShell();
                emitHitEffect();
                AmmoCurrent--;
                smokeElapsedTime = 0;

                BarrelSmokeAnimator.Play("BarrelSmokeAnimation", 0, 1);
            }

            if (!isAiming && CentralAim)
            {
                isAiming = true;
            }
            fromAimTime = Time.time + fromAimDelay;

            nextFireTime = 0;
        }

        void emitHitEffect()
        {
            for (int i = 0; i < BulletsCount; i++)
            {
                Quaternion randomRotation = Quaternion.AngleAxis(Random.Range(BulletAngleMin, BulletAngleMax), FirePoint.up) * Quaternion.AngleAxis(Random.Range(BulletAngleMin, BulletAngleMax), FirePoint.right);

                Ray r = new Ray(FirePoint.position, randomRotation * FirePoint.forward);
                RaycastHit hitInfo;

                if (Physics.Raycast(r, out hitInfo, 1000f, FireCollisionLayer, QueryTriggerInteraction.Ignore))
                {
                    IHittableObject hittable = hitInfo.collider.GetComponent<IHittableObject>();

                    if (hittable == null)
                    {
                        hitInfo.collider.GetComponentInParent<IHittableObject>();
                    }

                    DecalOnHit dOnHit = hitInfo.collider.GetComponent<DecalOnHit>();

                    if (dOnHit == null)
                    {
                        GameObject decal = SpawnHitDecal();
                        decal.SetActive(true);
                        decal.GetComponent<TimedCallbackEvent>().DestroyWithDelay(hitDecalLifetime, () => decalDestroyed(decal));
                        decal.transform.position = hitInfo.point;
                        decal.transform.forward = hitInfo.normal;
                        decal.transform.SetParent(hitInfo.transform);
                    }

                    if (hittable != null)
                    {
                        DamageData damage = new DamageData();
                        damage.DamageAmount = DamageAmount;
                        damage.HitDirection = r.direction;
                        damage.HitPosition = hitInfo.point;
                        hittable.TakeDamage(damage);
                    }
                }
            }
        }

        void decalDestroyed(GameObject decal)
        {
            decal.transform.SetParent(null);
            decal.SetActive(false);
            gunHitDecalPool.Enqueue(decal);
        }

        void gunShotInstantiate()
        {
            GameObject gunshotPrefab = Silence ? gunshotSilencer : gunshot;

            GameObject instance = GameObject.Instantiate(gunshotPrefab, gunshotPrefab.transform.position, gunshotPrefab.transform.rotation, gunshotPrefab.transform.parent);
            //instance.transform.rotation *= Quaternion.AngleAxis(Random.value * 360f, Vector3.forward);
            instance.SetActive(true);

            GameObject.Destroy(instance, GunShotLiveTime);
        }

        public void EmitShell()
        {
            Rigidbody shellInstance = GameObject.Instantiate(shell, shell.transform.position, shell.transform.rotation);

            shellInstance.gameObject.SetActive(true);
            shellInstance.transform.SetParent(null);
            shellInstance.maxAngularVelocity = 20;

            shellInstance.linearVelocity = controller.PlayerVelocity;
            shellInstance.AddForce(shellForceDir.forward * Random.Range(shellForceMin, shellForceMax));
            shellInstance.AddTorque(shellTorqueDir.forward * shellTorque);
            shellInstance.maxAngularVelocity = 65;
        }
        
        protected virtual void aim()
        {
            if (isReloading)
            {
                reloadEnding();
                return;
            }

            if (!isDeployed || controller.IsFreezed)
                return;

            toIdleElapsedTime = 0;
            if (isAiming)
            {
                needSmoothInAim = true;

                fromUpgrade(false);
                if (currentInputData.MouseSecondHold)
                {
                    if (CentralAim)
                    {
                        handsAnimator.CrossFadeInFixedTime(centralAim, 0.05f, 0);
                    }
                    else
                    {
                        handsAnimator.Play(aimInAimAnim, 0, 0);
                    }
                }
                else
                {
                    handsAnimator.Play(aimInAimAnim, 0, 0);
                }
                centralAimFromDefaultAim = true;
            }
            else
            {
                // TO DO: REFACTOR
                if (handsAnimator.GetCurrentAnimatorStateInfo(0).IsName(shotWithoutAimAnim))
                {
                    if (CentralAim)
                    {
                        //handsAnimator.CrossFadeInFixedTime(centralAimFromIdle, 0.25f, 0, 0.4f);
                    }
                    else
                    {
                        handsAnimator.CrossFadeInFixedTime(aimAnim, 0.25f, 0, 0.4f);
                    }
                }
                else
                {
                    if (CentralAim)
                    {
                        handsAnimator.CrossFadeInFixedTime(centralAimFromIdle, 0.25f, 0);
                    }
                    else
                    {
                        handsAnimator.CrossFadeInFixedTime(aimAnim, 0.05f, 0);
                    }
                }

                fromAimTime = -1;
                isAiming = true;
                fromUpgrade(false);
            }

            if (LowPoseInAiming)
            {
                needLowPose = true;
            }

            HandsLerp.Multiplier = 1.5f;
        }

        protected virtual void fromAim(bool withAnim = true)
        {
            if (isReloading)
                return;

            HandsLerp.Multiplier = 1;
            toIdleElapsedTime = 0;
            if (isAiming)
            {
                needSmoothInAim = false;
                if (CentralAim)
                {
                    if (withAnim)
                    {
                        if (centralAimFromDefaultAim)
                        {
                            handsAnimator.CrossFadeInFixedTime(centralFromAim, 0.05f, 0);
                        }
                        else
                        {
                            handsAnimator.CrossFadeInFixedTime(centralFromAim, 0.05f, 0);
                            //handsAnimator.CrossFadeInFixedTime(centralFromAimToIdle, 0.25f, 0);
                        }
                    }

                    if (centralAimFromDefaultAim)
                    {
                        fromAimTime = Time.time + fromAimDelay;
                    }
                    else
                    {
                        isAiming = false;
                    }
                }
                else
                {
                    if (Time.time >= fromAimTime && fromAimTime >= 0)
                    {
                        if (withAnim)
                        {
                            handsAnimator.CrossFadeInFixedTime(fromAimAnim, 0.05f, 0);
                        }
                        fromAimTime = -1;
                        isAiming = false;
                    }
                    else
                    {
                        fromAimTime = Time.time + fromAimDelay;
                        if(withAnim)
                            handsAnimator.Play(aimInAimAnim, 0, 0);
                    }
                }
            }
            centralAimFromDefaultAim = false;
            if (LowPoseInAiming)
            {
                needLowPose = false;
            }
        }

        protected virtual void aimUpdate()
        {

        }

        protected virtual void toUpgrade()
        {
            if (isReloading)
            {
                reloadEnding();
                return;
            }

            sphere.enabled = true;
            customization.ShowModifications();
            isUpgrading = true;
            fromAim(false);
            handsAnimator.CrossFadeInFixedTime(toUpgradeAnim, 0.25f, 0);
            controller.Freeze(true);
        }

        protected virtual void fromUpgrade(bool withAnim = true)
        {
            if (isReloading)
                return;

            sphere.enabled = false;
            customization.CloseModifications();
            isUpgrading = false;

            if (withAnim)
                handsAnimator.CrossFadeInFixedTime(fromUpgradeAnim, 0.25f, 0);

            controller.Freeze(false);
        }

        public void HideBullets()
        {
            BulletToGun.SetActive(false);
            BulletInHand.SetActive(false);
        }

        public void ReloadBulletInserted()
        {
            BulletToGun.SetActive(false);
        }

        public void ReloadLoopStart()
        {
            BulletToGun.SetActive(true);
            if (needOneBullet())
            {
                BulletInHand.SetActive(false);
            }
            else
            {
                BulletInHand.SetActive(true);
            }
        }

        bool needOneBullet()
        {
            return AmmoCapacity == 1 || (AmmoMax - AmmoCurrent == 1);
        }

        public void AddOneBullet()
        {
            /*int needToReload = AmmoMax - AmmoCurrent;
            int willBeReload = Mathf.Clamp(needToReload, 0, AmmoMax);
            willBeReload = Mathf.Clamp(needToReload, 0, AmmoCapacity);

            AmmoCurrent += willBeReload;
            AmmoCapacity -= willBeReload;

            isReloading = false;*/

            AmmoCurrent++;
            ammoItem.Consume(1);
            //AmmoCapacity--;
            if (AmmoCurrent >= AmmoMax || AmmoCapacity <= 0)
            {
                reloadEnding();
            }
        }

        void reloadEnding()
        {
            handsAnimator.SetTrigger(ReloadEndTrigger);
        }

        public void ReloadEnd()
        {
            isReloading = false;
        }

        public Sprite GetCurrentModeSprite()
        {
            return ShootModeSprite;
        }

        public int GetCurrentAmmo()
        {
            return AmmoCurrent;
        }

        public int GetMaxAmmo()
        {
            return AmmoMax;
        }

        public string GetCurrentAmmoString()
        {
            return AmmoCurrent.ToString();
        }

        public string GetCapacityAmmoString()
        {
            return AmmoCapacity.ToString();
        }

        protected override BaseItem createHandsItem()
        {
            return new ShotgunItem("Item_Shotgun");
        }

        public BaseItem GetAmmoItem()
        {
            return ammoItem;
        }
    }

    public class ShotgunItem : BaseItem
    {
        public ShotgunItem(string itemName) : base(itemName)
        {
            MaxCapacity = 1;
        }
    }

    public class ShotgunAmmoItem : BaseItem
    {
        public ShotgunAmmoItem(string itemName) : base(itemName)
        {
            MaxCapacity = 32;
            IconName = "ShotgunAmmo";
        }
    }
}