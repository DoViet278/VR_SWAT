using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VRSwat.Tutorial
{
    public class TutorialManager : MonoBehaviour
    {
        public static TutorialManager Instance { get; private set; }

        [SerializeField] TutorialRoomBase[] rooms;    // kéo đúng thứ tự 10 phòng
        [SerializeField] XROrigin xrOrigin;
        [SerializeField] string mainSceneName = "Mission_01";

        [Header("Test")]
        [Tooltip("Bắt đầu thẳng ở phòng này (0 = phòng đầu). Các phòng trước coi như đã xong.")]
        [SerializeField] int startRoom = 0;
        [Tooltip("Chỉ bật Content của phòng hiện tại và các phòng cách tối đa N phòng")]
        [SerializeField] int keepActiveRadius = 1;

        public Transform PlayerHead => xrOrigin.Camera.transform;
        public TutorialRoomBase CurrentRoom => index >= 0 && index < rooms.Length ? rooms[index] : null;

        int index = -1;
        bool loading;

        void Awake()
        {
            Instance = this;
            // Ô XR Origin bị trống (ví dụ đổi XR Rig mới) thì PlayerHead lỗi NullReference,
            // mọi bước chờ người chơi vào vùng (WaitZone, vào phòng mới, vùng bắn phòng 3) đều đứng im.
            if (xrOrigin == null)
            {
                xrOrigin = FindFirstObjectByType<XROrigin>();
                if (xrOrigin) Debug.LogWarning($"[TutorialManager] Chưa gán XR Origin, tự lấy '{xrOrigin.name}'. Nên kéo vào Inspector.", this);
                else Debug.LogError("[TutorialManager] Không tìm thấy XR Origin trong scene", this);
            }
        }

        void Start()
        {
            if (rooms == null || rooms.Length == 0)
            {
                Debug.LogError("[TutorialManager] Chưa kéo phòng nào vào mảng Rooms", this);
                return;
            }
            startRoom = Mathf.Clamp(startRoom, 0, rooms.Length - 1);
            for (int i = 0; i < startRoom; i++) rooms[i].CompleteInstantly();
            if (startRoom > 0) Teleport(rooms[startRoom].spawnPoint);
            EnterRoom(startRoom);
        }

        void EnterRoom(int i)
        {
            index = i;
            Debug.Log($"[TutorialManager] Bắt đầu phòng {i + 1}: {rooms[i].name}", rooms[i]);
            RefreshActiveRooms();
            rooms[i].Completed += OnRoomCompleted;
            rooms[i].StartRoom();
        }

        void OnRoomCompleted(TutorialRoomBase room)
        {
            room.Completed -= OnRoomCompleted;
            Debug.Log($"[TutorialManager] Xong phòng {index + 1}: {room.name}", room);
            if (index + 1 < rooms.Length) StartCoroutine(WaitForEntry(index + 1));
            else LoadMainScene();   // phòng cuối xong = vào màn chính
        }

        IEnumerator WaitForEntry(int next)
        {
            var area = rooms[next].entryArea;
            Debug.Log($"[TutorialManager] Chờ người chơi bước vào Entry Area của phòng {next + 1}", area ? (Object)area : rooms[next]);
            while (area && !area.Contains(PlayerHead.position)) yield return null;
            EnterRoom(next);
        }

        // ===== Gọi từ menu cổ tay =====
        public void ReplayVoice() => Narrator.Instance.Replay();
        public void RestartTutorial() => Load(SceneManager.GetActiveScene().name);   // về phòng 1
        public void SkipTutorial() => LoadMainScene();
        public void LoadMainScene() => Load(mainSceneName);

        void Load(string scene)
        {
            if (loading) return;
            loading = true;
            SceneManager.LoadSceneAsync(scene);
        }

        // Chỉ dùng khi test với startRoom > 0
        void Teleport(Transform target)
        {
            if (!target) return;
            var cc = xrOrigin.GetComponent<CharacterController>();
            if (cc) cc.enabled = false;
            xrOrigin.MatchOriginUpCameraForward(Vector3.up, target.forward);
            xrOrigin.MoveCameraToWorldLocation(target.position + Vector3.up * xrOrigin.CameraInOriginSpaceHeight);
            if (cc) cc.enabled = true;
        }

        void RefreshActiveRooms()
        {
            for (int i = 0; i < rooms.Length; i++)
                if (rooms[i].content)
                    rooms[i].content.SetActive(Mathf.Abs(i - index) <= keepActiveRadius);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.nKey.wasPressedThisFrame && CurrentRoom) CurrentRoom.SkipCurrentWait(); // N = bỏ qua đoạn đang chờ
            if (kb.rKey.wasPressedThisFrame) RestartTutorial();                             // R = restart
#endif
        }
#endif
    }
}