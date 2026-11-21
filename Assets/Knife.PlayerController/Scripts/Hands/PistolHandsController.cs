using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KnifePlayerController
{
    [System.Serializable]
    public class PistolHandsController : HandsController, IWeaponAmmo, IWeaponShootMode, IAmmoItemGetter
    {
        public float CameraFov
        {
            get
            {
                return cameraFov;
            }
        }

        [Header("Ammo")]
        public int AmmoCurrent = 20;
        public int AmmoMaxDefault = 20;
        public int AmmoMaxLong = 30;
        public int AmmoMax = 20;
        public int AmmoCapacity
        {
            get
            {
                return ammoItem.GetCurrentCount();
            }
        }
        public float DamageAmount = 10;

        [Header("Options")]
        public bool CentralAim = false;
        public bool BackReload = false;
        public bool LowPoseInAiming = false;
        public bool LongReload = false;
        public bool Silence = false;
        public bool Auto = false;
        public bool Flashlight = false;

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

        TransformStateSetupper propsTransformState;

        float recoilTime = 0;
        Vector2 currentRecoil = new Vector2();

        [Header("Animations")]
        public string FromAimDefaultAnim = "FromAimDefault";
        public string CentralToBasicAnim = "PistolCentralToBasic";
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
        string reloadAnim = "Reload";
        [SerializeField]
        string reloadBackAnim = "ReloadBack";
        [SerializeField]
        string reloadLongAnim = "ReloadLong";
        [SerializeField]
        string reloadLongBackAnim = "ReloadLongBack";
        [SerializeField]
        string aimAnim = "Aim";
        [SerializeField]
        string fromAimAnim = "FromAim";
        [SerializeField]
        string fromAimToIdleAnim = "FromAimDefault";
        [SerializeField]
        string aimInAimAnim = "AimInAim";
        [SerializeField]
        string aimFromAimAnim = "AimFromAim";
        [SerializeField]
        string basicToCentralAimAnim = "PistolBasicToCentral";
        [SerializeField]
        string toUpgradeAnim = "UpgradeDeploy";
        [SerializeField]
        string fromUpgradeAnim = "UpgradeHide";
        [SerializeField]
        string lowposeAnim = "LowPose";

        public string UpgradeIdleAnim = "UpgradeIdle";

        [SerializeField]
        string shotAnim = "Shot";
        [SerializeField]
        string shotWithoutAimAnim = "ShotWithoutAim";
        [SerializeField]
        string lastShotWithoutAimAnim = "LastShotWithoutAim";
        [SerializeField]
        string lastShotAnim = "LastShot";
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

        [Header("Damage")]
        [SerializeField]
        string damageAimAnim = "PistolDamageAim";
        [SerializeField]
        string damageIdleAnim = "PistolDamageIdle";

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

        [Header("Sounds")]
        public string DefaultShoot = "PistolShoot";
        public string SilenceShoot = "PistolShootSilence";


        public bool IsAiming
        {
            get
            {
                return isAiming && lastInputData.MouseSecondHold;
            }
        }

        float cameraFov = 60f;

        [Header("Other")]
        [SerializeField]
        GameObject propsRoot;
        public string EmptyParam = "Empty";

        protected bool firstTimeDeploy;

        bool isAiming = false;
        bool isReloading = false;
        bool isUpgrading = false;

        bool needSmoothInAim = false;
        bool needLowPose = false;

        float toIdleElapsedTime = 0;
        float fromAimTime = 0;

        float currentLowPoseWeight = 0;

        float autoFireRate = 0.1f;
        float nextFireTime;

        bool flashLightIsOn = false;
        Light flashlightLight;
        MeshRenderer flashLightVolumetric;

        Queue<GameObject> gunHitDecalPool = new Queue<GameObject>();
        int poolSize = 100;
        float hitDecalLifetime = 20f;

        Rigidbody clonedPropsBody;

        PistolAmmoItem ammoItem;

        public GameObject VisualBulletFirstTime;
        public GameObject VisualBullets;

        public override void Init(GameObject root)
        {
            base.Init(root);

            propsTransformState = PropsBody.GetComponent<TransformStateSetupper>();

            firstTimeDeploy = true;

            initPool();
            controller.JumpStartEvent += jumpStart;
            controller.JumpEndEvent += jumpEnd;

            controller.CrouchEvent += crouch;
            controller.StandUpEvent += standup;

            controller.DamageHandler.DamagedEvent.AddListener(damaged);
            controller.DamageHandler.ResurrectEvent.AddListener(resurrectEvent);

            if(!propsTransformState.CalculateVelocity)
            {
                Debug.LogWarning("Weapon velocity calculation disabled, it's enables automatically", PropsBody);
                propsTransformState.CalculateVelocity = true;
            }

            playerInventory.AddItem(new PistolAmmoItem("Item_Pistol_Ammo"));
            ammoItem = playerInventory.GetItem<PistolAmmoItem>();
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

        public void DisableVisualFirstTimeBullet()
        {
            VisualBulletFirstTime.SetActive(false);
        }

        public void EnableVisualBullets()
        {
            VisualBullets.SetActive(true);
        }

        public void DisableVisualBullets()
        {
            VisualBullets.SetActive(false);
        }

        protected virtual void crouch()
        {
            if (!IsDeployed)
                return;

            if (isReloading)
                return;

            if (!CanControl)
                return;

            if (AmmoCurrent == 0)
            {
                handsAnimator.SetFloat(EmptyParam, 1f);
            }
            else
            {
                handsAnimator.SetFloat(EmptyParam, 0f);
            }

            if (CentralAim && isAiming && lastInputData.MouseSecondHold)
                handsAnimator.CrossFadeInFixedTime(CrouchCentralAnim, 0.1f, 0);
            else if(isAiming)
                handsAnimator.CrossFadeInFixedTime(CrouchAimAnim, 0.1f, 0);
            else
                handsAnimator.CrossFadeInFixedTime(CrouchIdleAnim, 0.1f, 0);
        }

        protected virtual void standup()
        {
            if (!IsDeployed)
                return;

            if (isReloading)
                return;

            if (!CanControl)
                return;

            if (AmmoCurrent == 0)
            {
                handsAnimator.SetFloat(EmptyParam, 1f);
            }
            else
            {
                handsAnimator.SetFloat(EmptyParam, 0f);
            }

            if (CentralAim && isAiming && lastInputData.MouseSecondHold)
                handsAnimator.CrossFadeInFixedTime(StandUpCentralAnim, 0.1f, 0);
            else if (isAiming)
                handsAnimator.CrossFadeInFixedTime(StandUpAimAnim, 0.1f, 0);
            else
                handsAnimator.CrossFadeInFixedTime(StandUpIdleAnim, 0.1f, 0);
        }

        protected override BaseItem createHandsItem()
        {
            return new PistolItem("Item_Pistol");
        }

        void resurrectEvent()
        {
            if(IsDeployed)
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

            if (IsReloading)
                return;

            AnimatorStateInfo currentState = handsAnimator.GetCurrentAnimatorStateInfo(0);

            if(currentState.IsName("Idle"))
            {
                handsAnimator.Play(damageIdleAnim);
            } else if((currentState.IsName("Aim") || currentState.IsName("AimInAim")) && currentState.normalizedTime >= 0.6f)
            {
                handsAnimator.Play(damageAimAnim);
            }
        }

        protected virtual void jumpStart()
        {
            if (!IsDeployed)
                return;

            if (AmmoCurrent == 0)
            {
                handsAnimator.SetFloat(EmptyParam, 1f);
            }
            else
            {
                handsAnimator.SetFloat(EmptyParam, 0f);
            }

            if (!isReloading)
            {
                handsAnimator.SetBool("JumpFall", false);
                handsAnimator.SetTrigger("Jump");
            }
        }

        protected virtual void jumpEnd()
        {
            if (!IsDeployed)
                return;
            if (isReloading)
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

        public string ShootSound
        {
            get
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

        public bool IsReloading
        {
            get
            {
                return isReloading;
            }
        }

        public float currentAimFov
        {
            get
            {
                return CentralAim ? aimFovCentral : aimFov;
            }
        }

        public string CrouchCentralAnim = "PistolCentralCrouch";
        public string CrouchAimAnim = "PistolAimCrouch";
        public string CrouchIdleAnim = "PistolIdleCrouch";
        public string StandUpCentralAnim = "PistolCentralStandUp";
        public string StandUpAimAnim = "PistolAimStandUp";
        public string StandUpIdleAnim = "PistolIdleStandUp";

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
            if (controller.IsFreezed || !canControl) //|| !controller.IsGrounded)
                return;

            base.Update(deltaTime);

            bool nowIsCentralController = handsAnimator.runtimeAnimatorController == ControllerCentral;

            if (CentralAim != nowIsCentralController)
            {
                handsAnimator.runtimeAnimatorController = CentralAim ? ControllerCentral : ControllerDefault;
                if (isDeployed)
                {
                    handsAnimator.Play(deployAnim, 0, 1);
                }
            }

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

            if (Auto)
            {
                if (lastInputData.MouseHold && AmmoCurrent > 0)
                {
                    nextFireTime += Time.deltaTime;
                    if (nextFireTime >= autoFireRate)
                    {
                        fire();
                    }
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

                if (AmmoCurrent == 0)
                {
                    handsAnimator.SetFloat(EmptyParam, 1f);
                }
                else
                {
                    handsAnimator.SetFloat(EmptyParam, 0f);
                }

                handsAnimator.Play(firstTimeAnim);
                SetDeployed(true);

                propsRoot.SetActive(true);
            }
            else
            {
                base.Deploy();

                if (AmmoCurrent == 0)
                {
                    handsAnimator.SetFloat(EmptyParam, 1f);
                }
                else
                {
                    handsAnimator.SetFloat(EmptyParam, 0f);
                }

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

            if (AmmoCurrent == 0)
            {
                handsAnimator.SetFloat(EmptyParam, 1f);
            }
            else
            {
                handsAnimator.SetFloat(EmptyParam, 0f);
            }
        }

        public override void SetDeployed(bool value)
        {
            base.SetDeployed(value);
            handsAnimator.Play(lowposeAnim, 1);
            if (LockControlOnDeploy)
                canControl = false;
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
            if (AmmoCurrent == 0)
            {
                handsAnimator.SetFloat(EmptyParam, 1f);
            }
            else
            {
                handsAnimator.SetFloat(EmptyParam, 0f);
            }
            isAiming = false;
            isReloading = false;
        }

        protected override void inputEvent(InputEventType et)
        {
            //if (!controller.IsGrounded)
            //return;
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


            if (AmmoCurrent == 0)
            {
                handsAnimator.SetFloat(EmptyParam, 1f);

                if (LongReload)
                {
                    handsAnimator.CrossFadeInFixedTime(reloadLongAnim, 0.1f, 0);
                }
                else
                {
                    handsAnimator.CrossFadeInFixedTime(reloadAnim, 0.1f, 0);
                }

                DisableVisualBullets();
            }
            else
            {
                handsAnimator.SetFloat(EmptyParam, 0f);

                if (LongReload)
                {
                    if (BackReload)
                    {
                        handsAnimator.CrossFadeInFixedTime(reloadLongBackAnim, 0.1f, 0);
                    }
                    else
                    {
                        handsAnimator.CrossFadeInFixedTime(reloadLongAnim, 0.1f, 0);
                    }
                }
                else
                {
                    if (BackReload)
                    {
                        handsAnimator.CrossFadeInFixedTime(reloadBackAnim, 0.1f, 0);
                    }
                    else
                    {
                        handsAnimator.CrossFadeInFixedTime(reloadAnim, 0.1f, 0);
                    }
                }
                EnableVisualBullets();
            }

            fromUpgrade(false);
            fromAim(false);
            isReloading = true;
        }

        protected virtual void fire()
        {
            if (isReloading || !isDeployed || controller.IsFreezed)
                return;

            if (AmmoCurrent == 0)
            {
                if (isAiming && lastInputData.MouseSecondHold)
                {
                    handsAnimator.Play(shotAnim, 0, 0);
                }
                else
                {
                    handsAnimator.CrossFadeInFixedTime(shotWithoutAimAnim, 0.05f, 0);
                }
            }
            else
            {
                if (isAiming && lastInputData.MouseSecondHold)
                {
                    if (AmmoCurrent == 1)
                    {
                        handsAnimator.Play(lastShotAnim, 0, 0);
                    }
                    else
                    {
                        handsAnimator.Play(shotAnim, 0, 0);
                    }
                }
                else
                {
                    if (AmmoCurrent == 1)
                    {
                        handsAnimator.CrossFadeInFixedTime(lastShotWithoutAimAnim, 0.05f, 0);
                    }
                    else
                    {
                        handsAnimator.CrossFadeInFixedTime(shotWithoutAimAnim, 0.05f, 0);
                    }
                }

                currentRecoil += getRandomVector(RecoilAmountMin, RecoilAmountMax) * (isAiming ? AimRecoilMultiplier : 1f);
                recoilTime = 0;
                gunShotInstantiate();
                emitShell();
                emitHitEffect();
                AmmoCurrent--;
            }

            if (AmmoCurrent == 0)
            {
                handsAnimator.SetFloat(EmptyParam, 1f);
            }
            else
            {
                handsAnimator.SetFloat(EmptyParam, 0f);
            }

            if (CentralAim && isAiming)
                isAiming = true;

            if (!isAiming)
            {
                isAiming = true;
            }
            fromAimTime = Time.time + fromAimDelay;

            nextFireTime = 0;
        }

        void emitHitEffect()
        {
            Ray r = new Ray(FirePoint.position, FirePoint.forward);
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

        void emitShell()
        {
            Rigidbody shellInstance = GameObject.Instantiate(shell, shell.transform.position, shell.transform.rotation);

            shellInstance.gameObject.SetActive(true);
            shellInstance.transform.SetParent(null);
            shellInstance.linearVelocity = controller.PlayerVelocity;
            shellInstance.AddForce(shellForceDir.forward * Random.Range(shellForceMin, shellForceMax));
            shellInstance.AddTorque(shellTorqueDir.forward * shellTorque);
            shellInstance.maxAngularVelocity = 65;
        }

        protected virtual void aim()
        {
            if (isReloading || !isDeployed || controller.IsFreezed)
                return;

            toIdleElapsedTime = 0;
            if (isAiming)
            {
                needSmoothInAim = true;
                if (AmmoCurrent == 0)
                {
                    handsAnimator.SetFloat(EmptyParam, 1f);
                }
                else
                {
                    handsAnimator.SetFloat(EmptyParam, 0f);
                }

                fromUpgrade(false);
                if (CentralAim)
                {
                    handsAnimator.Play(basicToCentralAimAnim, 0, 0);
                }
                else
                {
                    handsAnimator.Play(aimInAimAnim, 0, 0);
                }
            }
            else
            {
                if (AmmoCurrent == 0)
                {
                    handsAnimator.SetFloat(EmptyParam, 1f);
                }
                else
                {
                    handsAnimator.SetFloat(EmptyParam, 0f);
                }

                if (handsAnimator.GetCurrentAnimatorStateInfo(0).IsName(shotWithoutAimAnim))
                    handsAnimator.CrossFadeInFixedTime(aimAnim, 0.25f, 0, 0.4f);
                else
                    handsAnimator.CrossFadeInFixedTime(aimAnim, 0.25f, 0);

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
                    if (Time.time >= fromAimTime && fromAimTime >= 0)
                    {
                        if (withAnim)
                        {
                            if (AmmoCurrent == 0)
                            {
                                handsAnimator.SetFloat(EmptyParam, 1f);
                            }
                            else
                            {
                                handsAnimator.SetFloat(EmptyParam, 0f);
                            }
                            handsAnimator.CrossFadeInFixedTime(fromAimToIdleAnim, 0.25f, 0);
                        }
                        fromAimTime = -1;
                        isAiming = false;
                    }
                    else
                    {
                        fromAimTime = Time.time + fromAimDelay;

                        if(withAnim)
                            playFromAimAnim();
                    }
                }
                else
                {
                    if (Time.time >= fromAimTime && fromAimTime >= 0)
                    {
                        if (withAnim)
                        {
                            playFromAimAnim();
                        }
                        fromAimTime = -1;
                        isAiming = false;
                    }
                    else
                    {
                        fromAimTime = Time.time + fromAimDelay;

                        if (withAnim)
                            handsAnimator.Play(aimFromAimAnim, 0, 0);
                    }
                }
            }
            if (LowPoseInAiming)
            {
                needLowPose = false;
            }
        }

        protected virtual void playFromAimAnim()
        {
            if (AmmoCurrent == 0)
            {
                handsAnimator.SetFloat(EmptyParam, 1f);
            }
            else
            {
                handsAnimator.SetFloat(EmptyParam, 0f);
            }

            if (CentralAim)
            {
                handsAnimator.CrossFadeInFixedTime(CentralToBasicAnim, 0.25f, 0);
            }
            else
            {
                handsAnimator.CrossFadeInFixedTime(fromAimAnim, 0.25f, 0);
            }
        }

        protected virtual void aimUpdate()
        {

        }

        protected virtual void toUpgrade()
        {
            if (isReloading)
                return;

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
            {
                handsAnimator.CrossFadeInFixedTime(fromUpgradeAnim, 0.25f, 0);
                isAiming = false;
            }

            controller.Freeze(false);
        }

        public void Reloaded()
        {
            int needToReload = AmmoMax - AmmoCurrent;
            int willBeReload = ammoItem.Consume(needToReload);

            AmmoCurrent += willBeReload;

            isReloading = false;
        }

        public void ReloadEnd()
        {
            if (AmmoCurrent == 0)
            {
                handsAnimator.SetFloat(EmptyParam, 1f);
            }
            else
            {
                handsAnimator.SetFloat(EmptyParam, 0f);
            }
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

        public Sprite GetCurrentModeSprite()
        {
            return ShootModeSprite;
        }

        public BaseItem GetAmmoItem()
        {
            return ammoItem;
        }
    }

    public class PistolItem : BaseItem
    {
        public PistolItem(string itemName) : base(itemName)
        {
            MaxCapacity = 1;
        }
    }

    public class PistolAmmoItem : BaseItem
    {
        public PistolAmmoItem(string itemName) : base(itemName)
        {
            MaxCapacity = 120;
            IconName = "PistolAmmo";
        }
    }
}
