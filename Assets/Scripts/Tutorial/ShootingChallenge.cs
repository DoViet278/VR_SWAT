using System;
using UnityEngine;
using UnityEngine.Events;
using static CONST;

// Màn bắn bia: hoàn thành khi mọi LightTarget trong danh sách đều đủ số phát.
// Ví dụ: Target 1 = Pistol, Target 2 = Rifle, Target 3 = Shotgun, mỗi bia 3 phát.
public class ShootingChallenge : MonoBehaviour
{
    [SerializeField] private LightTarget[] targets;

    public UnityEvent onAllCompleted;

    public bool IsCompleted { get; private set; }

    private void OnEnable()
    {
        ObserverManager.Register(TARGET_COMPLETED, (Action<LightTarget>)OnTargetCompleted);
    }

    private void OnDisable()
    {
        ObserverManager.Unregister(TARGET_COMPLETED, (Action<LightTarget>)OnTargetCompleted);
    }

    private void OnTargetCompleted(LightTarget target)
    {
        if (IsCompleted || Array.IndexOf(targets, target) < 0) return;

        foreach (var t in targets)
            if (t != null && !t.IsCompleted) return;

        IsCompleted = true;
        Debug.Log($"[ShootingChallenge] {name}: hoàn thành tất cả bia");
        onAllCompleted?.Invoke();
    }

    public void ResetChallenge()
    {
        IsCompleted = false;
        foreach (var t in targets)
            if (t != null) t.ResetTarget();
    }
}
