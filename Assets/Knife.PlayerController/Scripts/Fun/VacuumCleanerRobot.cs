using UnityEngine;
using System.Collections;
using UnityEngine.Playables;

namespace KnifePlayerController
{
    public class VacuumCleanerRobot : MonoBehaviour
    {
        public PickupableItem[] AmmoBoxPrefabs;
        public Transform PickupableItemSpawnPoint;
        public float StartCloseDelay = 1f;
        public float StartMoveDelay = 1f;
        [Range(0f, 1f)]
        public float OpenBoxFraction = 0.9f;
        public WaypointFollower WaypointFollower;

        public Animator Animator;

        bool hasAmmoBoxInSlot = false;

        bool isPlaying = false;
        PickupableItem spawnedItem;

        private void Awake()
        {
            StartCoroutine(processing());
            WaypointFollower.AddEvent(OpenBoxFraction, openBox);
            WaypointFollower.EndWayEvent.AddListener(closeBoxWithSpawn);
        }

        private void closeBoxWithSpawn()
        {
            Animator.Play("CloseWithSpawn");
            hasAmmoBoxInSlot = true;
            isPlaying = false;
        }

        private void closeBox()
        {
            Animator.Play("Close");
        }

        private void openBox()
        {
            Animator.Play("Open");
        }

        IEnumerator processing()
        {
            while (true)
            {
                while (hasAmmoBoxInSlot)
                    yield return null;

                openBox();
                yield return new WaitForSeconds(StartMoveDelay);
                WaypointFollower.StartMove();
                yield return new WaitForSeconds(StartCloseDelay);
                closeBox();

                isPlaying = true;

                while (isPlaying)
                    yield return null;

                yield return null;
            }
        }

        public void SpawnAmmoBox()
        {

            spawnedItem = Instantiate(AmmoBoxPrefabs[Random.Range(0, AmmoBoxPrefabs.Length)]);
            spawnedItem.transform.SetParent(PickupableItemSpawnPoint);
            spawnedItem.transform.localPosition = Vector3.zero;
            spawnedItem.transform.localRotation = Quaternion.identity;

            spawnedItem.ItemPickedUp.AddListener(itemPickedUp);
        }

        void itemPickedUp()
        {
            hasAmmoBoxInSlot = false;
        }
    }
}
