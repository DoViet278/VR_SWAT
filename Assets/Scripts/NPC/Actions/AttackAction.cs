using System;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;
using Unity.Properties;
using Random = UnityEngine.Random;

/// <summary>
/// Bắn một loạt vào Target rồi trả Success để graph đánh giá lại.
/// - Đang nấp: ngồi chờ tới CoverReadyTime, đứng dậy (chờ StandUpTime), bắn random 2-4 phát,
///   ngồi xuống lại và nấp random 3-5 giây rồi mới đứng dậy bắn tiếp (chỉnh ở NPC > Cover combat).
/// - Ngoài trống: đứng lại, quay mặt về Target rồi bắn. Bị chắn tầm nhìn thì tiến lại gần (cần NavMesh).
/// Sát thương gửi bằng SendMessage("TakeDamage", float), khớp NPC.TakeDamage và script máu của Player sau này.
/// Có NPCWeaponHolder: bắn bằng súng thật (đạn, chớp lửa, tiếng súng). Hết đạn thì thay đạn ngay
/// (đang nấp thì ngồi xuống thay rồi mới đứng dậy bắn tiếp). Ngồi nấp mà băng còn ít đạn thì thay sớm.
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Attack", story: "Attack [Target] within [AttackRange]", category: "Action", id: "0e21bc41473634158931fd058b5dc48c")]
public partial class AttackAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Target;
    [Tooltip("Nên nối cùng biến với AttackRange của CanAttack.")]
    [SerializeReference] public BlackboardVariable<float> AttackRange = new(10f);
    [SerializeReference] public BlackboardVariable<float> Damage = new(10f);
    [SerializeReference] public BlackboardVariable<float> FireInterval = new(0.3f);
    [SerializeReference] public BlackboardVariable<int> ShotsPerBurst = new(5);
    [Tooltip("Xác suất trúng mỗi phát (0-1).")]
    [SerializeReference] public BlackboardVariable<float> Accuracy = new(0.6f);

    private const float AimTolerance = 15f;
    private static readonly int FireHash = Animator.StringToHash("Fire");

    private NavMeshAgent _agent;
    private Animator _animator;
    private NPC _npc;
    private NPCWeaponHolder _holder;
    private int _shots;
    private int _burst;
    private float _nextFire;
    private bool _fromCover;
    private bool _hidden;
    private float _standAt;

    protected override Status OnStart()
    {
        _npc = NPCConditionUtils.StatsOf(GameObject);
        GameObject target = NPCConditionUtils.ResolveTarget(Target, _npc);
        if (target == null)
            return Status.Failure;

        // Agent là tùy chọn: không có NavMesh thì NPC vẫn đứng bắn tại chỗ.
        NPCActionUtils.TryGetAgent(GameObject, out _agent);
        _animator = GameObject.GetComponentInChildren<Animator>();
        _holder = GameObject.GetComponentInChildren<NPCWeaponHolder>();
        if (_holder != null && !_holder.HasWeapon) _holder = null;
        NPCActionUtils.SetupLocomotion(GameObject, target.transform);
        NPCActionUtils.Stop(_agent);
        _shots = 0;
        _nextFire = Time.time + FireInterval.Value;

        _fromCover = _npc != null && _npc.IsInCover;
        _hidden = _fromCover;
        _burst = _fromCover ? _npc.RollCoverShots() : ShotsPerBurst.Value;
        if (_fromCover)
        {
            _npc.SetCrouch(true); // đã ngồi thì không đổi gì, anim Crouch không phát lại
            _standAt = Mathf.Max(Time.time, _npc.CoverReadyTime);
        }
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        GameObject target = NPCConditionUtils.ResolveTarget(Target, _npc);
        if (target == null)
            return Status.Failure;

        // Đang bắn từ chỗ nấp mà chỗ nấp không còn dùng được (Target vòng ra sau...): để graph chọn lại.
        if (_fromCover && !_npc.IsInCover)
            return Status.Success;

        // Target ra ngoài tầm bắn: kết thúc để graph chọn Chase (rời chỗ nấp, đuổi theo).
        if (!TargetInRange(target))
            return Status.Success;

        // Đang thay đạn: chờ anim reload xong (đang nấp thì vẫn ngồi).
        if (_holder != null)
        {
            // Hết sạch đạn (không còn băng dự phòng): thôi bắn để graph chọn ném bom / núp.
            if (_holder.IsOutOfAmmo) return Status.Success;
            if (_holder.IsReloading || _holder.IsThrowing) return Status.Running;
            if (_holder.NeedsReload)
            {
                StartReload(false);
                return Status.Running;
            }
        }

        if (_hidden)
        {
            if (Time.time < _standAt) return Status.Running;
            _hidden = false;
            _npc.SetCrouch(false);
            _nextFire = Mathf.Max(_nextFire, Time.time + _npc.StandUpTime);
            return Status.Running;
        }

        // Bị chắn: đi bộ lại gần cho tới khi thấy mục tiêu (rời chỗ nấp nếu đang nấp).
        if (!NPCConditionUtils.HasLineOfSight(GameObject, target))
        {
            if (!NPCActionUtils.TryGetAgent(GameObject, out _agent)) return Status.Success;
            if (_fromCover) { _npc.LeaveCover(); _fromCover = false; }
            float speed = NPCActionUtils.SpeedFor(GameObject, 1f);
            if (!NPCActionUtils.MoveTo(_agent, target.transform.position, speed, 0f) || NPCActionUtils.PathFailed(_agent))
                return Status.Failure;
            return Status.Running;
        }

        NPCActionUtils.Stop(_agent);

        // Chờ NPCLocomotion quay mặt về mục tiêu rồi mới bắn.
        if (NPCConditionUtils.HorizontalAngleTo(GameObject, target.transform.position) > AimTolerance || Time.time < _nextFire)
            return Status.Running;

        if (!Fire(target))
            return Status.Running;
        _shots++;

        // Vừa bắn hết băng: thay đạn ngay, xong mới bắn tiếp phần còn lại của loạt.
        if (_holder != null && _holder.NeedsReload)
            StartReload(false);
        return _shots >= _burst ? Status.Success : Status.Running;
    }

    /// <summary>Đang nấp thì ngồi xuống thay đạn, thay xong đứng dậy (StandUpTime) rồi bắn tiếp.</summary>
    private void StartReload(bool tactical)
    {
        if (_fromCover && _npc != null && _npc.IsInCover)
        {
            _npc.SetCrouch(true);
            _hidden = true;
            _standAt = Time.time;
        }
        _holder.Reload(tactical);
    }

    protected override void OnEnd()
    {
        // Còn ở chỗ nấp: ngồi xuống lại (anim Crouch chạy 1 lần) và nấp random 3-5s trước lần bắn sau.
        // Chưa kịp đứng dậy (_hidden) thì giữ nguyên hẹn giờ cũ.
        // Target đã ra ngoài tầm thì không ngồi xuống nữa: Chase sẽ chạy ra đuổi ngay.
        GameObject target = NPCConditionUtils.ResolveTarget(Target, _npc);
        if (_npc != null && _npc.IsInCover && target != null && TargetInRange(target))
        {
            _npc.SetCrouch(true);
            if (!_hidden) _npc.CoverReadyTime = Time.time + _npc.RollCoverHideTime();
            // Ngồi nấp mà băng còn ít đạn: tranh thủ thay đạn (cất băng cũ, không thả xuống đất).
            if (_holder != null && _holder.WantsTacticalReload) _holder.Reload(true);
        }
        else
        {
            NPCActionUtils.SetupLocomotion(GameObject, null);
        }
        NPCActionUtils.Stop(_agent);
    }

    private bool TargetInRange(GameObject target)
    {
        float range = NPCConditionUtils.Resolve(AttackRange, _npc, n => n.AttackRange);
        if (_fromCover) range += CanAttackCondition.CoverRangeMarginFor(_npc);
        return NPCConditionUtils.IsWithin(GameObject.transform.position, target.transform.position, range);
    }

    private bool Fire(GameObject target)
    {
        // Morale thấp: tay run, bắn kém chính xác hơn (NPC.AccuracyScale).
        float accuracy = Accuracy.Value * (_npc != null ? _npc.AccuracyScale : 1f);
        bool hit = Random.value <= accuracy;

        // Có súng: anim Fire + chớp lửa + tiếng súng + vỏ đạn trong cùng một frame. Hết đạn / đang reload thì không bắn.
        if (_holder != null)
        {
            if (!_holder.Fire(target, hit)) return false;
        }
        else if (_animator != null)
        {
            _animator.SetTrigger(FireHash);
        }

        _nextFire = Time.time + FireInterval.Value;
        if (hit)
        {
            float damage = NPCConditionUtils.Resolve(Damage, _npc, n => n.Damage);
            target.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
            if (_npc != null && _npc.MoraleComponent != null) _npc.MoraleComponent.OnHitPlayer();
        }
        return true;
    }
}
