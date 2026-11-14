using UnityEngine;

namespace Letsail.Items
{
    /// <summary>
    /// 총구 불꽃 이펙트 (메쉬 애니메이션). 만들어지면 발사 클립을 처음부터 재생하고, 불꽃이 보이는 동안 불빛을 켠 뒤 스스로 사라진다.
    /// 연기가 월드에 남도록 총구에 붙이지 않고 그 자리에 만든다 (PlayerHands, 나중에 대포).
    /// </summary>
    public sealed class MuzzleFlash : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [SerializeField, Tooltip("재생할 상태 이름")] string state = "Fire";
        [SerializeField, Tooltip("불꽃이 보이는 동안 켜는 불빛 (없어도 된다)")] Light flashLight;
        [SerializeField, Tooltip("불빛을 켜 두는 시간 (초). 클립의 섬광 5프레임")] float flashSeconds = 0.17f;
        [SerializeField, Tooltip("이펙트가 사라지기까지 (초)")] float lifetime = 1.4f;

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
            if (flashLight != null)
            {
                flashLight.intensity = Mathf.Lerp(lightIntensity, 0f, age / flashSeconds);
                if (age >= flashSeconds) flashLight.enabled = false;
            }
            if (age >= lifetime) Destroy(gameObject);
        }

        /// <summary>총구 자리에 이펙트를 만든다. rotation 은 이펙트의 +Z 가 쏘는 방향이 되게.</summary>
        public static void Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float scale)
        {
            if (prefab == null) return;
            var fx = Instantiate(prefab, position, rotation);
            fx.transform.localScale = Vector3.one * scale;
        }
    }
}
