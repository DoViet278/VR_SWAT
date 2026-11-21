using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class PointerEnterExitNotifier : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public bool InZone
    {
        get
        {
            return inZone;
        }
    }

    bool inZone = false;

    public void OnPointerEnter(PointerEventData eventData)
    {
        inZone = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        inZone = false;
    }
	
}
