using UnityEngine;
using System.Collections;
using UnityEngine.UI;

namespace KnifePlayerController
{
    public class WeaponUIInfo : MonoBehaviour
    {
        [Header("Context")]
        [SerializeField]
        private PlayerHands target;
        public Text CurrentAmmoLabel;
        public Text CapacityAmmoLabel;
        public CountableProgressbar AmmoBar;
        public Image ShootModeImage;

        [Header("Default options")]
        public string DefaultAmmo = "-";
        public string DefaultAmmoCapacity = "-";

        public int DefaultAmmoBarCount = 0;
        public int DefaultAmmoBarMaxCount = 0;

        public bool DefaultShootModeImageEnabled = false;
        public Sprite DefaultShootModeSprite;

        HandsController currentHands = null;
        IWeaponAmmo currentAmmo;
        IWeaponShootMode currentShootMode;

        public PlayerHands Target
        {
            get
            {
                if (target == null)
                {
                    target = FindObjectOfType<PlayerHands>();
                }
                return target;
            }

            set
            {
                target = value;
            }
        }

        private void Awake()
        {
            handsChanged(null);
            Target.HandsChanged.AddListener(handsChanged);
        }

        private void handsChanged(HandsController hands)
        {
            if(hands == null)
            {
                setDefaultAmmo();
                setDefaultShootMode();
            }
            else
            {
                currentAmmo = hands as IWeaponAmmo;
                currentShootMode = hands as IWeaponShootMode;

                if (currentAmmo == null)
                {
                    setDefaultAmmo();
                }
                else
                {
                    setAmmo(currentAmmo.GetCurrentAmmoString(), currentAmmo.GetCapacityAmmoString(), currentAmmo.GetCurrentAmmo(), currentAmmo.GetMaxAmmo());
                }

                if(currentShootMode == null)
                {
                    setDefaultShootMode();
                }
                else
                {
                    setShootMode(true, currentShootMode.GetCurrentModeSprite());
                }
            }
            currentHands = hands;
        }

        private void Update()
        {
            if(currentHands != null)
            {
                if(currentAmmo != null)
                    setAmmo(currentAmmo.GetCurrentAmmoString(), currentAmmo.GetCapacityAmmoString(), currentAmmo.GetCurrentAmmo(), currentAmmo.GetMaxAmmo());

                if (currentShootMode != null)
                    setShootMode(true, currentShootMode.GetCurrentModeSprite());
            }
        }

        void setAmmo(string current, string capacity, int currentCount, int maxCount)
        {
            CurrentAmmoLabel.text = current;
            CapacityAmmoLabel.text = capacity;

            if (maxCount >= 0)
                AmmoBar.SetMaxCount(maxCount);

            AmmoBar.SetCurrentCount(currentCount);
        }

        void setShootMode(bool enabled, Sprite sprite)
        {
            ShootModeImage.enabled = enabled && sprite != null;
            if (ShootModeImage.sprite != sprite)
            {
                ShootModeImage.sprite = sprite;
                ShootModeImage.SetNativeSize();
            }
        }

        void setDefaultAmmo()
        {
            setAmmo(DefaultAmmo, DefaultAmmoCapacity, DefaultAmmoBarCount, DefaultAmmoBarMaxCount);
        }

        void setDefaultShootMode()
        {
            setShootMode(DefaultShootModeImageEnabled, DefaultShootModeSprite);
        }
    }
}