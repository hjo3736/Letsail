using Letsail.World.Islands;
using UnityEditor;
using UnityEngine;

namespace Letsail.Editor
{
    /// <summary>
    /// 환경 소품 FBX 와 컬러맵을 가져올 때 설정을 맞춘다 (Blender 지시서 규칙).
    /// - 모델: 스케일 1, 단위 변환, 노멀 그대로(플랫 셰이딩 유지), 커스텀 속성 읽기, 애니메이션·카메라·조명 끔.
    /// - pcg_* 속성 → PcgInfo. (빛 위치의 조명은 EnvironmentSetup 이 이름으로 붙인다: 임포트 중에는 Light 를 붙일 수 없다.)
    /// - 머티리얼: 반짝이지 않게 (스무스니스·메탈릭 0).
    /// - 컬러맵·지형 색: Point 필터, 압축·밉맵 끔.
    /// </summary>
    public sealed class EnvironmentAssetPostprocessor : AssetPostprocessor
    {
        const string ModelFolder = "Assets/_Project/Art/Models/Environment/";
        const string PaletteFolder = "Assets/_Project/Art/Textures/Palette/";
        const string TerrainColorFolder = "Assets/_Project/Art/Textures/Environment/";

        bool IsEnvironmentModel => assetPath.StartsWith(ModelFolder);

        void OnPreprocessModel()
        {
            if (!IsEnvironmentModel)
            {
                return;
            }
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.addCollider = false;
            importer.isReadable = true; // MeshCollider 용
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(PaletteFolder) && !assetPath.StartsWith(TerrainColorFolder))
            {
                return;
            }
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
        }

        void OnPostprocessMaterial(Material material)
        {
            if (!IsEnvironmentModel)
            {
                return;
            }
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        }

        void OnPostprocessGameObjectWithUserProperties(GameObject go, string[] names, object[] values)
        {
            if (!IsEnvironmentModel)
            {
                return;
            }
            if (System.Array.IndexOf(names, "pcg_biome") >= 0)
            {
                var info = go.GetComponent<PcgInfo>();
                if (info == null) info = go.AddComponent<PcgInfo>();
                for (int i = 0; i < names.Length; i++)
                {
                    object v = values[i];
                    switch (names[i])
                    {
                        case "pcg_biome": info.biome = v as string; break;
                        case "pcg_category": info.category = v as string; break;
                        case "pcg_collider": info.colliderType = v as string; break;
                        case "pcg_radius": info.radius = ToFloat(v); break;
                        case "pcg_slope_max": info.slopeMax = ToFloat(v); break;
                        case "pcg_height_min": info.heightMin = ToFloat(v); break;
                        case "pcg_height_max": info.heightMax = ToFloat(v); break;
                        case "pcg_scale_min": info.scaleMin = ToFloat(v); break;
                        case "pcg_scale_max": info.scaleMax = ToFloat(v); break;
                        case "pcg_align_normal": info.alignNormal = ToFloat(v) > 0.5f; break;
                    }
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
