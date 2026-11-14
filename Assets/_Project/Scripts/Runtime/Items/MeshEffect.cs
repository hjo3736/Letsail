using UnityEngine;

namespace Letsail.Items
{
    /// <summary>
    /// 메쉬 애니메이션 이펙트 (총구 불꽃, 포탄 폭발·물보라·땅 충격, 포탄 꼬리). 파티클·텍스처 없이 자식 메쉬가 움직인다.
    /// 만들어지면 클립을 처음부터 재생하고, 불꽃이 보이는 동안 불빛을 켠 뒤 lifetime 이 지나면 스스로 사라진다.
    /// lifetime 이 0 이면 사라지지 않는다 (포탄 꼬리처럼 다른 것에 붙어 반복하는 이펙트).
    /// 연기가 월드에 남도록 한 번 재생하는 이펙트는 부모 없이 그 자리에 만든다.
    /// </summary>
    public sealed class MeshEffect : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [SerializeField, Tooltip("재생할 상태 이름")] string state = "Fire";
        [SerializeField, Tooltip("불꽃이 보이는 동안 켜는 불빛 (없어도 된다)")] Light flashLight;
        [SerializeField, Tooltip("불빛을 켜 두는 시간 (초). 클립의 섬광 프레임")] float flashSeconds = 0.17f;
        [SerializeField, Tooltip("이펙트가 사라지기까지 (초). 0 이면 사라지지 않는다")] float lifetime = 1.4f;

        float age;
        float lightIntensity;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (flashLight != null) lightIntensity = flashLight.intensity;
        }

        void OnEnable()
        {
            age = 0f;
            if (animator != null) animator.Play(state, 0, 0f);
            if (flashLight != null)
            {
                flashLight.enabled = true;
                flashLight.intensity = lightIntensity;
            }
        }

        void Update()
        {
            age += Time.deltaTime;
            if (flashLight != null && flashLight.enabled)
            {
                flashLight.intensity = Mathf.Lerp(lightIntensity, 0f, age / Mathf.Max(0.01f, flashSeconds));
                if (age >= flashSeconds) flashLight.enabled = false;
            }
            if (lifetime > 0f && age >= lifetime) Destroy(gameObject);
        }

        /// <summary>그 자리에 이펙트를 만든다 (부모 없이). rotation 은 이펙트의 축 규칙에 맞춰 준다.</summary>
        public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float scale = 1f)
        {
            if (prefab == null) return null;
            var fx = Instantiate(prefab, position, rotation);
            fx.transform.localScale = Vector3.one * scale;
            return fx;
        }
    }
}
