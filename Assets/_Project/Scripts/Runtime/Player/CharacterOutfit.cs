using System;
using System.Collections.Generic;
using UnityEngine;

namespace Letsail.Player
{
    /// <summary>
    /// 캐릭터 모델의 커스터마이징 부품을 부위마다 하나씩만 켠다.
    /// 부품 오브젝트 이름은 "부위_이름" 형식이다 (예: Hat_Tricorn, Top_StripedShirt).
    /// 부품은 이름(사람이 읽기 좋음)이나 번호(네트워크로 보내기 좋음)로 고를 수 있다.
    /// 번호 0은 "없음", 1부터는 이름순으로 정렬한 부품 목록이다.
    /// </summary>
    [ExecuteAlways]
    public sealed class CharacterOutfit : MonoBehaviour
    {
        public enum Slot
        {
            Hair,
            Hat,
            Face,
            Top,
            Bottom,
            Shoes,
            Back,
        }

        public static readonly Slot[] AllSlots = (Slot[])Enum.GetValues(typeof(Slot));

        [SerializeField, Tooltip("부품들이 들어 있는 모델. 비우면 자기 자신")]
        Transform model;

        [Header("부품 이름 (접두어 제외). 비우면 해당 부위 없음")]
        [SerializeField] string hair = "Messy";
        [SerializeField] string hat = "Bandana";
        [SerializeField] string face = "";
        [SerializeField] string top = "StripedShirt";
        [SerializeField] string bottom = "Pants";
        [SerializeField] string shoes = "Boots";
        [SerializeField] string back = "";

        readonly Dictionary<Slot, string[]> partCache = new Dictionary<Slot, string[]>();

        /// <summary>어느 부위든 바뀌면 호출된다.</summary>
        public event Action Changed;

        /// <summary>"없음"을 고를 수 없는 부위 (맨머리, 맨몸, 맨발은 안 된다).</summary>
        public static bool IsRequired(Slot slot)
        {
            return slot == Slot.Hair || slot == Slot.Top || slot == Slot.Bottom || slot == Slot.Shoes;
        }

        void OnEnable()
        {
            partCache.Clear();
            Apply();
        }

        void OnValidate()
        {
            partCache.Clear();
            Apply();
        }

        public string Get(Slot slot)
        {
            switch (slot)
            {
                case Slot.Hair: return hair;
                case Slot.Hat: return hat;
                case Slot.Face: return face;
                case Slot.Top: return top;
                case Slot.Bottom: return bottom;
                case Slot.Shoes: return shoes;
                case Slot.Back: return back;
                default: throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }

        public void Set(Slot slot, string part)
        {
            part ??= "";
            if (Get(slot) == part)
            {
                return;
            }
            switch (slot)
            {
                case Slot.Hair: hair = part; break;
                case Slot.Hat: hat = part; break;
                case Slot.Face: face = part; break;
                case Slot.Top: top = part; break;
                case Slot.Bottom: bottom = part; break;
                case Slot.Shoes: shoes = part; break;
                case Slot.Back: back = part; break;
            }
            Apply();
            Changed?.Invoke();
        }

        /// <summary>모델에 있는 해당 부위의 부품 이름 목록 (접두어 제외, 이름순).</summary>
        public string[] GetAvailableParts(Slot slot)
        {
            if (partCache.TryGetValue(slot, out string[] cached))
            {
                return cached;
            }
            string prefix = slot + "_";
            var parts = new List<string>();
            foreach (Transform child in Root)
            {
                if (child.name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    parts.Add(child.name.Substring(prefix.Length));
                }
            }
            parts.Sort(StringComparer.Ordinal);
            string[] result = parts.ToArray();
            partCache[slot] = result;
            return result;
        }

        /// <summary>현재 부품의 번호. 0은 없음, 모르는 이름도 0.</summary>
        public int GetIndex(Slot slot)
        {
            string current = Get(slot);
            if (string.IsNullOrEmpty(current))
            {
                return 0;
            }
            int i = Array.IndexOf(GetAvailableParts(slot), current);
            return i < 0 ? 0 : i + 1;
        }

        /// <summary>번호로 부품을 고른다. 범위를 벗어나면 없음.</summary>
        public void SetIndex(Slot slot, int index)
        {
            string[] parts = GetAvailableParts(slot);
            Set(slot, index >= 1 && index <= parts.Length ? parts[index - 1] : "");
        }

        /// <summary>선택한 부품만 켜고 같은 부위의 나머지는 끈다. 입은 옷이 덮는 몸 부위(Body_*)는 숨긴다.</summary>
        public void Apply()
        {
            coveredRegions.Clear();
            foreach (Transform child in Root)
            {
                int separator = child.name.IndexOf('_');
                if (separator <= 0 || !Enum.TryParse(child.name.Substring(0, separator), out Slot slot))
                {
                    continue;
                }
                string wanted = Get(slot);
                bool on = !string.IsNullOrEmpty(wanted) && child.name.Length == separator + 1 + wanted.Length
                    && string.CompareOrdinal(child.name, separator + 1, wanted, 0, wanted.Length) == 0;
                if (child.gameObject.activeSelf != on)
                {
                    child.gameObject.SetActive(on);
                }
                if (on && Coverage.TryGetValue(child.name, out string[] regions))
                {
                    coveredRegions.UnionWith(regions);
                }
            }

            // 옷 안에 몸이 있으면 움직일 때 옷을 뚫고 나온다. 덮인 몸 부위는 아예 숨긴다.
            // 모델의 몸이 Body_Torso, Body_Thighs 처럼 부위별로 나뉘어 있을 때만 동작한다.
            const string bodyPrefix = "Body_";
            foreach (Transform child in Root)
            {
                if (!child.name.StartsWith(bodyPrefix, StringComparison.Ordinal))
                {
                    continue;
                }
                bool visible = !coveredRegions.Contains(child.name.Substring(bodyPrefix.Length));
                if (child.gameObject.activeSelf != visible)
                {
                    child.gameObject.SetActive(visible);
                }
            }
        }

        readonly HashSet<string> coveredRegions = new HashSet<string>();

        /// <summary>
        /// 옷마다 덮는 몸 부위. 부위 이름은 몸 오브젝트 이름 Body_부위 에서 따온다.
        /// 여기에 없는 옷은 아무 부위도 숨기지 않는다 (샌들처럼 맨살이 보이는 옷).
        /// </summary>
        static readonly Dictionary<string, string[]> Coverage = new Dictionary<string, string[]>
        {
            // 반팔
            { "Top_TShirt", new[] { "Torso", "UpperArms" } },
            { "Top_StripedShirt", new[] { "Torso", "UpperArms" } },
            { "Top_SailorShirt", new[] { "Torso", "UpperArms" } },
            { "Top_ExplorerShirt", new[] { "Torso", "UpperArms" } },
            // 긴팔
            { "Top_Hoodie", new[] { "Torso", "UpperArms", "Forearms" } },
            { "Top_CaptainCoat", new[] { "Torso", "UpperArms", "Forearms" } },
            { "Top_ChefJacket", new[] { "Torso", "UpperArms", "Forearms" } },
            { "Top_Armor", new[] { "Torso", "UpperArms", "Forearms" } },
            // 하의
            { "Bottom_Pants", new[] { "Hips", "Thighs", "Calves" } },
            { "Bottom_GrayPants", new[] { "Hips", "Thighs", "Calves" } },
            { "Bottom_ArmorLegs", new[] { "Hips", "Thighs", "Calves" } },
            { "Bottom_Shorts", new[] { "Hips", "Thighs" } },
            { "Bottom_Skirt", new[] { "Hips", "Thighs" } },
            // 신발
            { "Shoes_Boots", new[] { "Feet", "Calves" } },
            { "Shoes_ArmorBoots", new[] { "Feet", "Calves" } },
            { "Shoes_Sneakers", new[] { "Feet" } },
        };

        Transform Root => model != null ? model : transform;

        static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
        {
            { "Long", "긴 머리" }, { "Messy", "덥수룩한 머리" }, { "Ponytail", "포니테일" }, { "Short", "짧은 머리" },
            { "Bandana", "반다나" }, { "Beanie", "비니" }, { "Cap", "야구 모자" }, { "Chef", "요리사 모자" }, { "Crown", "왕관" },
            { "KnightHelmet", "기사 투구" }, { "SailorCap", "수병모" }, { "Straw", "밀짚모자" }, { "Tricorn", "삼각모" }, { "Wizard", "마법사 모자" },
            { "Beard", "수염" }, { "Eyepatch", "안대" }, { "Glasses", "안경" },
            { "Armor", "갑옷" }, { "CaptainCoat", "선장 코트" }, { "ChefJacket", "요리사 옷" }, { "ExplorerShirt", "탐험가 셔츠" },
            { "Hoodie", "후드티" }, { "SailorShirt", "수병복" }, { "StripedShirt", "줄무늬 셔츠" }, { "TShirt", "티셔츠" },
            { "ArmorLegs", "갑옷 바지" }, { "GrayPants", "회색 바지" }, { "Pants", "바지" }, { "Shorts", "반바지" }, { "Skirt", "치마" },
            { "ArmorBoots", "철 장화" }, { "Boots", "장화" }, { "Sandals", "샌들" }, { "Sneakers", "운동화" },
            { "Backpack", "배낭" }, { "Cape", "망토" },
        };

        public static string GetSlotDisplayName(Slot slot)
        {
            switch (slot)
            {
                case Slot.Hair: return "머리";
                case Slot.Hat: return "모자";
                case Slot.Face: return "얼굴";
                case Slot.Top: return "상의";
                case Slot.Bottom: return "하의";
                case Slot.Shoes: return "신발";
                case Slot.Back: return "등";
                default: return slot.ToString();
            }
        }

        public static string GetPartDisplayName(string part)
        {
            if (string.IsNullOrEmpty(part))
            {
                return "없음";
            }
            return DisplayNames.TryGetValue(part, out string name) ? name : part;
        }
    }
}
