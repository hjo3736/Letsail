using UnityEngine;
using UnityEngine.InputSystem;

namespace Letsail.Interaction
{
    /// <summary>
    /// 대포. 앉은 사람이 카메라를 움직이면 받침이 좌우로(±maxYaw), 포신이 위아래로 따라간다. 왼쪽 클릭으로 쏜다.
    /// 값 하나에 좌우·위아래 각도를 함께 담아 보낸다 (Pack / Unpack). 예전 값(위아래 각도만)도 그대로 읽는다.
    /// 발사·장전·포탄은 배의 ShipCannons 가 맡는다. 반동(뒤로 밀렸다 돌아오기)은 모든 PC 에서 보인다.
    /// </summary>
    public sealed class CannonControl : SeatControl
    {
        [SerializeField, Tooltip("위아래로 기울일 포신")] Transform barrel;
        [SerializeField, Tooltip("포구. 포탄이 나가는 방향(앞쪽)을 가리킨다")] Transform muzzle;
        [SerializeField] float minElevation = -5f;
        [SerializeField] float maxElevation = 20f;
        [SerializeField, Tooltip("좌우로 돌릴 수 있는 각도 (도)")] float maxYaw = 25f;
        [SerializeField, Tooltip("카메라가 이 각도만큼 내려다볼 때 포신이 수평이 된다 (도)")] float cameraPitchForLevel = 10f;
        [SerializeField, Tooltip("포신이 카메라를 따라가는 속도 (도/초)")] float aimSpeed = 40f;
        [SerializeField, Tooltip("반동으로 밀리는 거리 (m)")] float recoilDistance = 0.3f;
        [SerializeField, Tooltip("반동 뒤 제자리로 돌아오는 시간 (초)")] float recoilReturn = 0.8f;

        Transform carriage;
        Quaternion carriageBaseRotation;
        Vector3 carriageBasePosition;
        Vector3 restAim;
        Quaternion baseRotation;
        Vector3 elevationAxis;
        float recoilAge = 99f;
        InputAction attackAction;
        Letsail.Ship.ShipCannons cannons;

        public Transform Muzzle => muzzle;

        /// <summary>배의 대포 관리자가 정해 준 번호.</summary>
        public int Index { get; set; } = -1;

        public override string ControlsHint
        {
            get
            {
                if (cannons == null) cannons = GetComponentInParent<Letsail.Ship.ShipCannons>();
                bool loaded = cannons != null && cannons.IsLoaded(Index);
                return loaded
                    ? "마우스  조준   ·   좌클릭  발사"
                    : "마우스  조준   ·   포탄이 없어요 (바구니에서 포탄을 들고 포신에 E)";
            }
        }

        public override string LoopAction => recoilAge < 0.6f ? "Cannon_Fire" : null;

        // ───────────────────────── 값 묶기 ─────────────────────────

        const float YawOffset = 300f;
        const float Slot = 64f;

        /// <summary>좌우(도, 0.1도 단위)와 위아래(도)를 값 하나로 묶는다.</summary>
        public static float Pack(float yaw, float elevation)
        {
            float yawTenths = Mathf.Round(Mathf.Clamp(yaw, -29.9f, 29.9f) * 10f);
            return (yawTenths + YawOffset) * Slot + Mathf.Clamp(elevation + 20f, 0f, Slot - 1f);
        }

        public static void Unpack(float value, out float yaw, out float elevation)
        {
            // 예전 값: 위아래 각도만
            if (value < 1000f)
            {
                yaw = 0f;
                elevation = value;
                return;
            }
            float k = Mathf.Floor(value / Slot);
            elevation = value - k * Slot - 20f;
            yaw = (k - YawOffset) / 10f;
        }

        // ───────────────────────── SeatControl ─────────────────────────

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

            // 받침 (포신의 부모): 좌우로 돌리고 반동으로 민다.
            carriage = parent;
            if (carriage != null)
            {
                carriageBaseRotation = carriage.localRotation;
                carriageBasePosition = carriage.localPosition;
                Transform outer = carriage.parent;
                Vector3 flat = Vector3.ProjectOnPlane(fire, Vector3.up).normalized;
                restAim = outer != null ? outer.InverseTransformDirection(flat) : flat;
            }
            cannons = GetComponentInParent<Letsail.Ship.ShipCannons>();
        }

        protected override float OnTick(float value, float deltaTime)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                return value;
            }
            Unpack(value, out float yaw, out float elevation);

            float cameraPitch = -Mathf.Asin(Mathf.Clamp(cam.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float wantedElevation = Mathf.Clamp(cameraPitchForLevel - cameraPitch, minElevation, maxElevation);
            elevation = Mathf.MoveTowards(elevation, wantedElevation, aimSpeed * deltaTime);

            if (carriage != null && carriage.parent != null)
            {
                Vector3 rest = carriage.parent.TransformDirection(restAim);
                Vector3 look = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
                float wantedYaw = Mathf.Clamp(Vector3.SignedAngle(Vector3.ProjectOnPlane(rest, Vector3.up), look, Vector3.up), -maxYaw, maxYaw);
                yaw = Mathf.MoveTowards(yaw, wantedYaw, aimSpeed * deltaTime);
            }

            // 발사
            if (attackAction == null) attackAction = InputSystem.actions.FindAction("Player/Attack", true);
            if (attackAction.WasPressedThisFrame() && Cursor.lockState == CursorLockMode.Locked)
            {
                if (cannons == null) cannons = GetComponentInParent<Letsail.Ship.ShipCannons>();
                if (cannons != null) cannons.RequestFire(Index);
            }
            return Pack(yaw, elevation);
        }

        protected override void OnApply(float value)
        {
            Unpack(value, out float yaw, out float elevation);
            if (barrel != null)
            {
                barrel.localRotation = Quaternion.AngleAxis(elevation, elevationAxis) * baseRotation;
            }
            if (carriage != null)
            {
                carriage.localRotation = Quaternion.AngleAxis(yaw, Vector3.up) * carriageBaseRotation;
            }
        }

        /// <summary>반동을 보인다 (모든 PC).</summary>
        public void PlayRecoil()
        {
            recoilAge = 0f;
        }

        void LateUpdate()
        {
            if (carriage == null || recoilAge > recoilReturn + 0.1f) return;
            recoilAge += Time.deltaTime;
            // 순식간에 밀렸다가 천천히 돌아온다.
            float push = recoilAge < 0.06f ? recoilAge / 0.06f : 1f - Mathf.SmoothStep(0f, 1f, (recoilAge - 0.06f) / recoilReturn);
            Vector3 back = carriage.parent != null ? -carriage.parent.InverseTransformDirection(muzzle != null ? Vector3.ProjectOnPlane(muzzle.forward, Vector3.up).normalized : carriage.forward) : -carriage.forward;
            carriage.localPosition = carriageBasePosition + back * recoilDistance * Mathf.Max(0f, push);
        }
    }
}
