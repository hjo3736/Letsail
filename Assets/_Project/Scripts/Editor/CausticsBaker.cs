using Letsail.Ocean;
using UnityEditor;
using UnityEngine;

namespace Letsail.Editor
{
    /// <summary>
    /// 물속 빛무늬(코스틱) 프레임을 굽는다. 가장자리가 이어지고(타일), 마지막 프레임이 첫 프레임으로 이어진다(반복).
    /// 보로노이 칸 경계를 밝은 선으로 그린 층 두 개를 겹친다. 칸 중심점들은 원을 그리며 돌아서 한 바퀴에 처음으로 돌아온다.
    /// </summary>
    public static class CausticsBaker
    {
        const string Path = "Assets/_Project/Art/Textures/Ocean/T_Caustics.asset";
        const int Size = 256;
        const int FrameCount = 32;

        [MenuItem("Letsail/물속 빛무늬 굽기")]
        public static void Bake()
        {
            var layerA = new Layer(5, 11, 0.18f, 1);
            var layerB = new Layer(7, 23, 0.14f, -1);
            var frames = new Texture2D[FrameCount];
            var pixels = new Color32[Size * Size];
            for (int f = 0; f < FrameCount; f++)
            {
                float t = f / (float)FrameCount;
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        float u = (x + 0.5f) / Size;
                        float v = (y + 0.5f) / Size;
                        float a = layerA.Lines(u, v, t);
                        float b = layerB.Lines(u + 0.37f, v + 0.21f, t);
                        float light = Mathf.Clamp01(a * 0.9f + b * 0.7f);
                        // 바탕은 어둡게(0.35), 빛무늬는 밝게(1).
                        float value = 0.35f + 0.65f * light;
                        pixels[y * Size + x] = new Color(value, value, value, 1f);
                    }
                }
                var texture = new Texture2D(Size, Size, TextureFormat.R8, true)
                {
                    name = "Caustics_" + f.ToString("00"),
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear,
                };
                texture.SetPixels32(pixels);
                texture.Apply(true, true);
                frames[f] = texture;
            }

            AssetDatabase.DeleteAsset(Path);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            var asset = ScriptableObject.CreateInstance<CausticsFrames>();
            asset.SetFrames(frames);
            AssetDatabase.CreateAsset(asset, Path);
            foreach (var frame in frames)
            {
                AssetDatabase.AddObjectToAsset(frame, asset);
            }
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"물속 빛무늬 {FrameCount}장 구움: {Path}");
        }

        sealed class Layer
        {
            readonly int cells;
            readonly Vector2[] basePoints;
            readonly float[] phases;
            readonly float radius;
            readonly int turns;

            public Layer(int cells, int seed, float radius, int turns)
            {
                this.cells = cells;
                this.radius = radius;
                this.turns = turns;
                var random = new System.Random(seed);
                basePoints = new Vector2[cells * cells];
                phases = new float[cells * cells];
                for (int i = 0; i < basePoints.Length; i++)
                {
                    basePoints[i] = new Vector2(0.25f + 0.5f * (float)random.NextDouble(), 0.25f + 0.5f * (float)random.NextDouble());
                    phases[i] = (float)random.NextDouble();
                }
            }

            Vector2 Point(int cx, int cy, float t)
            {
                int i = (((cy % cells) + cells) % cells) * cells + (((cx % cells) + cells) % cells);
                float angle = (t * turns + phases[i]) * Mathf.PI * 2f;
                return new Vector2(cx, cy) + basePoints[i] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }

            // 칸 경계 근처일수록 1에 가깝다.
            public float Lines(float u, float v, float t)
            {
                var p = new Vector2(Repeat(u) * cells, Repeat(v) * cells);
                int cx = Mathf.FloorToInt(p.x);
                int cy = Mathf.FloorToInt(p.y);
                float f1 = float.MaxValue;
                float f2 = float.MaxValue;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        float d = (Point(cx + dx, cy + dy, t) - p).magnitude;
                        if (d < f1)
                        {
                            f2 = f1;
                            f1 = d;
                        }
                        else if (d < f2)
                        {
                            f2 = d;
                        }
                    }
                }
                float edge = Mathf.Clamp01((f2 - f1) / 0.32f);
                return Mathf.Pow(1f - edge, 2.2f);
            }

            static float Repeat(float x) => x - Mathf.Floor(x);
        }
    }
}
