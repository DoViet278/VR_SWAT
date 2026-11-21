using System;
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

        public event Action<DoorController, DoorState, DoorState> StateChanged; // (door, from, to)
        public event Action<DoorController> LockedRattle;                       // tay kéo cửa khóa
        public event Action<DoorController, Vector3> Breached;                  // điểm trúng

        object grabber;
        float velocity;            // độ/giây
        float? autoTarget;
        float breachTarget;
        float rattleCooldown;
        
        void Awake()
        {
            State = startLocked ? DoorState.Locked : DoorState.Closed;
            Angle = 0f;
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

        // ===== nội bộ =====
        // Người đứng phía +Z → góc dương (cửa đi về -Z, ra xa họ), và ngược lại.
        // Nếu phía đó bị tường chặn (min/max = 0) thì mở về phía còn lại.
        float AwayAngle(Vector3 worldPos, float amount)
        {
            bool fromFront = transform.InverseTransformPoint(worldPos).z >= 0f;
            float wanted = fromFront ? Mathf.Min(amount, maxAngle) : Mathf.Max(-amount, minAngle);
            if (Mathf.Abs(wanted) < openThreshold)
                wanted = fromFront ? Mathf.Max(-amount, minAngle) : Mathf.Min(amount, maxAngle);
            return wanted;
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
            foreach (var t in new[] { stackLeft, stackRight, clearLeft, clearRight, breachFrom })
                if (t) Gizmos.DrawWireSphere(t.position, 0.2f);
        }
    }
}
