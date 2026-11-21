using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class EventOnAnimationEvent : MonoBehaviour
{
    public NamedEvent[] Events;

    Dictionary<string, UnityEvent> registeredEvents = new Dictionary<string, UnityEvent>();
    
	void Start ()
    {
		foreach(NamedEvent e in Events)
        {
            registeredEvents.Add(e.Name, e.Event);
        }
	}
    
    public void InvokeEvent(string eventName)
    {
        UnityEvent e;
        if(registeredEvents.TryGetValue(eventName, out e))
        {
            e.Invoke();
        }
    }
}

[System.Serializable]
public class NamedEvent
{
    public string Name;
    public UnityEvent Event;
}