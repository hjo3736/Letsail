using System;
using UnityEngine;

namespace Letsail.Player
{
    /// <summary>
    /// 잠수할 때 숨. 머리가 물에 잠기면 줄고, 물 밖으로 나오면 빠르게 다시 찬다.
    /// 다 떨어지면 잠수할 수 없고 수면으로 떠오른다 (숨이 어느 정도 찰 때까지).
    /// 체력이 생기면 OutOfBreath 로 피해를 주면 된다. 자기 캐릭터에서만 계산한다 (화면 표시용).
    /// </summary>
    [RequireComponent(typeof(PlayerMotor))]
    public sealed class PlayerBreath : MonoBehaviour
    {
        [SerializeField, Tooltip("숨을 참을 수 있는 시간 (초)")] float maxBreath = 20f;
        [SerializeField, Tooltip("물 밖에서 숨이 다시 차는 빠르기 (참는 속도의 몇 배)")] float recoverRate = 4f;
        [SerializeField, Range(0f, 1f), Tooltip("숨이 다 떨어진 뒤 이만큼 다시 차야 잠수할 수 있다")] float diveAgainAt = 0.3f;

        PlayerMotor motor;
        float breath;

        /// <summary>남은 숨 (0~1).</summary>
        public float Breath01 => maxBreath > 0f ? breath / maxBreath : 1f;

        /// <summary>숨이 다 떨어져 떠오르는 중.</summary>
        public bool IsOutOfBreath { get; private set; }

        /// <summary>숨이 다 떨어졌을 때.</summary>
        public event Action OutOfBreath;

        /// <summary>자기 캐릭터의 숨.</summary>
        public static PlayerBreath Local => PlayerInteractor.Local != null ? PlayerInteractor.Local.GetComponent<PlayerBreath>() : null;

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            breath = maxBreath;
        }

        void OnDisable()
        {
            if (motor != null)
            {
                motor.DiveBlocked = false;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (motor.IsDiving)
            {
                breath = Mathf.Max(0f, breath - dt);
                if (breath <= 0f && !IsOutOfBreath)
                {
                    IsOutOfBreath = true;
                    OutOfBreath?.Invoke();
                }
            }
            else
            {
                breath = Mathf.Min(maxBreath, breath + dt * recoverRate);
            }
            if (IsOutOfBreath && !motor.IsDiving && Breath01 >= diveAgainAt)
            {
                IsOutOfBreath = false;
            }
            motor.DiveBlocked = IsOutOfBreath;
        }
    }
}
