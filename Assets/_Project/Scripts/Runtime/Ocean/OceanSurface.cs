using UnityEngine;
using UnityEngine.Rendering;

namespace Letsail.Ocean
{
    /// <summary>
    /// 바다 표면 격자 메시를 만들고 카메라를 따라다닌다. 파도 모양은 셰이더가 움직인다.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class OceanSurface : MonoBehaviour
    {
        [SerializeField, Min(16f), Tooltip("바다 한 변의 길이 (m)")]
        float size = 256f;

        [SerializeField, Min(0.5f), Tooltip("격자 한 칸의 크기 (m). 클수록 각진 로우폴리 느낌")]
        float cellSize = 2f;

        [SerializeField, Tooltip("격자 바깥으로 수평선까지 이어 붙이는 거리 (m). 멀리 있는 섬 아래가 비어 보이지 않게")]
        float farDistance = 2500f;

        [SerializeField, Min(0), Tooltip("바깥 고리 줄 수 (멀수록 칸이 커진다)")]
        int farRings = 14;

        [SerializeField, Tooltip("따라갈 대상. 비우면 메인 카메라")]
        Transform follow;

        // 파도가 메시를 위아래로 움직이므로 화면 밖으로 잘리지 않게 경계를 넓힌다.
        const float BoundsHeight = 20f;

        Mesh mesh;

        void OnEnable()
        {
            BuildMesh();
        }

        void OnDisable()
        {
            if (mesh != null)
            {
                DestroyImmediate(mesh);
                mesh = null;
            }
        }

        void OnValidate()
        {
            if (isActiveAndEnabled && mesh != null)
            {
                BuildMesh();
            }
        }

        void LateUpdate()
        {
            Transform target = follow;
            if (target == null && Camera.main != null)
            {
                target = Camera.main.transform;
            }
            if (target == null)
            {
                return;
            }

            // 칸 크기 단위로만 움직여야 꼭짓점이 미끄러져 보이지 않는다.
            Vector3 p = target.position;
            transform.position = new Vector3(
                Mathf.Round(p.x / cellSize) * cellSize,
                transform.position.y,
                Mathf.Round(p.z / cellSize) * cellSize);
        }

        void BuildMesh()
        {
            if (mesh == null)
            {
                mesh = new Mesh { name = "OceanSurface", hideFlags = HideFlags.DontSave };
            }
            mesh.Clear();

            int cells = Mathf.Max(1, Mathf.RoundToInt(size / cellSize));
            int line = cells + 1;
            float half = cells * cellSize * 0.5f;

            // 안쪽 격자 + 바깥 고리들. 고리는 안쪽 가장자리와 같은 수의 점으로 된 네모 테두리를 점점 크게 키운 것이라
            // 맞닿는 줄의 점이 안쪽 격자 가장자리 점과 같은 자리라 파도가 움직여도 틈이 생기지 않는다.
            int rings = farDistance > half ? farRings : 0;
            int loop = cells * 4;
            var vertices = new Vector3[line * line + rings * loop];
            for (int z = 0; z < line; z++)
            {
                for (int x = 0; x < line; x++)
                {
                    vertices[z * line + x] = new Vector3(x * cellSize - half, 0f, z * cellSize - half);
                }
            }

            for (int r = 0; r < rings; r++)
            {
                float scale = Mathf.Pow(farDistance / half, (r + 1) / (float)rings);
                for (int k = 0; k < loop; k++)
                {
                    Vector3 edge = EdgePoint(k, cells, half);
                    vertices[line * line + r * loop + k] = new Vector3(edge.x * scale, 0f, edge.z * scale);
                }
            }

            var triangles = new int[cells * cells * 6 + rings * loop * 6];
            int t = 0;
            for (int z = 0; z < cells; z++)
            {
                for (int x = 0; x < cells; x++)
                {
                    int i = z * line + x;
                    triangles[t++] = i;
                    triangles[t++] = i + line;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + line;
                    triangles[t++] = i + line + 1;
                }
            }

            for (int r = 0; r < rings; r++)
            {
                for (int k = 0; k < loop; k++)
                {
                    int k2 = (k + 1) % loop;
                    int a = r == 0 ? EdgeIndex(k, cells, line) : line * line + (r - 1) * loop + k;
                    int b = r == 0 ? EdgeIndex(k2, cells, line) : line * line + (r - 1) * loop + k2;
                    int c = line * line + r * loop + k;
                    int d = line * line + r * loop + k2;
                    // 테두리를 반시계로 돌므로 위에서 보이게 감는다.
                    triangles[t++] = a; triangles[t++] = d; triangles[t++] = c;
                    triangles[t++] = a; triangles[t++] = b; triangles[t++] = d;
                }
            }

            mesh.indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            // 파도 세기: 안쪽 격자 1, 바깥 고리 0 (셰이더가 uv.x 를 곱한다)
            var uvs = new Vector2[vertices.Length];
            // 안쪽 격자 가장자리 30m 는 파도를 서서히 줄여, 평평한 바깥 고리와 맞닿는 곳에 골이 생기지 않게 한다.
            float fadeWidth = rings > 0 ? Mathf.Min(30f, half * 0.4f) : 0f;
            for (int i = 0; i < line * line; i++)
            {
                Vector3 v = vertices[i];
                float edge = Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.z));
                float w = fadeWidth > 0f ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(half - fadeWidth, half, edge)) : 1f;
                uvs[i] = new Vector2(w, 0f);
            }
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            float extent = rings > 0 ? farDistance * 2f : cells * cellSize;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(extent, BoundsHeight, extent));

            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        // 안쪽 격자 가장자리를 반시계로 도는 k 번째 점 (아래 → 오른쪽 → 위 → 왼쪽).
        static void EdgeCoord(int k, int cells, out int x, out int z)
        {
            int side = k / cells, i = k % cells;
            switch (side)
            {
                case 0: x = i; z = 0; break;
                case 1: x = cells; z = i; break;
                case 2: x = cells - i; z = cells; break;
                default: x = 0; z = cells - i; break;
            }
        }

        static int EdgeIndex(int k, int cells, int line)
        {
            EdgeCoord(k, cells, out int x, out int z);
            return z * line + x;
        }

        Vector3 EdgePoint(int k, int cells, float half)
        {
            EdgeCoord(k, cells, out int x, out int z);
            return new Vector3(x * cellSize - half, 0f, z * cellSize - half);
        }
    }
}
