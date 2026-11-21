using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FreeCamera : MonoBehaviour
{
    public float Speed = 5f;
    public float ShiftSpeed = 10f;

    public float Sensivity = 1f;
    public float SensivityY = 50f;
    public Transform CharPivot;
    public Transform CamPivot;
    public Transform Neck;
    public float NeckIKWeight = 1f;

    Vector3 camRotation;
    Vector3 charRotation;

    float speedMultiplier = 1f;
    float sensivityMultiplier = 1f;


    
	void Start ()
    {
        camRotation = CamPivot.localEulerAngles;
        charRotation = CharPivot.eulerAngles;
	}
	
	void LateUpdate ()
    {
        if(Input.GetMouseButtonDown(0))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        
        if(Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        float mx = Input.GetAxis("Mouse X");
        float my = Input.GetAxis("Mouse Y");

        float speed = Speed;

        if (Input.GetKey(KeyCode.LeftShift))
        {
            speed = ShiftSpeed;
        }

        speed *= speedMultiplier;

        //transform.position += (transform.forward * v + transform.right * h) * speed * Time.deltaTime;

        Vector3 rotation = new Vector3(-my * SensivityY, 0, 0);
        Vector3 charDeltaRotation = new Vector3(0, mx, 0);

        rotation *= Sensivity * sensivityMultiplier * Time.deltaTime;

        camRotation += rotation;
        charRotation += charDeltaRotation;


        CharPivot.rotation = Quaternion.Euler(charRotation);
        CamPivot.localRotation = Quaternion.Euler(camRotation);
        Vector3 neckDirection = CamPivot.position - Neck.position;

        Quaternion neckRotation = Quaternion.LookRotation(transform.forward, neckDirection.normalized);
        Neck.rotation = Quaternion.Lerp(Neck.rotation, transform.rotation * neckRotation, NeckIKWeight);

        float mw = Input.GetAxis("Mouse ScrollWheel");

        if (Input.GetMouseButton(2))
        {
            sensivityMultiplier += mw;
            sensivityMultiplier = Mathf.Clamp(sensivityMultiplier, 0, float.MaxValue);
        }
        else
        {
            speedMultiplier += mw;
            speedMultiplier = Mathf.Clamp(speedMultiplier, 0, float.MaxValue);
        }
    }
}
