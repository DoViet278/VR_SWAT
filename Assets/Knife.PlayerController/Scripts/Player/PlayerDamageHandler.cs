using UnityEngine;
using System.Collections;
using UnityEngine.Events;

namespace KnifePlayerController
{
    public class PlayerDamageHandler : MonoBehaviour
    {
        public DamageEvent DamagedEvent
        {
            get
            {
                return damagedEvent;
            }
        }

        public DamageEvent DieEvent
        {
            get
            {
                return dieEvent;
            }
        }

        public UnityEvent ResurrectEvent
        {
            get
            {
                return resurrectEvent;
            }
        }

        public string[] CameraDamageAnimations;
        public int CameraLayer = 3;
        public string PlayerDeathAnimation;
        public Animator HandsAnimator;
        public PlayerHands Hands;
        public PlayerController Controller;
        public TransformTranslater DefaultCameraAnimationTranslater;
        public TransformTranslater DeathCameraAnimationTranslater;
        public TransformStateSetupper CameraStateSetupper;

        public PlayerHealth Health
        {
            get
            {
                return health;
            }
        }

        DamageEvent damagedEvent = new DamageEvent();
        DamageEvent dieEvent = new DamageEvent();
        UnityEvent resurrectEvent = new UnityEvent();

        PlayerHealth health;

        private void Awake()
        {
            health = GetComponent<PlayerHealth>();
            health.DamagedEvent.AddListener(damaged);
            health.DieEvent.AddListener(die);
        }

        private void die(DamageData damage)
        {
            if (!health.IsAlive)
                return;

            if (dieEvent != null)
            {
                dieEvent.Invoke(damage);
            }
            
            HandsAnimator.CrossFadeInFixedTime(PlayerDeathAnimation, 0.05f, 0);

            Controller.Freeze(true);
            Controller.SetNoiseEnabled(false);
            DefaultCameraAnimationTranslater.enabled = false;
            DeathCameraAnimationTranslater.enabled = true;
        }

        public void Resurrect()
        {
            health.Heal(100);
            Controller.Freeze(false);
            Controller.SetNoiseEnabled(true);
            DefaultCameraAnimationTranslater.enabled = true;
            DeathCameraAnimationTranslater.enabled = false;
            HandsAnimator.Play("Default State", 0, 0);
            HandsAnimator.Play("Default State", CameraLayer, 0);
            CameraStateSetupper.LoadSavedState();
            if (resurrectEvent != null)
            {
                resurrectEvent.Invoke();
            }
        }

        private void Update()
        {
            if(Input.GetKeyDown(KeyCode.J))
            {
                Resurrect();
            }
        }

        private void FixedUpdate()
        {
            if (!health.IsAlive)
            {
                Controller.UpdateDefaultDeath();
            }
        }

        private void damaged(DamageData damage)
        {
            if (health.RealIsAlive)
            {
                HandsAnimator.Play(CameraDamageAnimations[Random.Range(0, CameraDamageAnimations.Length)], CameraLayer, 0f);

                if (damagedEvent != null)
                {
                    damagedEvent.Invoke(damage);
                }
            }
        }
    }
}