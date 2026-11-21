using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KnifePlayerController
{
    public class FireRangeTargetButton : MonoBehaviour, IPlayerAction
    {
        public FireRangeTarget Target;
        public float Direction;
        public Material DefaultMaterial;
        public Material PressedMaterial;
        public MeshRenderer TargetMesh;
        public Vector3 DefaultPosition;
        public Vector3 PressedPosition;

        public void UseEnd()
        {
            Target.MoveEnd();
            TargetMesh.sharedMaterial = DefaultMaterial;
            transform.localPosition = DefaultPosition;
        }

        public void UseStart()
        {
            TargetMesh.sharedMaterial = PressedMaterial;
            transform.localPosition = PressedPosition;
        }

        public void UseUpdate()
        {
            Target.Move(Direction);
        }

        [ContextMenu("Set default pos")]
        void currentPositionAsDefault()
        {
            DefaultPosition = transform.localPosition;
        }

        [ContextMenu("Set pressed pos")]
        void currentPositionAsPressed()
        {
            PressedPosition = transform.localPosition;
        }
    }
}