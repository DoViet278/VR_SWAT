using UnityEngine;
using System.Collections;
using UnityEngine.Events;
using System.Collections.Generic;

namespace KnifePlayerController
{
    public class WaypointFollower : MonoBehaviour
    {
        public Transform[] Path;
        public float Speed = 0.35f;
        public float RotationSpeed = 60f;
        public float WaypointPauseMin = 1f;
        public float WaypointPauseMax = 1.5f;
        public List<PathFractionEvent> Events;
        public UnityEvent EndWayEvent = new UnityEvent();
        [Range(0f, 1f)]
        public float FractionTest;

        [System.Serializable]
        public class PathFractionEvent
        {
            public float FractionValue;
            public bool Used = false;
            public UnityEvent Event = new UnityEvent();
        }

        bool isMoving = false;
        int targetWaypoint = 0;

        public void StartMove()
        {
            if (isMoving)
                return;

            isMoving = true;
            targetWaypoint = 0;
            transform.position = Path[targetWaypoint].position;
            targetWaypoint = 1;

            Vector3 targetDirection = Path[targetWaypoint].position - transform.position;
            targetDirection.y = 0;
            targetDirection.Normalize();

            Quaternion targetRotation = Quaternion.LookRotation(targetDirection, Vector3.up);
            transform.rotation = targetRotation;

            StartCoroutine(moving());
        }

        public void AddEvent(float fraction, UnityAction action)
        {
            PathFractionEvent pathFractionEvent = new PathFractionEvent();
            pathFractionEvent.FractionValue = fraction;
            pathFractionEvent.Event = new UnityEvent();
            pathFractionEvent.Event.AddListener(action);
            Events.Add(pathFractionEvent);
        }

        private void Update()
        {
            if(isMoving)
            {
                int lastPoint = targetWaypoint - 1;
                int nextWaypoint = targetWaypoint;

                lastPoint = Mathf.Clamp(lastPoint, 0, Path.Length - 1);
                nextWaypoint = Mathf.Clamp(nextWaypoint, 0, Path.Length - 1);

                float localFraction = InverseLerp(Path[lastPoint].position, Path[nextWaypoint].position, transform.position);
                float fraction1 = indexToFraction(lastPoint);
                float fraction2 = indexToFraction(nextWaypoint);

                float fraction = localFraction * (fraction2 - fraction1) + fraction1;
                foreach(PathFractionEvent e in Events)
                {
                    if (fraction < e.FractionValue)
                        e.Used = false;

                    if(fraction >= e.FractionValue && !e.Used)
                    {
                        e.Used = true;
                        e.Event.Invoke();
                    }
                }
            }
        }

        public static float InverseLerp(Vector3 a, Vector3 b, Vector3 value)
        {
            Vector3 AB = b - a;
            Vector3 AV = value - a;
            return Vector3.Dot(AV, AB) / Vector3.Dot(AB, AB);
        }

        IEnumerator moving()
        {
            while (true)
            {
                yield return StartCoroutine(rotating());

                transform.position = Vector3.MoveTowards(transform.position, Path[targetWaypoint].position, Time.deltaTime * Speed);
                float distance = Vector3.Distance(transform.position, Path[targetWaypoint].position);

                if(distance < 0.02f)
                {
                    transform.position = Path[targetWaypoint].position;
                    targetWaypoint++;
                    yield return StartCoroutine(pause());
                }

                if(targetWaypoint >= Path.Length)
                {
                    isMoving = false;
                    EndWayEvent.Invoke();
                    yield break;
                }

                yield return null;
            }
        }

        IEnumerator rotating()
        {
            Vector3 targetDirection = Path[targetWaypoint].position - transform.position;
            targetDirection.y = 0;
            targetDirection.Normalize();

            Quaternion targetRotation = Quaternion.LookRotation(targetDirection, Vector3.up);

            float deltaAngle = Quaternion.Angle(targetRotation, transform.rotation);
            while(deltaAngle > 1f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, Time.deltaTime * RotationSpeed);
                deltaAngle = Quaternion.Angle(targetRotation, transform.rotation);
                yield return null;
            }
            transform.rotation = targetRotation;
        }

        IEnumerator pause()
        {
            yield return new WaitForSeconds(Random.Range(WaypointPauseMin, WaypointPauseMax));
        }

        int fractionToIndex(float fraction)
        {
            return (int)((Path.Length - 1) * fraction);
        }

        float indexToFraction(int index)
        {
            return (float)index / (Path.Length - 1);
        }

        Vector3 getPositionByFraction(float fraction)
        {
            int index1 = fractionToIndex(fraction);
            int index2 = index1 + 1;
            index2 = Mathf.Clamp(index2, 0, Path.Length - 1);

            if (index1 == index2)
                return Path[index1].position;

            float fraction1 = indexToFraction(index1);
            float fraction2 = indexToFraction(index2);

            float localFraction = (fraction - fraction1) / (fraction2 - fraction1);

            return Vector3.Lerp(Path[index1].position, Path[index2].position, localFraction);
        }

        private void OnDrawGizmos()
        {
            if (Path == null || Path.Length < 2)
                return;

            for (int i = 0; i < Path.Length - 1; i++)
            {
                Vector3 p1 = Path[i].position;
                Vector3 p2 = Path[i + 1].position;

                Gizmos.color = Color.red;
                Gizmos.DrawLine(p1, p2);
            }

            Vector3 fractionTestPosition = getPositionByFraction(FractionTest);

            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(fractionTestPosition, 0.25f);
        }
    }
}