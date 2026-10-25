using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KnifePlayerController
{
    [CustomEditor(typeof(PlayerController))]
    public class PlayerControllerEditor : Editor
    {
        Texture2D logoTexture;

        public override void OnInspectorGUI()
        {
            loadResources();

            float ratio = ((float)logoTexture.width) / (float)logoTexture.height;
            
            float width = EditorGUIUtility.currentViewWidth * 0.92f;
            float height = width / ratio;

            GUILayoutOption widthOption = GUILayout.MaxWidth(width);
            GUILayoutOption heightOption = GUILayout.MaxHeight(height);

            GUILayout.Label(logoTexture, widthOption, heightOption);

            base.OnInspectorGUI();
        }


        void loadResources()
        {
            if (logoTexture == null)
                logoTexture = Resources.Load<Texture2D>("PlayerControllerLogo");
        }

        private void OnDestroy()
        {
            if (logoTexture != null)
            {
                Resources.UnloadAsset(logoTexture);
            }
        }
    }
}