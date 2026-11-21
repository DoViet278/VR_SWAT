using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

// See _ReadMe.txt for an overview
[ExecuteInEditMode]
public class CommandBufferBlurRefraction : MonoBehaviour
{
    public float BlurSize = 2.0f;
    public int Amount = 2;
	public Shader m_BlurShader;
	private Material m_Material;

	private Camera m_Cam;

	// We'll want to add a command buffer on any camera that renders us,
	// so have a dictionary of them.
	private Dictionary<Camera,CommandBuffer> m_Cameras = new Dictionary<Camera,CommandBuffer>();

	// Remove command buffers from all cameras we added into
	private void Cleanup()
	{
		foreach (var cam in m_Cameras)
		{
			if (cam.Key)
			{
				cam.Key.RemoveCommandBuffer (CameraEvent.AfterSkybox, cam.Value);
			}
		}
		m_Cameras.Clear();
		Object.DestroyImmediate (m_Material);
	}

	public void OnEnable()
	{
		Cleanup();
	}

	public void OnDisable()
	{
		Cleanup();
	}

    private void OnValidate()
    {
        Cleanup();
    }

    // Whenever any camera will render us, add a command buffer to do the work on it
    public void OnWillRenderObject()
	{
		var act = gameObject.activeInHierarchy && enabled;
		if (!act)
		{
			Cleanup();
			return;
		}
		
		var cam = Camera.current;
		if (!cam)
			return;

		CommandBuffer buf = null;
		// Did we already add the command buffer on this camera? Nothing to do then.
		if (m_Cameras.ContainsKey(cam))
			return;

        CommandBuffer[] buffers = cam.GetCommandBuffers(CameraEvent.AfterSkybox);

        foreach(CommandBuffer b in buffers)
        {
            if(b.name.Equals("Grab screen and blur"))
            {
                return;
            }
        }

        if (!m_Material)
		{
			m_Material = new Material(m_BlurShader);
			m_Material.hideFlags = HideFlags.HideAndDontSave;
		}

		buf = new CommandBuffer();
		buf.name = "Grab screen and blur";
		m_Cameras[cam] = buf;

		// copy screen into temporary RT
		int screenCopyID = Shader.PropertyToID("_ScreenCopyTexture");
		buf.GetTemporaryRT (screenCopyID, -1, -1, 0, FilterMode.Bilinear);
		buf.Blit (BuiltinRenderTextureType.CurrentActive, screenCopyID);

        // get two smaller RTs
        int blurredID = Shader.PropertyToID("_Temp1");
        int blurredID2 = Shader.PropertyToID("_Temp2");
		buf.GetTemporaryRT (blurredID, -2, -2, 0, FilterMode.Bilinear);
		buf.GetTemporaryRT (blurredID2, -2, -2, 0, FilterMode.Bilinear);
        int notBlurredID = Shader.PropertyToID("_Temp3");
        buf.GetTemporaryRT(notBlurredID, -2, -2, 0, FilterMode.Bilinear);

        // downsample screen copy into smaller RT, release screen RT
        buf.Blit(screenCopyID, blurredID);
        buf.Blit(screenCopyID, notBlurredID);
        buf.ReleaseTemporaryRT (screenCopyID);

        // horizontal blur
        for (int i = 0; i < Amount; i++)
        {
            buf.SetGlobalVector("offsets", new Vector4(BlurSize * i / Screen.width, 0, 0, 0));
            buf.Blit(blurredID, blurredID2, m_Material);
            // vertical blur
            buf.SetGlobalVector("offsets", new Vector4(0, BlurSize * i / Screen.height, 0, 0));
            buf.Blit(blurredID2, blurredID, m_Material);
        }
        /*// horizontal blur
		buf.SetGlobalVector("offsets", new Vector4(BlurSize * 2f/ Screen.width,0,0,0));
		buf.Blit (blurredID, blurredID2, m_Material);
		// vertical blur
		buf.SetGlobalVector("offsets", new Vector4(0, BlurSize * 2f/ Screen.height,0,0));
		buf.Blit (blurredID2, blurredID, m_Material);*/

        buf.SetGlobalTexture("_GrabBlurTexture", blurredID);
        buf.SetGlobalTexture("_GrabNotBlurTexture", notBlurredID);

        cam.AddCommandBuffer (CameraEvent.AfterSkybox, buf);
	}	
}
