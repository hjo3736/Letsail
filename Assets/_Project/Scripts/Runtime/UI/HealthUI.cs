using Letsail.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Letsail.UI
{
    /// <summary>
    /// 체력 게이지. 체력이 다 차 있지 않을 때만 화면 가운데 아래(숨 게이지 위)에 나온다.
    /// 맞으면 잠깐 붉게 번쩍이고, 쓰러지면 남은 시간을 알려 준다. HUD 캔버스 아래에 둔다.
    /// </summary>
    public sealed class HealthUI : MonoBehaviour
    {
        [SerializeField] CanvasGroup group;
        [SerializeField, Tooltip("Image, Filled (가로)")] Image fill;
        [SerializeField, Tooltip("쓰러졌을 때 안내 (없어도 된다)")] Text label;
        [SerializeField] Color fullColor = new Color(0.45f, 0.9f, 0.45f);
        [SerializeField] Color lowColor = new Color(1f, 0.35f, 0.3f);
        [SerializeField] Color hitColor = new Color(1f, 0.85f, 0.85f);
        [SerializeField, Range(0f, 1f)] float lowAt = 0.3f;
        [SerializeField] float fadeSpeed = 4f;

        PlayerHealth subscribed;
        float flash;

        void OnDisable()
        {
            if (subscribed != null) subscribed.Damaged -= OnDamaged;
            subscribed = null;
        }

        void OnDamaged(float amount) => flash = 1f;

        void Update()
        {
            var health = PlayerHealth.Local;
            if (health != subscribed)
            {
                if (subscribed != null) subscribed.Damaged -= OnDamaged;
                subscribed = health;
                if (subscribed != null) subscribed.Damaged += OnDamaged;
            }
            float value = health != null ? health.Health01 : 1f;
            bool downed = health != null && health.IsDowned;
            bool show = health != null && (value < 0.999f || downed);
            flash = Mathf.MoveTowards(flash, 0f, Time.unscaledDeltaTime * 3f);

            if (group != null) group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f, fadeSpeed * Time.unscaledDeltaTime);
            if (fill != null)
            {
                fill.fillAmount = value;
                Color color = Color.Lerp(fullColor, lowColor, value < lowAt ? 1f : 0f);
                fill.color = Color.Lerp(color, hitColor, flash);
            }
            if (label != null)
            {
                label.enabled = downed;
                if (downed) label.text = $"쓰러졌어요  ·  동료가 일으켜 주거나 {Mathf.CeilToInt(health.SelfReviveRemaining)}초 뒤 일어나요";
            }
        }
    }
}
