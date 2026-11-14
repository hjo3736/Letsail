using UnityEngine;

namespace Letsail.Items
{
    public enum ItemKind
    {
        /// <summary>근접 무기 (칼, 단검).</summary>
        Melee,
        /// <summary>총 (권총, 장총). 화면 가운데로 쏜다.</summary>
        Gun,
        /// <summary>두 손으로 들고 나르는 것 (고기). 들고 있는 동안 공격하지 못한다.</summary>
        Carry,
    }

    /// <summary>
    /// 손에 드는 아이템 하나. 모델은 원점이 쥐는 지점이고, 캐릭터 손 소켓(Socket_Hand_R)에 그대로 붙인다.
    /// 공격 동작·피해·거리·간격은 여기서 정한다. 동작 이름은 캐릭터 애니메이터의 상태 이름(PlayerAnimator.ActionStates).
    /// </summary>
    [CreateAssetMenu(menuName = "Letsail/Item", fileName = "Item")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [Tooltip("화면에 보이는 이름")] public string displayName = "아이템";
        public ItemKind kind;
        [Tooltip("손에 드는 모델 (원점 = 쥐는 지점)")] public GameObject model;

        [Header("공격")]
        [Tooltip("공격 동작 (예: Sword_Slash, Pistol_Shoot)")] public string attackAction;
        [Tooltip("공격 뒤 이어지는 동작 (예: Pistol_Reload). 없으면 비운다")] public string followAction;
        public float damage = 10f;
        [Tooltip("근접: 닿는 거리, 총: 사거리 (m)")] public float range = 1.5f;
        [Tooltip("공격을 시작하고 맞는 순간까지 (동작 길이 비율 0~1)")] [Range(0f, 1f)] public float hitMoment = 0.4f;
        [Tooltip("다음 공격까지 최소 간격 (초). 동작이 더 길면 동작 길이를 쓴다")] public float cooldown = 0.6f;

        [Header("총구 불꽃 (총)")]
        [Tooltip("쏠 때 만드는 이펙트 (MuzzleFlash)")] public GameObject fireEffect;
        [Tooltip("모델 안의 총구 이름 (예: Tool_Musket_Muzzle)")] public string muzzleName;
        [Tooltip("이펙트 크기 (장총 1.3)")] public float fireEffectScale = 1f;

        [Header("바닥에 놓을 때")]
        [Tooltip("놓인 모델의 회전 (눕히기)")] public Vector3 restEuler = new Vector3(0f, 0f, 90f);
        [Tooltip("놓인 모델을 바닥에서 띄우는 높이 (m)")] public float restHeight = 0.05f;
    }
}
