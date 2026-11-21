using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VRSwat.Tutorial
{
    // Điều khiển cụm PopUpTarget: bật ngẫu nhiên từng bia (hoặc vài bia cùng lúc).
    // Chỉ bật bia mới khi người chơi đang đứng trong shootingArea. Ra khỏi vùng thì ngừng bật.
    // Đặt cùng object với TargetGroup (object cha của các bia) cho gọn.
    public class PopUpSpawner : MonoBehaviour
    {
        [Tooltip("Để trống = tự lấy mọi PopUpTarget con")]
        [SerializeField] private PopUpTarget[] targets;
        [Tooltip("Vùng đứng bắn")]
        [SerializeField] private TutorialArea shootingArea;

        [Header("Nhịp bật bia")]
        [Tooltip("Chờ bao lâu sau khi người chơi bước vào vùng mới bật bia đầu tiên")]
        [SerializeField, Min(0f)] private float startDelay = 1f;
        [Tooltip("Khoảng nghỉ ngẫu nhiên giữa 2 lần bật (giây)")]
        [SerializeField, Min(0f)] private float minInterval = 1f;
        [SerializeField, Min(0f)] private float maxInterval = 2.5f;
        [Tooltip("Tối đa bao nhiêu bia đứng cùng lúc")]
        [SerializeField, Min(1)] private int maxUpAtOnce = 2;
        [Tooltip("> 0 thì ghi đè Up Time của từng bia (mặc định mỗi bia 5s)")]
        [SerializeField, Min(0f)] private float upTimeOverride = 0f;
        [Tooltip("Tránh bật lại đúng bia vừa bật")]
        [SerializeField] private bool avoidRepeat = true;

        public bool IsRunning { get; private set; }

        private Coroutine loop;
        private PopUpTarget last;
        private readonly List<PopUpTarget> free = new List<PopUpTarget>();

        private void Awake()
        {
            if (targets == null || targets.Length == 0)
                targets = GetComponentsInChildren<PopUpTarget>(true);
        }

        public void Begin()
        {
            if (IsRunning) return;
            IsRunning = true;
            loop = StartCoroutine(Loop());
        }

        // lowerAll = hạ hết bia đang đứng (khi qua phòng)
        public void Stop(bool lowerAll = true)
        {
            IsRunning = false;
            if (loop != null) StopCoroutine(loop);
            loop = null;
            if (!lowerAll) return;
            foreach (var t in targets)
                if (t) t.PopDown();
        }

        private bool PlayerInArea()
        {
            if (shootingArea == null) return true;
            var mgr = TutorialManager.Instance;
            return mgr != null && mgr.PlayerHead != null && shootingArea.Contains(mgr.PlayerHead.position);
        }

        private IEnumerator Loop()
        {
            while (IsRunning)
            {
                // Chờ người chơi vào vùng bắn
                while (!PlayerInArea()) yield return null;
                yield return new WaitForSeconds(startDelay);

                while (IsRunning && PlayerInArea())
                {
                    if (CountUp() < maxUpAtOnce)
                    {
                        var t = PickFree();
                        if (t)
                        {
                            if (upTimeOverride > 0f) t.PopUp(upTimeOverride); else t.PopUp();
                            last = t;
                        }
                    }
                    yield return new WaitForSeconds(Random.Range(minInterval, Mathf.Max(minInterval, maxInterval)));
                }
            }
        }

        private int CountUp()
        {
            int n = 0;
            foreach (var t in targets)
                if (t && t.IsBusy) n++;
            return n;
        }

        private PopUpTarget PickFree()
        {
            free.Clear();
            foreach (var t in targets)
                if (t && t.isActiveAndEnabled && !t.IsBusy && !(avoidRepeat && t == last && targets.Length > 1))
                    free.Add(t);
            return free.Count > 0 ? free[Random.Range(0, free.Count)] : null;
        }
    }
}
