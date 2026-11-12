using UnityEngine;

namespace Letsail.World
{
    /// <summary>
    /// 자식 오브젝트들을 스폰 위치로 쓴다 (자식 순서대로 1번, 2번...). 항해 씬의 갑판 등에 둔다.
    /// </summary>
    public sealed class SpawnPoints : MonoBehaviour, IPlayerSpawnProvider
    {
        public Transform GetSpawnPoint(int playerIndex)
        {
            if (transform.childCount == 0)
            {
                return transform;
            }
            return transform.GetChild(Mathf.Abs(playerIndex) % transform.childCount);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f);
            foreach (Transform t in transform)
            {
                Gizmos.DrawWireSphere(t.position + Vector3.up * 0.5f, 0.35f);
                Gizmos.DrawRay(t.position + Vector3.up * 0.5f, t.forward * 0.8f);
            }
        }
    }
}
