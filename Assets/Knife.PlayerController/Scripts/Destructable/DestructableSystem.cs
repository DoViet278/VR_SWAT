using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public class DestructableSystem : MonoBehaviour
{
    public DestructablePart[] Parts;
    public float PerPartMass = 0.2f;
    public float ConnectionForce = 50f;

    [System.Serializable]
    public struct DestructableLink
    {
        public List<DestructablePart> Other;
    }

    Dictionary<DestructablePart, DestructableLink> links;

    [ContextMenu("Get start params")]
    void getStartParams()
    {
        Parts = GetComponentsInChildren<DestructablePart>();
    }


#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (links == null)
            return;

        if(Selection.activeGameObject != null)
        {
            DestructablePart part = Selection.activeGameObject.GetComponent<DestructablePart>();
            if(part != null)
            {
                MeshRenderer m = part.GetComponent<MeshRenderer>();
                Gizmos.DrawWireCube(m.bounds.center, m.bounds.size);
                DestructableLink link;

                if (links.TryGetValue(part, out link))
                {
                    if (link.Other != null)
                    {
                        Gizmos.color = Color.red;
                        foreach (DestructablePart otherPart in link.Other)
                        {
                            Gizmos.DrawLine(part.transform.position, otherPart.transform.position);
                        }
                    }
                }
            }
        }
    }
#endif

    private void OnValidate()
    {
        if (Application.isPlaying)
            return;

        getStartParams();

        foreach(DestructablePart part in Parts)
        {
            part.GetStartParams();
            part.Body.mass = PerPartMass;
        }
    }

    private void Awake()
    {
        if (!Application.isPlaying)
            return;

        foreach (DestructablePart p in Parts)
        {
            var part = p;
            p.DieEvent.AddListener(partDestroyed);
        }

        links = new Dictionary<DestructablePart, DestructableLink>();
        for (int i = 0; i < Parts.Length; i++)
        {
            DestructableLink link = new DestructableLink();
            link.Other = new List<DestructablePart>();
            Bounds currentBounds = Parts[i].GetComponent<MeshRenderer>().bounds;
            Bounds otherBounds;
            Collider c1 = Parts[i].GetComponent<MeshCollider>();
            Vector3 p1 = Parts[i].transform.position;
            Quaternion r1 = Parts[i].transform.rotation;
            for (int j = 0; j < Parts.Length; j++)
            {
                if (i == j)
                    continue;

                otherBounds = Parts[j].GetComponent<MeshRenderer>().bounds;

                Vector3 penetrationDirection;
                float penetrationDistance;

                Collider c2 = Parts[j].GetComponent<MeshCollider>();
                Vector3 p2 = Parts[j].transform.position;
                Quaternion r2 = Parts[j].transform.rotation;

                if (currentBounds.Intersects(otherBounds))
                {
                    bool isPenetrating = Physics.ComputePenetration(c1, p1, r1, c2, p2, r2, out penetrationDirection, out penetrationDistance);

                    if (isPenetrating)
                    {
                        FixedJoint fixedJoint = Parts[i].gameObject.AddComponent<FixedJoint>();
                        fixedJoint.connectedBody = Parts[j].Body;
                        if(Parts[j].Invulnerable)
                            fixedJoint.breakForce = ConnectionForce;
                        else
                            fixedJoint.breakForce = ConnectionForce;

                        link.Other.Add(Parts[j]);
                    }
                }
            }
            if (!Parts[i].Invulnerable)
                Parts[i].Body.isKinematic = false;

            links.Add(Parts[i], link);
        }
    }

    void partDestroyed(DamageData damage)
    {
        BaseHittableObject hittable = damage.Receiver as BaseHittableObject;

        if(hittable != null)
        {
            hittable.DieEvent.AddListener(partDestroyed);
        }

        foreach (DestructablePart targetPart in Parts)
        {
            if (targetPart != null && !targetPart.Destructed)
            {
                DestructableLink link;

                bool hasDownLink = false;

                if (links.TryGetValue(targetPart, out link))
                {
                    foreach (DestructablePart otherPart in link.Other)
                    {
                        if (!otherPart.Destructed)
                        {
                            if (otherPart.transform.position.y < targetPart.transform.position.y)
                            {
                                hasDownLink = true;
                                //Debug.Log(targetPart.name + " has link with: " + otherPart.name);
                                break;
                            }
                        }
                    }
                }

                if (!hasDownLink)
                {
                    damage.Deadly = true;
                    targetPart.TakeDamage(damage);
                    links.Remove(targetPart);
                }
            }
        }
    }
}
