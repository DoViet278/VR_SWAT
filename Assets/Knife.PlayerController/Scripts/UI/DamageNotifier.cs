using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KnifePlayerController
{
    public class DamageNotifier : MonoBehaviour
    {
        [Header("Player")]
        [SerializeField]
        private PlayerHealth targetPlayer;

        [Header("Red corners")]
        public Image RedCorners;
        public float RedCornersStartFraction = 0.5f;
        public AnimationCurve RedCornersEffectIntensityCurve;
        public Image RedCornersDamage;

        public AnimationCurve RedCornersEffectCurve;
        public float RedCornersEffectDuration = 1f;
        public AnimationCurve RedCornersEffectDurationCurve;

        [Header("Blood effect")]
        public Image BloodImageTemplate;
        public Sprite[] BloodSprites;
        public float BloodImageEffectDuration = 5f;
        public AnimationCurve BloodImageEffectCurve;
        public int MaxBloodSpritesCount = 5;

        List<BloodEffectInfo> aliveBloodEffects = new List<BloodEffectInfo>();
        Queue<Image> bloodImagesPool = new Queue<Image>();

        float lastDamageTime;

        public PlayerHealth TargetPlayer
        {
            get
            {
                if (targetPlayer == null)
                    targetPlayer = FindObjectOfType<PlayerHealth>();

                return targetPlayer;
            }

            set
            {
                targetPlayer = value;
            }
        }

        private void Awake()
        {
            TargetPlayer.DamagedEvent.AddListener(damaged);

            for(int i = 0; i < MaxBloodSpritesCount; i++)
            {
                createImage();
            }
            lastDamageTime = -1000f;
        }

        void createImage()
        {
            Image bloodImage = Instantiate(BloodImageTemplate, BloodImageTemplate.transform.parent);
            bloodImagesPool.Enqueue(bloodImage);
        }

        private void damaged(DamageData damage)
        {
            if(aliveBloodEffects.Count < MaxBloodSpritesCount)
            {
                if (bloodImagesPool.Count == 0)
                    createImage();

                Image bloodImage = bloodImagesPool.Dequeue();
                bloodImage.sprite = BloodSprites[Random.Range(0, BloodSprites.Length)];
                bloodImage.name = bloodImage.sprite.name;
                BloodEffectInfo effect = new BloodEffectInfo(bloodImage, BloodImageEffectDuration, BloodImageEffectCurve);
                aliveBloodEffects.Add(effect);
            }

            lastDamageTime = Time.time;
        }

        private void Update()
        {
            foreach (BloodEffectInfo b in aliveBloodEffects)
            {
                b.Update();
                if(b.CanBeDestroyed())
                {
                    bloodImagesPool.Enqueue(b.Destroy());
                }
            }

            aliveBloodEffects.RemoveAll(
                (b) => 
                {
                    return b.CanBeDestroyed();
                }
            );

            float redCornersHealthFraction = 1f - TargetPlayer.HealthFraction / RedCornersStartFraction;
            redCornersHealthFraction = Mathf.Clamp01(redCornersHealthFraction);
            float redCornersHealthAlpha = RedCornersEffectIntensityCurve.Evaluate(redCornersHealthFraction);

            float redCornersTimeDuration = RedCornersEffectDurationCurve.Evaluate(redCornersHealthFraction) * RedCornersEffectDuration;
            float redCornersTimeFraction = Mathf.Repeat(Time.time, redCornersTimeDuration) / redCornersTimeDuration;
            float redCornersTimeAlpha = RedCornersEffectCurve.Evaluate(redCornersTimeFraction);

            Color c = RedCorners.color;
            c.a = redCornersHealthAlpha * redCornersTimeAlpha;
            RedCorners.color = c;

            float damageFraction = (Time.time - lastDamageTime) / BloodImageEffectDuration;
            float damageAlpha = BloodImageEffectCurve.Evaluate(damageFraction);
            c = RedCornersDamage.color;
            c.a = damageAlpha;
            RedCornersDamage.color = c;
        }

        [System.Serializable]
        public class BloodEffectInfo
        {
            Image effectTargetImage;
            float effectDuration;
            float effectTime;
            AnimationCurve effectCurve;

            public BloodEffectInfo(Image targetImage, float duration, AnimationCurve curve)
            {
                effectTargetImage = targetImage;
                effectDuration = duration;
                effectCurve = curve;
                effectTime = 0;
            }

            public void Update()
            {
                effectTime += Time.deltaTime;
                Color c = effectTargetImage.color;
                c.a = effectCurve.Evaluate(effectTime / effectDuration);
                effectTargetImage.color = c;

            }

            public bool CanBeDestroyed()
            {
                return effectTime >= effectDuration;
            }

            public Image Destroy()
            {
                return effectTargetImage;
            }
        }
    }
}