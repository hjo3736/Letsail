using Letsail.Player;
using Letsail.Wildlife;
using UnityEngine;
using UnityEngine.UI;

namespace Letsail.UI
{
    /// <summary>
    /// 화면 위 가운데 보스 체력바. 이 PC 의 선원이 살아 있는 보스에게서 showDistance 안에 있을 때만 보인다.
    /// 보스 이름과 체력 막대를 스스로 만든다 (HUD 캔버스 아래에 빈 오브젝트로 두면 된다).
    /// </summary>
    public sealed class BossHealthUI : MonoBehaviour
    {
        [SerializeField, Tooltip("이 거리 안의 보스만 보인다 (m)")] float showDistance = 45f;
        [SerializeField] Vector2 barSize = new Vector2(520f, 16f);
        [SerializeField] Color fillColor = new Color(0.85f, 0.25f, 0.2f);
        [SerializeField] Color backColor = new Color(0f, 0f, 0f, 0.55f);
        [SerializeField] float fadeSpeed = 4f;

        CanvasGroup group;
        RectTransform fill;
        Text label;
        float shown = 1f;

        void Awake()
        {
            // (Unity 의 가짜 null 때문에 ?? 대신 TryGetComponent 로 확인한다)
            if (!TryGetComponent<RectTransform>(out var rect)) rect = gameObject.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -36f);
            rect.sizeDelta = new Vector2(barSize.x, barSize.y + 30f);
            if (!TryGetComponent(out group)) group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            var back = MakeImage("Back", rect, backColor);
            back.anchorMin = new Vector2(0f, 0f);
            back.anchorMax = new Vector2(1f, 0f);
            back.pivot = new Vector2(0.5f, 0f);
            back.sizeDelta = new Vector2(0f, barSize.y);
            back.anchoredPosition = Vector2.zero;

            fill = MakeImage("Fill", back, fillColor);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = new Vector2(2f, 2f);
            fill.offsetMax = new Vector2(-2f, -2f);

            var labelGo = new GameObject("Name", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(rect, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.sizeDelta = new Vector2(0f, 26f);
            labelRect.anchoredPosition = Vector2.zero;
            label = labelGo.GetComponent<Text>();
            var anyText = FindAnyObjectByType<Text>(FindObjectsInactive.Include);
            if (anyText != null && anyText != label) label.font = anyText.font;
            label.fontSize = 20;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
        }

        static RectTransform MakeImage(string name, RectTransform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }

        void Update()
        {
            var me = PlayerHealth.Local;
            BossAnimal nearest = null;
            float best = showDistance;
            if (me != null)
            {
                foreach (var boss in BossAnimal.All)
                {
                    if (boss == null || boss.IsDead || boss.Health01 <= 0f) continue;
                    float d = Vector3.Distance(boss.transform.position, me.transform.position);
                    if (d < best)
                    {
                        best = d;
                        nearest = boss;
                    }
                }
            }
            group.alpha = Mathf.MoveTowards(group.alpha, nearest != null ? 1f : 0f, fadeSpeed * Time.unscaledDeltaTime);
            if (nearest == null) return;
            label.text = nearest.DisplayName;
            // 막대가 부드럽게 줄어든다.
            shown = Mathf.MoveTowards(shown, nearest.Health01, Time.unscaledDeltaTime * 0.8f);
            fill.anchorMax = new Vector2(Mathf.Clamp01(shown), 1f);
        }
    }
}
