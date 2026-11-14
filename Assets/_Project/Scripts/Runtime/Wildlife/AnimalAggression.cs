using UnityEngine;

namespace Letsail.Wildlife
{
    /// <summary>동물의 성향.</summary>
    public enum Temperament
    {
        /// <summary>먼저 공격하지 않는다. 가까이 가면 도망친다.</summary>
        Friendly,
        /// <summary>세력권에 들어온 선원을 먼저 공격한다.</summary>
        Hostile,
    }

    /// <summary>
    /// 동물의 공격 설정. preemptive 가 켜져 있으면 적대적(Hostile): 세력권에 들어온 선원을 먼저 공격한다.
    /// 꺼져 있으면 방어적인 친화적 동물: 먼저 공격하지 않고, 맞으면 공격한 선원에게 반격한다.
    /// 이 컴포넌트가 없는 동물은 맞으면 도망만 친다.
    /// 세력권(집에서 territoryRadius) 안에 선원이 들어오면 쫓아가 공격한다. 세력권을 leash 배 넘게 벗어나면 그만두고 집으로 돌아간다.
    /// 땅 동물은 땅 위 선원만, 바다 동물은 물속 선원만 노린다. 쓰러진 선원과 배 위 선원은 노리지 않는다.
    /// 메뉴 Letsail > 야생동물·아이템 만들기 가 종마다 값을 넣는다.
    /// </summary>
    public sealed class AnimalAggression : MonoBehaviour
    {
        [Tooltip("켜면 적대적: 세력권에 들어온 선원을 먼저 공격. 끄면 맞았을 때만 반격")] public bool preemptive = true;
        [Tooltip("세력권 반경 (m). 이 안에 선원이 들어오면 공격한다")] public float territoryRadius = 8f;
        [Tooltip("쫓다가 세력권의 이 배수 넘게 벗어나면 그만둔다")] public float leash = 1.5f;
        [Tooltip("한 번 공격 피해")] public float damage = 10f;
        [Tooltip("공격이 닿는 거리 (m)")] public float attackRange = 1.2f;
        [Tooltip("다음 공격까지 (초)")] public float cooldown = 2f;
        [Tooltip("공격 동작 중 맞는 순간 (동작 길이 비율)")] [Range(0f, 1f)] public float hitMoment = 0.5f;
        [Tooltip("공격 동작 (Attack, Strike)")] public string attackAction = "Attack";
        [Tooltip("가끔 섞어 쓰는 다른 공격 동작 (Swipe). 없으면 비운다")] public string altAttackAction;
        [Tooltip("처음 알아챘을 때 동작 (Roar, WakeUp). 없으면 비운다")] public string alertAction;
        [Tooltip("쫓는 속도 배율 (달리기 속도 기준)")] public float chaseSpeed = 1f;
        [Tooltip("맞으면 세력권 밖이라도 이 배수 안의 공격한 선원을 쫓는다")] public float retaliateRange = 2.5f;
    }
}
