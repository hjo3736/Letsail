using System.Collections.Generic;
using System.IO;
using Letsail.World.Islands;
using UnityEditor;
using UnityEngine;

namespace Letsail.Editor
{
    /// <summary>
    /// 가져온 환경 소품으로 섬 생성에 필요한 것을 만든다. 메뉴 Letsail > 환경 소품 프리팹 만들기.
    /// 1. 컬러맵 팔레트 (Letsail_Palette.json → ColorPalette)
    /// 2. 지형 머티리얼 (컬러맵 하나, 반짝임 없음)
    /// 3. 소품 프리팹 (모델 + pcg_collider 대로 콜라이더, 떠다니는 것은 파도 따라 뜨기, 바다 바위는 거품 고리)
    /// 4. 소품 목록 (IslandPropCatalog) 과 섬 프리팹 (IslandGenerator)
    /// 모델이 바뀌면 다시 누르면 된다 (프리팹을 새로 만든다).
    /// </summary>
    public static class EnvironmentSetup
    {
        const string ModelRoot = "Assets/_Project/Art/Models/Environment";
        const string PrefabRoot = "Assets/_Project/Prefabs/Environment";
        const string DataRoot = "Assets/_Project/ScriptableObjects/Environment";
        const string ColormapPath = "Assets/_Project/Art/Textures/Palette/Letsail_Colormap.png";
        const string PaletteJsonPath = "Assets/_Project/Art/Textures/Palette/Letsail_Palette.json";
        const string MaterialPath = "Assets/_Project/Art/Materials/Environment/M_IslandTerrain.mat";
        const string FoamRingPath = ModelRoot + "/Water/Water_FoamRing.fbx";
        static readonly string[] Biomes = { "Beach", "Jungle", "Volcanic", "Sea", "Shore" };

        [System.Serializable] class PaletteJson { public PaletteColor[] colors; }
        [System.Serializable] class PaletteColor { public string hex; public float[] uv; public string[] materials; }

        [MenuItem("Letsail/환경 소품 프리팹 만들기")]
        public static void Run()
        {
            var palette = BuildPalette();
            var material = BuildMaterial(palette);
            var catalog = BuildPrefabs();
            BuildIslandPrefab(palette, material, catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"환경 소품 {catalog.Props.Count}개 프리팹, 팔레트, 지형 머티리얼, 섬 프리팹을 만들었다.");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        static ColorPalette BuildPalette()
        {
            var json = JsonUtility.FromJson<PaletteJson>(File.ReadAllText(PaletteJsonPath));
            var entries = new List<ColorPalette.Entry>();
            foreach (var c in json.colors)
            {
                ColorUtility.TryParseHtmlString(c.hex, out var color);
                var uv = c.uv != null && c.uv.Length >= 2 ? new Vector2(c.uv[0], c.uv[1]) : Vector2.zero;
                foreach (var name in c.materials)
                {
                    entries.Add(new ColorPalette.Entry { name = name, color = color, uv = uv });
                }
            }
            var palette = LoadOrCreate<ColorPalette>(DataRoot + "/ColorPalette.asset");
            palette.Set(AssetDatabase.LoadAssetAtPath<Texture2D>(ColormapPath), entries);
            EditorUtility.SetDirty(palette);
            return palette;
        }

        static Material BuildMaterial(ColorPalette palette)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                EnsureFolder(Path.GetDirectoryName(MaterialPath).Replace('\\', '/'));
                material = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.SetTexture("_BaseMap", palette.Colormap);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0f);
            material.SetFloat("_SpecularHighlights", 0f);
            material.DisableKeyword("_SPECULAR_COLOR");
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            EditorUtility.SetDirty(material);
            return material;
        }

        static IslandPropCatalog BuildPrefabs()
        {
            var foamRing = AssetDatabase.LoadAssetAtPath<GameObject>(FoamRingPath);
            var prefabs = new List<GameObject>();
            foreach (var biome in Biomes)
            {
                string folder = $"{ModelRoot}/{biome}";
                if (!AssetDatabase.IsValidFolder(folder)) continue;
                string prefabFolder = $"{PrefabRoot}/{biome}";
                EnsureFolder(prefabFolder);
                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var info = model.GetComponentInChildren<PcgInfo>(true);
                    if (info == null)
                    {
                        Debug.LogWarning($"pcg 속성이 없어 건너뜀: {path}");
                        continue;
                    }
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                    try
                    {
                        // 속성이 루트가 아닌 자식에 붙었으면 루트로 옮겨 담는다 (섬 생성이 루트에서 읽는다).
                        if (instance.GetComponent<PcgInfo>() == null)
                        {
                            instance.AddComponent<PcgInfo>().CopyFrom(info);
                        }
                        AddColliders(instance, info.collider);
                        AddAttachLights(instance);
                        if (info.category == "floating")
                        {
                            instance.AddComponent<FloatOnWater>();
                        }
                        if (info.category == "sea_rock" && foamRing != null)
                        {
                            var ring = (GameObject)PrefabUtility.InstantiatePrefab(foamRing, instance.transform);
                            ring.name = "FoamRing";
                            ring.transform.localPosition = Vector3.zero;
                            ring.transform.localScale = Vector3.one * Mathf.Max(0.8f, info.radius * 1.05f);
                            var follow = ring.AddComponent<FloatOnWater>();
                            follow.Offset = 0.02f;
                            var so = new SerializedObject(follow);
                            so.FindProperty("tilt").floatValue = 0f;
                            so.ApplyModifiedPropertiesWithoutUndo();
                            foreach (var r in ring.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        }
                        foreach (var r in instance.GetComponentsInChildren<Renderer>())
                        {
                            if (info.category == "detail") r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        }
                        var prefab = PrefabUtility.SaveAsPrefabAsset(instance, $"{prefabFolder}/{model.name}.prefab");
                        prefabs.Add(prefab);
                    }
                    finally
                    {
                        Object.DestroyImmediate(instance);
                    }
                }
            }
            prefabs.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            var catalog = LoadOrCreate<IslandPropCatalog>(DataRoot + "/IslandPropCatalog.asset");
            catalog.SetProps(prefabs);
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        // 부착 지점(빈 오브젝트)에 조명. FBX 의 light_* 속성이 빈 오브젝트에서는 넘어오지 않아 이름으로 정한다.
        static void AddAttachLights(GameObject root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                bool glow = t.name.EndsWith("_Glow");
                bool buoy = t.name == "Buoy_Light";
                if ((!glow && !buoy) || t.GetComponent<Light>() != null) continue;
                var light = t.gameObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadows = LightShadows.None;
                light.color = glow ? new Color(1f, 0.478f, 0.18f) : new Color(1f, 0.82f, 0.384f);
                light.range = glow ? (t.name.Contains("Large") || t.name.Contains("Vent") ? 7f : 4.5f) : 5f;
                light.intensity = glow ? 2f : 1.5f;
            }
        }

        static void AddColliders(GameObject root, string kind)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0 || string.IsNullOrEmpty(kind) || kind == "none") return;
            // 루트 기준 경계
            var bounds = new Bounds(root.transform.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
            foreach (var r in renderers)
            {
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    bounds.Encapsulate(root.transform.InverseTransformPoint(corner));
                }
            }
            switch (kind)
            {
                case "box":
                    var box = root.AddComponent<BoxCollider>();
                    box.center = bounds.center;
                    box.size = bounds.size;
                    break;
                case "capsule":
                    // 나무 줄기: 잎이 퍼진 너비가 아니라 가는 기둥으로
                    var capsule = root.AddComponent<CapsuleCollider>();
                    float height = Mathf.Max(0.5f, bounds.max.y);
                    capsule.radius = Mathf.Clamp(Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.2f, 0.15f, 0.5f);
                    capsule.height = height;
                    capsule.center = new Vector3(0f, height * 0.5f, 0f);
                    capsule.direction = 1;
                    break;
                case "mesh":
                    foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
                    {
                        if (filter.sharedMesh == null) continue;
                        var collider = filter.gameObject.AddComponent<MeshCollider>();
                        collider.sharedMesh = filter.sharedMesh;
                    }
                    break;
            }
        }

        static void BuildIslandPrefab(ColorPalette palette, Material material, IslandPropCatalog catalog)
        {
            string path = PrefabRoot + "/Island.prefab";
            var go = new GameObject("Island");
            try
            {
                var generator = go.AddComponent<IslandGenerator>();
                var so = new SerializedObject(generator);
                so.FindProperty("palette").objectReferenceValue = palette;
                so.FindProperty("terrainMaterial").objectReferenceValue = material;
                so.FindProperty("catalog").objectReferenceValue = catalog;
                so.ApplyModifiedPropertiesWithoutUndo();
                EnsureFolder(PrefabRoot);
                PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
