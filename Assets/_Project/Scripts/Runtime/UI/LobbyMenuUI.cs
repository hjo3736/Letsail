using Letsail.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace Letsail.UI
{
    /// <summary>
    /// 로비 메뉴. 접속 전에는 방 만들기 / 코드로 참가를 보여 주고, 접속 후에는 조작 안내만 작게 띄운다.
    /// (참가 코드, 선원 목록, 나가기는 TAB 메뉴에 있다.)
    /// </summary>
    public sealed class LobbyMenuUI : MonoBehaviour
    {
        [Header("접속 전")]
        [SerializeField] GameObject menuPanel;
        [SerializeField] Button hostButton;
        [SerializeField] InputField codeInput;
        [SerializeField] Button joinButton;
        [SerializeField] Button retryButton;
        [SerializeField] Button settingsButton;
        [SerializeField] Text statusText;

        [Header("접속 후")]
        [SerializeField, Tooltip("방에 있을 때 띄우는 조작 안내")] GameObject sessionPanel;

        SessionManager sessions;

        void OnEnable()
        {
            sessions = SessionManager.Instance != null ? SessionManager.Instance : FindAnyObjectByType<SessionManager>();
            if (sessions != null)
            {
                sessions.Changed += Refresh;
            }
            hostButton.onClick.AddListener(OnHost);
            joinButton.onClick.AddListener(OnJoin);
            retryButton.onClick.AddListener(OnRetry);
            settingsButton.onClick.AddListener(OnSettings);
            codeInput.onValueChanged.AddListener(_ => Refresh());
            Refresh();
        }

        void OnDisable()
        {
            if (sessions != null)
            {
                sessions.Changed -= Refresh;
            }
            hostButton.onClick.RemoveListener(OnHost);
            joinButton.onClick.RemoveListener(OnJoin);
            retryButton.onClick.RemoveListener(OnRetry);
            settingsButton.onClick.RemoveListener(OnSettings);
            codeInput.onValueChanged.RemoveAllListeners();
        }

        void Refresh()
        {
            // 플레이 종료 등으로 메뉴가 먼저 사라진 뒤 늦게 온 알림은 무시한다.
            if (this == null)
            {
                return;
            }
            SessionManager.State state = sessions != null ? sessions.Current : SessionManager.State.Offline;
            bool inSession = state == SessionManager.State.InSession;
            menuPanel.SetActive(!inSession);
            sessionPanel.SetActive(inSession);

            bool ready = state == SessionManager.State.Ready;
            hostButton.interactable = ready;
            joinButton.interactable = ready && codeInput.text.Trim().Length > 0;
            codeInput.interactable = ready;
            retryButton.gameObject.SetActive(state == SessionManager.State.Offline);

            string status;
            switch (state)
            {
                case SessionManager.State.SigningIn: status = "온라인 서비스에 연결하는 중..."; break;
                case SessionManager.State.Connecting: status = "접속하는 중..."; break;
                case SessionManager.State.Offline: status = "오프라인"; break;
                default: status = "방을 만들거나 친구에게 받은 코드를 입력하세요."; break;
            }
            if (sessions != null && !string.IsNullOrEmpty(sessions.LastError))
            {
                status = sessions.LastError;
            }
            statusText.text = status;
        }

        async void OnHost()
        {
            await sessions.HostAsync();
        }

        async void OnJoin()
        {
            await sessions.JoinAsync(codeInput.text);
        }

        async void OnRetry()
        {
            await sessions.SignInAsync();
        }

        void OnSettings()
        {
            var settings = SettingsMenuUI.Instance != null ? SettingsMenuUI.Instance : FindAnyObjectByType<SettingsMenuUI>(FindObjectsInactive.Include);
            if (settings != null)
            {
                settings.Open();
            }
        }
    }
}
