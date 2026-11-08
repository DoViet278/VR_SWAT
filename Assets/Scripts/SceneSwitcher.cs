using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Switch between scenes with keyboard shortcuts.
/// Scenes must be added to File > Build Profiles (Build Settings) to be loadable.
/// </summary>
public class SceneSwitcher : MonoBehaviour
{
    [Serializable]
    public struct SceneShortcut
    {
        public KeyCode key;
        public string sceneName;
    }

    [SerializeField] private SceneShortcut[] shortcuts =
    {
        new SceneShortcut { key = KeyCode.F1, sceneName = "SampleScene" },
        new SceneShortcut { key = KeyCode.F2, sceneName = "BasicScene" },
        new SceneShortcut { key = KeyCode.F3, sceneName = "Duong's Scene" },
    };

    [SerializeField] private KeyCode reloadKey = KeyCode.F5;
    [SerializeField] private bool persistAcrossScenes = true;

    private static SceneSwitcher instance;

    private void Awake()
    {
        if (persistAcrossScenes)
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
    }

    private void Update()
    {
        bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (ctrlHeld && Input.GetKeyDown(reloadKey))
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
        }

        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (!ctrl) return;

        foreach (var s in shortcuts)
        {
            if (Input.GetKeyDown(s.key))
            {
                if (Application.CanStreamedLevelBeLoaded(s.sceneName))
                    SceneManager.LoadScene(s.sceneName);
                else
                    Debug.LogWarning($"SceneSwitcher: scene '{s.sceneName}' is not in Build Settings.");
                return;
            }
        }
    }
}
