using System.Collections.Generic;
using Letsail.Networking;
using Letsail.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Letsail.UI
{
    /// <summary>
    /// TAB 을 누르고 있는 동안 보이는 선원 메뉴. 선원 목록, 참가 코드(복사·가리기), 나가기가 있다 (설정은 Esc).
    /// 누르고 있는 동안만 커서가 풀리고, 떼면 바로 다시 조작한다. 방에 있을 때만 열린다.
    /// </summary>
    public sealed class TabMenuUI : MonoBehaviour
    {
        [SerializeField] GameObject panel;
        [SerializeField] Text titleText;
        [SerializeField, Tooltip("선원 한 명당 한 줄. 최대 인원만큼 둔다")] Text[] crewRows;
        [SerializeField] Text codeText;
        [SerializeField] Button copyButton;
        [SerializeField, Tooltip("참가 코드를 가리거나 보여 주는 버튼 (방송·화면 공유용)")] Button hideCodeButton;
        [SerializeField] Button leaveButton;
        [SerializeField, Tooltip("복사 버튼에 '복사됨' 을 보여 주는 시간 (초)")] float copiedFeedbackSeconds = 1.5f;

        readonly List<NetworkPlayer> crew = new List<NetworkPlayer>();
        SessionManager sessions;
        // 방에 들어갈 때마다 코드는 가려진 상태로 시작한다 (방송·화면 공유 중 실수로 노출되지 않게).
        bool hideCode = true;
        bool wasInSession;
        float copiedUntil = -1f;

        public bool IsOpen => panel.activeSelf;

        void Awake()
        {
            copyButton.onClick.AddListener(OnCopy);
            hideCodeButton.onClick.AddListener(OnToggleHideCode);
            leaveButton.onClick.AddListener(OnLeave);
            panel.SetActive(false);
        }

        void OnDisable()
        {
            SetOpen(false);
        }

        void Update()
        {
            if (sessions == null)
            {
                sessions = SessionManager.Instance;
            }
            bool inSession = sessions != null && sessions.Current == SessionManager.State.InSession && sessions.Session != null;
            if (inSession && !wasInSession)
            {
                hideCode = true;
            }
            wasInSession = inSession;

            // 설정이 열려 있으면 TAB 은 무시한다 (설정 위에 겹치지 않게).
            bool settingsOpen = SettingsMenuUI.Instance != null && SettingsMenuUI.Instance.IsOpen;
            bool held = Keyboard.current != null && Keyboard.current.tabKey.isPressed;
            SetOpen(inSession && held && !settingsOpen);
            if (IsOpen)
            {
                Refresh();
            }
        }

        void SetOpen(bool open)
        {
            if (panel.activeSelf != open)
            {
                panel.SetActive(open);
            }
            PlayerCamera.HoldCursor(this, open);
        }

        void Refresh()
        {
            var session = sessions.Session;
            titleText.text = $"선원  {session.PlayerCount} / {session.MaxPlayers}";

            crew.Clear();
            crew.AddRange(NetworkPlayer.All);
            crew.Sort((a, b) => a.SpawnIndex.CompareTo(b.SpawnIndex));
            for (int i = 0; i < crewRows.Length; i++)
            {
                crewRows[i].text = i < crew.Count ? CrewLabel(crew[i]) : "· 빈 자리";
                crewRows[i].color = new Color(1f, 1f, 1f, i < crew.Count ? 1f : 0.35f);
            }

            string code = session.Code ?? "";
            codeText.text = $"참가 코드   {(hideCode ? new string('●', code.Length) : code)}";
            hideCodeButton.GetComponentInChildren<Text>().text = hideCode ? "코드 보이기" : "코드 숨기기";
            copyButton.GetComponentInChildren<Text>().text = Time.unscaledTime < copiedUntil ? "복사됨!" : "코드 복사";
        }

        static string CrewLabel(NetworkPlayer player)
        {
            string label = $"· 선원 {player.SpawnIndex + 1}";
            if (player.OwnerClientId == NetworkManager.ServerClientId)
            {
                label += "  (선장)";
            }
            if (player.IsOwner)
            {
                label += "  ← 나";
            }
            return label;
        }

        void OnCopy()
        {
            if (sessions != null && sessions.Session != null)
            {
                GUIUtility.systemCopyBuffer = sessions.Session.Code;
                copiedUntil = Time.unscaledTime + copiedFeedbackSeconds;
            }
        }

        void OnToggleHideCode()
        {
            hideCode = !hideCode;
        }

        async void OnLeave()
        {
            SetOpen(false);
            if (sessions != null)
            {
                await sessions.LeaveAsync();
            }
        }
    }
}
