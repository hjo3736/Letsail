using System.Collections.Generic;
using UnityEngine;

namespace Letsail.Items
{
    /// <summary>
    /// 게임의 모든 아이템. 네트워크로는 이 목록의 번호만 보낸다 (모든 PC 가 같은 목록을 쓴다).
    /// 메뉴 Letsail > 야생동물·아이템 만들기 가 채운다.
    /// </summary>
    [CreateAssetMenu(menuName = "Letsail/Item Catalog", fileName = "ItemCatalog")]
    public sealed class ItemCatalog : ScriptableObject
    {
        [SerializeField] List<ItemDefinition> items = new List<ItemDefinition>();

        public IReadOnlyList<ItemDefinition> Items => items;

        public ItemDefinition Get(int id) => id >= 0 && id < items.Count ? items[id] : null;

        public int IndexOf(ItemDefinition item) => items.IndexOf(item);

        public int IndexOf(string assetName) => items.FindIndex(i => i != null && i.name == assetName);

#if UNITY_EDITOR
        public void SetItems(List<ItemDefinition> list)
        {
            items = list;
        }
#endif
    }
}
