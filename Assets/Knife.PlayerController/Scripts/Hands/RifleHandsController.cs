using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace KnifePlayerController
{
    [System.Serializable]
    public class RifleHandsController : HandsController, IWeaponAmmo, IWeaponShootMode
    {
        public float CameraFov
        {
            get
            {
                return cameraFov;
            }
        }

        public enum ShootingMode
        {
            Once,
            SemiAuto,
            Auto
        }

        [Header("Ammo")]
        public int AmmoCurrent = 20;
        public int AmmoMax = 20;
        public int AmmoCapacity
        {
            get
            {
                return ammoItem.GetCurrentCount();
            }
        }

        [Header("Options")]
        public bool CentralAim = false;
        public bool BackReload = false;
        public bool LowPoseInAiming = false;
        public bool TopPoseInAiming = false;
        public bool Aim4X = false;
        public bool Aim12X = false;
        public bool Collimator = false;
        public bool Silence = false;

        [Header("Shooting")]
        public ShootingMode ShootMode = ShootingMode.Auto;
        public int SemiAutoBurst = 3;
        public float SemiAutoInterval = 0.5f;

        [Header("Poses blending")]
        public float LowPoseWeightTarget = 0.08f;
        public float LowPoseWeightBlendSpeed = 0.2f;
        public float TopPoseWeightTarget = 0.08f;
        public float TopPoseWeightZoomedAim4XTarget = 0.22f;
        public float TopPoseWeightZoomedAim12XTarget = 0.22f;
        public float TopPoseWeightBlendSpeed = 0.2f;

        [Header("Fire")]
        public float DamageAmount = 10;
        public LayerMask FireCollisionLayer;
        public Transform FirePoint;
        public Transform FirePointAim4X;
        public Transform FirePointAim12X;
        public Transform FirePointCollimator;
        public GameObject GunHitDecalPrefab;
        public Vector2 RecoilAmountMin = new Vector2(0.3f, 1f);
        public Vector2 RecoilAmountMax = new Vector2(-0.3f, 1f);
        public AnimationCurve RecoilCurve;
        public float RecoilFadeOutTime = 0.3f;
        public float AimRecoilMultiplier = 0.3f;
        public float autoFireRate = 0.1f;

        [Header("Aiming")]
        public LerpToRoot HandsLerp;
        public float HandsLerpMultiplier = 1.5f;
        public float HeadbobWeightInAim = 0.25f;
        public float AimingNoise;
        public TransformNoise CameraNoise;
        public float SensivityInAimInZoom = 0.2f;
        public float SensivityInAimInZoom12X = 0.05f;
        public float SensivityInAim = 0.7f;
        public float DofSmoothSpeed = 17.5f;
        public PostProcessingController.DepthOfFieldSettings AimingDof;
        public PostProcessingController.DepthOfFieldSettings AimingZoomDof;

        [Header("Death props")]
        public Rigidbody PropsBody;
        public Collider[] PropsCollision;
        public float DetachPropsForce;
        public Transform DetachPropsDirection;

        TransformStateSetupper propsTransformState;
        float recoilTime = 0;
        Vector2 currentRecoil = new Vector2();

        [Header("Animations")]
        public float RiflePoseBlendSpeed = 2f;
        public float RifleEmptyBlendSpeed = 6f;
        public float FovSmoothSpeed = 20f;
        public float FromAimDelay = 15f;
        public float DefaultFov = 60f;
        public float AimFov = 35f;
        public float AimFovCentral = 16f;
        public string FovParameter = "FOV";
        public string FirstTimeAnim = "RifleFirstDeploy";
        public string ReloadAnim = "RifleReload";

        [Header("Aim")]
        public string AimAnim = "RifleAimBasic";
        public string FromAimAnim = "RifleCentralReAimToIdle";
        public string AimInAimAnim = "RifleAimInAimBasic";
        public string AimCentralAnimFromIdle = "RifleCentralAimFromIdle";
        public string FromCentralAimToIdleAnim = "RifleCentralAimToIdle";
        public string AimCentralAnimFromBasicAim = "RifleCentralAimFromBasicAim";
        public string FromCentralAimToBasicAimAnim = "RifleCentralAimToBasicAim";
        public string LowPoseAnim = "RifleLowPose";
        public string TopPoseAnim = "RifleTopPose";

        [Header("Upgrade")]
        public string ToUpgradeAnim = "RifleUpgradeStart";
        public string FromUpgradeAnim = "RifleUpgradeEnd";
        public string UpgradeIdleAnim = "RifleUpgradeIdle";

        [Header("Shots")]
        public string ShotAnim = "RifleAimBasicShot";
        public string LastShotAnim = "RifleAimBasicLastShot";
        public string ModeSwitchAnimation = "RifleSwitch";

        [Header("Jump")]
        public string JumpAnim = "RifleJump";
        public string Landing = "RifleLanding";

        [Header("Crouch")]
        public string CrouchAnim = "RifleCrouch";
        public string StandUpAnim = "RifleStandUp";

        [Header("Damage")]
        public string DamageAnim = "RifleDamage";
        public string[] StatesForDamage;

        [Header("4X Aim Blender")]
        public Material AimLensMaterial;
        public string SpecularColorProperty = "_SpecularColor";
        public string EmissionColorProperty = "_EmissionColor";
        public Color SpecularDefault;
        public Color SpecularZoomed;
        public Color EmissionDefault;
        public Color EmissionZoomed;
        public float MinAngleFromCamera = 0f;
        public float MaxAngleFromCamera = 45f;
        public Transform AimForwardReference;
        public Transform CameraForwardReference;

        [Header("Common")]
        public Sprite[] ShootModeSprites;
        public GameObject Gunshot;
        public GameObject GunshotSilencer;
        public float GunShotLiveTime = 0.1f;
        public PlayerController Controller;
        public WeaponCustomization Customization;
        public MeshRenderer Sphere;
        public Rigidbody Shell;
        public Transform ShellForceDir;
        public float ShellForceMin = 10;
        public float ShellForceMax = 15;
        public float ShellRandomAngleMin = -5;
        public float ShellRandomAngleMax = 5;
        public Transform ShellTorqueDir;
        public float ShellTorque = 600f;

        [Header("Sounds")]
        public string DefaultShoot = "RifleShoot";
        public string SilenceShoot = "RifleShootSilence";
        public string DefaultLastShoot = "RifleShoot";
        public string SilenceLastShoot = "RifleShootSilence";

        public bool IsAiming
        {
            get
            {
                return isAiming;
            }
        }

        float cameraFov = 60f;

        public GameObject propsRoot;
        public GameObject propsRootFirstDeploy;
        public string EmptyParam = "Empty";
        public string PoseParam = "RiflePose";

        protected bool firstTimeDeploy;

        bool isAiming = false;
        bool isIdle = true;
        bool isReloading = false;
        bool isUpgrading = false;

        bool needSmoothInAim = false;
        bool needOtherPose = false;

        float toIdleElapsedTime = 0;
        float fromAimTime = 0;

        float currentOtherPoseWeight = 0;
        float nextFireTime;
        float nextBurstTime;
        int burstCount = 0;

        bool flashLightIsOn = false;
        Light flashlightLight;
        MeshRenderer flashLightVolumetric;

        Queue<GameObject> gunHitDecalPool = new Queue<GameObject>();
        int poolSize = 100;
        float hitDecalLifetime = 20f;
        float riflePose;

        float emptyValue = 0;
        float emptyTargetValue = 0;

        Rigidbody clonedPropsBody;

        RifleAmmoItem ammoItem;

        // blend Rifle pose (0 - idle, 1 - basic aim, 2 - central aim)
        int targetRiflePose;

        float topPoseTarget
        {
            get
            {
                if (Aim12X)
                    return TopPoseWeightZoomedAim12XTarget;

                if (Aim4X)
                    return TopPoseWeightZoomedAim4XTarget;

                return TopPoseWeightTarget;
            }
        }

        public override void Init(GameObject root)
        {
            base.Init(root);
            firstTimeDeploy = true;

            propsTransformState = PropsBody.GetComponent<TransformStateSetupper>();

            initPool();
            Controller.JumpStartEvent += jumpStart;
            Controller.JumpEndEvent += jumpEnd;

            Controller.CrouchEvent += crouch;
            Controller.StandUpEvent += standup;

            Controller.DamageHandler.DamagedEvent.AddListener(damaged);
            Controller.DamageHandler.ResurrectEvent.AddListener(resurrectEvent);

            if (!propsTransformState.CalculateVelocity)
            {
                Debug.LogWarning("Weapon velocity calculation disabled, it's enables automatically", PropsBody);
                propsTransformState.CalculateVelocity = true;
            }

            ammoItem = new RifleAmmoItem("Item_Rifle_Ammo");
            playerInventory.AddItem(ammoItem);
            Controller.PlayerFreezeChanged.AddListener(playerFreezeChanged);
        }

        private void playerFreezeChanged(bool isFreezed)
        {
            if (!isFreezed)
            {
                if (isUpgrading)
                {
                    Controller.Freeze(true);
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
            if (!isDeployed)
                return;

            if (isReloading)
                return;

            if (AmmoCurrent == 0)
            {
                emptyTargetValue = 1f;
            }
            else
            {
                emptyTargetValue = 0f;
            }

            AnimatorStateInfo currentState = handsAnimator.GetCurrentAnimatorStateInfo(0);
            foreach(string s in StatesForDamage)
            {
                if(currentState.IsName(s))
                {
                    handsAnimator.Play(DamageAnim);
                    break;
                }
            }
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
                emptyTargetValue = 1f;
            }
            else
            {
                emptyTargetValue = 0f;
            }

            handsAnimator.CrossFadeInFixedTime(CrouchAnim, 0.1f, 0);
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
                emptyTargetValue = 1f;
            }
            else
            {
                emptyTargetValue = 0f;
            }

            handsAnimator.CrossFadeInFixedTime(StandUpAnim, 0.1f, 0);
        }

        public void SetAiming(bool onlyBasic = true, bool aim4X = false, bool aim12X = false, bool collimator = false)
        {
            if(onlyBasic)
            {
                TopPoseInAiming = false;
                CentralAim = false;
            }
            else
            {
                TopPoseInAiming = true;
                CentralAim = true;
            }

            if (aim12X)
            {
                Aim4X = false;
                Aim12X = true;
                Collimator = false;
            }
            else if (aim4X)
            {
                Aim4X = true;
                Aim12X = false;
                Collimator = false;
            }
            else if(collimator)
            {
                Collimator = true;
                Aim4X = false;
                Aim12X = false;
            }
            else
            {
                Collimator = false;
                Aim4X = false;
                Aim12X = false;
            }
        }

        protected virtual void jumpStart()
        {
            if (!IsDeployed || !canControl)
                return;

            if (AmmoCurrent == 0)
            {
                emptyTargetValue = 1f;
            }
            else
            {
                emptyTargetValue = 0f;
            }

            if (!isReloading)
            {
                handsAnimator.SetBool("JumpFall", false);
                handsAnimator.CrossFadeInFixedTime(JumpAnim, 0.1f, 0);
            }
        }

        protected virtual void jumpEnd()
        {
            if (!IsDeployed)
                return;
            if (isReloading || !canControl)
                return;

            if (AmmoCurrent == 0)
            {
                emptyTargetValue = 1f;
            }
            else
            {
                emptyTargetValue = 0f;
            }

            handsAnimator.SetBool("JumpFall", true);
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

        public string LastShootSound
        {
            get
            {
                if (Silence)
                {
                    return SilenceLastShoot;
                }
                else
                {
                    return DefaultLastShoot;
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
                return CentralAim ? AimFovCentral : AimFov;
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

        void changeShootMode()
        {
            if (IsReloading)
                return;

            if (AmmoCurrent == 0)
            {
                emptyTargetValue = 1f;
            }
            else
            {
                emptyTargetValue = 0f;
            }
            emptyValue = emptyTargetValue;

            handsAnimator.CrossFadeInFixedTime(ModeSwitchAnimation, 0.1f, 0);
            int shootMode = (int)ShootMode;
            shootMode++;
            if (shootMode > 2)
            {
                shootMode = 0;
            }
            ShootMode = (ShootingMode)shootMode;
        }

        public override void Update(float deltaTime)
        {
            if (isUpgrading)
                fromAimTime = Time.time + FromAimDelay;

            if (Controller.IsFreezed || !IsDeployed)
                return;

            base.Update(deltaTime);

            // blend scope lens material (can view only if in center)

            Vector3 forwardDirection = AimForwardReference.position - CameraForwardReference.position;
            forwardDirection.Normalize();

            float angle = Vector3.Angle(CameraForwardReference.forward, forwardDirection);

            float angleFraction = angle / MaxAngleFromCamera;

            angleFraction = Mathf.Clamp01(angleFraction);
            Color specularColor = Color.Lerp(SpecularDefault, SpecularZoomed, 1f - angleFraction);
            Color emissionColor = Color.Lerp(EmissionDefault, EmissionZoomed, 1f - angleFraction);
            AimLensMaterial.SetColor(SpecularColorProperty, specularColor);
            AimLensMaterial.SetColor(EmissionColorProperty, emissionColor);

            if (isAiming)
            {
                if (CentralAim)
                {
                    targetRiflePose = 2;
                }
                else
                {
                    targetRiflePose = 1;
                }
            }
            else
            {
                if (isIdle)
                {
                    targetRiflePose = 0;
                }
                else
                {
                    targetRiflePose = 1;
                }
            }

            // shoot mode chagind
            if (canControl)
            {
                if (Input.GetKeyDown(KeyCode.X))
                {
                    changeShootMode();
                }
            }

            // blend Rifle pose only if no transitions and root is active self (deployed)
            AnimatorStateInfo nextState = handsAnimator.GetNextAnimatorStateInfo(0);
            if (nextState.fullPathHash == 0) // has't transition
            {
                riflePose = Mathf.MoveTowards(riflePose, targetRiflePose, RiflePoseBlendSpeed * deltaTime);
                handsAnimator.SetFloat(PoseParam, riflePose);
            }

            if (propsRoot.activeSelf)
            {
                emptyValue = Mathf.MoveTowards(emptyValue, emptyTargetValue, RifleEmptyBlendSpeed * deltaTime);
                handsAnimator.SetFloat(EmptyParam, emptyValue);
            }

            // if aiming (or just basic aim)
            if (isAiming)
            {
                // if aiming by fov
                if (lastInputData.MouseSecondHold)
                {
                    CameraNoise.NoiseAmount = Mathf.MoveTowards(CameraNoise.NoiseAmount, AimingNoise, deltaTime * 5f);
                    fromAimTime = Time.time + FromAimDelay;
                    cameraFov = Mathf.Lerp(cameraFov, Mathf.Lerp(DefaultFov, currentAimFov, handsAnimator.GetFloat(FovParameter)), deltaTime * FovSmoothSpeed);

                    if (Aim4X || Aim12X)
                    {
                        Controller.PPController.LerpDof(Controller.PPController.DofSettings, AimingZoomDof, DofSmoothSpeed * deltaTime);
                    }
                    else
                    {
                        Controller.PPController.LerpDof(Controller.PPController.DofSettings, AimingDof, DofSmoothSpeed * deltaTime);
                    }
                }
                // else basic aiming
                else
                {
                    // go to idle if run or by timer
                    if (Input.GetKey(KeyCode.LeftShift))
                    {
                        fromAimTime = 0;
                    }
                    toIdleElapsedTime = 0;
                    if (Time.time >= fromAimTime && fromAimTime >= 0)
                    {
                        fromAim();
                    }
                    cameraFov = Mathf.Lerp(cameraFov, DefaultFov, deltaTime * FovSmoothSpeed);
                    Controller.PPController.LerpDof(Controller.PPController.DofSettings, Controller.DefaultDofSettings, DofSmoothSpeed * deltaTime);
                }
            }
            else
            {
                Controller.PPController.LerpDof(Controller.PPController.DofSettings, Controller.DefaultDofSettings, DofSmoothSpeed * deltaTime);

                if (isIdle)
                {
                }
                else
                {
                    if (Input.GetKey(KeyCode.LeftShift) && !Input.GetMouseButton(0))
                    {
                        fromAimTime = 0;
                    }
                    toIdleElapsedTime = 0;
                    if (Time.time >= fromAimTime && fromAimTime >= 0)
                    {
                        fromAim();
                    }

                }
                cameraFov = Mathf.Lerp(cameraFov, DefaultFov, deltaTime * FovSmoothSpeed);
            }

            // auto shooting and semiauto shooting
            if(canControl)
            {
                if (ShootMode == ShootingMode.Auto)
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
                else if (ShootMode == ShootingMode.SemiAuto)
                {
                    if (AmmoCurrent == 0)
                    {
                        burstCount = 0;
                    }
                    else
                    {
                        if (burstCount > 0)
                        {
                            nextFireTime += Time.deltaTime;
                            if (nextFireTime >= autoFireRate)
                            {
                                fire();
                            }
                        }
                        else
                        {
                            if (lastInputData.MouseHold)
                            {
                                nextBurstTime += Time.deltaTime;
                                if (nextBurstTime >= SemiAutoInterval)
                                {
                                    fire();
                                }
                            }
                        }
                    }
                }
            }

            // recoil animation
            recoilTime += Time.deltaTime;
            float recoilFraction = recoilTime / RecoilFadeOutTime;
            float recoilValue = RecoilCurve.Evaluate(recoilFraction);

            currentRecoil = Vector2.Lerp(Vector2.zero, currentRecoil, recoilValue);

            Vector2 recoilResult = currentRecoil;
            Controller.Look.LookRotation(recoilResult.x, recoilResult.y, deltaTime);

            // blend pose (for aiming)
            if (IsDeployed)
            {
                if (LowPoseInAiming)
                {
                    currentOtherPoseWeight = Mathf.MoveTowards(currentOtherPoseWeight, (needOtherPose && isDeployed) ? LowPoseWeightTarget : 0, LowPoseWeightBlendSpeed * deltaTime);
                    handsAnimator.SetLayerWeight(1, currentOtherPoseWeight);
                }
                else if(TopPoseInAiming)
                {
                    currentOtherPoseWeight = Mathf.MoveTowards(currentOtherPoseWeight, (needOtherPose && isDeployed) ? topPoseTarget : 0, TopPoseWeightBlendSpeed * deltaTime);
                    handsAnimator.SetLayerWeight(1, currentOtherPoseWeight);
                }
            }
        }

        public void FirstDeployEnd()
        {
            propsRoot.SetActive(true);
            propsRootFirstDeploy.SetActive(false);
        }

        public override void Deploy()
        {
            if (Controller.IsFreezed)
                return;

            if (LockControlOnDeploy)
                canControl = false;

            lastInputData = default(HandsControllerInput);
            currentInputData = default(HandsControllerInput);
            fromAimTime = 0;
            isAiming = false;
            isIdle = true;
            riflePose = 0;
            if (firstTimeDeploy)
            {
                if (isDeployed)
                    return;

                if (AmmoCurrent == 0)
                {
                    emptyTargetValue = 1f;
                }
                else
                {
                    emptyTargetValue = 0f;
                }

                handsAnimator.Play(FirstTimeAnim);
                SetDeployed(true);

                propsRoot.SetActive(false);
                propsRootFirstDeploy.SetActive(true);
            }
            else
            {
                base.Deploy();

                if (AmmoCurrent == 0)
                {
                    emptyTargetValue = 1f;
                }
                else
                {
                    emptyTargetValue = 0f;
                }

                propsRootFirstDeploy.SetActive(false);
                propsRoot.SetActive(true);
            }
        }

        public override void HideProps()
        {
            propsRoot.SetActive(false);
            propsRootFirstDeploy.SetActive(false);
        }

        public override void ShowProps()
        {
            if (firstTimeDeploy)
            {
                propsRoot.SetActive(false);
                propsRootFirstDeploy.SetActive(true);
            }
            else
            {
                propsRoot.SetActive(true);
                propsRootFirstDeploy.SetActive(false);
            }
        }

        public override void SetDeployed(bool value)
        {
            base.SetDeployed(value);
            if (LowPoseInAiming)
            {
                handsAnimator.Play(LowPoseAnim, 1);
            }
            if (TopPoseInAiming)
            {
                handsAnimator.Play(TopPoseAnim, 1);
            }
            if (LockControlOnDeploy)
                canControl = false;
        }

        public override void Hide()
        {
            if (!isDeployed || Controller.IsFreezed)
                return;

            base.Hide();

            firstTimeDeploy = false;

            toIdleElapsedTime = 0;
            if (AmmoCurrent == 0)
            {
                emptyTargetValue = 1f;
            }
            else
            {
                emptyTargetValue = 0f;
            }
            isAiming = false;
            isReloading = false;

            Controller.SetSensivityMultiplier(1);
        }

        protected override void inputEvent(InputEventType et)
        {
            //if (!controller.IsGrounded)
            //return;
            if (!propsRoot.activeSelf)
                return;

            base.inputEvent(et);

            if (!canControl)
                return;

            if (et == InputEventType.Reload)
            {
                reload();
            }

            if (et == InputEventType.MouseDown)
            {
                if (IsReloading)
                    return;

                if (AmmoCurrent == 0)
                {
                    emptyTargetValue = 1f;
                }
                else
                {
                    emptyTargetValue = 0f;
                }
                emptyValue = emptyTargetValue;

                fire();
            }

            if (et == InputEventType.MouseHold)
            {

            }

            if (et == InputEventType.MouseSecondDown)
            {
                if (IsReloading)
                    return;

                if (AmmoCurrent == 0)
                {
                    emptyTargetValue = 1f;
                }
                else
                {
                    emptyTargetValue = 0f;
                }
                emptyValue = emptyTargetValue;

                aim();
            }

            if (et == InputEventType.MouseSecondHold)
            {
                if (IsReloading)
                    return;

                if (AmmoCurrent == 0)
                {
                    emptyTargetValue = 1f;
                }
                else
                {
                    emptyTargetValue = 0f;
                }
                emptyValue = emptyTargetValue;

                aimUpdate();
            }

            if (et == InputEventType.MouseSecondUp)
            {
                if (IsReloading)
                    return;

                if (AmmoCurrent == 0)
                {
                    emptyTargetValue = 1f;
                }
                else
                {
                    emptyTargetValue = 0f;
                }
                emptyValue = emptyTargetValue;

                fromAim();
            }

            if (et == InputEventType.Upgrade)
            {
                if (IsReloading)
                    return;

                if (AmmoCurrent == 0)
                {
                    emptyTargetValue = 1f;
                }
                else
                {
                    emptyTargetValue = 0f;
                }
                emptyValue = emptyTargetValue;

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

        protected virtual void reload()
        {
            if (isReloading || !isDeployed || Controller.IsFreezed)
                return;

            if (AmmoCurrent == AmmoMax || AmmoCapacity == 0)
                return;

            if (AmmoCurrent == 0)
            {
                emptyTargetValue = 1f;
            }
            else
            {
                emptyTargetValue = 0f;
            }
            handsAnimator.CrossFadeInFixedTime(ReloadAnim, 0.1f, 0);

            fromUpgrade(false);
            fromAim(false);
            isIdle = false;
            isReloading = true;
            fromAimTime = Time.time + FromAimDelay;
        }

        protected virtual void fire()
        {
            if (isReloading || !isDeployed || Controller.IsFreezed)
                return;

            if (isAiming)
            {
                if (CentralAim)
                {
                    targetRiflePose = 2;
                }
                else
                {
                    targetRiflePose = 1;
                }
            }
            else
            {
                if (isIdle)
                {
                    targetRiflePose = 0;
                }
                else
                {
                    targetRiflePose = 1;
                }
            }
            if (riflePose != targetRiflePose)
            {
                riflePose = targetRiflePose;
                handsAnimator.SetFloat(PoseParam, riflePose);
            }

            isIdle = false;
            if (AmmoCurrent == 1)
            {
                handsAnimator.Play(LastShotAnim, 0, 0);
            }
            else
            {
                handsAnimator.Play(ShotAnim, 0, 0);
            }

            if (AmmoCurrent > 0)
            {
                currentRecoil += getRandomVector(RecoilAmountMin, RecoilAmountMax) * (isAiming ? AimRecoilMultiplier : 1f);
                recoilTime = 0;
                gunShotInstantiate();
                emitShell();
                emitHitEffect();
                AmmoCurrent--;
            }

            if (AmmoCurrent == 0)
            {
                emptyTargetValue = 1f;
            }
            else
            {
                emptyTargetValue = 0f;
            }

            fromAimTime = Time.time + FromAimDelay;
            nextFireTime = 0;
            if(ShootMode == ShootingMode.SemiAuto)
            {
                if (burstCount == 0)
                {
                    nextBurstTime = 0;
                    burstCount = SemiAutoBurst - 1;
                }
                else
                {
                    burstCount--;
                }
            }
        }

        void emitHitEffect()
        {
            Vector3 position = FirePoint.position;
            Vector3 direction = FirePoint.forward;

            if (isAiming)
            {
                if (Aim4X)
                {
                    position = FirePointAim4X.position;
                    direction = FirePointAim4X.forward;
                }
                else if (Aim12X)
                {
                    position = FirePointAim12X.position;
                    direction = FirePointAim12X.forward;
                } else if(Collimator)
                {
                    position = FirePointCollimator.position;
                    direction = FirePointCollimator.forward;
                }
            }

            Ray r = new Ray(position, direction);
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
            GameObject gunshotPrefab = Silence ? GunshotSilencer : Gunshot;

            GameObject instance = GameObject.Instantiate(gunshotPrefab, gunshotPrefab.transform.position, gunshotPrefab.transform.rotation, gunshotPrefab.transform.parent);
            //instance.transform.rotation *= Quaternion.AngleAxis(Random.value * 360f, Vector3.forward);
            instance.SetActive(true);

            GameObject.Destroy(instance, GunShotLiveTime);
        }

        void emitShell()
        {
            Rigidbody shellInstance = GameObject.Instantiate(Shell, Shell.transform.position, Shell.transform.rotation);

            shellInstance.gameObject.SetActive(true);
            shellInstance.transform.SetParent(null);
            shellInstance.linearVelocity = Controller.PlayerVelocity;
            Quaternion randomRotation = Quaternion.Euler(Random.Range(ShellRandomAngleMin, ShellRandomAngleMax), Random.Range(ShellRandomAngleMin, ShellRandomAngleMax), Random.Range(ShellRandomAngleMin, ShellRandomAngleMax));
            shellInstance.AddForce(randomRotation * ShellForceDir.forward * Random.Range(ShellForceMin, ShellForceMax));
            shellInstance.AddTorque(ShellTorqueDir.forward * ShellTorque);
            shellInstance.maxAngularVelocity = 105;
        }

        protected virtual void aim()
        {
            if (isReloading || !isDeployed || Controller.IsFreezed)
                return;

            toIdleElapsedTime = 0;
            if (LowPoseInAiming)
            {
                handsAnimator.Play(LowPoseAnim, 1);
            }
            if (TopPoseInAiming)
            {
                handsAnimator.Play(TopPoseAnim, 1);
            }
            if (isAiming || (!isIdle && CentralAim))
            {
                needSmoothInAim = true;
                if (AmmoCurrent == 0)
                {
                    emptyTargetValue = 1f;
                }
                else
                {
                    emptyTargetValue = 0f;
                }
                fromUpgrade(false);

                if(CentralAim)
                {
                    //handsAnimator.CrossFadeInFixedTime(AimCentralAnimFromBasicAim, 0.075f, 0);
                    handsAnimator.Play(AimCentralAnimFromBasicAim, 0, 0);
                }
                else
                {
                    handsAnimator.CrossFadeInFixedTime(AimInAimAnim, 0.075f, 0);
                }

                isAiming = true;
            }
            else
            {
                if (AmmoCurrent == 0)
                {
                    emptyTargetValue = 1f;
                }
                else
                {
                    emptyTargetValue = 0f;
                }

                if (CentralAim)
                {
                    handsAnimator.CrossFadeInFixedTime(AimCentralAnimFromIdle, 0.075f, 0);
                }
                else
                {
                    handsAnimator.CrossFadeInFixedTime(AimAnim, 0.075f, 0);
                }

                fromAimTime = -1;
                isAiming = true;
                fromUpgrade(false);
            }
            isIdle = false;

            if (LowPoseInAiming || TopPoseInAiming)
            {
                needOtherPose = true;
            }

            HandsLerp.Multiplier = HandsLerpMultiplier;
            Controller.HandsHeadbobMultiplier = HeadbobWeightInAim;

            if (Aim4X)
            {
                Controller.SetSensivityMultiplier(SensivityInAimInZoom);
            }
            else if (Aim12X)
            {
                Controller.SetSensivityMultiplier(SensivityInAimInZoom12X);
            }
            else
            {
                Controller.SetSensivityMultiplier(SensivityInAim);
            }
        }

        protected virtual void fromAim(bool withAnim = true)
        {
            if (isReloading)
                return;

            HandsLerp.Multiplier = 1;
            toIdleElapsedTime = 0;
            if (isAiming || !isIdle)
            {
                needSmoothInAim = false;
                if (CentralAim)
                {
                    if (Time.time >= fromAimTime && fromAimTime >= 0)
                    {
                        if (withAnim)
                        {
                            if (isAiming)
                            {
                                handsAnimator.CrossFadeInFixedTime(FromCentralAimToIdleAnim, 0.075f, 0);
                            }
                            else
                            {
                                handsAnimator.CrossFadeInFixedTime(FromAimAnim, 0.075f, 0);
                            }
                        }
                        fromAimTime = -1;
                        isAiming = false;
                        isIdle = true;
                    }
                    else
                    {
                        if (withAnim)
                        {
                            handsAnimator.CrossFadeInFixedTime(FromCentralAimToBasicAimAnim, 0.075f, 0);
                        }
                        isAiming = false;
                        isIdle = false;
                    }
                }
                else
                {
                    if (Time.time >= fromAimTime && fromAimTime >= 0)
                    {
                        if (withAnim)
                        {
                            handsAnimator.CrossFadeInFixedTime(FromAimAnim, 0.075f, 0);
                        }
                        fromAimTime = -1;
                        isAiming = false;
                        isIdle = true;
                    }
                    else
                    {
                        fromAimTime = Time.time + FromAimDelay;

                        if(withAnim)
                            handsAnimator.CrossFadeInFixedTime(AimInAimAnim, 0.075f, 0);
                    }
                }
            }
            if (LowPoseInAiming || TopPoseInAiming)
            {
                needOtherPose = false;
            }
            Controller.HandsHeadbobMultiplier = 1f;
            Controller.SetSensivityMultiplier(1f);
        }

        protected virtual void aimUpdate()
        {

        }

        protected virtual void toUpgrade()
        {
            if (isReloading)
                return;

            Sphere.enabled = true;
            Customization.ShowModifications();
            isUpgrading = true;
            fromAim(false);
            handsAnimator.CrossFadeInFixedTime(ToUpgradeAnim, 0.25f, 0);
            Controller.Freeze(true);
        }

        protected virtual void fromUpgrade(bool withAnim = true)
        {
            if (isReloading)
                return;

            Sphere.enabled = false;
            Customization.CloseModifications();
            isUpgrading = false;

            if (withAnim)
                handsAnimator.CrossFadeInFixedTime(FromUpgradeAnim, 0.25f, 0);

            Controller.Freeze(false);
            isIdle = false;
        }

        public void Reloaded()
        {
            fromAimTime = Time.time + FromAimDelay;
            int needToReload = AmmoMax - AmmoCurrent;
            int willBeReload = ammoItem.Consume(needToReload);

            AmmoCurrent += willBeReload;

            isReloading = false;
        }

        public void ReloadEnd()
        {
            fromAimTime = Time.time + FromAimDelay;
            if (AmmoCurrent == 0)
            {
                emptyTargetValue = 1f;
            }
            else
            {
                emptyTargetValue = 0f;
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
            int shootMode = (int)ShootMode;

            return ShootModeSprites[shootMode];
        }

        protected override BaseItem createHandsItem()
        {
            return new RifleItem("Item_Rifle");
        }
    }

    public class RifleItem : BaseItem
    {
        public RifleItem(string itemName) : base(itemName)
        {
            MaxCapacity = 1;
        }
    }

    public class RifleAmmoItem : BaseItem
    {
        public RifleAmmoItem(string itemName) : base(itemName)
        {
            MaxCapacity = 120;
            IconName = "RifleAmmo";
        }
    }
}