using System;
using System.Collections.Generic;
using UnityEngine;

namespace Letsail.World.Islands
{
    /// <summary>
    /// 공용 컬러맵(Letsail_Colormap.png)의 칸 위치. 색 이름(머티리얼 이름)으로 칸 중심 UV 를 찾는다.
    /// 새로 만드는 지형은 이 칸들의 색만 쓴다 (면마다 UV 를 칸 중심 하나로 몰아 준다).
    /// 에디터 메뉴 Letsail > 환경 소품 프리팹 만들기 가 Letsail_Palette.json 에서 채운다.
    /// </summary>
    [CreateAssetMenu(menuName = "Letsail/Color Palette")]
    public sealed class ColorPalette : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string name;
            public Color color;
            public Vector2 uv;
        }

        [SerializeField] Texture2D colormap;
        [SerializeField] List<Entry> entries = new List<Entry>();

        Dictionary<string, Entry> lookup;

        public Texture2D Colormap => colormap;

        public void Set(Texture2D texture, List<Entry> value)
        {
            colormap = texture;
            entries = value;
            lookup = null;
        }

        public Vector2 Uv(string colorName)
        {
            return TryGet(colorName, out var entry) ? entry.uv : Vector2.zero;
        }

        public bool TryGet(string colorName, out Entry entry)
        {
            if (lookup == null)
            {
                lookup = new Dictionary<string, Entry>();
                foreach (var e in entries)
                {
                    lookup[e.name] = e;
                }
            }
            return lookup.TryGetValue(colorName, out entry);
        }
    }
}
