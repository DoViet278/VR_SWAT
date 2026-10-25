using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class TimedCallbackEvent : MonoBehaviour
{
    public void DestroyWithDelay(float delay, UnityAction callback)
    {
        StartCoroutine(destroying(delay, callback));
    }

    IEnumerator destroying(float delay, UnityAction callback)
    {
        yield return new WaitForSeconds(delay);
        callback();
    }
}
