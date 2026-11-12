using System.Collections.Generic;
using UnityEngine;

namespace Letsail.World
{
    /// <summary>
    /// 로비(항구 마을) 모델 안의 기능 지점들을 이름으로 찾아 모아 둔다.
    /// 스폰, 승선 구역, 배 정박 위치 등을 다른 시스템이 여기서 가져다 쓴다.
    /// </summary>
    public sealed class LobbyLayout : MonoBehaviour, IPlayerSpawnProvider
    {
        [SerializeField, Tooltip("지점들이 들어 있는 모델. 비우면 자기 자신")]
        Transform model;

        readonly List<Transform> spawnPoints = new List<Transform>();
        readonly List<Transform> boardSpots = new List<Transform>();

        public IReadOnlyList<Transform> SpawnPoints => spawnPoints;
        public IReadOnlyList<Transform> BoardSpots => boardSpots;
        public Transform BoardZone { get; private set; }
        public Transform ShipDockPoint { get; private set; }
        public Transform GangplankTop { get; private set; }
        public Transform NoticeBoard { get; private set; }
        public Transform Wardrobe { get; private set; }

        void Awake()
        {
            Collect();
        }

        /// <summary>모델에서 이름 규칙으로 지점을 다시 찾는다.</summary>
        public void Collect()
        {
            Transform root = model != null ? model : transform;
            spawnPoints.Clear();
            boardSpots.Clear();

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n.StartsWith("Spawn_Player_"))
                {
                    spawnPoints.Add(t);
                }
                else if (n.StartsWith("Board_Spot_"))
                {
                    boardSpots.Add(t);
                }
                else if (n == "Board_Zone")
                {
                    BoardZone = t;
                }
                else if (n == "Ship_DockPoint")
                {
                    ShipDockPoint = t;
                }
                else if (n == "Gangplank_Top")
                {
                    GangplankTop = t;
                }
                else if (n == "Interact_NoticeBoard")
                {
                    NoticeBoard = t;
                }
                else if (n == "Interact_Wardrobe")
                {
                    Wardrobe = t;
                }
            }

            spawnPoints.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            boardSpots.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        /// <summary>플레이어 번호(0부터)에 맞는 스폰 지점. 지점보다 사람이 많으면 돌아가며 쓴다.</summary>
        public Transform GetSpawnPoint(int playerIndex)
        {
            if (spawnPoints.Count == 0)
            {
                return transform;
            }
            return spawnPoints[Mathf.Abs(playerIndex) % spawnPoints.Count];
        }

        void OnDrawGizmos()
        {
            Transform root = model != null ? model : transform;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Spawn_Player_"))
                {
                    Gizmos.color = new Color(0.2f, 0.8f, 1f);
                    Gizmos.DrawWireSphere(t.position + Vector3.up * 0.5f, 0.35f);
                    Gizmos.DrawRay(t.position + Vector3.up * 0.5f, t.forward * 0.8f);
                }
                else if (t.name.StartsWith("Board_Spot_"))
                {
                    Gizmos.color = new Color(1f, 0.8f, 0.2f);
                    Gizmos.DrawWireCube(t.position + Vector3.up * 0.05f, new Vector3(0.9f, 0.1f, 0.9f));
                }
            }
        }
    }
}
