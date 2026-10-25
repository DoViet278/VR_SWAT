using UnityEngine;
using System.Collections;
using Unity.Collections;
using Unity.Jobs;

namespace KnifePlayerController
{
    public class SphereRaycastCommandTest : MonoBehaviour
    {
        public float Radius;
        public int CircleResolution = 8;
        public int SphereResolution = 4;
        public Color GizmosColor;
        public LayerMask Layer;
        public bool DrawTestGizmos = false;
        public int MaxHits = 2;

        SphereRaycastCommand commandExecuter;
        JobHandle jobHandle;

        public SphereRaycastCommand CommandExecuter
        {
            get
            {
                if (commandExecuter == null)
                    commandExecuter = new SphereRaycastCommand();

                return commandExecuter;
            }

            set
            {
                commandExecuter = value;
            }
        }

        private void Awake()
        {
            CommandExecuter.Radius = Radius;
            CommandExecuter.CircleResolution = CircleResolution;
            CommandExecuter.SphereResolution = SphereResolution;
            CommandExecuter.Center = transform.position;
            CommandExecuter.Layer = Layer;
            CommandExecuter.MaxHits = MaxHits;
            CommandExecuter.Prepare();
        }

        [ContextMenu("Test max hits")]
        void test()
        {
            int maxHits = 5;

            NativeArray<RaycastCommand> commands = new NativeArray<RaycastCommand>(1, Allocator.Temp);
            NativeArray<RaycastHit> result = new NativeArray<RaycastHit>(maxHits, Allocator.Temp);

            commands[0] = new RaycastCommand(transform.position, Vector3.forward, 999, Layer, maxHits);

            JobHandle jobHandle = RaycastCommand.ScheduleBatch(commands, result, 1);
            jobHandle.Complete();

            for (int i = 0; i < commands.Length; i++)
            {
                for (int j = 0; j < maxHits; j++)
                {
                    RaycastHit hit = result[i * MaxHits + j];

                    if (hit.collider == null)
                    {
                        Debug.Log("NULL");
                    }
                    else
                    {
                        Debug.Log(hit.collider.name);
                    }
                }
            }

            commands.Dispose();
            result.Dispose();
        }

        private void Update()
        {
            bool executed = CommandExecuter.WaitJobComplete();

            if (executed)
            {
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    cast();
                }
            }
            else
            {
                //Debug.Log("WAIT");
            }
        }

        private void OnDestroy()
        {
            CommandExecuter.Dispose();
            jobHandle.Complete();
        }

        void cast()
        {
            CommandExecuter.Radius = Radius;
            CommandExecuter.CircleResolution = CircleResolution;
            CommandExecuter.SphereResolution = SphereResolution;
            CommandExecuter.Center = transform.position;
            CommandExecuter.Layer = Layer;
            CommandExecuter.MaxHits = MaxHits;
            CommandExecuter.Cast(resultHandler);
        }

        void resultHandler(NativeArray<RaycastHit> raycastHits)
        {
            RaycastHit[] hits;
            hits = raycastHits.ToArray();
            int raysNum = CircleResolution * SphereResolution;
            for (int i = 0; i < raysNum; i++)
            {
                for (int j = 0; j < MaxHits; j++)
                {
                    RaycastHit hit = hits[(i * MaxHits) + j];
                    if (hit.collider == null)
                    {
                        break;
                    }
                    else
                    {
                        if (j == 0)
                        {
                            RaycastCommand command = CommandExecuter.GetCommandByIndex(i);
                            //float realDistance = Vector3.Distance(command.from, hit.point);
                            //Debug.Log(command.distance + " " + hit.distance + " " + realDistance);
                            Debug.DrawLine(command.from, hit.point, Color.red, 10f);
                        }
                        else
                        {
                            RaycastCommand command = CommandExecuter.GetCommandByIndex(i);
                            RaycastHit prevHit = hits[(i * MaxHits) + (j - 1)];
                            float realDistance = Vector3.Distance(prevHit.point, hit.point);
                            Debug.Log(command.distance + " " + hit.distance + " " + realDistance);
                            Debug.DrawLine(prevHit.point, hit.point, Color.green, 10f);
                        }
                    }
                }
            }


            CommandExecuter.Dispose();
        }

        private void OnDrawGizmos()
        {
            if (DrawTestGizmos)
            {
                Gizmos.color = GizmosColor;

                CommandExecuter.Radius = Radius;
                CommandExecuter.CircleResolution = CircleResolution;
                CommandExecuter.SphereResolution = SphereResolution;
                CommandExecuter.Center = transform.position;

                CommandExecuter.DrawGizmosLines();
            }
        }
    }
}