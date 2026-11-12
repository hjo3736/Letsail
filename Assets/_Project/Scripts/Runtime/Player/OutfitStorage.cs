using System;
using UnityEngine;

namespace Letsail.Player
{
    /// <summary>
    /// 내가 고른 복장을 이 PC에 저장한다. 부품 이름으로 저장하므로 모델에 부품이 추가돼도 깨지지 않는다.
    /// </summary>
    public static class OutfitStorage
    {
        const string KeyPrefix = "Letsail.Outfit.";

        [Serializable]
        struct SavedOutfit
        {
            public string hair, hat, face, top, bottom, shoes, back;
        }

        /// <summary>
        /// 저장 칸 이름. 같은 PC에서 여러 개를 실행할 때(가상 플레이어 등) 서로 덮어쓰지 않게 프로필마다 따로 둔다.
        /// </summary>
        public static string Profile { get; set; } = "main";

        public static bool HasSaved => PlayerPrefs.HasKey(KeyPrefix + Profile);

        public static void Save(CharacterOutfit outfit)
        {
            var data = new SavedOutfit
            {
                hair = outfit.Get(CharacterOutfit.Slot.Hair),
                hat = outfit.Get(CharacterOutfit.Slot.Hat),
                face = outfit.Get(CharacterOutfit.Slot.Face),
                top = outfit.Get(CharacterOutfit.Slot.Top),
                bottom = outfit.Get(CharacterOutfit.Slot.Bottom),
                shoes = outfit.Get(CharacterOutfit.Slot.Shoes),
                back = outfit.Get(CharacterOutfit.Slot.Back),
            };
            PlayerPrefs.SetString(KeyPrefix + Profile, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        /// <summary>저장된 복장을 입힌다. 저장된 게 없으면 false.</summary>
        public static bool Load(CharacterOutfit outfit)
        {
            string json = PlayerPrefs.GetString(KeyPrefix + Profile, null);
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }
            SavedOutfit data;
            try
            {
                data = JsonUtility.FromJson<SavedOutfit>(json);
            }
            catch (ArgumentException)
            {
                return false;
            }
            Apply(outfit, CharacterOutfit.Slot.Hair, data.hair);
            Apply(outfit, CharacterOutfit.Slot.Hat, data.hat);
            Apply(outfit, CharacterOutfit.Slot.Face, data.face);
            Apply(outfit, CharacterOutfit.Slot.Top, data.top);
            Apply(outfit, CharacterOutfit.Slot.Bottom, data.bottom);
            Apply(outfit, CharacterOutfit.Slot.Shoes, data.shoes);
            Apply(outfit, CharacterOutfit.Slot.Back, data.back);
            return true;
        }

        // 지금 모델에 없는 부품 이름이면 무시하고 현재 것을 유지한다.
        static void Apply(CharacterOutfit outfit, CharacterOutfit.Slot slot, string part)
        {
            if (string.IsNullOrEmpty(part))
            {
                if (!CharacterOutfit.IsRequired(slot))
                {
                    outfit.Set(slot, "");
                }
                return;
            }
            if (Array.IndexOf(outfit.GetAvailableParts(slot), part) >= 0)
            {
                outfit.Set(slot, part);
            }
        }
    }
}
