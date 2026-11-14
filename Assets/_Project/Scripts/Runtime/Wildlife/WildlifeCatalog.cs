using System.Collections.Generic;
using UnityEngine;

namespace Letsail.Wildlife
{
    /// <summary>
    /// 동물 프리팹 목록 (Animal 또는 FishSchool 이 붙은 네트워크 프리팹). 이름(종)으로 찾는다.
    /// 메뉴 Letsail > 야생동물·아이템 만들기 가 채운다.
    /// </summary>
    [CreateAssetMenu(menuName = "Letsail/Wildlife Catalog", fileName = "WildlifeCatalog")]
    public sealed class WildlifeCatalog : ScriptableObject
    {
        [SerializeField] List<GameObject> prefabs = new List<GameObject>();

        public IReadOnlyList<GameObject> Prefabs => prefabs;

        /// <summary>종 이름 (예: Crab, Fish_Striped)으로 프리팹을 찾는다.</summary>
        public GameObject Find(string species)
        {
            foreach (var p in prefabs)
            {
                if (p != null && (p.name == "Animal_" + species || p.name == "School_" + species)) return p;
            }
            return null;
        }

#if UNITY_EDITOR
        public void SetPrefabs(List<GameObject> list)
        {
            prefabs = list;
        }
#endif
    }
}
