using Unity;
using UnityEngine;

namespace VRSwat.Audio
{
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance;
        [SerializeField] protected AudioSource musicSource;
        [SerializeField] protected AudioSource sfxSource;
        [SerializeField] protected AudioSource voiceSource;



    }
}