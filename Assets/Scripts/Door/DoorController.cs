using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRSwat.Doors
{
    [DisallowMultipleComponent]
    public class DoorController : MonoBehaviour
    {
        [SerializeField] Transform hinge;
        [SerializeField] UnityEngine.AI.NavMeshObstacle lockBlocker;

        [Header("Angles")] [SerializeField] float minAngle = -100f;
        [SerializeField] float maxAngle = 100f;
        [SerializeField] float openThreshold = 5f;
        [SerializeField] float autoOpenAngle = 90f; 
        [SerializeField] float breachedAngle = 95f;
        
        [Header("Motion")]
        [SerializeField] float autoSpeed = 160f;
        [SerializeField] float breachSpeed = 600f;
        [SerializeField] float releaseDamping = 4f;
        [SerializeField] float maxReleaseSpeed = 250f;
        
        [Header("Start")]
        [SerializeField] bool startLocked;

        [Header("Ổ khóa")]
        [Tooltip("Vị trí ổ khóa: chỗ kit phá khóa và chỗ thanh tiến trình hiện ra. Để trống thì lấy gốc cửa.")]
        [SerializeField] Transform lockPoint;

        [Header("AI points (đặt cho phía đội thường tiến vào)")]
        public Transform stackLeft, stackRight;
        public Transform clearLeft, clearRight;
        public Transform breachFrom, breachAim;
        
        public DoorState State { get; private set; }
        public float Angle { get; private set; }          
        public bool IsPassable => State != DoorState.Locked;
        public bool IsGrabbed => grabber != null;
        public bool IsMoving => autoTarget.HasValue;
        public UnityEngine.Object Occupant { get; private set; }
        public Vector3 HingeLocalPosition => transform.InverseTransformPoint(hinge.position);
        public Transform LockPoint => lockPoint ? lockPoint : transform;

        public event Action<DoorController, DoorState, DoorState> StateChanged; // (door, from, to)
        public event Action<DoorController> LockedRattle;                       // tay kéo cửa khóa
        public event Action<DoorController, Vector3> Breached;                  // điểm trúng

        object grabber;
        float velocity;            // độ/giây
        float? autoTarget;
        float breachTarget;
        float rattleCooldown;
        // +1: góc dương đưa cánh cửa về -Z (cánh nằm phía +X của bản lề). -1: ngược lại (cánh nằm phía -X).
        float swingSign = 1f;
        
        // Danh sách cửa đang sống trong scene, để LockpickKit tìm ổ khóa gần nhất mà không
        // phải quét toàn scene mỗi frame.
        static readonly List<DoorController> all = new List<DoorController>();
        public static IReadOnlyList<DoorController> All => all;

        void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
        }

        void OnDisable()
        {
            all.Remove(this);
        }

        void Awake()
        {
            State = startLocked ? DoorState.Locked : DoorState.Closed;
            Angle = 0f;
            swingSign = ComputeSwingSign();
            ApplyAngle();
            ApplyNavigation();
        }
        
        void Update()
        {
            float dt = Time.deltaTime;
            rattleCooldown -= dt;

            switch (State)
            {
                case DoorState.Locked:
                    Angle = 0f; velocity = 0f;
                    break;
                case DoorState.Breached:
                    Angle = Mathf.MoveTowards(Angle, breachTarget, breachSpeed * dt);
                    break;
                default:
                    TickFree(dt);
                    break;
            }
            ApplyAngle();
        }
        
        void TickFree(float dt)
        {
            if (IsGrabbed)
            {
                // Angle đã được đặt trong ProposeAngle()
            }
            else if (autoTarget.HasValue)
            {
                Angle = Mathf.MoveTowards(Angle, autoTarget.Value, autoSpeed * dt);
                if (Mathf.Approximately(Angle, autoTarget.Value)) autoTarget = null;
                velocity = 0f;
            }
            else
            {
                Angle += velocity * dt;
                velocity *= Mathf.Exp(-releaseDamping * dt);
                if (Angle <= minAngle || Angle >= maxAngle) velocity = 0f;
                Angle = Mathf.Clamp(Angle, minAngle, maxAngle);

                // trong vùng chốt và không còn bay ra xa 0 → hút về chốt
                bool movingAway = Angle * velocity > 0.5f;
                if (Mathf.Abs(Angle) < openThreshold && !movingAway) { Angle = 0f; velocity = 0f; }
            }

            if (State == DoorState.Closed && Mathf.Abs(Angle) > openThreshold) SetState(DoorState.Open);
            else if (State == DoorState.Open && Angle == 0f && !IsGrabbed) SetState(DoorState.Closed);
        }
        
        public bool BeginGrab(object who)
        {
            if (State == DoorState.Breached || IsGrabbed) return false;
            grabber = who;
            autoTarget = null;   // người chơi giành quyền khỏi AI
            Occupant = null;
            return true;
        }
        
        public bool ProposeAngle(object who, float angle)
        {
            if (grabber != who) return false;
            if (State == DoorState.Locked)
            {
                if (Mathf.Abs(angle) > 3f && rattleCooldown <= 0f)
                {
                    rattleCooldown = 0.6f;
                    LockedRattle?.Invoke(this);
                }
                return false;
            }
            if (State == DoorState.Breached) return false;

            float next = Mathf.Clamp(angle, minAngle, maxAngle);
            float v = (next - Angle) / Mathf.Max(Time.deltaTime, 0.0001f);
            velocity = Mathf.Clamp(Mathf.Lerp(velocity, v, 0.5f), -maxReleaseSpeed, maxReleaseSpeed);
            Angle = next;
            return true;
        }
        
        public void EndGrab(object who)
        {
            if (grabber == who) grabber = null;
        }
        
        public DoorRequestResult Request(DoorAction action, UnityEngine.Object actor, Vector3 from)
        {
            if (Occupant != null && Occupant != actor) return DoorRequestResult.Busy;

            switch (action)
            {
                case DoorAction.Open:
                    if (State == DoorState.Locked) return DoorRequestResult.Refused;
                    if (State == DoorState.Breached) return DoorRequestResult.Accepted;
                    if (IsGrabbed) return DoorRequestResult.Busy;
                    StartAuto(AwayAngle(from, autoOpenAngle), actor);
                    return DoorRequestResult.Accepted;

                case DoorAction.Close:
                    if (State == DoorState.Breached) return DoorRequestResult.Refused;
                    if (IsGrabbed) return DoorRequestResult.Busy;
                    if (State == DoorState.Open) StartAuto(0f, actor);
                    return DoorRequestResult.Accepted;

                case DoorAction.Breach:
                    if (State != DoorState.Closed && State != DoorState.Locked) return DoorRequestResult.Refused;
                    Occupant = actor;   // giữ cửa, AI tự ngắm rồi gọi Breach()
                    return DoorRequestResult.Accepted;
            }
            return DoorRequestResult.Refused;
        }
        
        public void Release(UnityEngine.Object actor)
        {
            if (Occupant == actor) Occupant = null;
        }
        
        // shooterPos = vị trí nòng súng; cửa văng ra xa người bắn
        public bool Breach(Vector3 hitPoint, Vector3 shooterPos)
        {
            if (State != DoorState.Closed && State != DoorState.Locked) return false;
            grabber = null; autoTarget = null; velocity = 0f; Occupant = null;
            breachTarget = AwayAngle(shooterPos, breachedAngle);
            SetState(DoorState.Breached);
            Breached?.Invoke(this, hitPoint);
            return true;
        }

        public void Lock()   { if (State == DoorState.Closed) SetState(DoorState.Locked); }
        public void Unlock() { if (State == DoorState.Locked) SetState(DoorState.Closed); }

        /// <summary>Cánh cửa nằm phía nào của bản lề theo trục X local của cửa (+1 / -1).</summary>
        public float LeafAxisSign => swingSign;

        /// <summary>
        /// Phía (local Z của cửa) mà cánh cửa đang quay sang: -1 = về -Z, +1 = về +Z, 0 = đang đóng.
        /// Đã tính chiều cánh cửa so với bản lề, nên đúng với cả cửa bản lề trái lẫn phải.
        /// </summary>
        public int LeafSide => Mathf.Abs(Angle) < 0.01f ? 0 : (Angle * swingSign > 0f ? -1 : 1);

        // ===== nội bộ =====
        // Mở ra XA người đứng ở worldPos (đẩy vào trong): người phía +Z → cánh đi về -Z, và ngược lại.
        // Nếu phía đó bị tường chặn (min/max = 0) thì mở về phía còn lại.
        float AwayAngle(Vector3 worldPos, float amount)
        {
            bool fromFront = transform.InverseTransformPoint(worldPos).z >= 0f;
            // Góc dấu swingSign đưa cánh về -Z.
            bool positive = fromFront ? swingSign > 0f : swingSign < 0f;
            float wanted = positive ? Mathf.Min(amount, maxAngle) : Mathf.Max(-amount, minAngle);
            if (Mathf.Abs(wanted) < openThreshold)
                wanted = positive ? Mathf.Max(-amount, minAngle) : Mathf.Min(amount, maxAngle);
            return wanted;
        }

        /// <summary>
        /// Cánh cửa (renderer con của hinge) nằm phía nào của bản lề theo trục X của cửa.
        /// Quay +Y: +X đi về -Z, -X đi về +Z, nên cánh nằm phía -X thì phải đảo dấu góc.
        /// </summary>
        float ComputeSwingSign()
        {
            if (hinge == null) return 1f;
            Renderer[] renderers = hinge.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 1f;
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            float dx = transform.InverseTransformPoint(b.center).x - transform.InverseTransformPoint(hinge.position).x;
            return dx >= 0f ? 1f : -1f;
        }

        void StartAuto(float target, UnityEngine.Object actor)
        {
            autoTarget = target;
            Occupant = actor;
        }

        void SetState(DoorState next)
        {
            if (next == State) return;
            var prev = State;
            State = next;
            ApplyNavigation();
            StateChanged?.Invoke(this, prev, next);
        }

        void ApplyAngle() => hinge.localRotation = Quaternion.Euler(0f, Angle, 0f);

        void ApplyNavigation()
        {
            if (lockBlocker) lockBlocker.enabled = State == DoorState.Locked;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            foreach (var t in new[] { stackLeft, stackRight, clearLeft, clearRight, breachFrom, lockPoint })
                if (t) Gizmos.DrawWireSphere(t.position, 0.2f);
        }
    }
}
