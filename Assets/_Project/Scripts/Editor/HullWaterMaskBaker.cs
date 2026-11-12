using Letsail.Ocean;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Letsail.Editor
{
    /// <summary>
    /// 배의 물 가림 메시(Hull_WaterMask)를 위에서 내려다본 높이 지도 텍스처로 굽는다.
    /// 칸마다 아래·위로 광선을 쏴서 배 안쪽 공간이 어디서 시작하고 끝나는지 기록한다.
    /// 배 모델이 바뀌면 메뉴 Letsail > 배 물 가림 다시 굽기 를 누른다.
    /// </summary>
    public static class HullWaterMaskBaker
    {
        const string PrefabPath = "Assets/_Project/Prefabs/PlayerShip/playership_001.prefab";
        const string TexturePath = "Assets/_Project/Art/Textures/PlayerShip/T_HullWaterMask.asset";
        // 칸 크기 (m). 작을수록 배 벽 근처가 정확하다.
        const float CellSize = 0.03f;
        // 배 안쪽이 아닌 칸. 어떤 높이도 이 사이에 들어오지 않는다.
        const float Outside = 1000f;

        [MenuItem("Letsail/배 물 가림 다시 굽기")]
        public static void BakePlayerShip()
        {
            string result = Bake(PrefabPath, TexturePath);
            Debug.Log(result);
        }

        public static string Bake(string prefabPath, string texturePath)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var maskComponent = root.GetComponentInChildren<HullWaterMask>(true);
                if (maskComponent == null)
                {
                    return "HullWaterMask 컴포넌트가 없어요: " + prefabPath;
                }
                var mesh = maskComponent.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null)
                {
                    return "물 가림 메시가 없어요";
                }

                Texture2D texture = BakeTexture(mesh, out Vector2 min, out Vector2 size);
                texture = SaveTexture(texture, texturePath);
                maskComponent.SetBakedData(texture, min, size);
                EditorUtility.SetDirty(maskComponent);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return $"배 물 가림을 구웠어요: {texture.width}×{texture.height} 칸, 범위 {min} ~ {min + size}";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static Texture2D BakeTexture(Mesh mesh, out Vector2 min, out Vector2 size)
        {
            Bounds bounds = mesh.bounds;
            min = new Vector2(bounds.min.x, bounds.min.z) - Vector2.one * CellSize;
            size = new Vector2(bounds.size.x, bounds.size.z) + Vector2.one * (CellSize * 2f);
            int width = Mathf.CeilToInt(size.x / CellSize);
            int height = Mathf.CeilToInt(size.y / CellSize);
            size = new Vector2(width * CellSize, height * CellSize);

            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = new GameObject("MaskBake");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                Physics.SyncTransforms();
                var physics = scene.GetPhysicsScene();

                float top = bounds.max.y + 1f;
                float bottom = bounds.min.y - 1f;
                float rayLength = top - bottom;
                var pixels = new Color[width * height];
                for (int z = 0; z < height; z++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float px = min.x + (x + 0.5f) * CellSize;
                        float pz = min.y + (z + 0.5f) * CellSize;
                        bool hitTop = physics.Raycast(new Vector3(px, top, pz), Vector3.down, out RaycastHit down, rayLength);
                        bool hitBottom = physics.Raycast(new Vector3(px, bottom, pz), Vector3.up, out RaycastHit up, rayLength);
                        pixels[z * width + x] = hitTop && hitBottom && up.point.y < down.point.y
                            ? new Color(up.point.y, down.point.y, 0f, 0f)
                            : new Color(Outside, -Outside, 0f, 0f);
                    }
                }
                var texture = new Texture2D(width, height, TextureFormat.RGHalf, false, true)
                {
                    name = "T_HullWaterMask",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                texture.SetPixels(pixels);
                texture.Apply(false, false);
                return texture;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static Texture2D SaveTexture(Texture2D texture, string path)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(texture, path);
                return texture;
            }
            // 이미 있으면 내용만 바꿔서 프리팹 연결(GUID)을 유지한다.
            EditorUtility.CopySerialized(texture, existing);
            Object.DestroyImmediate(texture);
            AssetDatabase.SaveAssets();
            return existing;
        }
    }
}
