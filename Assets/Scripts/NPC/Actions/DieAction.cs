using System;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;
using Unity.Properties;
using Random = UnityEngine.Random;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Die", story: "Die", category: "Action", id: "6df9c1bdd35bdb628782192f0ecaac0f")]
public partial class DieAction : Action
{
    [SerializeReference] public BlackboardVariable<float> DestroyDelay = new(5f);

    // Controller NPC_M4A1 có 4 trigger chết, không có trigger "Die".
    private static readonly int[] DieTriggers =
    {
        Animator.StringToHash("DieFront"), Animator.StringToHash("DieBack"),
        Animator.StringToHash("DieLeft"), Animator.StringToHash("DieRight"),
    };

    protected override Status OnStart()
    {
        Kill(GameObject, DestroyDelay.Value);
        return Status.Running;
    }

    // Giữ Running để logic chết chỉ chạy một lần.
    protected override Status OnUpdate() => Status.Running;

    /// <summary>
    /// Dừng di chuyển, tắt collider, phát anim chết theo hướng trúng đòn và hủy object sau destroyDelay giây.
    /// NPC gọi trực tiếp khi chết lúc đã đầu hàng (Behavior Graph đã tắt).
    /// </summary>
    public static void Kill(GameObject npc, float destroyDelay)
    {
        if (npc.TryGetComponent(out NPCLocomotion locomotion))
            locomotion.enabled = false;

        if (NPCActionUtils.TryGetAgent(npc, out NavMeshAgent agent))
            agent.isStopped = true;
        if (agent != null)
            agent.enabled = false;

        foreach (Collider col in npc.GetComponentsInChildren<Collider>())
            col.enabled = false;

        // Chết theo hướng trúng đòn cuối (NPC.LastHitSide), không có NPC thì chọn ngẫu nhiên.
        Animator animator = npc.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.SetTrigger(npc.TryGetComponent(out NPC stats)
                ? Animator.StringToHash("Die" + stats.LastHitSide)
                : DieTriggers[Random.Range(0, DieTriggers.Length)]);
        }

        UnityEngine.Object.Destroy(npc, destroyDelay);
    }
}
