using System;
using Letsail.Core;
using UnityEngine;

namespace Letsail.Interaction
{
    /// <summary>
    /// 켜고 끄는 상호작용 (서랍 열기·닫기, 불 피우기·끄기, 닻 내리기·올리기 등).
    /// 상태는 InteractionHub 가 모든 선원에게 맞춰 주고, 모습은 같은 오브젝트의 ToggleVisual 들이 바꾼다.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public sealed class ToggleInteraction : MonoBehaviour
    {
        [SerializeField, Tooltip("꺼져 있을 때의 안내 (예: 서랍 열기)")] string turnOnPrompt = "켜기";
        [SerializeField, Tooltip("켜져 있을 때의 안내 (예: 서랍 닫기)")] string turnOffPrompt = "끄기";
        [SerializeField, Tooltip("처음 상태")] bool initialOn;

        Interactable interactable;
        InteractionHub hub;
        int index = -1;
        bool known;

        public bool InitialOn => initialOn;
        public bool IsOn { get; private set; }

        /// <summary>상태가 바뀔 때 (켜짐 여부, 바로 바꿀지). 처음 상태를 맞출 때는 바로 바꾼다.</summary>
        public event Action<bool, bool> StateChanged;

        void Awake()
        {
            interactable = GetComponent<Interactable>();
            hub = GetComponentInParent<InteractionHub>();
            if (hub != null)
            {
                index = hub.IndexOf(interactable);
            }
            IsOn = initialOn;
        }

        void OnEnable()
        {
            interactable.Interacted += OnUse;
            if (hub != null)
            {
                hub.Changed += OnHubChanged;
            }
            known = false;
            Refresh();
        }

        void OnDisable()
        {
            interactable.Interacted -= OnUse;
            if (hub != null)
            {
                hub.Changed -= OnHubChanged;
            }
        }

        void OnUse(GameObject who)
        {
            if (hub != null && index >= 0)
            {
                hub.RequestToggle(index);
            }
            else
            {
                Set(!IsOn, false);
            }
        }

        /// <summary>켜짐/꺼짐을 정해진 값으로 바꾼다 (모두에게 맞춰진다).</summary>
        public void RequestSet(bool on)
        {
            if (hub != null && index >= 0)
            {
                hub.RequestSetState(index, on);
            }
            else
            {
                Set(on, false);
            }
        }

        void OnHubChanged(int changed)
        {
            if (changed < 0 || changed == index)
            {
                Refresh();
            }
        }

        void Refresh()
        {
            bool on = hub != null && index >= 0 ? hub.GetState(index) : IsOn;
            Set(on, !known);
            known = true;
        }

        void Set(bool on, bool instant)
        {
            bool changed = on != IsOn;
            IsOn = on;
            interactable.Prompt = on ? turnOffPrompt : turnOnPrompt;
            if (changed || instant)
            {
                StateChanged?.Invoke(on, instant);
            }
        }
    }
}
