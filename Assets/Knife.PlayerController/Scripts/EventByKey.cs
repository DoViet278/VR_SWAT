using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EventByKey : MonoBehaviour
{
    public KeyCode Key = KeyCode.F2;
    public UnityEngine.Events.UnityEvent TargetEvent;



    void Update ()
    {
		if(Input.GetKeyDown(Key))
        {
            TargetEvent.Invoke();
        }
	}
}
