using UnityEngine;

namespace Letsail.Player
{
    /// <summary>
    /// PlayerMotor 상태를 애니메이터 파라미터로 넘기고, 상호작용 동작(키 잡기, 서랍 열기 등)을 재생한다.
    /// 상호작용 동작은 정수 파라미터 Action 하나로 고른다 (0 = 없음, 1부터 ActionStates 순서).
    /// NetworkAnimator 가 파라미터를 다른 선원에게 보내므로, 동작도 같이 보인다.
    /// - SetBaseAction: 계속 유지하는 동작 (키 잡고 서 있기, 누워 쉬기, 닻 감기 등). null 이면 평소 이동.
    /// - PlayOnce: 한 번 재생하고 기본 동작으로 돌아간다 (잡기, 놓기, 서랍 열기 등).
    /// </summary>
    // Motor 가 이번 프레임에 움직인 뒤의 상태를 읽어야 한다.
    [DefaultExecutionOrder(10)]
    [RequireComponent(typeof(PlayerMotor))]
    public sealed class PlayerAnimator : MonoBehaviour
    {
        /// <summary>
        /// 애니메이터의 상호작용 동작 상태 이름 (클립 이름과 같다). 번호가 파라미터 값이 되므로 순서를 바꾸지 말고 뒤에 추가한다.
        /// 애니메이터 컨트롤러는 이 목록으로 상태와 전환을 만든다.
        /// </summary>
        public static readonly string[] ActionStates =
        {
            "Helm_Grab", "Helm_Idle", "Helm_TurnLeft", "Helm_TurnRight", "Helm_Release",
            "Sail_Grab", "Sail_Idle", "Sail_Pull", "Sail_Release", "Sail_LetGo",
            "Cannon_Grab", "Cannon_Idle", "Cannon_Fire", "Cannon_Reload", "Cannon_Release",
            "Anchor_Crank",
            "Drawer_Open", "Drawer_Close", "Drawer_Store",
            "Stove_Light", "Stove_Douse", "Cook_Chop", "Cook_Stir",
            "Bunk_LieDown", "Bunk_Sleep", "Bunk_GetUp",
            "Ladder_Exit_Top", "Ladder_Exit_Bottom", "Climb_Over_Rail", "Lookout_Enter",
            "Wardrobe_Pose",
            "Ladder_Enter_Bottom", "Ladder_Climb_Up", "Ladder_Climb_Down", "Ladder_Enter_Top", "Ladder_Idle",
            "Ratline_Climb_Up", "Ratline_Climb_Down", "RopeLadder_Climb_Up",
            "Swim_Idle", "Swim_Forward", "Water_Fall",
        };

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int GroundedId = Animator.StringToHash("Grounded");
        static readonly int CrouchId = Animator.StringToHash("Crouch");
        static readonly int JumpId = Animator.StringToHash("Jump");
        static readonly int FallingId = Animator.StringToHash("Falling");
        static readonly int ActionId = Animator.StringToHash("Action");

        [SerializeField, Tooltip("비우면 자식에서 찾는다")]
        Animator animator;

        [SerializeField, Tooltip("Run 애니메이션이 발이 미끄러지지 않는 속도 (m/s). 이보다 빠르면 재생 속도를 올린다")]
        float runClipSpeed = 3.5f;

        [SerializeField, Tooltip("속도 파라미터를 부드럽게 바꾸는 시간 (초)")]
        float speedDampTime = 0.08f;

        [SerializeField, Tooltip("발판에서 이 시간 넘게 떨어져 있으면 떨어지는 동작으로 바꾼다 (초). 계단 등에서 깜빡이지 않게")]
        float fallDelay = 0.25f;

        [SerializeField, Tooltip("이 속도(m/s)보다 빠르면 앞으로 헤엄치는 동작")] float swimMoveThreshold = 0.3f;
        [SerializeField, Tooltip("이 속도(m/s)보다 빠르게 떨어져 물에 들어가면 빠지는 동작을 한다")] float waterFallSpeed = 3f;

        PlayerMotor motor;
        Unity.Netcode.Components.NetworkAnimator networkAnimator;

        string baseAction;
        string oneShot;
        float oneShotRemaining;
        bool oneShotLocks;
        int appliedAction = -1;

        /// <summary>상호작용 동작의 재생 속도 (사다리를 빨리 오를 때 등). 평소 1.</summary>
        public float ActionSpeed { get; set; } = 1f;

        /// <summary>지금 재생 중인 동작. 없으면 null. 헤엄치는 중이면 헤엄 동작.</summary>
        public string CurrentAction => oneShot ?? baseAction ?? SwimAction;

        // 헤엄칠 때는 움직이면 앞으로 헤엄치기, 멈추면 떠 있기.
        // 잠수해서 위아래로 움직일 때도 헤엄치는 동작을 한다.
        string SwimAction => motor != null && motor.IsSwimming
            ? (motor.PlanarSpeed > swimMoveThreshold || (motor.IsDiving && Mathf.Abs(motor.VerticalVelocity) > 0.6f) ? "Swim_Forward" : "Swim_Idle")
            : null;

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            networkAnimator = GetComponentInChildren<Unity.Netcode.Components.NetworkAnimator>();
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }
        }

        void OnEnable()
        {
            motor.Jumped += OnJumped;
            motor.EnteredWater += OnEnteredWater;
        }

        void OnDisable()
        {
            motor.Jumped -= OnJumped;
            motor.EnteredWater -= OnEnteredWater;
            if (oneShotLocks)
            {
                motor.Locked = false;
            }
        }

        /// <summary>계속 유지할 동작. null 이면 평소 이동으로 돌아간다. 한 번 재생 중이면 그게 끝난 뒤 적용된다.</summary>
        public void SetBaseAction(string state)
        {
            baseAction = string.IsNullOrEmpty(state) ? null : state;
        }

        /// <summary>
        /// 동작을 한 번 재생하고 기본 동작으로 돌아간다.
        /// holdStill 이면 재생하는 동안 걷거나 뛰지 못한다 (서랍 열기 등 제자리 동작).
        /// </summary>
        public void PlayOnce(string state, bool holdStill = false)
        {
            if (string.IsNullOrEmpty(state) || IndexOf(state) == 0)
            {
                return;
            }
            oneShot = state;
            oneShotRemaining = GetClipLength(state);
            SetLock(holdStill);
        }

        /// <summary>재생 중인 한 번 재생 동작을 멈춘다.</summary>
        public void StopOnce()
        {
            oneShot = null;
            oneShotRemaining = 0f;
            SetLock(false);
        }

        public float GetClipLength(string clipName)
        {
            var controller = animator != null ? animator.runtimeAnimatorController : null;
            if (controller != null)
            {
                foreach (var clip in controller.animationClips)
                {
                    if (clip != null && clip.name == clipName)
                    {
                        return clip.length;
                    }
                }
            }
            return 0.5f;
        }

        void Update()
        {
            if (animator == null)
            {
                return;
            }

            if (oneShot != null)
            {
                oneShotRemaining -= Time.deltaTime;
                if (oneShotRemaining <= 0f)
                {
                    StopOnce();
                }
            }
            int action = IndexOf(CurrentAction);
            if (action != appliedAction)
            {
                appliedAction = action;
                animator.SetInteger(ActionId, action);
            }

            float speed = motor.PlanarSpeed;
            animator.SetFloat(SpeedId, speed, speedDampTime, Time.deltaTime);
            animator.SetBool(GroundedId, motor.IsGrounded);
            animator.SetBool(CrouchId, motor.IsCrouching);
            animator.SetBool(FallingId, !motor.IsGrounded && motor.enabled && motor.AirTime > fallDelay && motor.VerticalVelocity < -1f);
            bool moving = action == 0 && motor.IsGrounded && !motor.IsCrouching;
            animator.speed = moving ? Mathf.Max(1f, speed / runClipSpeed) : (action != 0 ? ActionSpeed : 1f);
        }

        void SetLock(bool locked)
        {
            oneShotLocks = locked;
            motor.Locked = locked;
        }

        static int IndexOf(string state)
        {
            if (state == null)
            {
                return 0;
            }
            int index = System.Array.IndexOf(ActionStates, state);
            return index < 0 ? 0 : index + 1;
        }

        void OnEnteredWater(float impact)
        {
            if (impact >= waterFallSpeed)
            {
                PlayOnce("Water_Fall");
            }
        }

        void OnJumped()
        {
            if (animator != null)
            {
                // 같은 프레임에 Grounded 가 아직 true 면 점프 상태에서 바로 착지로 빠져나가 버린다.
                animator.SetBool(GroundedId, false);
                // 트리거는 NetworkAnimator 를 거쳐야 다른 플레이어에게도 전달된다.
                if (networkAnimator != null && networkAnimator.IsSpawned)
                {
                    networkAnimator.SetTrigger(JumpId);
                }
                else
                {
                    animator.SetTrigger(JumpId);
                }
            }
        }
    }
}
