using System;
using UnityEngine;

namespace Letsail.Player
{
    /// <summary>
    /// 캐릭터 이동, 점프, 웅크리기, 중력.
    /// 입력은 다른 컴포넌트(지금은 PlayerInputHandler, 나중에는 네트워크)가 넣어 준다.
    /// Rigidbody가 있는 바닥(배, 움직이는 발판)을 밟고 있으면 그 바닥과 함께 움직인다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [Header("이동 (m/s)")]
        [SerializeField] float moveSpeed = 3.5f;
        [SerializeField] float sprintSpeed = 5f;
        [SerializeField] float crouchSpeed = 1.2f;
        [SerializeField, Tooltip("가속도 (m/s²)")] float acceleration = 25f;
        [SerializeField, Range(0f, 1f), Tooltip("공중에서의 조작 비율")] float airControl = 0.35f;
        [SerializeField, Tooltip("켜면 이동 방향으로 몸을 돌린다 (3인칭). 1인칭에서는 끄고 AddYaw 로 돌린다")]
        bool faceMoveDirection;
        [SerializeField, Tooltip("이동 방향으로 몸을 돌리는 속도 (도/초)")] float turnSpeed = 720f;

        [Header("점프·중력")]
        [SerializeField, Tooltip("점프 높이 (m)")] float jumpHeight = 0.9f;
        [SerializeField] float gravity = -20f;
        [SerializeField, Tooltip("땅에 붙어 있게 아래로 누르는 속도")] float groundStickVelocity = -2f;
        [SerializeField, Tooltip("착지 직전에 누른 점프도 받아 주는 시간 (초)")] float jumpBuffer = 0.15f;
        [SerializeField, Tooltip("발판에서 떨어진 직후에도 점프할 수 있는 시간 (초)")] float coyoteTime = 0.12f;

        [Header("웅크리기")]
        [SerializeField, Range(0.3f, 1f), Tooltip("웅크렸을 때 키 비율")] float crouchHeightRatio = 0.65f;

        [Header("수영")]
        [SerializeField, Tooltip("헤엄칠 때 발밑이 수면보다 내려가는 깊이 (m). 어깨와 머리만 물 위로 나온다")] float swimDepth = 0.66f;
        [SerializeField, Tooltip("헤엄 속도 (m/s)")] float swimSpeed = 1.6f;
        [SerializeField, Tooltip("달리기 키를 누르고 헤엄칠 때 속도 (m/s)")] float swimSprintSpeed = 2.3f;
        [SerializeField, Tooltip("헤엄 가속도 (m/s²)")] float swimAcceleration = 4f;
        [SerializeField, Tooltip("수면 높이를 따라가는 빠르기")] float buoyancySharpness = 5f;

        [Header("잠수")]
        [SerializeField, Tooltip("잠수 키를 누르고 있을 때 내려가는 속도 (m/s)")] float diveSpeed = 2f;
        [SerializeField, Tooltip("잠수 키를 놓았을 때 떠오르는 속도 (m/s)")] float floatUpSpeed = 1.2f;
        [SerializeField, Tooltip("물속에서 점프 키를 누르고 있을 때 올라가는 속도 (m/s)")] float swimUpSpeed = 2.5f;
        [SerializeField, Tooltip("위아래 속도가 바뀌는 빠르기 (m/s²)")] float diveAcceleration = 6f;
        [SerializeField, Tooltip("수면에서 이보다 깊이는 내려가지 못한다 (m)")] float maxDiveDepth = 12f;

        [Header("바닥 감지")]
        [SerializeField, Tooltip("바닥과 천장으로 인식할 레이어. 플레이어 자신의 레이어는 빼야 한다")]
        LayerMask groundMask = ~0;

        CharacterController controller;
        float standingHeight;
        Vector3 standingCenter;

        Vector3 moveDirection;
        bool sprintHeld;
        bool crouchHeld;
        bool diveHeld;
        bool riseHeld;
        // 남은 점프 입력 유효 시간. 프레임마다 줄여서, 프레임이 길어져도 최소 한 번은 확인된다.
        float jumpBufferRemaining;
        // 마지막으로 땅을 밟은 뒤 지난 시간. 프레임 시간을 쌓아서 재므로 프레임이 길어도 정확하다.
        float airTime = float.PositiveInfinity;

        // 바닥(발판) 기준 속도. 바닥 자체의 움직임은 FollowPlatform 이 따로 더한다.
        Vector3 planarVelocity;
        float verticalVelocity;
        // 움직이는 바닥에서 뛰어내리면 그 바닥의 속도를 이어받는다.
        Vector3 inheritedVelocity;

        Transform platform;
        Vector3 platformLocalPosition;
        Quaternion platformLastRotation;
        Vector3 platformVelocity;

        public bool IsGrounded { get; private set; }
        /// <summary>true 면 이동과 점프 입력을 무시한다 (상호작용 동작을 재생하는 동안 등). 중력은 그대로 받는다.</summary>
        public bool Locked { get; set; }
        /// <summary>위아래 속도 (m/s). 떨어지는 중이면 음수.</summary>
        public float VerticalVelocity => verticalVelocity;
        /// <summary>마지막으로 땅을 밟은 뒤 지난 시간 (초).</summary>
        public float AirTime => airTime;
        public bool IsCrouching { get; private set; }
        public float PlanarSpeed => planarVelocity.magnitude;
        public Transform Platform => platform;
        /// <summary>현재 키 / 선 키. 웅크리면 작아진다.</summary>
        public float HeightRatio => IsCrouching ? crouchHeightRatio : 1f;
        /// <summary>켜면 이동 방향으로 몸을 돌린다. 1인칭이나 조준 중에는 꺼 둔다.</summary>
        public bool FaceMoveDirection
        {
            get => faceMoveDirection;
            set => faceMoveDirection = value;
        }
        /// <summary>이번 프레임에 밟고 있는 바닥이 좌우로 돈 각도 (도). 카메라가 같이 돌 때 쓴다.</summary>
        public float PlatformYawDelta { get; private set; }

        public event Action Jumped;

        /// <summary>물에 떠서 헤엄치는 중.</summary>
        public bool IsSwimming { get; private set; }

        /// <summary>수면 아래로 잠수해 있는 중 (머리까지 물에 잠김).</summary>
        public bool IsDiving { get; private set; }

        /// <summary>true 면 잠수할 수 없고 빨리 떠오른다 (숨이 다 떨어졌을 때).</summary>
        public bool DiveBlocked
        {
            get => diveBlocked;
            set
            {
                // 숨이 떨어져 막혔다가 풀려도, 잠수 키를 한 번 뗐다 눌러야 다시 잠수한다 (누른 채로 다시 빨려 들어가지 않게).
                if (value && !diveBlocked) waitDiveRelease = true;
                diveBlocked = value;
            }
        }

        bool diveBlocked;
        bool waitDiveRelease;

        /// <summary>물에 들어갔을 때 (들어갈 때 아래로 떨어지던 속도, m/s).</summary>
        public event Action<float> EnteredWater;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            standingHeight = controller.height;
            standingCenter = controller.center;
        }

        /// <summary>이동 방향 (월드, 수평). 길이 1이 최대 속도.</summary>
        public void SetMoveDirection(Vector3 worldDirection)
        {
            worldDirection.y = 0f;
            moveDirection = Vector3.ClampMagnitude(worldDirection, 1f);
        }

        public void SetSprint(bool held) => sprintHeld = held;

        public void SetCrouch(bool held) => crouchHeld = held;

        public void RequestJump() => jumpBufferRemaining = jumpBuffer;

        /// <summary>헤엄칠 때 누르고 있으면 물 아래로 잠수한다.</summary>
        public void SetDive(bool held) => diveHeld = held;

        /// <summary>잠수 중에 누르고 있으면 빨리 떠오른다.</summary>
        public void SetRise(bool held) => riseHeld = held;

        /// <summary>
        /// 순간이동 (리스폰, 위치 보정 등). 밟고 있던 바닥과 속도를 초기화한다.
        /// transform.position 을 직접 바꾸면 CharacterController 와 바닥 추적이 어긋나므로 이것을 쓴다.
        /// </summary>
        public void Teleport(Vector3 position)
        {
            controller.enabled = false;
            transform.position = position;
            controller.enabled = true;

            platform = null;
            planarVelocity = Vector3.zero;
            inheritedVelocity = Vector3.zero;
            verticalVelocity = 0f;
            airTime = float.PositiveInfinity;
            IsGrounded = false;
            IsSwimming = false;
            IsDiving = false;
        }

        /// <summary>
        /// 그 자리에 멈춰 선다 (자리에 앉는 등 이동을 끄기 전에). 속도와 입력을 비우고, 땅을 밟은 상태로 둔다.
        /// 끄기 전에 부르지 않으면 걷던 속도가 남아 걷는 애니메이션이 계속된다.
        /// </summary>
        public void Stop()
        {
            moveDirection = Vector3.zero;
            planarVelocity = Vector3.zero;
            inheritedVelocity = Vector3.zero;
            verticalVelocity = 0f;
            jumpBufferRemaining = 0f;
            platform = null;
            PlatformYawDelta = 0f;
            IsGrounded = true;
            IsSwimming = false;
            IsDiving = false;
        }

        /// <summary>몸을 좌우로 돌린다 (도). 1인칭 시점에서 마우스로 돌릴 때 쓴다.</summary>
        public void AddYaw(float degrees)
        {
            if (degrees != 0f)
            {
                transform.Rotate(0f, degrees, 0f, Space.World);
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            FollowPlatform(dt);
            UpdateSwimming();
            if (IsSwimming)
            {
                Swim(dt);
                Turn(dt);
                return;
            }
            UpdateCrouch();
            UpdatePlanarVelocity(dt);
            UpdateVerticalVelocity(dt);

            Vector3 motion = (planarVelocity + inheritedVelocity + Vector3.up * verticalVelocity) * dt;
            CollisionFlags flags = controller.Move(motion);
            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
            {
                verticalVelocity = 0f;
            }

            UpdateGround(dt);
            Turn(dt);
            RememberPlatformPose();
        }

        // 발이 땅에 닿지 않은 채 수면보다 깊이 내려가면 헤엄친다. 얕은 곳에서 발이 바닥에 밀려 올라오면 다시 걷는다.
        // 배 안(선창)은 수면보다 낮아도 물이 아니므로 뺀다.
        void UpdateSwimming()
        {
            var ocean = Letsail.Ocean.OceanSystem.Instance;
            if (ocean == null)
            {
                IsSwimming = false;
                return;
            }
            Vector3 position = transform.position;
            float depth = ocean.GetWaterHeight(position) - position.y;
            bool insideHull = Letsail.Ocean.HullWaterMask.IsInsideHull(position + Vector3.up * 0.5f);
            if (!IsSwimming)
            {
                // 떨어지다 물에 들어가거나, 걸어서 가슴 높이보다 깊이 들어가면 헤엄친다.
                if (!insideHull && ((!IsGrounded && depth > swimDepth - 0.05f) || depth > swimDepth + 0.15f))
                {
                    IsSwimming = true;
                    IsCrouching = false;
                    crouchHeld = false;
                    controller.height = standingHeight;
                    controller.center = standingCenter;
                    platform = null;
                    inheritedVelocity = Vector3.zero;
                    float impact = -verticalVelocity;
                    verticalVelocity = 0f;
                    jumpBufferRemaining = 0f;
                    EnteredWater?.Invoke(impact);
                }
                return;
            }
            if (insideHull || depth < swimDepth - 0.3f)
            {
                IsSwimming = false;
                IsDiving = false;
                airTime = float.PositiveInfinity;
            }
        }

        void Swim(float dt)
        {
            float speed = sprintHeld ? swimSprintSpeed : swimSpeed;
            Vector3 target = Locked ? Vector3.zero : moveDirection * speed;
            planarVelocity = Vector3.MoveTowards(planarVelocity, target, swimAcceleration * dt);
            float waterLevel = Letsail.Ocean.OceanSystem.Instance.GetWaterHeight(transform.position);
            float floatY = waterLevel - swimDepth;
            float y = transform.position.y;
            if (!diveHeld) waitDiveRelease = false;
            bool dive = diveHeld && !Locked && !diveBlocked && !waitDiveRelease;
            if (dive || y < floatY - 0.15f)
            {
                // 잠수: 누르는 동안 내려가고, 놓으면 천천히 (점프 키면 빨리) 떠오른다. 수면 근처에서는 물결 따라가기로 넘긴다.
                float targetVertical;
                if (dive)
                {
                    targetVertical = waterLevel - y >= maxDiveDepth ? 0f : -diveSpeed;
                }
                else
                {
                    float up = (riseHeld && !Locked) || DiveBlocked ? swimUpSpeed : floatUpSpeed;
                    targetVertical = Mathf.Min(up, (floatY - y) * buoyancySharpness);
                }
                verticalVelocity = Mathf.MoveTowards(verticalVelocity, targetVertical, diveAcceleration * dt);
            }
            else
            {
                verticalVelocity = Mathf.Clamp((floatY - y) * buoyancySharpness, -4f, 4f);
            }
            // 머리(키의 85%)까지 물에 잠겼으면 잠수 중
            IsDiving = y + controller.height * 0.85f < waterLevel;
            jumpBufferRemaining = 0f;
            controller.Move((planarVelocity + Vector3.up * verticalVelocity) * dt);
            IsGrounded = false;
            airTime = 0f;
            PlatformYawDelta = 0f;
        }

        void FollowPlatform(float dt)
        {
            PlatformYawDelta = 0f;
            if (platform == null)
            {
                return;
            }

            // 바닥이 돌면 캐릭터도 같은 만큼 돈다 (좌우 회전만. 기울기는 위치로만 따라간다).
            Vector3 lastForward = Vector3.ProjectOnPlane(platformLastRotation * Vector3.forward, Vector3.up);
            Vector3 forward = Vector3.ProjectOnPlane(platform.rotation * Vector3.forward, Vector3.up);
            float yaw = Vector3.SignedAngle(lastForward, forward, Vector3.up);
            PlatformYawDelta = yaw;
            if (Mathf.Abs(yaw) > 0f)
            {
                transform.Rotate(0f, yaw, 0f, Space.World);
                planarVelocity = Quaternion.Euler(0f, yaw, 0f) * planarVelocity;
            }

            Vector3 delta = platform.TransformPoint(platformLocalPosition) - transform.position;
            platformVelocity = delta / dt;
            controller.Move(delta);
        }

        void UpdateCrouch()
        {
            bool crouch = crouchHeld || (IsCrouching && !CanStandUp());
            if (crouch == IsCrouching)
            {
                return;
            }

            IsCrouching = crouch;
            float height = crouch ? standingHeight * crouchHeightRatio : standingHeight;
            controller.height = height;
            controller.center = standingCenter - Vector3.up * ((standingHeight - height) * 0.5f);
        }

        bool CanStandUp()
        {
            float radius = controller.radius * 0.95f;
            Vector3 feet = transform.position + Vector3.up * (standingCenter.y - standingHeight * 0.5f);
            Vector3 bottom = feet + Vector3.up * (controller.radius + 0.05f);
            Vector3 top = feet + Vector3.up * (standingHeight - controller.radius);
            return !Physics.CheckCapsule(bottom, top, radius, groundMask, QueryTriggerInteraction.Ignore);
        }

        void UpdatePlanarVelocity(float dt)
        {
            float speed = IsCrouching ? crouchSpeed : (sprintHeld ? sprintSpeed : moveSpeed);
            Vector3 target = Locked ? Vector3.zero : moveDirection * speed;
            float rate = acceleration * (IsGrounded ? 1f : airControl);
            planarVelocity = Vector3.MoveTowards(planarVelocity, target, rate * dt);
        }

        void UpdateVerticalVelocity(float dt)
        {
            if (IsGrounded && verticalVelocity <= 0f)
            {
                verticalVelocity = groundStickVelocity;
            }
            else
            {
                verticalVelocity += gravity * dt;
            }

            bool canJump = (IsGrounded || airTime <= coyoteTime) && !IsCrouching;
            if (jumpBufferRemaining > 0f && canJump && !Locked)
            {
                verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);
                jumpBufferRemaining = 0f;
                airTime = float.PositiveInfinity;
                IsGrounded = false;
                LeavePlatform();
                Jumped?.Invoke();
            }
            jumpBufferRemaining -= dt;
        }

        void UpdateGround(float dt)
        {
            IsGrounded = controller.isGrounded && verticalVelocity <= 0f;
            if (!IsGrounded)
            {
                // 흔들리는 갑판에서 잠깐 떨어지는 건 무시하고, 확실히 떨어졌을 때만 바닥을 놓는다.
                airTime += dt;
                if (platform != null && airTime > coyoteTime)
                {
                    LeavePlatform();
                }
                return;
            }

            airTime = 0f;
            inheritedVelocity = Vector3.zero;

            Vector3 origin = transform.position + controller.center;
            float radius = controller.radius * 0.9f;
            float distance = controller.height * 0.5f - radius + controller.skinWidth + 0.2f;
            Transform ground = null;
            if (Physics.SphereCast(origin, radius, Vector3.down, out RaycastHit hit, distance, groundMask, QueryTriggerInteraction.Ignore))
            {
                // Rigidbody가 있는 것만 움직이는 바닥으로 본다. 정적인 땅은 따라갈 필요가 없다.
                ground = hit.rigidbody != null ? hit.rigidbody.transform : null;
            }

            if (ground != platform)
            {
                platform = ground;
                platformVelocity = Vector3.zero;
            }
        }

        void LeavePlatform()
        {
            if (platform == null)
            {
                return;
            }
            inheritedVelocity = platformVelocity;
            platform = null;
        }

        void RememberPlatformPose()
        {
            if (platform == null)
            {
                return;
            }
            platformLocalPosition = platform.InverseTransformPoint(transform.position);
            platformLastRotation = platform.rotation;
        }

        void Turn(float dt)
        {
            if (!faceMoveDirection || moveDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }
            Quaternion target = Quaternion.LookRotation(moveDirection, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
        }
    }
}
