using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;   // XRI 3.x, kiểm tra lại theo bản đang dùng

namespace VRSwat.Tutorial
{
    public class TutorialHint : MonoBehaviour
    {
        [Tooltip("Bóng mờ làm mẫu, Animator có trigger \"Play\"")]
        [SerializeField] Animator ghost;
        [SerializeField] HapticImpulsePlayer hapticHand;   // tay cần dùng
        [SerializeField] float hapticAmplitude = 0.4f, hapticDuration = 0.15f;
        public UnityEvent onReplay;

        static readonly int PlayHash = Animator.StringToHash("Play");

        public void Show()
        {
            gameObject.SetActive(true);
            Replay();
        }

        public void Replay()
        {
            if (ghost)
            {
                ghost.gameObject.SetActive(true);
                ghost.SetTrigger(PlayHash);
            }
            if (hapticHand) hapticHand.SendHapticImpulse(hapticAmplitude, hapticDuration);
            onReplay.Invoke();
        }

        public void Hide()
        {
            if (ghost) ghost.gameObject.SetActive(false);
            gameObject.SetActive(false);
        }
    }
}