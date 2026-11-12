using Letsail.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Letsail.UI
{
    /// <summary>
    /// 숨 게이지. 잠수하거나 숨이 덜 찼을 때만 화면 가운데 아래에 나온다.
    /// 숨이 적으면 붉게 바뀌고, 다 떨어지면 깜빡인다. HUD 캔버스 아래에 둔다.
    /// </summary>
    public sealed class BreathUI : MonoBehaviour
    {
        [SerializeField] CanvasGroup group;
        [SerializeField, Tooltip("Image, Filled (가로)")] Image fill;
        [SerializeField] Color fullColor = new Color(0.55f, 0.85f, 1f);
        [SerializeField] Color lowColor = new Color(1f, 0.4f, 0.35f);
        [SerializeField, Range(0f, 1f)] float lowAt = 0.3f;
        [SerializeField, Tooltip("나타나고 사라지는 빠르기")] float fadeSpeed = 4f;

        void Update()
        {
            var breath = PlayerBreath.Local;
            bool show = false;
            float value = 1f;
            if (breath != null)
            {
                var motor = breath.GetComponent<PlayerMotor>();
                value = breath.Breath01;
                show = (motor != null && motor.IsDiving) || value < 0.999f;
            }
            if (group != null)
            {
                group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f, fadeSpeed * Time.unscaledDeltaTime);
            }
            if (fill != null)
            {
                fill.fillAmount = value;
                Color color = value < lowAt ? lowColor : fullColor;
                if (breath != null && breath.IsOutOfBreath)
                {
                    color.a = 0.4f + 0.6f * Mathf.PingPong(Time.unscaledTime * 3f, 1f);
                }
                fill.color = color;
            }
        }
    }
}
