using System;
using System.Collections.Generic;
using UnityEngine;

namespace Letsail.Core
{
    /// <summary>
    /// 플레이어가 가까이 가서 상호작용 키(E)로 쓸 수 있는 대상. 키, 돛, 닻, 옷장, 수리 지점 등이 이것을 단다.
    /// 실제 동작은 같은 오브젝트의 다른 컴포넌트가 Interacted 이벤트를 받아서 한다.
    /// 한 번 누르는 대상과 길게 눌러야 하는 대상(닻 감기 등)이 있다.
    /// </summary>
    public sealed class Interactable : MonoBehaviour
    {
        public enum InteractMode
        {
            /// <summary>한 번 누르면 바로 실행.</summary>
            Press,
            /// <summary>holdSeconds 동안 누르고 있어야 실행.</summary>
            Hold,
        }

        static readonly List<Interactable> all = new List<Interactable>();

        [SerializeField, Tooltip("화면에 보이는 안내 문구 (예: 옷장 열기)")] string prompt = "상호작용";
        [SerializeField, Tooltip("이 거리 안에 들어와야 쓸 수 있다 (m)")] float radius = 1.8f;
        [SerializeField, Tooltip("거리를 재는 기준점. 비우면 자기 위치")] Transform point;
        [SerializeField, Tooltip("바라보는 대상 (서랍, 매트리스 등). 화면 가운데에 가장 가까운 대상을 고를 때 쓴다. 비우면 서는 자리보다 0.9m 위")]
        Transform aimPoint;
        [SerializeField, Tooltip("바라보는 대상에서 위로 올린 높이 (m)")] float aimHeight;
        [SerializeField] InteractMode mode = InteractMode.Press;
        [SerializeField, Tooltip("길게 누르기일 때 눌러야 하는 시간 (초)")] float holdSeconds = 1.5f;
        [SerializeField, Tooltip("비어 있지 않으면 안내는 보이지만 쓸 수 없다 (예: 재료가 필요해요)")] string blockedReason;

        /// <summary>지금 씬에서 켜져 있는 모든 대상.</summary>
        public static IReadOnlyList<Interactable> All => all;

        public string Prompt
        {
            get => prompt;
            set => prompt = value;
        }

        public float Radius
        {
            get => radius;
            set => radius = value;
        }

        public Vector3 Position => point != null ? point.position : transform.position;

        /// <summary>플레이어가 바라봐야 하는 위치. 여러 대상이 가까이 있을 때 이것으로 고른다.</summary>
        public Vector3 AimPosition => aimPoint != null ? aimPoint.position + Vector3.up * aimHeight : Position + Vector3.up * 0.9f;

        /// <summary>바라보는 대상 오브젝트. 시야를 가리는지 볼 때 이 대상의 충돌체는 가림으로 치지 않는다.</summary>
        public Transform AimTarget => aimPoint;

        public InteractMode Mode
        {
            get => mode;
            set => mode = value;
        }

        public float HoldSeconds
        {
            get => holdSeconds;
            set => holdSeconds = value;
        }

        /// <summary>쓸 수 없는 이유. 비어 있으면 쓸 수 있다. 안내에 함께 보인다 (다른 선원이 쓰는 중 등).</summary>
        public string BlockedReason
        {
            get => blockedReason;
            set => blockedReason = value;
        }

        public bool IsBlocked => !string.IsNullOrEmpty(blockedReason);

        /// <summary>상호작용한 플레이어 오브젝트를 넘겨준다.</summary>
        public event Action<GameObject> Interacted;

        /// <summary>false 면 근처에 있어도 안내가 뜨지 않는다.</summary>
        public bool Available { get; set; } = true;

        void OnEnable()
        {
            all.Add(this);
        }

        void OnDisable()
        {
            all.Remove(this);
        }

        public void Interact(GameObject who)
        {
            if (Available && !IsBlocked)
            {
                Interacted?.Invoke(who);
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.6f);
            Gizmos.DrawWireSphere(Position, radius);
        }
    }
}
