using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class TransformTranslater : MonoBehaviour
{
    public Transform Reference;
    public Transform Target;
    public bool InLocal = true;
    public bool TranslatePosition = false;
    public bool TranslateRotation = true;
    public bool Additive = true;

    Quaternion lastRotation;
    Vector3 lastPosition;

    Transform target
    {
        get
        {
            if (Target == null)
                return transform;

            return Target;
        }
    }

    private void Awake()
    {
        if (Target == null)
            Target = transform;
    }

    private void OnEnable()
    {
        if (Reference == null)
            return;

        if (InLocal)
        {
            lastPosition = Reference.localPosition;
            lastRotation = Reference.localRotation;
        }
        else
        {
            lastPosition = Reference.position;
            lastRotation = Reference.rotation;
        }
    }

    void FixedUpdate ()
    {
        if (Reference == null)
            return;

        if (Additive)
            applyAdditive();
        else
            applyForce();
    }

    void applyAdditive()
    {
        if (InLocal)
        {
            if (TranslatePosition)
            {
                target.localPosition -= lastPosition;
                target.localPosition += Reference.localPosition;
                lastPosition = Reference.localPosition;
            }

            if (TranslateRotation)
            {
                target.localRotation *= Quaternion.Inverse(lastRotation);
                target.localRotation *= Reference.localRotation;
                lastRotation = Reference.localRotation;
            }
        }
        else
        {
            if (TranslatePosition)
            {
                target.position -= lastPosition;
                target.position += Reference.position;
                lastPosition = Reference.position;
            }

            if (TranslateRotation)
            {
                target.rotation *= Quaternion.Inverse(lastRotation);
                target.rotation *= Reference.rotation;
                lastRotation = Reference.rotation;
            }
        }
    }

    void applyForce()
    {
        if (InLocal)
        {
            if (TranslatePosition)
            {
                target.localPosition = Reference.localPosition;
            }

            if (TranslateRotation)
            {
                target.localRotation = Reference.localRotation;
            }
        }
        else
        {
            if (TranslatePosition)
            {
                target.position = Reference.position;
            }

            if (TranslateRotation)
            {
                target.rotation = Reference.rotation;
            }
        }
    }

    void Update()
    {
        if(!Application.isPlaying)
        {
            if (Additive)
                applyAdditive();
            else
                applyForce();
        }
    }
}
