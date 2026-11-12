using Letsail.Core;
using Letsail.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Letsail.UI
{
    /// <summary>
    /// 화면 아래쪽 상호작용 안내.
    /// - 대상이 있으면 "[E]  서랍 열기", 길게 누르는 대상은 "[E 길게]  닻 올리기" 와 진행 막대.
    /// - 쓸 수 없는 대상은 흐리게 이유와 함께 (예: 요리하기 · 재료가 필요해요).
    /// - 자리에 앉아 있으면 "[E]  키 놓기" 와 조작 안내.
    /// </summary>
    public sealed class InteractionPromptUI : MonoBehaviour
    {
        [SerializeField] GameObject root;
        [SerializeField] Text label;
        [SerializeField, Tooltip("길게 누를 때 차오르는 막대 (Image, Filled)")] Image holdFill;
        [SerializeField, Tooltip("자리에 앉아 있을 때 조작 안내")] Text hintText;
        [SerializeField] string keyName = "E";

        static readonly Color Normal = Color.white;
        static readonly Color Dimmed = new Color(1f, 1f, 1f, 0.55f);

        void OnEnable()
        {
            Hide();
        }

        void Update()
        {
            var interactor = PlayerInteractor.Local;
            if (interactor == null)
            {
                Hide();
                return;
            }

            if (interactor.Seat != null)
            {
                Show($"[{keyName}]  {interactor.Seat.LeavePrompt}", Normal, 0f, interactor.Seat.ControlsHint);
                return;
            }

            Interactable target = interactor.Focused;
            if (target == null)
            {
                Hide();
                return;
            }
            if (target.IsBlocked)
            {
                Show($"{target.Prompt}  ·  {target.BlockedReason}", Dimmed, 0f, null);
                return;
            }
            bool hold = target.Mode == Interactable.InteractMode.Hold;
            string key = hold ? $"[{keyName} 길게]" : $"[{keyName}]";
            Show($"{key}  {target.Prompt}", Normal, hold ? interactor.HoldProgress : 0f, null);
        }

        void Show(string text, Color color, float progress, string hint)
        {
            if (!root.activeSelf)
            {
                root.SetActive(true);
            }
            label.text = text;
            label.color = color;
            if (holdFill != null)
            {
                holdFill.fillAmount = progress;
                holdFill.enabled = progress > 0f;
            }
            if (hintText != null)
            {
                hintText.text = hint ?? "";
                hintText.enabled = !string.IsNullOrEmpty(hint);
            }
        }

        void Hide()
        {
            if (root.activeSelf)
            {
                root.SetActive(false);
            }
        }
    }
}
