using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Letsail.Core;
using Letsail.Interaction;
using Letsail.Networking;
using Letsail.Ocean;
using Letsail.Ship;
using Letsail.World;
using Letsail.World.Islands;
using Unity.Netcode;
using UnityEngine;

namespace Letsail.Testing
{
    /// <summary>
    /// 멀티플레이 반복 검사 (개발용). 호스트가 시작하면 한 바퀴마다:
    /// 1. 아무 선원(호스트 또는 참가자)을 골라 아무 행동을 시킨다: 상호작용 켜고 끄기, 자리 앉기·일어나기, 조작 값 보내기,
    ///    날씨 바꾸기, 캐릭터 순간이동.
    /// 2. 동기화될 시간을 기다린 뒤, 모든 PC 가 자기 화면의 상태를 적어 호스트에 보낸다.
    /// 3. 호스트가 자기 상태와 맞춰 본다: 상호작용 상태·자리·값, 섬 배치, 같은 시각의 날씨·파도·시각, 배 위치,
    ///    선원 위치(배 기준), 오류 로그. 다르면 조금 기다렸다 다시 맞춰 보고(전달 중일 수 있으므로), 그래도 다르면 실패로 적는다.
    /// 항해 씬에 하나 둔다 (NetworkObject 필요). 시작하기 전에는 아무것도 하지 않는다.
    /// </summary>
    public sealed class SoakTester : NetworkBehaviour
    {
        [SerializeField, Tooltip("행동 뒤 기다리는 네트워크 틱 수")] int settleTicks = 3;
        [SerializeField, Tooltip("참가자가 한 행동 뒤 기다리는 네트워크 틱 수 (참가자 → 호스트 → 모두, 한 번 더 오간다)")] int clientSettleTicks = 7;
        [SerializeField, Tooltip("순간이동 뒤 기다리는 네트워크 틱 수")] int teleportSettleTicks = 12;
        [SerializeField, Tooltip("다를 때 다시 맞춰 보는 횟수")] int retries = 5;
        [SerializeField, Tooltip("보고를 기다리는 최대 시간 (초)")] float reportTimeout = 4f;
        [SerializeField, Tooltip("배 위치 허용 오차 (m, 호스트 배가 최근 1초 동안 지나온 자리 기준)")] float shipTolerance = 0.6f;
        [SerializeField, Tooltip("선원 위치 허용 오차 (m, 주인이 최근 1초 동안 지나온 자리 기준)")] float playerTolerance = 0.35f;

        static readonly Vector3[] WeatherSamples =
        {
            new Vector3(0f, 0f, 0f), new Vector3(400f, 0f, 250f), new Vector3(-700f, 0f, 500f),
            new Vector3(900f, 0f, -800f), new Vector3(-300f, 0f, -1100f),
        };

        public static SoakTester Instance { get; private set; }

        // 결과 (eval 로 읽는다)
        public bool Running { get; private set; }
        public int Iteration { get; private set; }
        public int Target { get; private set; }
        public int Transient { get; private set; }
        public int ActionsByClients { get; private set; }
        public readonly List<string> Failures = new List<string>();
        public readonly Dictionary<string, int> ActionCounts = new Dictionary<string, int>();
        /// <summary>처음엔 달랐다가 다시 맞춰 보니 같아진 경우의 이유별 횟수 (전달 중이던 값).</summary>
        public readonly Dictionary<string, int> TransientReasons = new Dictionary<string, int>();
        public float Elapsed => Running ? Time.realtimeSinceStartup - startedAt : finishedIn;

        float startedAt;
        float finishedIn;
        int errorCount;
        string lastError = "";
        readonly Dictionary<ulong, string> reports = new Dictionary<ulong, string>();
        readonly Dictionary<ulong, int> seenErrors = new Dictionary<ulong, int>();
        int waitingIteration = -1;
        int waitingAttempt = -1;
        System.Random random;
        Coroutine loop;

        void Awake()
        {
            Instance = this;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Application.logMessageReceived -= OnLog;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            Application.logMessageReceived += OnLog;
        }

        public override void OnNetworkDespawn()
        {
            Application.logMessageReceived -= OnLog;
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                // 이 검사 전부터 있던 알려진 경고성 오류는 세지 않는다.
                if (message.Contains("[Wire") || message.Contains("Failed to stop session")) return;
                errorCount++;
                lastError = message.Length > 160 ? message.Substring(0, 160) : message;
            }
        }

        /// <summary>호스트에서 검사를 시작한다.</summary>
        public void Begin(int iterations, int seed = 0)
        {
            if (!IsServer || Running) return;
            Target = iterations;
            Iteration = 0;
            Transient = 0;
            ActionsByClients = 0;
            Failures.Clear();
            ActionCounts.Clear();
            TransientReasons.Clear();
            seenErrors.Clear();
            errorCount = 0;
            lastError = "";
            random = new System.Random(seed != 0 ? seed : Random.Range(1, int.MaxValue));
            startedAt = Time.realtimeSinceStartup;
            Running = true;
            ResetErrorsRpc();
            loop = StartCoroutine(Loop());
        }

        public void Stop()
        {
            if (loop != null) StopCoroutine(loop);
            Finish();
        }

        void Finish()
        {
            Running = false;
            finishedIn = Time.realtimeSinceStartup - startedAt;
            loop = null;
        }

        /// <summary>요약 (eval 용).</summary>
        public string Summary()
        {
            var sb = new StringBuilder();
            sb.Append($"running={Running} iter={Iteration}/{Target} fail={Failures.Count} transient={Transient} byClients={ActionsByClients} time={Elapsed:F0}s clients={NetworkManager.ConnectedClientsIds.Count}");
            foreach (var kv in ActionCounts) sb.Append($" {kv.Key}={kv.Value}");
            sb.Append(" | firstDiff:");
            foreach (var kv in TransientReasons) sb.Append(" ").Append(kv.Key).Append('=').Append(kv.Value);
            for (int i = 0; i < Mathf.Min(8, Failures.Count); i++) sb.Append(" || ").Append(Failures[i]);
            return sb.ToString();
        }

        [Rpc(SendTo.Everyone)]
        void ResetErrorsRpc()
        {
            errorCount = 0;
            lastError = "";
        }

        // ───────────────────────── 호스트 반복 ─────────────────────────

        IEnumerator Loop()
        {
            while (Iteration < Target)
            {
                var hub = FindAnyObjectByType<InteractionHub>();
                if (hub == null)
                {
                    Failures.Add($"#{Iteration} 배(InteractionHub) 없음");
                    break;
                }
                lastByClient = false;
                int settle = DoRandomAction(hub);
                if (lastByClient) settle = Mathf.Max(settle, clientSettleTicks);
                yield return WaitTicks(settle);

                bool ok = false;
                string diff = null;
                for (int attempt = 0; attempt <= retries && !ok; attempt++)
                {
                    if (attempt > 0)
                    {
                        yield return WaitTicks(settleTicks * 2);
                    }
                    double time = NetworkManager.ServerTime.Time;
                    reports.Clear();
                    waitingIteration = Iteration;
                    waitingAttempt = attempt;
                    ProbeRpc(Iteration, attempt, time);
                    // 호스트 상태는 물어본 그 순간에 적는다 (보고를 다 기다린 뒤에 적으면 그만큼 앞서 간다).
                    string mine = BuildReport(time);
                    float deadline = Time.realtimeSinceStartup + reportTimeout;
                    int expected = NetworkManager.ConnectedClientsIds.Count - 1;
                    while (reports.Count < expected && Time.realtimeSinceStartup < deadline)
                    {
                        yield return null;
                    }
                    diff = Compare(mine, expected);
                    ok = diff == null;
                    if (ok && attempt > 0) Transient++;
                    if (!ok && attempt == 0)
                    {
                        string reason = Reason(diff);
                        TransientReasons[reason] = TransientReasons.TryGetValue(reason, out int r) ? r + 1 : 1;
                    }
                }
                if (!ok)
                {
                    Failures.Add($"#{Iteration} {diff}");
                    Debug.LogWarning($"[Soak] #{Iteration} 불일치: {diff}");
                }
                Iteration++;
            }
            Finish();
            Debug.Log("[Soak] 끝: " + Summary());
        }

        IEnumerator WaitTicks(int ticks)
        {
            int start = NetworkManager.ServerTime.Tick;
            float limit = Time.realtimeSinceStartup + 3f;
            while (NetworkManager.ServerTime.Tick - start < ticks && Time.realtimeSinceStartup < limit)
            {
                yield return null;
            }
        }

        void Count(string kind)
        {
            ActionCounts[kind] = ActionCounts.TryGetValue(kind, out int n) ? n + 1 : 1;
        }

        // 아무 선원에게 아무 행동을 시킨다. 기다릴 틱 수를 돌려준다.
        int DoRandomAction(InteractionHub hub)
        {
            var points = hub.GetComponentsInChildren<Interactable>(true);
            var clients = new List<ulong>(NetworkManager.ConnectedClientsIds);
            ulong peer = clients[random.Next(clients.Count)];
            double roll = random.NextDouble();

            if (roll < 0.35)
            {
                int index = PickIndex(points, p => p.GetComponent<ToggleInteraction>() != null);
                if (index < 0) return settleTicks;
                Count("toggle");
                Send(peer, 0, index, 0f, Vector3.zero);
            }
            else if (roll < 0.55)
            {
                int index = PickIndex(points, p => p.GetComponent<SeatInteraction>() != null);
                if (index < 0) return settleTicks;
                ulong occupant = hub.GetOccupant(index);
                if (occupant == InteractionHub.Nobody)
                {
                    Count("sit");
                    Send(peer, 1, index, 0f, Vector3.zero);
                }
                else
                {
                    Count("standUp");
                    Send(occupant, 2, index, 0f, Vector3.zero);
                }
            }
            else if (roll < 0.7)
            {
                // 자리에 앉은 사람이 조작 값을 보낸다 (키 돌리기, 돛 올리고 내리기 등).
                var seated = new List<int>();
                for (int i = 0; i < points.Length; i++)
                {
                    if (hub.GetOccupant(i) != InteractionHub.Nobody && points[i].GetComponent<SeatControl>() != null) seated.Add(i);
                }
                if (seated.Count == 0) return settleTicks;
                int index = seated[random.Next(seated.Count)];
                Count("value");
                Send(hub.GetOccupant(index), 3, index, (float)(random.NextDouble() * 2.0 - 1.0), Vector3.zero);
            }
            else if (roll < 0.8)
            {
                var weather = WeatherSystem.Instance;
                if (weather == null) return settleTicks;
                Count("weather");
                double w = random.NextDouble();
                weather.SetForcedLevel(w < 0.5 ? -1f : (float)(0.5 + random.NextDouble() * 1.7));
            }
            else if (roll < 0.88)
            {
                // 앉아 있지 않은 선원을 배 위 아무 상호작용 근처로 순간이동.
                var free = new List<ulong>();
                foreach (var c in clients)
                {
                    bool seatedNow = false;
                    for (int i = 0; i < points.Length; i++) if (hub.GetOccupant(i) == c) seatedNow = true;
                    if (!seatedNow) free.Add(c);
                }
                if (free.Count == 0) return settleTicks;
                var target = points[random.Next(points.Length)].transform;
                Count("teleport");
                Send(free[random.Next(free.Count)], 4, 0, 0f, target.position + Vector3.up * 0.2f);
                return teleportSettleTicks;
            }
            else
            {
                Count("wait");
            }
            return settleTicks;
        }

        int PickIndex(Interactable[] points, System.Func<Interactable, bool> filter)
        {
            var list = new List<int>();
            for (int i = 0; i < points.Length; i++) if (filter(points[i])) list.Add(i);
            return list.Count > 0 ? list[random.Next(list.Count)] : -1;
        }

        bool lastByClient;

        void Send(ulong peer, int kind, int index, float value, Vector3 position)
        {
            lastByClient = peer != NetworkManager.ServerClientId;
            if (!lastByClient)
            {
                Perform(kind, index, value, position);
            }
            else
            {
                ActionsByClients++;
                ActionRpc(kind, index, value, position, RpcTarget.Single(peer, RpcTargetUse.Temp));
            }
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ActionRpc(int kind, int index, float value, Vector3 position, RpcParams rpcParams)
        {
            Perform(kind, index, value, position);
        }

        static void Perform(int kind, int index, float value, Vector3 position)
        {
            var hub = FindAnyObjectByType<InteractionHub>();
            switch (kind)
            {
                case 0: hub.RequestToggle(index); break;
                case 1: hub.RequestOccupy(index); break;
                case 2: hub.RequestRelease(index); break;
                case 3: hub.SubmitValue(index, value); break;
                case 4:
                    if (NetworkPlayer.Local != null) NetworkPlayer.Local.TeleportTo(position, Quaternion.identity);
                    break;
            }
        }

        // ───────────────────────── 상태 보고 ─────────────────────────

        [Rpc(SendTo.NotServer)]
        void ProbeRpc(int iteration, int attempt, double time)
        {
            ReportRpc(iteration, attempt, BuildReport(time));
        }

        [Rpc(SendTo.Server)]
        void ReportRpc(int iteration, int attempt, string report, RpcParams rpcParams = default)
        {
            if (iteration == waitingIteration && attempt == waitingAttempt)
            {
                reports[rpcParams.Receive.SenderClientId] = report;
            }
        }

        static string F(float v, string format = "F3") => v.ToString(format, CultureInfo.InvariantCulture);

        // ───────────────────────── 최근 자리 기록 ─────────────────────────

        const float HistorySeconds = 1f;
        const int TrailStride = 3;

        struct TrailSample
        {
            public float time;
            public Vector3 local;
            public Vector3 world;
            public bool aboard;
        }

        readonly List<TrailSample> trail = new List<TrailSample>();
        readonly List<(float time, Vector3 position)> shipHistory = new List<(float, Vector3)>();

        void LateUpdate()
        {
            if (!IsSpawned) return;
            float now = Time.realtimeSinceStartup;
            var ship = FindAnyObjectByType<ShipMotion>();
            var me = NetworkPlayer.Local;
            if (me != null)
            {
                Vector3 w = me.transform.position;
                var sync = me.GetComponent<ShipRelativeSync>();
                trail.Add(new TrailSample
                {
                    time = now,
                    world = w,
                    local = ship != null ? ship.transform.InverseTransformPoint(w) : w,
                    aboard = sync != null && sync.Aboard,
                });
            }
            if (IsServer && ship != null)
            {
                shipHistory.Add((now, ship.transform.position));
            }
            while (trail.Count > 0 && now - trail[0].time > HistorySeconds + 0.5f) trail.RemoveAt(0);
            while (shipHistory.Count > 0 && now - shipHistory[0].time > HistorySeconds + 0.5f) shipHistory.RemoveAt(0);
        }

        static StringBuilder AppendSample(StringBuilder sb, Vector3 local, Vector3 world, bool aboard)
        {
            return sb.Append(F(local.x, "F2")).Append(',').Append(F(local.y, "F2")).Append(',').Append(F(local.z, "F2")).Append(',')
                     .Append(F(world.x, "F2")).Append(',').Append(F(world.y, "F2")).Append(',').Append(F(world.z, "F2")).Append(',')
                     .Append(aboard ? '1' : '0');
        }

        // 이 PC 화면의 상태. 줄마다 "이름:값".
        string BuildReport(double time)
        {
            var sb = new StringBuilder();
            var hub = FindAnyObjectByType<InteractionHub>();
            sb.Append("hub:");
            if (hub != null)
            {
                int count = hub.GetComponentsInChildren<Interactable>(true).Length;
                for (int i = 0; i < count; i++)
                {
                    ulong o = hub.GetOccupant(i);
                    sb.Append(o == InteractionHub.Nobody ? "-" : o.ToString()).Append(hub.GetState(i) ? '1' : '0').Append(F(hub.GetValue(i), "F2")).Append(',');
                }
            }
            sb.Append('\n');

            var world = FindAnyObjectByType<IslandWorld>();
            sb.Append("isl:");
            if (world != null)
            {
                sb.Append(world.Seed).Append('/').Append(world.Islands.Count);
                foreach (var island in world.Islands)
                {
                    if (island == null) continue;
                    var props = island.transform.Find("Props");
                    var terrain = island.transform.Find("Terrain");
                    var mesh = terrain != null ? terrain.GetComponent<MeshFilter>().sharedMesh : null;
                    sb.Append(';').Append(F(island.transform.position.x, "F1")).Append(',').Append(F(island.transform.position.z, "F1"))
                      .Append(',').Append(props != null ? props.childCount : -1).Append(',').Append(mesh != null ? mesh.vertexCount : -1)
                      .Append(',').Append(mesh != null ? F(mesh.bounds.max.y, "F2") : "-");
                }
            }
            sb.Append('\n');

            var weather = WeatherSystem.Instance;
            sb.Append("wx:");
            if (weather != null)
            {
                sb.Append(F(weather.ForcedLevel, "F2"));
                foreach (var p in WeatherSamples) sb.Append(',').Append(F(weather.LevelAtTime(p, time)));
            }
            sb.Append('\n');

            sb.Append("wave:").Append(OceanSystem.Instance != null ? OceanSystem.Instance.WaveDigest() : "-").Append('\n');
            sb.Append("hour:").Append(SkyController.Instance != null ? F(SkyController.Instance.HourAt(time)) : "-").Append('\n');

            var ship = FindAnyObjectByType<ShipMotion>();
            sb.Append("ship:");
            if (ship != null)
            {
                Vector3 p = ship.transform.position;
                sb.Append(F(p.x, "F2")).Append(',').Append(F(p.y, "F2")).Append(',').Append(F(p.z, "F2")).Append(',').Append(F(ship.Speed, "F2"));
            }
            sb.Append('\n');

            // 선원 위치: 배 기준, 월드, 배 위인지(1/0). 주인 PC 의 값과 비교한다.
            // 배 위면 배 기준으로 (ShipRelativeSync), 배 밖(헤엄)이면 월드로 (NetworkTransform) 맞춘다.
            sb.Append("pl:");
            foreach (var player in NetworkPlayer.All)
            {
                if (player == null || !player.IsSpawned) continue;
                Vector3 w = player.transform.position;
                Vector3 p = ship != null ? ship.transform.InverseTransformPoint(w) : w;
                var sync = player.GetComponent<ShipRelativeSync>();
                bool aboard = sync != null ? sync.Aboard : ShipMotion.FindShipUnder(w) != null;
                AppendSample(sb.Append(player.OwnerClientId).Append('='), p, w, aboard).Append(player.IsOwner ? "*" : "").Append(';');
            }
            sb.Append('\n');

            // 내 캐릭터가 최근 지나온 자리. 다른 PC 는 네트워크 지연만큼 늦게 보므로, 이 중 어디와든 가까우면 맞는 것이다.
            sb.Append("tr:");
            float now = Time.realtimeSinceStartup;
            for (int i = trail.Count - 1; i >= 0; i -= TrailStride)
            {
                if (now - trail[i].time > HistorySeconds) break;
                AppendSample(sb, trail[i].local, trail[i].world, trail[i].aboard).Append('/');
            }
            sb.Append('\n');
            sb.Append("err:").Append(errorCount).Append(' ').Append(lastError.Replace('\n', ' '));
            return sb.ToString();
        }

        static Dictionary<string, string> Parse(string report)
        {
            var map = new Dictionary<string, string>();
            foreach (var line in report.Split('\n'))
            {
                int colon = line.IndexOf(':');
                if (colon > 0) map[line.Substring(0, colon)] = line.Substring(colon + 1);
            }
            return map;
        }

        // 다른 곳이 있으면 설명, 다 같으면 null.
        string Compare(string mineText, int expected)
        {
            if (reports.Count < expected)
            {
                return $"보고 {reports.Count}/{expected} 만 옴";
            }
            var mine = Parse(mineText);
            var all = new Dictionary<ulong, Dictionary<string, string>> { [NetworkManager.ServerClientId] = mine };
            foreach (var kv in reports) all[kv.Key] = Parse(kv.Value);

            foreach (var kv in all)
            {
                var other = kv.Value;
                // 오류 로그는 새로 늘었을 때만 한 번 실패로 적는다.
                if (other.TryGetValue("err", out var err))
                {
                    int space = err.IndexOf(' ');
                    int count = int.Parse(space > 0 ? err.Substring(0, space) : err, CultureInfo.InvariantCulture);
                    int seen = seenErrors.TryGetValue(kv.Key, out int s) ? s : 0;
                    if (count > seen)
                    {
                        seenErrors[kv.Key] = count;
                        return $"선원 {kv.Key} 오류 로그: {err}";
                    }
                }
                if (kv.Key == NetworkManager.ServerClientId) continue;
                foreach (var key in new[] { "hub", "isl", "wx", "wave", "hour" })
                {
                    string a = mine.TryGetValue(key, out var x) ? x : "";
                    string b = other.TryGetValue(key, out var y) ? y : "";
                    if (a != b)
                    {
                        return $"선원 {kv.Key} {key} 다름: 호스트[{Shorten(a, b)}] 참가자[{Shorten(b, a)}]";
                    }
                }
                string shipDiff = CompareShip(mine["ship"], other["ship"]);
                if (shipDiff != null) return $"선원 {kv.Key} {shipDiff}";
            }

            // 선원 위치: 주인이 최근 1초 동안 지나온 자리 중 하나와 다른 PC 가 보는 자리가 가까워야 한다.
            // 배 위였던 자리는 배 기준, 물이었던 자리는 월드 기준으로 잰다.
            var trails = new Dictionary<string, List<PlayerEntry>>();
            foreach (var kv in all)
            {
                var list = new List<PlayerEntry>();
                foreach (var entry in ParsePlayers(kv.Value["pl"]))
                {
                    if (entry.owner) { list.Add(entry); trails[entry.id] = list; }
                }
                if (list.Count > 0 && kv.Value.TryGetValue("tr", out var tr))
                {
                    foreach (var part in tr.Split('/'))
                    {
                        if (TryParseSample(part, out var sample)) list.Add(sample);
                    }
                }
            }
            foreach (var kv in all)
            {
                foreach (var entry in ParsePlayers(kv.Value["pl"]))
                {
                    if (entry.owner || !trails.TryGetValue(entry.id, out var trail)) continue;
                    float best = float.MaxValue;
                    foreach (var truth in trail)
                    {
                        float d = truth.aboard ? Vector3.Distance(truth.local, entry.local) : Vector3.Distance(truth.world, entry.world);
                        if (d < best) best = d;
                    }
                    if (best > playerTolerance)
                    {
                        return $"선원 {kv.Key} 가 보는 선원 {entry.id} 위치가 {best:F2}m 다름 ({(trail[0].aboard ? "배 위" : "물")})";
                    }
                }
            }
            return null;
        }

        // 참가자 화면의 배 위치가 호스트 배가 최근 1초 동안 지나온 자리 중 하나와 가까워야 한다.
        string CompareShip(string a, string b)
        {
            var pa = a.Split(',');
            var pb = b.Split(',');
            if (pa.Length < 4 || pb.Length < 4) return a == b ? null : "배 없음";
            var vb = new Vector3(P(pb[0]), P(pb[1]), P(pb[2]));
            float best = Vector3.Distance(new Vector3(P(pa[0]), P(pa[1]), P(pa[2])), vb);
            float now = Time.realtimeSinceStartup;
            foreach (var (time, position) in shipHistory)
            {
                if (now - time <= HistorySeconds) best = Mathf.Min(best, Vector3.Distance(position, vb));
            }
            return best > shipTolerance ? $"배 위치 {best:F2}m 다름 (속도 {Mathf.Abs(P(pa[3])):F1})" : null;
        }

        // "선원 2 hub 다름: ..." → "hub"
        static string Reason(string diff)
        {
            foreach (var key in new[] { "hub", "isl", "wx", "wave", "hour", "배 위치", "위치가", "오류", "보고" })
            {
                if (diff.Contains(key)) return key.Replace(" ", "");
            }
            return "기타";
        }

        static float P(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        struct PlayerEntry
        {
            public string id;
            public Vector3 local;
            public Vector3 world;
            public bool aboard;
            public bool owner;
        }

        static IEnumerable<PlayerEntry> ParsePlayers(string text)
        {
            foreach (var part in text.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string rest = part.Substring(eq + 1);
                bool owner = rest.EndsWith("*");
                var v = rest.TrimEnd('*').Split(',');
                if (v.Length < 7) continue;
                yield return new PlayerEntry
                {
                    id = part.Substring(0, eq),
                    local = new Vector3(P(v[0]), P(v[1]), P(v[2])),
                    world = new Vector3(P(v[3]), P(v[4]), P(v[5])),
                    aboard = v[6] == "1",
                    owner = owner,
                };
            }
        }

        // "lx,ly,lz,wx,wy,wz,a"
        static bool TryParseSample(string text, out PlayerEntry sample)
        {
            sample = default;
            var v = text.Split(',');
            if (v.Length < 7) return false;
            sample.local = new Vector3(P(v[0]), P(v[1]), P(v[2]));
            sample.world = new Vector3(P(v[3]), P(v[4]), P(v[5]));
            sample.aboard = v[6] == "1";
            return true;
        }

        // 긴 문자열에서 처음 다른 곳 근처만 보여 준다.
        static string Shorten(string a, string b)
        {
            int i = 0;
            while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
            int start = Mathf.Max(0, i - 20);
            return (start > 0 ? "…" : "") + a.Substring(start, Mathf.Min(60, a.Length - start));
        }
    }
}
