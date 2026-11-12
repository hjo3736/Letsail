using UnityEngine;

namespace Letsail.Prototyping
{
    /// <summary>
    /// 테스트용: Kinematic Rigidbody 를 왕복·회전·흔들리게 움직인다.
    /// 움직이는 발판이나 파도에 흔들리는 배를 흉내 낼 때 쓴다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PlatformMover : MonoBehaviour
    {
        [Header("이동")]
        [SerializeField, Tooltip("일정한 속도로 계속 이동 (m/s)")] Vector3 linearVelocity;
        [SerializeField, Tooltip("왕복 이동 폭 (m)")] Vector3 oscillation;
        [SerializeField, Min(0.1f), Tooltip("왕복 한 번에 걸리는 시간 (초)")] float oscillationPeriod = 6f;

        [Header("회전")]
        [SerializeField, Tooltip("좌우 회전 속도 (도/초)")] float yawSpeed;
        [SerializeField, Tooltip("앞뒤(x)·좌우(z) 기울기 폭 (도)")] Vector2 rockDegrees;
        [SerializeField, Min(0.1f), Tooltip("기울기 한 번에 걸리는 시간 (초)")] float rockPeriod = 5f;

        Rigidbody body;
        Vector3 startPosition;
        Quaternion startRotation;
        float time;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            startPosition = body.position;
            startRotation = body.rotation;
        }

        void FixedUpdate()
        {
            time += Time.fixedDeltaTime;
            const float tau = Mathf.PI * 2f;

            float swing = Mathf.Sin(time * tau / oscillationPeriod);
            Vector3 position = startPosition + linearVelocity * time + oscillation * swing;

            float pitch = rockDegrees.x * Mathf.Sin(time * tau / (rockPeriod * 1.37f));
            float roll = rockDegrees.y * Mathf.Sin(time * tau / rockPeriod);
            Quaternion rotation = startRotation * Quaternion.Euler(0f, yawSpeed * time, 0f) * Quaternion.Euler(pitch, 0f, roll);

            body.MovePosition(position);
            body.MoveRotation(rotation);
        }
    }
}
