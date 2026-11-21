using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using static CONST;

namespace VRSwat.Tutorial
{
    // Bia bật lên / hạ xuống cho phòng 3.
    // PopUp() -> chạy animation bật lên, đứng trong upTime giây -> chạy animation hạ xuống.
    // Bắn trúng khi bia đang dựng = 1 điểm (phát Knocked cho TargetGroup) rồi bia hạ xuống luôn.
    // Cần: Animator có 2 state tên đúng như popUpState / popDownState (không cần transition, không cần parameter),
    //      Collider (nằm trong hittableLayers của Gun) trên object này hoặc object con.
    public class PopUpTarget : MonoBehaviour, ITutorialTarget
    {
        [Header("Animator")]
        [SerializeField] private Animator animator;
        [Tooltip("Tên state trong Animator chạy clip bật lên")]
        [SerializeField] private string popUpState = "PopUp";
        [Tooltip("Tên state trong Animator chạy clip hạ xuống")]
        [SerializeField] private string popDownState = "PopDown";

        [Header("Thời gian")]
        [Tooltip("Số giây bia đứng lên (tính từ lúc bắt đầu bật lên)")]
        [SerializeField, Min(0.1f)] private float upTime = 5f;

        [Header("Luật")]
        [Tooltip("Bia con tin: bắn trúng không được điểm mà báo lỗi")]
        [SerializeField] private bool isCivilian = false;
        [Tooltip("Trúng 1 phát là hạ xuống luôn")]
        [SerializeField] private bool dropOnHit = true;

        [Header("Sự kiện (tùy chọn, nối trong Inspector: âm thanh, hiệu ứng...)")]
        public UnityEvent onPopUp;
        public UnityEvent onHit;
        public UnityEvent onPopDown;

        public event Action<ITutorialTarget> Knocked;
        public bool IsCivilian => isCivilian;

        public bool IsUp { get; private set; }      // đang dựng, bắn được
        public bool IsBusy { get; private set; }    // đang lên / đứng / đang xuống
        public float UpTime { get => upTime; set => upTime = Mathf.Max(0.1f, value); }

        private Coroutine routine;
        private bool scoredThisPop;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        private void Start()
        {
            SnapDown();
        }

        private void OnEnable()
        {
            ObserverManager.Register(PLAYER_SHOOT_TARGET, (Action<Gun, GameObject, RaycastHit>)OnPlayerShoot);
        }

        private void OnDisable()
        {
            ObserverManager.Unregister(PLAYER_SHOOT_TARGET, (Action<Gun, GameObject, RaycastHit>)OnPlayerShoot);
            // Object bị tắt giữa chừng thì coroutine dừng, reset trạng thái để lần sau còn bật được
            routine = null;
            IsUp = false;
            IsBusy = false;
        }

        // ===== Gọi từ PopUpSpawner (hoặc UnityEvent) =====
        public void PopUp() => PopUp(upTime);

        public void PopUp(float seconds)
        {
            if (IsBusy || !isActiveAndEnabled) return;
            routine = StartCoroutine(UpRoutine(seconds));
        }

        public void PopDown()
        {
            if (!IsUp) return;
            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(DownRoutine());
        }

        // Hạ ngay không chạy animation (đầu game, reset phòng)
        public void SnapDown()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;
            IsUp = false;
            IsBusy = false;
            if (animator) animator.Play(popDownState, 0, 1f);   // nhảy tới frame cuối của clip hạ
        }

        private IEnumerator UpRoutine(float seconds)
        {
            IsBusy = true;
            IsUp = true;
            scoredThisPop = false;
            if (animator) animator.Play(popUpState, 0, 0f);
            onPopUp?.Invoke();

            yield return new WaitForSeconds(seconds);
            yield return DownRoutine();
        }

        private IEnumerator DownRoutine()
        {
            IsUp = false;
            if (animator) animator.Play(popDownState, 0, 0f);
            onPopDown?.Invoke();

            // Chờ clip hạ chạy xong rồi mới cho bật lại
            if (animator)
            {
                yield return null;
                while (true)
                {
                    var st = animator.GetCurrentAnimatorStateInfo(0);
                    if (!st.IsName(popDownState) || st.normalizedTime >= 1f) break;
                    yield return null;
                }
            }
            IsBusy = false;
            routine = null;
        }

        private void OnPlayerShoot(Gun gun, GameObject hitObject, RaycastHit hit)
        {
            if (!IsUp || gun == null || hitObject == null) return;

            // Mọi phát bắn đều phát sự kiện, nên mỗi bia tự lọc xem có phải mình bị trúng không
            if (hitObject != gameObject && !hitObject.transform.IsChildOf(transform)) return;
            if (scoredThisPop) return;   // mỗi lần bật chỉ tính 1 điểm

            scoredThisPop = true;
            onHit?.Invoke();
            Knocked?.Invoke(this);   // TargetGroup cộng điểm (hoặc báo bắn nhầm con tin)

            if (dropOnHit) PopDown();
        }
    }
}
