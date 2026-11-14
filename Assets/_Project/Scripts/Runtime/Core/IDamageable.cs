using UnityEngine;

namespace Letsail.Core
{
    /// <summary>
    /// 공격받을 수 있는 것 (동물, 물고기 무리). 서버에서만 불린다.
    /// </summary>
    public interface IDamageable
    {
        /// <summary>죽었으면 더 맞지 않는다.</summary>
        bool IsDead { get; }

        /// <summary>피해를 준다. from 은 공격한 위치 (도망칠 방향 등).</summary>
        void ServerTakeDamage(float amount, Vector3 from);
    }
}
