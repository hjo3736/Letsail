using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Letsail.Networking
{
    /// <summary>
    /// 온라인 세션(방) 관리. 호스트가 방을 만들면 참가 코드가 생기고, 친구들은 그 코드로 들어온다.
    /// 연결은 Unity 중계 서버를 거치므로 포트포워딩이 필요 없다. 세션이 만들어지면 Netcode 도 자동으로 시작된다.
    /// </summary>
    public sealed class SessionManager : MonoBehaviour
    {
        public enum State
        {
            SigningIn,
            Ready,
            Connecting,
            InSession,
            Offline,
        }

        public const int MaxPlayers = 4;

        [SerializeField, Tooltip("방에서 나가면 돌아갈 로비 씬 이름")] string lobbyScene = "Lobby";

        public static SessionManager Instance { get; private set; }

        public State Current { get; private set; } = State.SigningIn;
        /// <summary>마지막 오류를 사람이 읽을 수 있게 적은 것. 없으면 null.</summary>
        public string LastError { get; private set; }
        public ISession Session { get; private set; }

        /// <summary>상태, 인원, 오류가 바뀔 때마다 호출된다.</summary>
        public event Action Changed;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // 로비로 돌아오면 씬에 든 NetworkManager 가 하나 더 생긴다. 계속 살아 있는 기존 것을 쓰고 새것은 지운다.
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Letsail.Player.OutfitStorage.Profile = GetProfileName();
            // 접속 중에는 바다 시간을 호스트 시간으로 맞춰서 모든 선원의 파도(와 그 위의 배)가 같게 한다.
            Letsail.Ocean.OceanSystem.TimeSource = () =>
            {
                var nm = NetworkManager.Singleton;
                return nm != null && nm.IsListening ? nm.ServerTime.Time : Time.timeAsDouble;
            };
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        async void Start()
        {
            await SignInAsync();
        }

        /// <summary>Unity 서비스 초기화와 익명 로그인. 실패하면 Offline 이 되고 다시 시도할 수 있다.</summary>
        public async Task SignInAsync()
        {
            if (Current == State.Ready || Current == State.InSession || Current == State.Connecting)
            {
                return;
            }
            SetState(State.SigningIn, null);
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    await UnityServices.InitializeAsync(new InitializationOptions().SetProfile(GetProfileName()));
                }
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }
                SetState(State.Ready, null);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                SetState(State.Offline, "온라인 서비스에 연결하지 못했어요. 인터넷 연결을 확인하고 다시 시도해 주세요.");
            }
        }

        /// <summary>방을 만들고 호스트가 된다. 성공하면 Session.Code 가 참가 코드다.</summary>
        public async Task HostAsync()
        {
            if (Current != State.Ready)
            {
                return;
            }
            SetState(State.Connecting, null);
            try
            {
                var options = new SessionOptions
                {
                    MaxPlayers = MaxPlayers,
                    IsPrivate = true,
                }.WithRelayNetwork();
                Track(await MultiplayerService.Instance.CreateSessionAsync(options));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                SetState(State.Ready, "방을 만들지 못했어요. 잠시 후 다시 시도해 주세요.");
            }
        }

        /// <summary>참가 코드로 친구의 방에 들어간다.</summary>
        public async Task JoinAsync(string code)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (Current != State.Ready || code.Length == 0)
            {
                return;
            }
            SetState(State.Connecting, null);
            try
            {
                Track(await MultiplayerService.Instance.JoinSessionByCodeAsync(code));
            }
            catch (SessionException e)
            {
                Debug.LogException(e);
                SetState(State.Ready, $"참가하지 못했어요. 코드를 확인하거나 방이 꽉 찼는지 확인해 주세요. ({e.Error})");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                SetState(State.Ready, "참가하지 못했어요. 잠시 후 다시 시도해 주세요.");
            }
        }

        /// <summary>방에서 나간다. 호스트가 나가면 방이 없어지고 다른 사람도 모두 나가진다.</summary>
        public async Task LeaveAsync()
        {
            ISession session = Session;
            Untrack();
            if (session != null)
            {
                try
                {
                    await session.LeaveAsync();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"세션에서 나가는 중 오류: {e.Message}");
                }
            }
            StopNetcode();
            ReturnToLobby();
            SetState(State.Ready, null);
        }

        void Track(ISession session)
        {
            Session = session;
            session.Changed += OnSessionChanged;
            session.RemovedFromSession += OnRemovedFromSession;
            session.Deleted += OnRemovedFromSession;
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientStopped += OnNetcodeStopped;
            }
            SetState(State.InSession, null);
        }

        void Untrack()
        {
            if (Session == null)
            {
                return;
            }
            Session.Changed -= OnSessionChanged;
            Session.RemovedFromSession -= OnRemovedFromSession;
            Session.Deleted -= OnRemovedFromSession;
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientStopped -= OnNetcodeStopped;
            }
            Session = null;
        }

        // 세션 알림보다 연결이 먼저 끊기는 경우(호스트 PC가 꺼짐 등)에도 메뉴로 돌아가게 한다.
        async void OnNetcodeStopped(bool wasHost)
        {
            if (Session == null)
            {
                return;
            }
            await LeaveAsync();
            if (!wasHost)
            {
                SetState(State.Ready, "호스트와의 연결이 끊겼어요.");
            }
        }

        void OnSessionChanged()
        {
            Changed?.Invoke();
        }

        void OnRemovedFromSession()
        {
            bool wasHost = Session != null && Session.IsHost;
            Untrack();
            StopNetcode();
            ReturnToLobby();
            SetState(State.Ready, wasHost ? null : "방이 닫혔어요.");
        }

        static void StopNetcode()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
            {
                nm.Shutdown();
            }
        }

        // 항해 중에 방이 끝나면 혼자 바다에 남지 않게 로비로 돌아간다.
        void ReturnToLobby()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != lobbyScene)
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(lobbyScene);
            }
        }

        void SetState(State state, string error)
        {
            Current = state;
            LastError = error;
            Changed?.Invoke();
        }

        /// <summary>
        /// 로그인 프로필 이름. 같은 PC에서 여러 개를 실행하면(Multiplayer Play Mode 가상 플레이어 등)
        /// 서로 다른 익명 계정을 써야 한다. 실행 인자 "-profile 이름" 으로 직접 정할 수도 있다.
        /// </summary>
        public static string GetProfileName()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-profile")
                {
                    return Sanitize(args[i + 1]);
                }
            }

            string path = Application.dataPath.Replace('\\', '/');
            const string marker = "/Library/VP/";
            int at = path.IndexOf(marker, StringComparison.Ordinal);
            if (at >= 0)
            {
                return Sanitize(path.Substring(at + marker.Length).Split('/')[0]);
            }
            return "main";
        }

        static string Sanitize(string value)
        {
            var chars = new System.Text.StringBuilder();
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                {
                    chars.Append(c);
                }
            }
            string result = chars.Length > 0 ? chars.ToString() : "main";
            return result.Length > 30 ? result.Substring(0, 30) : result;
        }
    }
}
