using System.Collections;
using UnityEngine;

namespace VRSwat.Doors
{
    public class DoorLockLight : MonoBehaviour
    {
        [SerializeField] DoorController door;
        [SerializeField] Renderer lamp;
        [SerializeField] Material lockedMat, unlockedMat, offMat;   // 3 material emissive có sẵn
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip rattleClip, unlockClip, breachClip;

        Coroutine blink;

        void Awake()
        {
            if (!door) door = GetComponentInParent<DoorController>();
        }

        void OnEnable()
        {
            door.StateChanged += OnStateChanged;
            door.LockedRattle += OnRattle;
            Refresh();
        }

        void OnDisable()
        {
            door.StateChanged -= OnStateChanged;
            door.LockedRattle -= OnRattle;
        }

        void Start() => Refresh();   // chạy sau Awake của DoorController, nên đèn đúng màu ngay từ đầu

        void OnStateChanged(DoorController d, DoorState from, DoorState to)
        {
            if (from == DoorState.Locked && to == DoorState.Closed) Play(unlockClip);
            if (to == DoorState.Breached) Play(breachClip);
            Refresh();
        }

        void OnRattle(DoorController d)
        {
            Play(rattleClip);
            if (blink != null) StopCoroutine(blink);
            blink = StartCoroutine(Blink());
        }

        IEnumerator Blink()
        {
            for (int i = 0; i < 3; i++)
            {
                lamp.sharedMaterial = offMat;    yield return new WaitForSeconds(0.08f);
                lamp.sharedMaterial = lockedMat; yield return new WaitForSeconds(0.08f);
            }
            blink = null;
            Refresh();
        }

        void Refresh()
        {
            if (!lamp) return;
            lamp.sharedMaterial = door.State switch
            {
                DoorState.Locked => lockedMat,
                DoorState.Breached => offMat,
                _ => unlockedMat
            };
        }

        void Play(AudioClip clip)
        {
            if (audioSource && clip) audioSource.PlayOneShot(clip);
        }
    }
}