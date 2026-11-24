using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Code viet lai tu video "Flash Bang in UNITY | After Image Effect" (shady poly)
// https://www.youtube.com/watch?v=dp1ldhSu2Yo
public class Grenade : MonoBehaviour
{
    [SerializeField]
    private float fuseTime = 3;
    [SerializeField]
    private GameObject explosionPrefab;

    private Camera cam;

    private void Start()
    {
        Invoke("Explode", fuseTime);
        cam = Camera.main;
    }

    private void Explode()
    {
        Destroy(gameObject);

        if (checkVisibility())
            BlindnessEffect.activeInstance.GoBlind();
        else
            Debug.Log("don't get affected!");

        Destroy(Instantiate(explosionPrefab, transform.position, Quaternion.identity), 5);
    }

    private bool checkVisibility()
    {
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        var point = transform.position;

        // Neu qua lu dan nam ngoai bat ky mat phang nao cua view frustum
        // => no khong nam trong tam nhin cua camera.
        foreach (var p in planes)
        {
            if (p.GetDistanceToPoint(point) < 0)
                return false;
        }

        // Nam trong frustum roi, nhung co the bi tuong che.
        // Ban mot tia tu camera toi qua lu dan: neu vat dau tien trung la chinh no
        // thi nguoi choi thuc su "nhin thay" no.
        Ray ray = new Ray(cam.transform.position, transform.position - cam.transform.position);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit))
            return hit.transform.gameObject == this.gameObject;
        else
            return false;
    }
}
