using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SoundOnCollision : MonoBehaviour
{
    public bool Once = true;
    public AudioSource Source;
    public AudioClip[] Clips;

    bool isTriggered = false;

    void OnCollisionEnter(Collision col)
    {
        if (Once && isTriggered)
            return;

        isTriggered = true;

        Source.clip = Clips[Random.Range(0, Clips.Length)];
        Source.Play();
    }
}
