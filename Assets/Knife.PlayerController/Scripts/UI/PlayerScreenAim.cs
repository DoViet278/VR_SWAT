using UnityEngine;
using System.Collections;
using UnityEngine.UI;

namespace KnifePlayerController
{
    public class PlayerScreenAim : MonoBehaviour
    {
        public Image Image;

        [SerializeField]
        private PlayerHands targetHands;

        public PlayerHands TargetHands
        {
            get
            {
                if(targetHands == null)
                {
                    targetHands = FindObjectOfType<PlayerHands>();
                }
                return targetHands;
            }

            set
            {
                targetHands = value;
            }
        }

        private void Update()
        {
            Image.enabled = TargetHands.NeedAimShow;
        }
    }
}