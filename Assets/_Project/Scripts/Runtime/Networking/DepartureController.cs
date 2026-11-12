using Letsail.World;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Letsail.Networking
{
    /// <summary>
    /// 출항 판정. 접속한 선원이 모두 승선 구역에 모이면 카운트다운을 하고, 끝나면 항해 씬으로 모두 함께 넘어간다.
    /// 판정과 씬 이동은 호스트(서버)가 하고, 다른 사람은 인원과 남은 시간만 받아서 보여 준다.
    /// 로비 씬에 하나 둔다 (NetworkObject 가 필요하다).
    /// </summary>
    public sealed class DepartureController : NetworkBehaviour
    {
        [SerializeField, Tooltip("넘어갈 항해 씬 이름 (Build Settings 에 들어 있어야 한다)")] string voyageScene = "Voyage";
        [SerializeField, Tooltip("모두 모인 뒤 출항까지 기다리는 시간 (초)")] float countdownSeconds = 5f;
        [SerializeField, Tooltip("승선 구역으로 쓸 로비 모델 오브젝트 이름들 (부두, 승선 계단)")]
        string[] zoneObjects = { "Harbor_Pier", "Harbor_BoardingStairs" };
        [SerializeField, Tooltip("부두가 모델에 없을 때: 승선 자리들을 감싸는 구역에서 바깥으로 더 넓히는 거리 (m)")] float zonePadding = 0.9f;

        readonly NetworkVariable<int> boardedCount = new NetworkVariable<int>();
        readonly NetworkVariable<int> crewCount = new NetworkVariable<int>();
        // 출항 시각 (서버 시간, 초). 카운트다운 중이 아니면 -1.
        readonly NetworkVariable<double> departAt = new NetworkVariable<double>(-1);

        LobbyLayout layout;
        Bounds zone;
        bool departing;

        public static DepartureController Instance { get; private set; }

        public int BoardedCount => boardedCount.Value;
        public int CrewCount => crewCount.Value;
        public bool IsCountingDown => departAt.Value >= 0;

        /// <summary>출항까지 남은 시간 (초). 카운트다운 중이 아니면 0.</summary>
        public float SecondsLeft => IsCountingDown && NetworkManager != null
            ? Mathf.Max(0f, (float)(departAt.Value - NetworkManager.ServerTime.Time))
            : 0f;

        void Awake()
        {
            Instance = this;
        }

        public override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            layout = FindAnyObjectByType<LobbyLayout>();
            if (layout != null)
            {
                layout.Collect();
                zone = BuildZone(layout);
            }
        }

        void Update()
        {
            if (!IsServer || !IsSpawned || layout == null || departing)
            {
                return;
            }

            int crew = 0;
            int boarded = 0;
            foreach (var player in NetworkPlayer.All)
            {
                crew++;
                if (zone.Contains(player.transform.position))
                {
                    boarded++;
                }
            }
            if (boardedCount.Value != boarded) boardedCount.Value = boarded;
            if (crewCount.Value != crew) crewCount.Value = crew;

            bool everyoneAboard = crew > 0 && boarded == crew;
            double now = NetworkManager.ServerTime.Time;
            if (!everyoneAboard)
            {
                if (departAt.Value >= 0) departAt.Value = -1;
                return;
            }
            if (departAt.Value < 0)
            {
                departAt.Value = now + countdownSeconds;
                return;
            }
            if (now >= departAt.Value)
            {
                Depart();
            }
        }

        void Depart()
        {
            departing = true;
            var status = NetworkManager.SceneManager.LoadScene(voyageScene, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"항해 씬 '{voyageScene}' 을(를) 불러오지 못했어요: {status}. Build Settings 에 들어 있는지 확인하세요.");
                departing = false;
                departAt.Value = -1;
            }
        }

        Bounds BuildZone(LobbyLayout source)
        {
            // 부두(와 승선 계단) 전체를 승선 구역으로 쓴다. 모델에 없으면 승선 자리 주변으로 대신한다.
            bool found = false;
            var pier = new Bounds();
            foreach (var r in source.GetComponentsInChildren<Renderer>(true))
            {
                if (System.Array.IndexOf(zoneObjects, r.name) < 0)
                {
                    continue;
                }
                if (!found)
                {
                    pier = r.bounds;
                    found = true;
                }
                else
                {
                    pier.Encapsulate(r.bounds);
                }
            }
            if (found)
            {
                // 물에 잠긴 기둥 부분은 빼고, 부두 바닥 위만 친다 (점프나 계단 위도 포함).
                float floor = source.BoardSpots.Count > 0 ? source.BoardSpots[0].position.y : pier.min.y;
                pier.SetMinMax(new Vector3(pier.min.x, floor - 1f, pier.min.z), new Vector3(pier.max.x, floor + 4f, pier.max.z));
                return pier;
            }

            var spots = source.BoardSpots;
            if (spots.Count == 0)
            {
                Transform center = source.BoardZone != null ? source.BoardZone : transform;
                return new Bounds(center.position, new Vector3(3f, 4f, 3f));
            }
            var bounds = new Bounds(spots[0].position, Vector3.zero);
            foreach (var spot in spots)
            {
                bounds.Encapsulate(spot.position);
            }
            bounds.Expand(new Vector3(zonePadding * 2f, 0f, zonePadding * 2f));
            // 높이는 넉넉하게 (점프 중에도 구역 안으로 친다).
            bounds.SetMinMax(bounds.min - Vector3.up * 1f, bounds.max + Vector3.up * 3f);
            return bounds;
        }

        void OnDrawGizmosSelected()
        {
            var source = layout != null ? layout : FindAnyObjectByType<LobbyLayout>();
            if (source == null)
            {
                return;
            }
            source.Collect();
            Bounds b = BuildZone(source);
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }
}
