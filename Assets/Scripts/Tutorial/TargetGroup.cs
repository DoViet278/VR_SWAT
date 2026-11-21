using System;
using UnityEngine;

namespace VRSwat.Tutorial
{
    // Đặt trên object cha của một cụm bia. Bia bật lên nhiều lần thì mỗi lần hạ đều tính.
    public class TargetGroup : MonoBehaviour
    {
        public int Hits { get; private set; }
        public int EnemyCount { get; private set; }

        ITutorialTarget[] targets;

        void Awake()
        {
            targets = GetComponentsInChildren<ITutorialTarget>(true);
            foreach (var t in targets)
            {
                t.Knocked += OnKnocked;
                if (!t.IsCivilian) EnemyCount++;
            }
        }

        void OnDestroy()
        {
            if (targets == null) return;
            foreach (var t in targets) t.Knocked -= OnKnocked;
        }

        void OnKnocked(ITutorialTarget t)
        {
            if (t.IsCivilian) TutorialSignals.Raise(Sig.CivilianHit, this);
            else Hits++;
        }
    }
}