using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Test : MonoBehaviour {
    public GameObject Prefab;

	// Use this for initialization
	void Start () {
		
	}
	
	// Update is called once per frame
	void Update () {
		if(Input.GetKeyDown(KeyCode.F5))
            {
            GameObject instance = Instantiate(Prefab);
            instance.SetActive(true);
            Destroy(instance, 5f);
        }
	}
}
