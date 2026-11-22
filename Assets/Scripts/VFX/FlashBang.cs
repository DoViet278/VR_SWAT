using System;
using Unity.VectorGraphics;
using UnityEngine;

namespace VFX
{
    public class FlashBang: MonoBehaviour
    {
        private Camera cam;

        private void Awake()
        {
            cam = Camera.main;
        }

        private bool checkVisibility()
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            var point = transform.position;

            foreach (var p in planes)
            {
                if (p.GetDistanceToPoint(point) > 0)
                {
                    Ray ray = new Ray(cam.transform.position, transform.position - cam.transform.position);
                    RaycastHit hit;
                    if(Physics.Raycast(ray, out hit))
                        return hit.transform.gameObject == this.gameObject;
                    else
                    {
                        return false;
                    }
                }
            }
            
            return false;
        } 
    }
}