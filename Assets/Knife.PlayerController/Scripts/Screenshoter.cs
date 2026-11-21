using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Screenshoter : MonoBehaviour
{
    [ContextMenu("Screenshot")]
    public void Screenshot()
    {
        ScreenCapture.CaptureScreenshot("Screenshot" + System.DateTime.Now.ToString().Replace('/', '_').Replace(' ', '_').Replace(':', '_') + ".png");
    }

    void Update()
    {
        if(Input.GetKeyDown(KeyCode.F10))
        {
            Screenshot();
        }
    }
}
