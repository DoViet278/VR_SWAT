using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KnifePlayerController
{
    public class CountableProgressbar : MonoBehaviour
    {
        public Image Template;
        public Color EmptyColor;
        public Color FillColor;

        List<Image> currentList;

        public void SetMaxCount(int maxCount)
        {
            if (currentList == null)
                currentList = new List<Image>();

            if(currentList.Count < maxCount)
            {
                while(currentList.Count != maxCount)
                {
                    Image instance = Instantiate(Template, Template.transform.parent);
                    instance.gameObject.SetActive(true);
                    currentList.Insert(0, instance);
                }
            } else if(currentList.Count > maxCount)
            {
                while (currentList.Count != maxCount)
                {
                    Destroy(currentList[0].gameObject);
                    currentList.RemoveAt(0);
                }
            }
        }

        public void SetCurrentCount(int count)
        {
            if (currentList == null)
                return;

            for (int i = 0; i < currentList.Count; i++)
            {
                if((i + 1) > count)
                {
                    currentList[i].color = FillColor;
                }
                else
                {
                    currentList[i].color = EmptyColor;
                }
            }
        }
    }
}