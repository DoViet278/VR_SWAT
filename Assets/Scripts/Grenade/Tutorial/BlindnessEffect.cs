using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Code viet lai tu video "Flash Bang in UNITY | After Image Effect" (shady poly)
// https://www.youtube.com/watch?v=dp1ldhSu2Yo
public class BlindnessEffect : MonoBehaviour
{
    [SerializeField]
    private Image img;

    private Animator anim;

    private int width, height;

    public static BlindnessEffect activeInstance;

    private void Start()
    {
        activeInstance = this;

        anim = GetComponent<Animator>();
        width = Screen.width;
        height = Screen.height;
    }

    public void GoBlind()
    {
        StartCoroutine(goBlind());
    }

    private IEnumerator goBlind()
    {
        // Doi toi cuoi frame, luc nay man hinh da ve xong -> doc duoc pixel.
        yield return new WaitForEndOfFrame();

        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();

        // Anh chup man hinh nay chinh la "after image" dinh tren vong mac.
        img.sprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100);

        anim.SetTrigger("Go Blind");
    }
}
