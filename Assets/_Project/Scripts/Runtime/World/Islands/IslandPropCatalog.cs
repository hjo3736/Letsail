using System.Collections.Generic;
using UnityEngine;

namespace Letsail.World.Islands
{
    /// <summary>
    /// 섬에 흩뿌릴 수 있는 환경 소품 프리팹 목록. 프리팹마다 PcgInfo 가 붙어 있다.
    /// 에디터 메뉴 Letsail > 환경 소품 프리팹 만들기 가 채운다.
    /// </summary>
    [CreateAssetMenu(menuName = "Letsail/Island Prop Catalog")]
    public sealed class IslandPropCatalog : ScriptableObject
    {
        [SerializeField] List<GameObject> props = new List<GameObject>();

        public IReadOnlyList<GameObject> Props => props;

        public void SetProps(List<GameObject> value) => props = value;

        public GameObject Find(string propName)
        {
            foreach (var prop in props)
            {
                if (prop != null && prop.name == propName)
                {
                    return prop;
                }
            }
            return null;
        }
    }
}
