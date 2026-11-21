using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FireTest : MonoBehaviour
{
    public GameObject LeftBullet;
    public GameObject RightBullet;
    Animator handsAnimator;

    bool needFire = false;
    
    void Start()
    {
        handsAnimator = GetComponent<Animator>();
    }

    public void FireLeft()
    {
        if (!needFire)
            return;

        GameObject bullet = Instantiate(LeftBullet, LeftBullet.transform.position, LeftBullet.transform.rotation);
        bullet.SetActive(true);
    }

    public void FireRight()
    {
        if (!needFire)
            return;


        GameObject bullet = Instantiate(RightBullet, RightBullet.transform.position, RightBullet.transform.rotation);
        bullet.SetActive(true);
    }

    void Update()
    {
        needFire = Input.GetMouseButton(0);
        handsAnimator.SetBool("Fire", needFire);
    }
}
