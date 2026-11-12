using UnityEngine;

namespace Letsail.Interaction
{
    /// <summary>
    /// 대포. 앉은 사람이 카메라를 위아래로 움직이면 포신이 따라 올라가고 내려간다. 값은 포신 각도(도, 위가 +).
    /// 발사는 아직 없다 (포탄과 피해 시스템이 생긴 뒤에 붙인다).
    /// </summary>
    public sealed class CannonControl : SeatControl
    {
        [SerializeField, Tooltip("위아래로 기울일 포신")] Transform barrel;
        [SerializeField, Tooltip("포구. 포탄이 나가는 방향(앞쪽)을 가리킨다")] Transform muzzle;
        [SerializeField] float minElevation = -5f;
        [SerializeField] float maxElevation = 20f;
        [SerializeField, Tooltip("카메라가 이 각도만큼 내려다볼 때 포신이 수평이 된다 (도)")] float cameraPitchForLevel = 10f;
        [SerializeField, Tooltip("포신이 카메라를 따라가는 속도 (도/초)")] float aimSpeed = 40f;

        Quaternion baseRotation;
        Vector3 elevationAxis;

        public override string ControlsHint => "마우스 위아래  조준   (발사는 다음 단계)";

        protected override void Initialize()
        {
            if (barrel == null)
            {
                return;
            }
            baseRotation = barrel.localRotation;
            // 포구 방향과 위쪽에 수직인 축으로 돌리면 포구가 위아래로 움직인다 (포신 부모 기준).
            Transform parent = barrel.parent;
            Vector3 fire = muzzle != null ? muzzle.forward : barrel.right;
            Vector3 localFire = parent != null ? parent.InverseTransformDirection(fire) : fire;
            Vector3 localUp = parent != null ? parent.InverseTransformDirection(Vector3.up) : Vector3.up;
            elevationAxis = Vector3.Cross(localFire, localUp).normalized;
        }

        protected override float OnTick(float value, float deltaTime)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                return value;
            }
            float cameraPitch = -Mathf.Asin(Mathf.Clamp(cam.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float wanted = Mathf.Clamp(cameraPitchForLevel - cameraPitch, minElevation, maxElevation);
            return Mathf.MoveTowards(value, wanted, aimSpeed * deltaTime);
        }

        protected override void OnApply(float value)
        {
            if (barrel != null)
            {
                barrel.localRotation = Quaternion.AngleAxis(value, elevationAxis) * baseRotation;
            }
        }
    }
}
