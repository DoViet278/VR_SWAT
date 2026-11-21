using UnityEngine;
using System.Collections;
using UnityEngine.UI;

namespace KnifePlayerController
{
    public class InjectorUIInfo : MonoBehaviour
    {
        public Text AmountLabel;
        private InjectorController controller;
        public Image CurrentFlaskAmountImage;

        public InjectorController Controller
        {
            get
            {
                if (controller == null)
                    controller = GameObject.FindObjectOfType<InjectorController>();
                return controller;
            }

            set
            {
                controller = value;
            }
        }

        private void Update()
        {
            AmountLabel.text = Controller.FlasksCount.ToString();
            CurrentFlaskAmountImage.fillAmount = Controller.CurrentFraction;
        }
    }
}