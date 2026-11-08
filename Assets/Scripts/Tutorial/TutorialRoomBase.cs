using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VRSwat.Doors;

namespace VRSwat.Tutorial
{
    public enum DoorGoal { OpenedPastAngle, Rattled, Breached, Unlocked }

    // Lớp cha của 10 script phòng. Phòng con chỉ cần viết Script().
    public abstract class TutorialRoomBase : MonoBehaviour
    {
        [Header("Chung cho mọi phòng")]
        public string title = "Phòng 1: Di chuyển";   // tiêu đề trên bảng, không đọc
        public Transform spawnPoint;                   // nằm trong entryArea, hướng về bảng
        public TutorialArea entryArea;                 // bước vào đây thì phòng bắt đầu
        public TutorialBoard board;
        [Tooltip("Cửa ra, Start Locked. Mở khóa khi Script() chạy xong. Có thể trống.")]
        public DoorController exitDoor;
        [Tooltip("Bia, NPC, súng... Manager tắt cụm này khi người chơi ở xa")]
        public GameObject content;
        [SerializeField] float remindAfter = 15f;

        public event Action<TutorialRoomBase> Completed;
        public bool IsComplete { get; private set; }

        readonly List<Action<TutorialSignal>> listeners = new List<Action<TutorialSignal>>();
        string lastKey;
        GameObject highlighted;
        bool skipWait;
        bool running;

        // Kịch bản của phòng, viết trong từng script con
        protected abstract IEnumerator Script();

        void OnEnable() => TutorialSignals.Raised += Dispatch;
        void OnDisable() => TutorialSignals.Raised -= Dispatch;

        void Dispatch(TutorialSignal s)
        {
            for (int i = listeners.Count - 1; i >= 0; i--) listeners[i](s);
        }

        // ===== Manager gọi =====
        public void StartRoom()
        {
            if (running || IsComplete) return;
            running = true;
            if (board) board.SetTitle(title);
            StartCoroutine(Run());
        }

        public void CompleteInstantly()     // test: bắt đầu từ phòng sau
        {
            StopAllCoroutines();
            Finish(false);
        }

        public void SkipCurrentWait() => skipWait = true;   // phím N

        IEnumerator Run()
        {
            yield return Script();
            Finish(true);
        }

        void Finish(bool announce)
        {
            running = false;
            IsComplete = true;
            listeners.Clear();
            Highlight(null);
            if (exitDoor) exitDoor.Unlock();   // DoorLockLight tự đổi đèn xanh + phát tiếng mở khóa
            if (announce) Say(CONST.TutorialLine.COMMON_ROOM_DONE);
            Completed?.Invoke(this);
        }

        // ===== Lời nói =====
        protected void Say(string key)
        {
            lastKey = key;
            Narrator.Instance.Say(key, board);
        }

        protected IEnumerator SayAndWait(string key)
        {
            Say(key);
            skipWait = false;
            while (Narrator.Instance.IsSpeaking)
            {
                if (skipWait) { skipWait = false; break; }
                yield return null;
            }
        }

        protected void Note(string key) => Narrator.Instance.Interject(key, board);

        // ===== Gợi ý =====
        protected void Highlight(GameObject go)
        {
            if (highlighted) highlighted.SetActive(false);
            highlighted = go;
            if (go) go.SetActive(true);
        }

        // ===== Chờ =====
        // Chờ tới khi done() đúng. count + goal để hiện "label: 3/8" trên bảng.
        protected IEnumerator WaitFor(Func<bool> done, Func<int> count = null, int goal = 0, string label = "Tiến độ")
        {
            float idle = 0f;
            int shown = -1;
            skipWait = false;
            while (!done())
            {
                if (skipWait) { skipWait = false; break; }
                if (count != null)
                {
                    int c = Mathf.Min(count(), goal);
                    if (c != shown)
                    {
                        shown = c;
                        idle = 0f;
                        if (board) board.SetProgress($"{label}: {c}/{goal}");
                    }
                }
                idle += Time.deltaTime;
                if (remindAfter > 0f && idle >= remindAfter && !Narrator.Instance.IsSpeaking)
                {
                    idle = 0f;
                    if (lastKey != null) Say(lastKey);
                }
                yield return null;
            }
            if (board) board.SetProgress(null);
            Narrator.Instance.PlayStepDone();
            yield return new WaitForSeconds(0.5f);
        }

        protected IEnumerator WaitSignal(string id, int count = 1, GameObject source = null,
                                         TutorialArea inside = null, string label = "Tiến độ")
        {
            int got = 0;
            Action<TutorialSignal> h = s =>
            {
                if (s.id != id) return;
                if (source && !IsFrom(s.source, source)) return;
                if (inside && !inside.Contains(s.position)) return;
                got++;
            };
            listeners.Add(h);
            yield return WaitFor(() => got >= count, count > 1 ? () => got : (Func<int>)null, count, label);
            listeners.Remove(h);
        }

        protected IEnumerator WaitTargets(TargetGroup group, int required = 0)
        {
            int goal = required > 0 ? required : group.EnemyCount;
            yield return WaitFor(() => group.Hits >= goal, () => group.Hits, goal, "Bia");
        }

        protected IEnumerator WaitDoor(DoorController door, DoorGoal goal, float minAngle = 45f)
        {
            bool rattled = false;
            Action<DoorController> onRattle = d => rattled = true;
            door.LockedRattle += onRattle;
            yield return WaitFor(() => goal switch
            {
                DoorGoal.OpenedPastAngle => door.Angle >= minAngle,
                DoorGoal.Rattled         => rattled,
                DoorGoal.Breached        => door.State == DoorState.Breached,
                _                        => door.State != DoorState.Locked
            });
            door.LockedRattle -= onRattle;
        }

        protected IEnumerator WaitZone(TutorialArea area)
        {
            Transform head = TutorialManager.Instance.PlayerHead;
            yield return WaitFor(() => area.Contains(head.position));
        }

        // ===== Phản hồi phụ, không chặn kịch bản (tự hủy khi xong phòng) =====
        protected void OnSignal(string id, Action action)
        {
            listeners.Add(s => { if (s.id == id) action(); });
        }

        protected void OnceOnSignal(string id, Action action)
        {
            Action<TutorialSignal> h = null;
            h = s =>
            {
                if (s.id != id) return;
                listeners.Remove(h);
                action();
            };
            listeners.Add(h);
        }

        static bool IsFrom(UnityEngine.Object o, GameObject expected)
        {
            GameObject go = o is Component c ? c.gameObject : o as GameObject;
            return go && (go == expected || go.transform.IsChildOf(expected.transform));
        }
    }
}