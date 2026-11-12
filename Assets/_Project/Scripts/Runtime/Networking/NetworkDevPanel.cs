using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Letsail.Networking
{
    /// <summary>
    /// 개발용 접속 패널 (화면 왼쪽 위). 같은 PC나 같은 네트워크에서 직접 연결로 테스트할 때 쓴다.
    /// 인터넷 너머 친구와 코드로 접속하는 정식 로비 UI 가 생기면 빼거나 개발 빌드에서만 켠다.
    /// </summary>
    public sealed class NetworkDevPanel : MonoBehaviour
    {
        [SerializeField] string address = "127.0.0.1";
        [SerializeField] ushort port = 7777;

        void OnGUI()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(10, 10, 260, 200), GUI.skin.box);
            if (!nm.IsClient && !nm.IsServer)
            {
                GUILayout.Label("직접 연결 (개발용)");
                address = GUILayout.TextField(address);
                if (GUILayout.Button("호스트 시작"))
                {
                    Configure(nm, "0.0.0.0");
                    nm.StartHost();
                }
                if (GUILayout.Button("접속"))
                {
                    Configure(nm, address);
                    nm.StartClient();
                }
            }
            else
            {
                string role = nm.IsHost ? "호스트" : nm.IsServer ? "서버" : "클라이언트";
                GUILayout.Label($"{role} · 접속 {(nm.IsServer ? nm.ConnectedClientsIds.Count : NetworkPlayer.All.Count)}명");
                if (NetworkPlayer.Local != null)
                {
                    GUILayout.Label($"내 번호 {NetworkPlayer.Local.SpawnIndex + 1}");
                }
                if (GUILayout.Button("연결 끊기"))
                {
                    nm.Shutdown();
                }
            }
            GUILayout.EndArea();
        }

        void Configure(NetworkManager nm, string listenOrConnect)
        {
            var transport = nm.GetComponent<UnityTransport>();
            if (transport != null)
            {
                transport.SetConnectionData(listenOrConnect == "0.0.0.0" ? "127.0.0.1" : listenOrConnect, port, listenOrConnect == "0.0.0.0" ? "0.0.0.0" : null);
            }
        }
    }
}
