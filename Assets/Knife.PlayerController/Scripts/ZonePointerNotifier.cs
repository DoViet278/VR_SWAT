using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class ZonePointerNotifier : MonoBehaviour
{
    public PointerEnterExitNotifier[] EnterNotifiers;
    public PointerEnterExitNotifier[] ExitNotifiers;
    public float ExitTimeThreshold = 0.5f;

    public UnityEvent EnterEvent;
    public UnityEvent ExitEvent;

    bool inZone = false;
    bool outEventInvoked = true;
    float outZoneTime = 0;
    
	void Start ()
    {
        outZoneTime = Mathf.Infinity;
    }
	
	void Update ()
    {
		if(inZone)
        {
            inZone = false;
            foreach (PointerEnterExitNotifier n in ExitNotifiers)
            {
                if (n.InZone)
                {
                    outZoneTime = 0;
                    inZone = true;
                    break;
                }
            }
        } else
        {
            foreach (PointerEnterExitNotifier n in EnterNotifiers)
            {
                if(n.InZone)
                {
                    inZone = true;
                    EnterEvent.Invoke();
                    outZoneTime = 0;
                    outEventInvoked = false;
                    break;
                }
            }

            if (!outEventInvoked)
            {
                outZoneTime += Time.deltaTime;
                if (outZoneTime >= ExitTimeThreshold)
                {
                    ExitEvent.Invoke();
                    outEventInvoked = true;
                }
            }
        }
	}
}
