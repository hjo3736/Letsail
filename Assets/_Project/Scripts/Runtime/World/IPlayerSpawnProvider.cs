using UnityEngine;

namespace Letsail.World
{
    /// <summary>
    /// 씬마다 플레이어가 나타날 위치를 알려 준다 (로비의 광장, 항해 씬의 갑판 등).
    /// 씬에 하나만 둔다.
    /// </summary>
    public interface IPlayerSpawnProvider
    {
        /// <summary>플레이어 번호(0부터)에 맞는 위치. 위치보다 사람이 많으면 돌아가며 쓴다.</summary>
        Transform GetSpawnPoint(int playerIndex);
    }

    public static class PlayerSpawn
    {
        /// <summary>지금 씬에서 켜져 있는 스폰 위치 제공자. 없으면 null.</summary>
        public static IPlayerSpawnProvider Find()
        {
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour is IPlayerSpawnProvider provider && behaviour.isActiveAndEnabled)
                {
                    return provider;
                }
            }
            return null;
        }
    }
}
