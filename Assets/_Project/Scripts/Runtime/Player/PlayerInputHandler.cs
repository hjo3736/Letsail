using UnityEngine;
using UnityEngine.InputSystem;

namespace Letsail.Player
{
    /// <summary>
    /// 로컬 키보드·마우스·게임패드 입력을 PlayerMotor 에 넘긴다.
    /// 이동 방향은 카메라가 보는 방향 기준이다.
    /// 멀티플레이에서는 자기 캐릭터에만 켜 둔다.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(PlayerMotor))]
    public sealed class PlayerInputHandler : MonoBehaviour
    {
        [SerializeField, Tooltip("이동 방향의 기준. 비우면 메인 카메라")]
        Transform cameraTransform;

        PlayerMotor motor;
        InputAction moveAction;
        InputAction sprintAction;
        InputAction crouchAction;
        InputAction jumpAction;
        bool crouchToggled;

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            InputActionAsset actions = InputSystem.actions;
            moveAction = actions.FindAction("Player/Move", true);
            sprintAction = actions.FindAction("Player/Sprint", true);
            crouchAction = actions.FindAction("Player/Crouch", true);
            jumpAction = actions.FindAction("Player/Jump", true);
        }

        void OnEnable()
        {
            InputSystem.actions.FindActionMap("Player", true).Enable();
        }

        void OnDisable()
        {
            if (motor != null)
            {
                motor.SetMoveDirection(Vector3.zero);
                motor.SetSprint(false);
                motor.SetCrouch(false);
                motor.SetDive(false);
                motor.SetRise(false);
            }
        }

        void Update()
        {
            Vector2 input = moveAction.ReadValue<Vector2>();
            motor.SetMoveDirection(ToWorldDirection(input));
            motor.SetSprint(sprintAction.IsPressed());

            // 헤엄칠 때는 웅크리기 키(C)가 잠수 키다 (누르고 있는 동안 내려간다). 점프 키를 누르고 있으면 빨리 떠오른다.
            motor.SetDive(motor.IsSwimming && crouchAction.IsPressed());
            motor.SetRise(motor.IsSwimming && jumpAction.IsPressed());
            if (motor.IsSwimming)
            {
                crouchToggled = false;
                motor.SetCrouch(false);
            }
            else if (Letsail.Core.GameSettings.ToggleCrouch)
            {
                if (crouchAction.WasPressedThisFrame())
                {
                    crouchToggled = !crouchToggled;
                }
                motor.SetCrouch(crouchToggled);
            }
            else
            {
                motor.SetCrouch(crouchAction.IsPressed());
            }

            if (jumpAction.WasPressedThisFrame())
            {
                motor.RequestJump();
            }
        }

        Vector3 ToWorldDirection(Vector2 input)
        {
            Transform cam = cameraTransform;
            if (cam == null && Camera.main != null)
            {
                cam = Camera.main.transform;
            }
            if (cam == null)
            {
                return new Vector3(input.x, 0f, input.y);
            }

            Vector3 forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.ProjectOnPlane(cam.up, Vector3.up);
            }
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            return forward * input.y + right * input.x;
        }
    }
}
