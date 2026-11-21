using UnityEngine;
using System.Collections;
using UnityEngine.Events;

namespace KnifePlayerController
{
    public class InjectorController : MonoBehaviour
    {
        public int FlasksCount
        {
            get
            {
                return medkitItem.GetCurrentCount();
            }
        }
        public string StartAnim;
        public string IdleAnim;
        public string EndAnim;
        public string SubstanceAnim;
        public Animator HandsAnimator;
        public Animator LampAnimator;

        public AudioSource StartSFX;
        public float StartSFXDelay = 0.3f;
        public AudioSource EndSFX;

        public string LampToIdle = "InjectorLampIdle";
        public string LampToWork = "InjectorLampToWork";
        public int InjectorSubstanceLayer = 4;

        public GameObject GasParticles;

        public SkinnedMeshRenderer InjectionMesh;
        public string BlendShaderParameter = "_Blend";
        public string BlendAnimatorParameter = "InjectionHandBlend";
        [Range(0f, 1f)]
        public float BlendParameterValue;

        public float UsingSpeed = 0.2f;
        public PostProcessingController PostProcessingController;

        public PostProcessingController.ColorGradingSettings EffectSettings;
        public AnimationCurve EffectCurve;
        public float EffectDuration = 5f;

        public PlayerHealth TargetHealth;
        public PlayerInventory Inventory;

        public float CurrentFraction
        {
            get
            {
                return currentFraction;
            }
        }

        float currentFraction
        {
            get
            {
                return medkitItem.FractionAmount;
            }
        }

        public UnityEvent InjectionEndEvent = new UnityEvent();
        public UnityEvent InjectionStartEvent = new UnityEvent();
        public GameObject[] Props;
        public bool InjectionInProcess
        {
            get
            {
                return injectionInProcess;
            }
        }

        bool injectionInProcess;
        bool endInProcess;

        float currentHealth
        {
            get
            {
                return TargetHealth.Health;
            }
        }
        float maxHealth
        {
            get
            {
                return TargetHealth.StartHealth;
            }
        }

        float effectTime = 0;

        MedkitItem medkitItem;

        private void Awake()
        {
            foreach(GameObject g in Props)
            {
                g.SetActive(false);
            }

            medkitItem = new MedkitItem("MedKit");
            Inventory.AddItem(medkitItem);
        }

        public void StartInject()
        {
            if (injectionInProcess || currentHealth >= maxHealth)
                return;

            injectionInProcess = true;
            endInProcess = false;

            
            foreach (GameObject g in Props)
            {
                g.SetActive(false);
            }
            StartCoroutine(waitForInject());
        }

        IEnumerator waitForInject()
        {
            AnimatorStateInfo state = HandsAnimator.GetCurrentAnimatorStateInfo(0);
            while (!state.IsName("Default State"))
            {
                state = HandsAnimator.GetCurrentAnimatorStateInfo(0);
                yield return null;
            }
            StartSFX.PlayDelayed(StartSFXDelay);
            HandsAnimator.Play(StartAnim);
            HandsAnimator.Play(SubstanceAnim, InjectorSubstanceLayer, 1f - currentFraction);
            foreach (GameObject g in Props)
            {
                g.SetActive(true);
            }
            GasParticles.SetActive(false);
            InjectionStartEvent.Invoke();
            LampAnimator.Play(LampToIdle);
            StartCoroutine(loopProcessing());
            effectTime = 0;
        }

        public void ForceInjectionEnd()
        {
            if (endInProcess)
                return;

            endInProcess = true;
            StartCoroutine(waitForIdle());
        }

        public void EndInject()
        {
            StartCoroutine(waitForEnd());
        }

        public void VisualInject()
        {
            GasParticles.SetActive(true);
            StartCoroutine(effectProcessing());
        }

        IEnumerator effectProcessing()
        {
            PostProcessingController.ColorGradingSettings currentSettings = PostProcessingController.ColorSettings;
            while (effectTime <= EffectDuration)
            {
                effectTime += Time.deltaTime;

                float fraction = effectTime / EffectDuration;
                float value = EffectCurve.Evaluate(fraction);

                PostProcessingController.LerpColorGrading(currentSettings, EffectSettings, value);

                yield return null;
            }
        }

        IEnumerator loopProcessing()
        {
            AnimatorStateInfo state = HandsAnimator.GetCurrentAnimatorStateInfo(0);
            while(!state.IsName(IdleAnim))
            {
                state = HandsAnimator.GetCurrentAnimatorStateInfo(0);
                yield return null;
            }
            LampAnimator.Play(LampToWork);
            while (state.IsName(IdleAnim))
            {
                state = HandsAnimator.GetCurrentAnimatorStateInfo(0);
                if (!endInProcess)
                {
                    float deltaFraction = UsingSpeed * Time.deltaTime;
                    //currentFraction -= deltaFraction;
                    medkitItem.ConsumeFraction(deltaFraction);
                    TargetHealth.Heal(maxHealth * deltaFraction);
                    float clampedHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
                    float deltaFromClamped = currentHealth - clampedHealth;
                    deltaFromClamped = Mathf.Clamp(deltaFromClamped, 0, 1);

                    float deltaFractionFromClamped = deltaFromClamped / maxHealth;
                    //currentFraction += deltaFractionFromClamped;
                    medkitItem.ConsumeFraction(-deltaFractionFromClamped);

                    if (currentFraction <= 0)
                    {
                        if (FlasksCount > 0)
                        {
                            medkitItem.Consume(1);
                        }
                        else
                        {
                            ForceInjectionEnd();
                            break;
                        }
                    }

                    if (currentHealth >= maxHealth)
                    {
                        ForceInjectionEnd();
                    }

                    HandsAnimator.Play(SubstanceAnim, InjectorSubstanceLayer, 1f - currentFraction);
                }
                yield return null;
            }
        }

        IEnumerator waitForIdle()
        {
            AnimatorStateInfo state = HandsAnimator.GetCurrentAnimatorStateInfo(0);
            while (!state.IsName(IdleAnim))
            {
                state = HandsAnimator.GetCurrentAnimatorStateInfo(0);
                yield return null;
            }
            EndSFX.Play();
            LampAnimator.Play(LampToIdle);
            HandsAnimator.CrossFadeInFixedTime(EndAnim, 0.05f);
        }

        IEnumerator waitForEnd()
        {
            AnimatorStateInfo state = HandsAnimator.GetCurrentAnimatorStateInfo(0);
            while(state.normalizedTime < 0.9f)
            {
                state = HandsAnimator.GetCurrentAnimatorStateInfo(0);
                yield return null;
            }
            foreach (GameObject g in Props)
            {
                g.SetActive(false);
            }
            InjectionEndEvent.Invoke();
            injectionInProcess = false;
        }

        private void Update()
        {
            BlendParameterValue = HandsAnimator.GetFloat(BlendAnimatorParameter);
            Material mat = InjectionMesh.sharedMaterial;
            mat.SetFloat(BlendShaderParameter, BlendParameterValue);
        }
    }

    public class MedkitItem : BaseItem
    {
        public float FractionAmount;

        public MedkitItem(string itemName) : base(itemName)
        {
            MaxCapacity = 5;
            FractionAmount = 0f;
            IconName = "MedKit";
        }

        public virtual void ConsumeFraction(float delta)
        {
            FractionAmount -= delta;
        }

        protected override void added(int count)
        {
            base.added(count);
            if (GetCurrentCount() > 0)
                FractionAmount = 1f;
            else
                FractionAmount = 0;
        }

        protected override void consumed(int count)
        {
            base.consumed(count);

            if (GetCurrentCount() > 0)
                FractionAmount = 1f;
            else
                FractionAmount = 0;
        }
    }
}