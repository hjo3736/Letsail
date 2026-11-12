using Letsail.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace Letsail.UI
{
    /// <summary>
    /// 출항 안내 (화면 위쪽). 누군가 승선 구역에 들어가면 인원을 보여 주고, 모두 모이면 카운트다운을 보여 준다.
    /// </summary>
    public sealed class DepartureUI : MonoBehaviour
    {
        [SerializeField] GameObject root;
        [SerializeField] Text label;

        void Update()
        {
            var departure = DepartureController.Instance;
            bool show = departure != null && departure.IsSpawned && departure.BoardedCount > 0;
            if (root.activeSelf != show)
            {
                root.SetActive(show);
            }
            if (!show)
            {
                return;
            }

            label.text = departure.IsCountingDown
                ? $"출항까지 {Mathf.CeilToInt(departure.SecondsLeft)}초"
                : $"부두에 모두 모이면 출항해요  ({departure.BoardedCount} / {departure.CrewCount})";
        }
    }
}
