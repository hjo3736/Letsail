using Letsail.World;
using UnityEngine;

namespace Letsail.Ship
{
    /// <summary>
    /// 돛대 꼭대기 깃발이 바람 부는 쪽으로 날린다. 선원이 바람 방향을 보고 돛과 키를 다룰 수 있게 한다.
    /// 깃발의 원점은 돛대에 붙은 쪽이어야 하고, 모델에서 깃발은 배 뒤쪽(-Z)으로 날리는 모양이다.
    /// </summary>
    public sealed class WindVane : MonoBehaviour
    {
        [SerializeField, Tooltip("돌릴 깃발 (원점이 돛대 쪽)")] Transform flag;
        [SerializeField, Tooltip("깃발이 바람을 따라 도는 속도")] float sharpness = 3f;

        Quaternion baseRotation;
        float currentYaw;

        void Awake()
        {
            if (flag != null)
            {
                baseRotation = flag.localRotation;
            }
        }

        void LateUpdate()
        {
            var weather = WeatherSystem.Instance;
            if (flag == null || weather == null)
            {
                return;
            }
            // 깃발은 기본으로 배 뒤쪽(배 기준 180도)을 향한다. 바람이 불어 가는 방향을 배 기준으로 바꿔 그만큼 돌린다.
            float shipYaw = transform.eulerAngles.y;
            float wanted = Mathf.DeltaAngle(0f, weather.WindAngle - shipYaw - 180f);
            currentYaw = Mathf.LerpAngle(currentYaw, wanted, 1f - Mathf.Exp(-sharpness * Time.deltaTime));
            flag.localRotation = Quaternion.AngleAxis(currentYaw, Vector3.up) * baseRotation;
        }
    }
}
