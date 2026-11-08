using UnityEngine;

namespace VRSwat.Tutorial
{
    // Gọi Emit() từ UnityEvent trong Inspector, ví dụ
    // XRGrabInteractable > Interactable Events > Select Entered.
    public class EmitSignal : MonoBehaviour
    {
        [SerializeField] string signalId = Sig.GrabWeapon;

        public void Emit() => TutorialSignals.Raise(signalId, gameObject, transform.position);
    }
}