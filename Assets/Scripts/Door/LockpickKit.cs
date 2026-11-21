using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

namespace VRSwat.Doors
{
    /// <summary>
    /// Bộ phá khóa: cầm kit lên, đứng sát ổ khóa, GIỮ cò thì thanh tiến trình chạy, đầy thanh là
    /// cửa hết khóa.
    ///
    /// Cò đọc qua <c>activated</c> / <c>deactivated</c> của XRI, giống Gun.cs, nên không cần tự
    /// bind InputAction: tay nào đang cầm thì cò tay đó tính.
    ///
    /// Mục tiêu tìm theo <see cref="DoorController.All"/> rồi lọc theo <see cref="DoorState.Locked"/>
    /// và khoảng cách tới <see cref="DoorController.LockPoint"/>, chứ không dùng trigger collider:
    /// ổ khóa là một điểm nên khỏi phải thêm collider cho từng cửa, và cửa đang khóa thì cánh
    /// không nhúc nhích nên điểm đó luôn đứng yên.
    ///
    /// Nhả cò hoặc đi ra xa thì tiến trình tụt dần (<c>decayPerSecond</c>) chứ không mất sạch
    /// ngay, để nhích tay một cái không phải làm lại từ đầu. Đổi sang cửa khác thì mất hết.
    /// </summary>
    [AddComponentMenu("VR SWAT/Lockpick Kit")]
    [DisallowMultipleComponent]
    public class LockpickKit : MonoBehaviour
    {
        public enum BarPlacement
        {
            Door,
            Kit
        }

        [Header("Tầm với")]
        [Tooltip("Đầu kit, dùng để đo khoảng cách tới ổ khóa. Để trống thì lấy chính object này.")]
        [SerializeField] private Transform tip;

        [Tooltip("Khoảng cách tối đa từ đầu kit tới ổ khóa (m).")]
        [SerializeField] private float range = 1f;

        [Header("Tiến trình")]
        [Tooltip("Giữ cò liên tục bao nhiêu giây thì đầy thanh.")]
        [SerializeField] private float unlockDuration = 4f;

        [Tooltip("Tốc độ tụt khi nhả cò hoặc ra ngoài tầm (phần thanh mỗi giây). 0 = mất sạch ngay.")]
        [SerializeField] private float decayPerSecond = 0.5f;

        [Header("Khi xong")]
        [Tooltip("Bật: cửa tự đẩy cánh ra sau khi hết khóa. Tắt: chỉ hết khóa, người chơi tự kéo.")]
        [SerializeField] private bool openAfterUnlock;

        [Header("Hiển thị")]
        [SerializeField] private UnlockProgressBar progressBar;

        [Tooltip("Thanh hiện ở ổ khóa hay ở đầu kit.")]
        [SerializeField] private BarPlacement barPlacement = BarPlacement.Door;

        [Header("Phản hồi")]
        [Tooltip("Tiếng lạch cạch lúc đang phá. Nên bật Loop trên AudioSource.")]
        [SerializeField] private AudioSource pickingLoop;

        [Tooltip("Tiếng cốc một cái khi khóa bật.")]
        [SerializeField] private AudioSource unlockedSfx;

        [Tooltip("Độ rung tay lúc đang phá. 0 = tắt.")]
        [SerializeField, Range(0f, 1f)] private float hapticAmplitude = 0.2f;

        /// <summary>Cửa đang bị phá, hoặc cửa phá dở đang tụt tiến trình. Null = không có.</summary>
        public DoorController Target { get; private set; }

        /// <summary>Tiến trình hiện tại, 0..1.</summary>
        public float Progress { get; private set; }

        /// <summary>Đang thực sự phá: có cửa khóa trong tầm và đang giữ cò.</summary>
        public bool IsPicking { get; private set; }

        public UnlockProgressBar ProgressBar
        {
            get => progressBar;
            set => progressBar = value;
        }

        public event Action<LockpickKit, DoorController> PickStarted;
        public event Action<LockpickKit, DoorController, float> PickProgress;
        public event Action<LockpickKit, DoorController> PickCancelled;
        public event Action<LockpickKit, DoorController> Unlocked;

        private XRBaseInteractable interactable;
        private bool triggerHeld;
        private float hapticTimer;

        private Transform Tip => tip != null ? tip : transform;
        private bool Held => interactable != null && interactable.isSelected;

        private void Awake()
        {
            interactable = GetComponent<XRBaseInteractable>();

            if (interactable == null)
                Debug.LogWarning(
                    $"{name}: thiếu XR Grab Interactable nên không đọc được cò. Thêm Hand Posed " +
                    "Grab Interactable (hoặc XR Grab Interactable) vào object này.", this);
        }

        private void OnEnable()
        {
            if (interactable == null)
                return;

            interactable.activated.AddListener(OnTriggerPressed);
            interactable.deactivated.AddListener(OnTriggerReleased);
            interactable.selectExited.AddListener(OnReleased);
        }

        private void OnDisable()
        {
            if (interactable != null)
            {
                interactable.activated.RemoveListener(OnTriggerPressed);
                interactable.deactivated.RemoveListener(OnTriggerReleased);
                interactable.selectExited.RemoveListener(OnReleased);
            }

            triggerHeld = false;
            StopPicking(false);
            Target = null;
            Progress = 0f;

            if (progressBar != null)
                progressBar.Hide();
        }

        private void OnTriggerPressed(ActivateEventArgs args) => triggerHeld = true;

        private void OnTriggerReleased(DeactivateEventArgs args) => triggerHeld = false;

        // Thả kit giữa lúc đang phá thì cò coi như nhả luôn, không thì lúc cầm lại nó chạy tiếp
        // dù người chơi chưa bóp gì.
        private void OnReleased(SelectExitEventArgs args) => triggerHeld = false;

        private void Update()
        {
            float dt = Time.deltaTime;
            DoorController inRange = Held && triggerHeld ? FindLockedDoorInRange() : null;

            // Đổi cửa thì bỏ tiến trình cũ.
            if (inRange != null && inRange != Target)
            {
                if (Target != null)
                    StopPicking(true);

                Target = inRange;
                Progress = 0f;
            }

            if (inRange != null)
            {
                if (!IsPicking)
                {
                    IsPicking = true;

                    if (pickingLoop != null && !pickingLoop.isPlaying)
                        pickingLoop.Play();

                    PickStarted?.Invoke(this, Target);
                }

                Progress += dt / Mathf.Max(0.01f, unlockDuration);
                Pulse(dt);

                if (Progress >= 1f)
                {
                    Complete();
                    return;
                }
            }
            else
            {
                if (IsPicking)
                    StopPicking(true);

                if (Target != null)
                {
                    // Cửa hết khóa bằng đường khác (bị breach, script khác gọi Unlock) thì thôi.
                    Progress = Target.State != DoorState.Locked || decayPerSecond <= 0f
                        ? 0f
                        : Progress - decayPerSecond * dt;

                    if (Progress <= 0f)
                    {
                        Progress = 0f;
                        Target = null;
                    }
                }
            }

            UpdateBar();

            if (Target != null)
                PickProgress?.Invoke(this, Target, Progress);
        }

        private void Complete()
        {
            DoorController door = Target;

            StopPicking(false);
            Target = null;
            Progress = 0f;

            door.Unlock();

            if (openAfterUnlock)
                door.Request(DoorAction.Open, this, Tip.position);

            if (unlockedSfx != null)
                unlockedSfx.Play();

            SendHaptic(0.6f, 0.12f);

            if (progressBar != null)
                progressBar.Hide();

            Unlocked?.Invoke(this, door);
        }

        private void StopPicking(bool notify)
        {
            if (!IsPicking)
                return;

            IsPicking = false;

            if (pickingLoop != null && pickingLoop.isPlaying)
                pickingLoop.Stop();

            if (notify && Target != null)
                PickCancelled?.Invoke(this, Target);
        }

        private DoorController FindLockedDoorInRange()
        {
            DoorController best = null;
            float bestSqr = range * range;
            Vector3 tipPos = Tip.position;
            var doors = DoorController.All;

            for (int i = 0; i < doors.Count; i++)
            {
                DoorController door = doors[i];

                if (door == null || door.State != DoorState.Locked)
                    continue;

                float sqr = (door.LockPoint.position - tipPos).sqrMagnitude;

                if (sqr > bestSqr)
                    continue;

                bestSqr = sqr;
                best = door;
            }

            return best;
        }

        private void UpdateBar()
        {
            if (progressBar == null)
                return;

            if (Target == null || Progress <= 0f)
            {
                progressBar.Hide();
                return;
            }

            progressBar.Show(barPlacement == BarPlacement.Kit ? Tip : Target.LockPoint, Progress);
        }

        private void Pulse(float dt)
        {
            if (hapticAmplitude <= 0f)
                return;

            hapticTimer -= dt;

            if (hapticTimer > 0f)
                return;

            hapticTimer = 0.1f;
            SendHaptic(hapticAmplitude, 0.05f);
        }

        private void SendHaptic(float amplitude, float duration)
        {
            if (interactable == null)
                return;

            var selecting = interactable.interactorsSelecting;

            for (int i = 0; i < selecting.Count; i++)
            {
                HapticImpulsePlayer player =
                    selecting[i].transform.GetComponentInParent<HapticImpulsePlayer>();

                if (player != null)
                    player.SendHapticImpulse(amplitude, duration);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(Tip.position, range);
        }
    }
}
