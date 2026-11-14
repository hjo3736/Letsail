using System.Collections.Generic;
using Letsail.Wildlife;
using UnityEditor;
using UnityEngine;

namespace Letsail.Editor
{
    /// <summary>
    /// 야생동물·아이템 FBX 를 가져올 때 설정을 맞춘다 (Blender 지시서 규칙).
    /// - 동물 (Art/Models/Wildlife): 스케일 1, 노멀 그대로, 커스텀 속성, Generic 뼈대(이 모델로 아바타), 루트 모션 없음.
    ///   반복 클립(Idle, Walk, Swim 등)은 Loop Time 을 켠다. wild_* 속성 → WildInfo.
    /// - 아이템 (Art/Models/Items): 스케일 1, 노멀 그대로, 애니메이션 없음.
    /// - 머티리얼: 반짝이지 않게.
    /// </summary>
    public sealed class WildlifeAssetPostprocessor : AssetPostprocessor
    {
        public const string WildlifeFolder = "Assets/_Project/Art/Models/Wildlife/";
        public const string ItemFolder = "Assets/_Project/Art/Models/Items/";
        public const string EffectFolder = "Assets/_Project/Art/Models/Effects/";

        // 이름이 이 동작으로 끝나는 클립은 반복한다.
        static readonly HashSet<string> LoopActions = new HashSet<string>
        {
            "Idle", "Walk", "Run", "Slither", "Hop", "Fly", "Glide", "Swim", "SwimFast", "Pulse", "Sit", "Hang", "Talk", "Lie", "Charge", "Hold",
        };

        bool IsWildlife => assetPath.StartsWith(WildlifeFolder);
        bool IsItem => assetPath.StartsWith(ItemFolder);
        bool IsEffect => assetPath.StartsWith(EffectFolder);

        void OnPreprocessModel()
        {
            if (!IsWildlife && !IsItem && !IsEffect) return;
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            if (IsWildlife || IsEffect)
            {
                importer.importAnimation = true;
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.optimizeGameObjects = false;
            }
            else
            {
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
            }
        }

        void OnPreprocessAnimation()
        {
            if (!IsWildlife && !IsEffect) return;
            var importer = (ModelImporter)assetImporter;
            // 항상 FBX 의 최신 클립 목록에서 시작한다 (새로 추가된 클립이 빠지지 않게).
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                string action = clip.name;
                int underscore = action.LastIndexOf('_');
                if (underscore >= 0) action = action.Substring(underscore + 1);
                // 이펙트 클립(…_Fire)은 한 번만 재생한다.
                clip.loopTime = IsEffect ? clip.name.Contains("Trail") : LoopActions.Contains(action);
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }

        // URP 가 머티리얼 설명으로 머티리얼을 만든 다음에 고치도록 뒤 순서로 돈다.
        public override int GetPostprocessOrder() => 100;

        // FBX 머티리얼은 "머티리얼 설명으로 가져오기" 방식이라 이 단계에서 고쳐야 한다 (OnPostprocessMaterial 은 불리지 않는다).
        void OnPreprocessMaterialDescription(UnityEditor.AssetImporters.MaterialDescription description, Material material, AnimationClip[] clips)
        {
            ApplyMaterial(material);
        }

        void OnPostprocessMaterial(Material material)
        {
            ApplyMaterial(material);
        }

        // 빛나는 머티리얼: 딱정벌레 무늬·박쥐 눈, 용암, 아귀 미끼
        static readonly string[] GlowMaterials = { "BeetleGlow", "LavaCore", "LureGlow" };

        // 이펙트에서 빛나는 불 머티리얼 (README: M_FX_Core, M_FX_Flash, M_FX_Fire, M_FX_Spark)
        static readonly string[] FireMaterials = { "M_FX_Core", "M_FX_Flash", "M_FX_Fire", "M_FX_Spark" };

        void ApplyMaterial(Material material)
        {
            if (!IsWildlife && !IsItem && !IsEffect) return;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            if (!material.HasProperty("_EmissionColor")) return;
            Color baseColor = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;
            float strength = 0f;
            // 불 머티리얼(속불·섬광·불꽃·불똥)만 같은 색으로 강도 2.5. 연기·물·흙·나무 파편은 그대로.
            if (IsEffect)
            {
                foreach (var fire in FireMaterials)
                {
                    if (material.name.Contains(fire)) strength = 2.5f;
                }
            }
            foreach (var glow in GlowMaterials)
            {
                if (material.name.Contains(glow)) strength = 1.6f;
            }
            if (strength <= 0f) return;
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", baseColor * strength);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        void OnPostprocessGameObjectWithUserProperties(GameObject go, string[] names, object[] values)
        {
            if (!IsWildlife || System.Array.IndexOf(names, "wild_biome") < 0) return;
            var w = go.GetComponent<WildInfo>();
            if (w == null) w = go.AddComponent<WildInfo>();
            for (int i = 0; i < names.Length; i++)
            {
                object v = values[i];
                switch (names[i])
                {
                    case "wild_biome": w.biome = v as string; break;
                    case "wild_habitat": w.habitat = v as string; break;
                    case "wild_move": w.move = v as string; break;
                    case "wild_clips": w.clips = v as string; break;
                    case "wild_school_size": w.schoolSize = v as string; break;
                    case "wild_size": w.size = v as string; break;
                    case "wild_speed": w.speed = ToFloat(v); break;
                    case "wild_run_speed": w.runSpeed = ToFloat(v); break;
                    case "wild_swim_speed": w.swimSpeed = ToFloat(v); break;
                    case "wild_flee_radius": w.fleeRadius = ToFloat(v); break;
                    case "wild_aggro_radius": w.aggroRadius = ToFloat(v); break;
                    case "wild_depth_min": w.depthMin = ToFloat(v); break;
                    case "wild_depth_max": w.depthMax = ToFloat(v); break;
                    case "wild_ship_avoid_radius": w.shipAvoidRadius = ToFloat(v); break;
                    case "wild_follow_ship": w.followShip = ToFloat(v) > 0.5f; break;
                    case "wild_damage_touch": w.damageTouch = ToFloat(v) > 0.5f; break;
                    case "wild_rideable_hint": w.rideableHint = ToFloat(v) > 0.5f; break;
                    case "wild_aggressive": w.aggressive = ToFloat(v) > 0.5f; break;
                    case "wild_fantasy": w.fantasy = ToFloat(v) > 0.5f; break;
                }
            }
        }

        static float ToFloat(object value)
        {
            try
            {
                return System.Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0f;
            }
        }
    }
}
