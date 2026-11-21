using UnityEngine;
using System.Collections;
using UnityEngine.UI;

namespace KnifePlayerController
{
    public class ImageAlphaCull : MonoBehaviour
    {
        Image myImage;

        private void Update()
        {
            if (myImage == null)
                myImage = GetComponent<Image>();

            Color c = myImage.color;
            if(c.a <= 0 && myImage.enabled)
            {
                myImage.enabled = false;
            } else if(c.a > 0 && !myImage.enabled)
            {
                myImage.enabled = true;
            }
        }
    }
}