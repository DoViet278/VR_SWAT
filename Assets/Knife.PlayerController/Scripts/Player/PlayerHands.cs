using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace KnifePlayerController
{
    public class PlayerHands : MonoBehaviour
    {
        public PistolHandsController PistolHands;
        public KnifeHandsController KnifeHands;
        public ShotgunHandsController ShotgunHands;
        public RifleHandsController RifleHands;
        public GrenadeHandsController GrenadeHands;

        public InjectorController Injector;
        PlayerInventory playerInventory;

        [Header("Parameters")]
        public string RunParam = "Run";

        [Header("Pistol Mods")]
        public int[] ModificatorsIdsForCentrailAim;

        public int[] ModificatorsIdsForBackReload;
        public int[] ModificatorsIdsForFlashlight;
        public int[] ModificatorsIdsForLowPose;
        public int[] ModificatorsIdsForLongReload;
        public int[] ModificatorsIdsForSilencer;

        [Header("SG Mods")]
        public int[] SGModificatorsIdsForCentrailAim;
        public int[] SGModificatorsIdsForFlashlight;
        public int[] SGModificatorsIdsForLowPose;
        public int[] SGModificatorsIdsForSilencer;
        public int[] SGModificatorsIdsForAddAmmo;

        [Header("Rifle Mods")]
        public int[] RifleModificatorsIdsForSilencer;
        public int[] RifleModificatorsIdsForBasicAim;

        public int[] RifleModificatorsIdsForCollimatorAim;
        public int[] RifleModificatorsIdsForZoomedAim;
        public int[] RifleModificatorsIdsForZoomed12XAim;

        [Header("Other")]
        public WeaponCustomization Customization;

        public float LowPoseWeightTarget = 0.08f;
        public float LowPoseWeightBlendSpeed = 0.2f;

        public float DefaultFov = 60f;
        public float RunFovAdd = 5f;
        public Camera TargetCamera;
        public PlayerController Controller;
        public float DofSmoothSpeed = 17.5f;

        public bool NeedAimShow
        {
            get
            {
                return !IsAiming;
            }
        }

        public AudioSource[] Sources;
        public string KnifeHitSoundName = "KnifeHit";
        public int KnifeHitsCount = 6;

        public HandsChangedEvent HandsChanged = new HandsChangedEvent();

        [SerializeField]
        Animator handsAnimator;

        Dictionary<string, AudioSource> sounds = new Dictionary<string, AudioSource>();
        HandsController.HandsControllerInput handsInput;

        float runFraction;

        List<HandsController> registeredHands = new List<HandsController>();

        HandsController lastHands = null;
        HandsController nextHands = null;

        HandsController deployedHandsBeforeInjection;

        public bool IsAiming
        {
            get
            {
                return (PistolHands.IsAiming && PistolHands.CentralAim) || (ShotgunHands.IsAiming && ShotgunHands.CentralAim) || (RifleHands.IsAiming && RifleHands.CentralAim);
            }
        }

        bool waitForOtherDeploy = false;

        void Awake()
        {
            handsAnimator = GetComponent<Animator>();

            Customization.ModificationEnd += modificationEnd;

            Controller.RunStartEvent += runStart;
            Controller.JumpStartEvent += jumpStart;
            Controller.JumpEndEvent += jumpEnd;

            Controller.CrouchEvent += crouch;
            Controller.StandUpEvent += standup;

            foreach (AudioSource s in Sources)
            {
                sounds.Add(s.name, s);
            }

            playerInventory = GetComponent<PlayerInventory>();
            PistolHands.Init(gameObject);
            PistolHands.SetCameraFov(60);
            KnifeHands.Init(gameObject);
            ShotgunHands.Init(gameObject);
            ShotgunHands.SetCameraFov(60);
            RifleHands.Init(gameObject);
            GrenadeHands.Init(gameObject);

            registeredHands.Add(PistolHands);
            registeredHands.Add(KnifeHands);
            registeredHands.Add(ShotgunHands);
            registeredHands.Add(RifleHands);
            registeredHands.Add(GrenadeHands);
            Injector.InjectionStartEvent.AddListener(injectionStart);
            Injector.InjectionEndEvent.AddListener(injectionEnd);

            foreach(HandsController hand in registeredHands)
            {
                var h = hand;
                hand.PickedUp.AddListener(() => handsPickedUp(h));
            }
        }

        public void PlayShotgunSound()
        {
            PlaySFX(ShotgunHands.ShotSound);
        }

        public void EnablePistolVisualBullets()
        {
            PistolHands.EnableVisualBullets();
        }

        public void DisablePistolVisualFirstTimeBullet()
        {
            PistolHands.DisableVisualFirstTimeBullet();
        }

        void handsPickedUp(HandsController hands) // lol
        {
            bool anyDeployed = false;
            foreach (HandsController h in registeredHands)
            {
                if (h.IsDeployed)
                {
                    anyDeployed = true;
                    break;
                }
            }

            if(!anyDeployed)
            {
                nextHands = hands;
                hands.Deploy();
                HandsChanged.Invoke(hands);
            }
        }

        public void PlayRifleShot()
        {
            PlaySFX(RifleHands.ShootSound);
        }

        public void PlayRifleLastShot()
        {
            PlaySFX(RifleHands.LastShootSound);
        }

        void injectionStart()
        {
            if (deployedHandsBeforeInjection != null)
                deployedHandsBeforeInjection.HideProps();
        }

        void injectionEnd()
        {
            if (deployedHandsBeforeInjection != null)
            {
                nextHands = deployedHandsBeforeInjection;
                deployedHandsBeforeInjection.Deploy();
            }

            deployedHandsBeforeInjection = null;
        }

        public void DetachPropsOnDead()
        {
            if (PistolHands.IsDeployed)
                PistolHands.DetachProps();

            if (RifleHands.IsDeployed)
                RifleHands.DetachProps();

            if (ShotgunHands.IsDeployed)
                ShotgunHands.DetachProps();

            if (KnifeHands.IsDeployed)
                KnifeHands.DetachProps();

            if (GrenadeHands.IsDeployed)
                GrenadeHands.DetachProps();
        }

        public void FullDeployEvent()
        {
            foreach(HandsController h in registeredHands)
            {
                if(h.IsDeployed)
                {
                    h.SetFullyDeployed();
                }
            }
        }

        public bool HideWeapons()
        {
            if (PistolHands.IsDeployed)
            {
                PistolHands.Hide();
                return true;
            }

            if (KnifeHands.IsDeployed)
            {
                KnifeHands.Hide();
                return true;
            }

            if (ShotgunHands.IsDeployed)
            {
                ShotgunHands.Hide();
                return true;
            }

            if (RifleHands.IsDeployed)
            {
                RifleHands.Hide();
                return true;
            }

            if (GrenadeHands.IsDeployed)
            {
                GrenadeHands.Hide();
                return true;
            }

            return false;
        }

        public HandsController GetHands(string handsName)
        {
            if(handsName.Equals("Knife"))
            {
                return KnifeHands;
            } else if(handsName.Equals("Pistol"))
            {
                return PistolHands;
            } else if(handsName.Equals("Shotgun"))
            {
                return ShotgunHands;
            }
            else if (handsName.Equals("Rifle"))
            {
                return RifleHands;
            }
            else if (handsName.Equals("Grenade"))
            {
                return GrenadeHands;
            }

            return null;
        }

        protected virtual void jumpStart()
        {
            if(Controller.DamageHandler.Health.RealIsAlive)
                handsAnimator.Play("PlayerJump", 3, 0);
        }

        protected virtual void jumpEnd()
        {
            if(Controller.DamageHandler.Health.RealIsAlive)
                handsAnimator.Play("PlayerLanding", 3, 0);
        }

        protected virtual void crouch()
        {
            if (Controller.DamageHandler.Health.RealIsAlive)
            {
                handsAnimator.Play("PlayerCrouch", 3, 0);
                //Debug.Log("CROUCH");
            }
        }

        protected virtual void standup()
        {
            if (Controller.DamageHandler.Health.RealIsAlive)
            {
                handsAnimator.Play("PlayerStandUp", 3, 0);
                //Debug.Log("STANDUP");
            }
        }

        public void PlaySFX(string sourceName)
        {
            AudioSource source;
            if (sounds.TryGetValue(sourceName, out source))
            {
                playSoundInstance(source);
            }
        }

        public void PlayPistolShoot()
        {
            PlaySFX(PistolHands.ShootSound);
        }

        public void PlayKnifeHit()
        {
            int randomHit = Random.Range(0, KnifeHitsCount);
            PlaySFX(KnifeHitSoundName + randomHit);
        }

        void playSoundInstance(AudioSource source)
        {
            AudioSource instance = Instantiate(source, source.transform.parent);
            instance.Play();
            Destroy(instance.gameObject, 10);
        }

        void runStart()
        {

        }

        void modificationEnd()
        {
            applyPistolMods();
            applyShotgunMods();
            applyRifleMods();
        }

        void applyRifleMods()
        {
            RifleHands.Silence = false;
            RifleHands.CentralAim = false;
            RifleHands.LowPoseInAiming = false;
            RifleHands.TopPoseInAiming = false;
            RifleHands.SetAiming(true, false, false, false);
            foreach (Modificator m in Customization.AttachedModificators)
            {
                foreach (int mId in RifleModificatorsIdsForBasicAim)
                {
                    if (m.ModId == mId)
                    {
                        RifleHands.SetAiming(false, false, false, false);
                        break;
                    }
                }
                foreach (int mId in RifleModificatorsIdsForCollimatorAim)
                {
                    if (m.ModId == mId)
                    {
                        RifleHands.SetAiming(false, false, false, true);
                        break;
                    }
                }
                foreach (int mId in RifleModificatorsIdsForZoomedAim)
                {
                    if (m.ModId == mId)
                    {
                        RifleHands.SetAiming(false, true, false, false);
                        break;
                    }
                }
                foreach (int mId in RifleModificatorsIdsForZoomed12XAim)
                {
                    if (m.ModId == mId)
                    {
                        RifleHands.SetAiming(false, false, true, false);
                        break;
                    }
                }
                foreach (int mId in RifleModificatorsIdsForSilencer)
                {
                    if (m.ModId == mId)
                    {
                        RifleHands.Silence = true;
                        break;
                    }
                }

            }
        }

        void applyShotgunMods()
        {
            ShotgunHands.CentralAim = false;
            ShotgunHands.LowPoseInAiming = false;
            ShotgunHands.Silence = false;
            ShotgunHands.Flashlight = false;
            ShotgunHands.AddAmmo = false;
            foreach (Modificator m in Customization.AttachedModificators)
            {
                foreach (int mId in SGModificatorsIdsForCentrailAim)
                {
                    if (m.ModId == mId)
                    {
                        ShotgunHands.CentralAim = true;
                        break;
                    }
                }
                foreach (int mId in SGModificatorsIdsForLowPose)
                {
                    if (m.ModId == mId)
                    {
                        ShotgunHands.LowPoseInAiming = true;
                        break;
                    }
                }
                foreach (int mId in SGModificatorsIdsForSilencer)
                {
                    if (m.ModId == mId)
                    {
                        ShotgunHands.Silence = true;
                        break;
                    }
                }
                foreach (int mId in SGModificatorsIdsForFlashlight)
                {
                    if (m.ModId == mId)
                    {
                        ShotgunHands.Flashlight = true;
                        ShotgunHands.PutFlashLight((m.CustomData as GameObject));
                        break;
                    }
                }
                foreach (int mId in SGModificatorsIdsForAddAmmo)
                {
                    if (m.ModId == mId)
                    {
                        ShotgunHands.AddAmmo = true;
                        break;
                    }
                }
            }

            ShotgunHands.AmmoMax = ShotgunHands.AddAmmo ? ShotgunHands.AmmoMaxAdd : ShotgunHands.AmmoMaxDefault;
            int ammoToCapacity = ShotgunHands.AmmoCurrent - ShotgunHands.AmmoMax;
            ammoToCapacity = Mathf.Clamp(ammoToCapacity, 0, ShotgunHands.AmmoMax);


            int ammoDelta = ShotgunHands.AmmoCurrent - ShotgunHands.AmmoMax;

            if (ammoDelta > 0)
            {
                ShotgunHands.AmmoCurrent -= ammoDelta;
                ShotgunHands.GetAmmoItem().Add(ammoDelta);
            }
        }
        
        void applyPistolMods()
        {
            PistolHands.CentralAim = false;
            PistolHands.LowPoseInAiming = false;
            PistolHands.LongReload = false;
            PistolHands.Silence = false;
            PistolHands.Flashlight = false;
            PistolHands.BackReload = false;
            foreach (Modificator m in Customization.AttachedModificators)
            {
                foreach (int mId in ModificatorsIdsForCentrailAim)
                {
                    if (m.ModId == mId)
                    {
                        PistolHands.CentralAim = true;
                        break;
                    }
                }
                foreach (int mId in ModificatorsIdsForLowPose)
                {
                    if (m.ModId == mId)
                    {
                        PistolHands.LowPoseInAiming = true;
                        break;
                    }
                }
                foreach (int mId in ModificatorsIdsForLongReload)
                {
                    if (m.ModId == mId)
                    {
                        PistolHands.LongReload = true;
                        break;
                    }
                }
                foreach (int mId in ModificatorsIdsForSilencer)
                {
                    if (m.ModId == mId)
                    {
                        PistolHands.Silence = true;
                        break;
                    }
                }
                foreach (int mId in ModificatorsIdsForBackReload)
                {
                    if (m.ModId == mId)
                    {
                        PistolHands.BackReload = true;
                        break;
                    }
                }
                foreach (int mId in ModificatorsIdsForFlashlight)
                {
                    if (m.ModId == mId)
                    {
                        PistolHands.Flashlight = true;
                        PistolHands.PutFlashLight((m.CustomData as GameObject));
                        break;
                    }
                }
            }

            PistolHands.AmmoMax = PistolHands.LongReload ? PistolHands.AmmoMaxLong : PistolHands.AmmoMaxDefault;

            int ammoDelta = PistolHands.AmmoCurrent - PistolHands.AmmoMax;

            if(ammoDelta > 0)
            {
                PistolHands.AmmoCurrent -= ammoDelta;
                PistolHands.GetAmmoItem().Add(ammoDelta);
            }

            if (PistolHands.IsDeployed)
            {
                bool nowIsCentralController = handsAnimator.runtimeAnimatorController == PistolHands.ControllerCentral;

                if (PistolHands.CentralAim != nowIsCentralController)
                {
                    if (PistolHands.AmmoCurrent == 0)
                    {
                        handsAnimator.SetFloat(PistolHands.EmptyParam, 1f);
                    }
                    else
                    {
                        handsAnimator.SetFloat(PistolHands.EmptyParam, 0f);
                    }

                    if (PistolHands.IsDeployed)
                    {
                        AnimatorStateInfo state = handsAnimator.GetCurrentAnimatorStateInfo(0);
                        handsAnimator.runtimeAnimatorController = PistolHands.CentralAim ? PistolHands.ControllerCentral : PistolHands.ControllerDefault;

                        handsAnimator.Play(PistolHands.UpgradeIdleAnim, 0, state.normalizedTime);
                    }
                }
            }
        }

        public void SetRun(float run)
        {
            handsAnimator.SetFloat(RunParam, run);
            runFraction = run;
        }

        public void ReloadedEvent()
        {
            PistolHands.Reloaded();
        }

        public void ReloadEndEvent()
        {
            PistolHands.ReloadEnd();
        }

        public void EmitShotgunShell()
        {
            ShotgunHands.EmitShell();
        }

        public void RifleFirstDeployEnd()
        {
            RifleHands.FirstDeployEnd();
        }

        public void RifleReloadedEvent()
        {
            RifleHands.Reloaded();
        }

        public void RifleReloadedEndEvent()
        {
            RifleHands.ReloadEnd();
        }

        public void SpawnGrenade()
        {
            GrenadeHands.SpawnGrenade();
        }

        /*public void ShotgunBulletInserted()
        {
            ShotgunHands.ReloadBulletInserted();
        }*/

        public void ShotgunAddBullet()
        {
            ShotgunHands.AddOneBullet();
            ShotgunHands.ReloadBulletInserted();
        }

        public void ShotgunReloadHideBullets()
        {
            ShotgunHands.HideBullets();
        }

        public void ShotgunReloadEndEvent()
        {
            ShotgunHands.ReloadEnd();
        }

        public void ShotgunReloadLoopStart()
        {
            ShotgunHands.ReloadLoopStart();
        }

        public void GrenadeAnimationDeployEvent()
        {
            if (GrenadeHands.GrenadesCount > 0)
                GrenadeHands.DeployedAnimationEvent();
            else
            {
                lastHands = null;
                nextHands = null;

                GrenadeHands.HideImmediately();
                HandsChanged.Invoke(null);
            }
        }

        // Switch props on switch :)
        public void SwitchObjects()
        {

            if (lastHands != null)
            {
                lastHands.HideProps();
            }

            waitForOtherDeploy = false;

            if (nextHands != null)
            {
                nextHands.Deploy();
                nextHands = null;
            }
            else
            {
                bool anyDeployed = false;
                foreach (HandsController h1 in registeredHands)
                {
                    if (h1.IsDeployed)
                    {
                        anyDeployed = true;
                        break;
                    }
                }
                if(!anyDeployed)
                    handsAnimator.Play("Default State", 0, 0);
            }
        }

        // Switch props on interrupt
        public void SwitchObjectsOnInterrupt()
        {
            /*
            foreach(HandsController h in registeredHands)
            {
                if(h == lastHands)
                {
                    lastHands.ShowProps();
                } else if(h == nextHands)
                {
                    nextHands.HideProps();
                }
                else
                {
                    h.HideProps();
                }
            }*/
        }

        public void HitEnd()
        {
            KnifeHands.NextAttack();
        }

        void deployHideInput()
        {

            if(Input.GetKeyDown(KeyCode.H) && !Injector.InjectionInProcess && Injector.TargetHealth.HealthFraction < 1f)
            {
                bool anyDeployed = false;
                HandsController deployedHands = null;
                foreach (HandsController h in registeredHands)
                {
                    if (h.IsDeployed)
                    {
                        anyDeployed = true;
                        deployedHands = h;
                        break;
                    }
                }
                if(anyDeployed)
                {
                    if (deployedHands.CanControl)
                    {
                        deployedHandsBeforeInjection = deployedHands;
                        deployedHandsBeforeInjection.Hide();
                        Injector.StartInject();
                    }
                }
                else
                {
                    Injector.StartInject();
                }
                return;
            }

            if (Input.GetKeyUp(KeyCode.H))
            {
                if (Injector.InjectionInProcess)
                {
                    Injector.ForceInjectionEnd();
                }
                return;
            }

            if (Injector.InjectionInProcess)
                return;

            if (!waitForOtherDeploy)
            {
                foreach (HandsController h in registeredHands)
                {
                    if (Input.GetKeyDown(h.DeployHideKey) && h.HandsItem.HasAny())
                    {
                        bool anyDeployed = false;
                        HandsController deployedHands = null;
                        foreach (HandsController h1 in registeredHands)
                        {
                            if (h1.IsDeployed)
                            {
                                anyDeployed = true;
                                deployedHands = h1;
                                break;
                            }
                        }
                        if (h.IsDeployed)
                        {
                            if (h.CanControl)
                            {
                                lastHands = h;
                                nextHands = null;

                                waitForOtherDeploy = true;
                                h.Hide();
                                HandsChanged.Invoke(null);
                            }
                        }
                        else
                        {
                            if (anyDeployed)
                            {
                                if (deployedHands.CanControl)
                                {
                                    //Debug.Log("DEPLOY " + h.DeployHideKey + " " + deployedHands.CanControl + " " + deployedHands.DeployHideKey);
                                    nextHands = h;
                                    lastHands = deployedHands;

                                    waitForOtherDeploy = true;
                                    deployedHands.Hide();
                                    //h.SetDeployed(true);
                                    SwitchObjectsOnInterrupt();
                                    HandsChanged.Invoke(h);
                                }
                            }
                            else
                            {
                                nextHands = h;
                                lastHands = null;
                                h.Deploy();
                                HandsChanged.Invoke(h);
                            }
                        }
                        break;
                    }
                }
            }
        }

        void Update()
        { 

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            else if (Input.GetKeyDown(KeyCode.End))
            {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }

            bool anyDeployed = false;

            handsInput = new HandsController.HandsControllerInput();

            if (!Controller.IsFreezed)
            {
                deployHideInput();

                handsInput.MouseDown = Input.GetMouseButtonDown(0);
                handsInput.MouseHold = Input.GetMouseButton(0);
                handsInput.MouseUp = Input.GetMouseButtonUp(0);

                handsInput.MouseSecondDown = Input.GetMouseButtonDown(1);
                handsInput.MouseSecondHold = Input.GetMouseButton(1);
                handsInput.MouseSecondUp = Input.GetMouseButtonUp(1);

                handsInput.Reload = Input.GetKeyDown(KeyCode.R);
                handsInput.Flashlight = Input.GetKeyDown(KeyCode.F);
            }

            handsInput.Upgrade = Input.GetKeyDown(KeyCode.V);

            float deltaTime = Time.deltaTime;

            if (KnifeHands.IsDeployed)
            {
                anyDeployed = true;
                KnifeHands.ApplyInput(handsInput);
                KnifeHands.Update(deltaTime);

                TargetCamera.fieldOfView = Mathf.Lerp(TargetCamera.fieldOfView, DefaultFov + RunFovAdd * runFraction, Time.deltaTime * 5f);
            }

            if (PistolHands.IsDeployed)
            {
                anyDeployed = true;
                PistolHands.ApplyInput(handsInput);
                PistolHands.Update(deltaTime);

                TargetCamera.fieldOfView = PistolHands.CameraFov + RunFovAdd * runFraction;
            }

            if (ShotgunHands.IsDeployed)
            {
                anyDeployed = true;
                ShotgunHands.ApplyInput(handsInput);
                ShotgunHands.Update(deltaTime);

                TargetCamera.fieldOfView = ShotgunHands.CameraFov + RunFovAdd * runFraction;
            }

            if (RifleHands.IsDeployed)
            {
                anyDeployed = true;
                RifleHands.ApplyInput(handsInput);
                RifleHands.Update(deltaTime);

                TargetCamera.fieldOfView = RifleHands.CameraFov + RunFovAdd * runFraction;
            }

            if (GrenadeHands.IsDeployed)
            {
                anyDeployed = true;
                GrenadeHands.ApplyInput(handsInput);
                GrenadeHands.Update(deltaTime);

                TargetCamera.fieldOfView = Mathf.Lerp(TargetCamera.fieldOfView, DefaultFov + RunFovAdd * runFraction, Time.deltaTime * 5f);
            }

            if (!anyDeployed)
            {
                TargetCamera.fieldOfView = Mathf.Lerp(TargetCamera.fieldOfView, DefaultFov + RunFovAdd * runFraction, Time.deltaTime * 5f);
                Controller.PPController.LerpDof(Controller.PPController.DofSettings, Controller.DefaultDofSettings, DofSmoothSpeed * Time.deltaTime);
            }
        }
        
    }

    public class HandsChangedEvent : UnityEvent<HandsController>
    {

    }
}