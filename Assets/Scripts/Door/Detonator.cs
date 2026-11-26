using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

namespace VRSwat.Doors
{
    /// <summary>
    /// Kíp nổ cầm tay: bóp cò là mọi <see cref="BreachCharge"/> đã dán lên cửa nổ cùng lúc.
    ///
    /// Cò đọc qua <c>activated</c> của XRI, giống Gun.cs và <see cref="LockpickKit"/>, nên tay
    /// nào đang cầm thì cò tay đó tính. Ở đây chỉ cần <c>activated</c> chứ không cần
    /// <c>deactivated</c>/<c>selectExited</c> như LockpickKit: kích nổ là hành động tức thời
    /// một nhịp, không có trạng thái "đang giữ" nào để phải dọn khi thả tay.
    ///
    /// Nổ đồng loạt chứ không nổ từng quả: đặt hai khối rồi phải bóp hai lần là sai nghiệp vụ,
    /// và cũng không có cách nào cho người chơi chọn quả nào trước.
    /// </summary>
    [AddComponentMenu("VR SWAT/Detonator")]
    [DisallowMultipleComponent]
    public class Detonator : MonoBehaviour
    {
        [Header("Âm thanh")]
        [Tooltip("Tiếng bấm kíp khi có bom nổ.")]
        [SerializeField] private AudioSource fireSfx;

        [Tooltip("Tiếng lạch cạch khi bấm mà chưa dán quả nào. Thiếu phản hồi này thì người chơi " +
                 "không phân biệt được là mình quên đặt bom hay kíp hỏng.")]
        [SerializeField] private AudioSource emptyClick;

        [Header("Phản hồi")]
        [SerializeField, Range(0f, 1f)] private float hapticAmplitude = 0.5f;
        [SerializeField] private float hapticDuration = 0.1f;

        /// <summary>Bắn ra sau mỗi lần bóp cò. int = số quả đã nhận lệnh nổ.</summary>
        public event Action<Detonator, int> Fired;

        /// <summary>
        /// Kíp đang sống trong scene. <see cref="DetonatorSpawner"/> đọc danh sách này để khỏi
        /// sinh thêm kíp khi người chơi đã có một cái trong tay (hoặc màn chơi đặt sẵn một cái).
        /// </summary>
        public static IReadOnlyList<Detonator> All => all;

        private static readonly List<Detonator> all = new List<Detonator>();

        private XRBaseInteractable interactable;

        private bool Held => interactable != null && interactable.isSelected;

        private void Awake()
        {
            interactable = GetComponent<XRBaseInteractable>();

            if (interactable == null)
                Debug.LogWarning(
                    $"{name}: thiếu XR Grab Interactable nên không đọc được cò. Thêm XR Grab " +
                    "Interactable (hoặc Hand Posed Grab Interactable) vào object này.", this);
        }

        private void OnEnable()
        {
            if (!all.Contains(this))
                all.Add(this);

            if (interactable != null)
                interactable.activated.AddListener(OnTriggerPressed);
        }

        private void OnDisable()
        {
            all.Remove(this);

            if (interactable != null)
                interactable.activated.RemoveListener(OnTriggerPressed);
        }

        private void OnTriggerPressed(ActivateEventArgs args)
        {
            if (!Held)
                return;

            int fired = DetonateAll();

            if (fired > 0)
            {
                if (fireSfx != null)
                    fireSfx.Play();

                SendHaptic(hapticAmplitude, hapticDuration);
            }
            else
            {
                if (emptyClick != null)
                    emptyClick.Play();

                SendHaptic(hapticAmplitude * 0.3f, 0.04f);
            }

            Fired?.Invoke(this, fired);
        }

        private static int DetonateAll()
        {
            var charges = BreachCharge.All;
            int fired = 0;

            // Duyệt ngược: quả bom tự hủy khi nổ, nên nó rời khỏi danh sách ngay trong lúc duyệt.
            for (int i = charges.Count - 1; i >= 0; i--)
            {
                BreachCharge charge = charges[i];

                if (charge != null && charge.Detonate())
                    fired++;
            }

            return fired;
        }

        private void SendHaptic(float amplitude, float duration)
        {
            if (interactable == null || amplitude <= 0f)
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
    }
}
