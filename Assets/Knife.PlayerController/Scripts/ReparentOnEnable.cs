using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ReparentOnEnable : MonoBehaviour
{
    public Transform TargetParent;
    
    void OnEnable()
    {
        transform.SetParent(TargetParent);
    }
}
