using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TimedDestructor : MonoBehaviour
{
    public float Delay = 5f;
    
	void Start ()
    {
        Invoke("delay", Delay);
	}
	
    void delay()
    {
        Destroy(gameObject);
    }
}
