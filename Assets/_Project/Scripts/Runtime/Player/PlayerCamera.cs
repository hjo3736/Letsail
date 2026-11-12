using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Letsail.Player
{
    /// <summary>
    /// 플레이어 카메라. 기본은 어깨 너머 3인칭이고, 1인칭으로 바꿀 수 있다.
    /// - 3인칭: 카메라가 캐릭터 주위를 돌고, 캐릭터는 이동 방향으로 몸을 돌린다. 벽에 막히면 앞으로 당겨진다.
    /// - 1인칭: 마우스 좌우는 캐릭터 몸을 돌리고, 위아래는 카메라만 기울인다.
    /// 어느 쪽이든 밟은 바닥(배)이 돌면 시야도 같이 돈다.
    /// 조작 중에는 마우스 커서를 잠근다. 메뉴(TAB, 설정 등)가 HoldCursor 로 커서를 잡고 있는 동안은 풀어 두고,
    /// 모두 놓으면 다시 잠근다.
    /// </summary>
    public sealed class PlayerCamera : MonoBehaviour
    {
        public enum ViewMode
        {
            ThirdPerson,
            FirstPerson,
        }

        [SerializeField] PlayerMotor target;
        [SerializeField] ViewMode mode = ViewMode.ThirdPerson;

        [Header("공통")]
        [SerializeField, Tooltip("서 있을 때 발밑에서 시점 기준점까지 높이 (m). 3인칭은 머리, 1인칭은 눈")]
        float pivotHeight = 0.9f;
        [SerializeField, Tooltip("웅크리거나 일어설 때 시점 높이가 따라가는 속도")] float heightSharpness = 12f;
        [SerializeField, Tooltip("카메라가 가까운 물체를 잘라내지 않는 최소 거리 (m)")] float nearClip = 0.05f;

        [Header("3인칭")]
        [SerializeField, Tooltip("기준점에서 카메라까지 거리 (m)")] float distance = 2.5f;
        [SerializeField, Tooltip("어깨 너머로 비키는 거리 (m). +는 오른쪽")] float shoulderOffset = 0.45f;
        [SerializeField, Tooltip("벽에 막혀도 이보다 가까이 오지 않는다 (m)")] float minDistance = 0.3f;
        [SerializeField, Tooltip("가려진 뒤 다시 멀어지는 속도 (m/s)")] float returnSpeed = 5f;
        [SerializeField, Tooltip("카메라가 이 거리보다 가까워지면 캐릭터를 숨긴다 (m)")] float hideBodyDistance = 0.6f;
        [SerializeField] float collisionRadius = 0.15f;
        [SerializeField, Tooltip("카메라를 가리는 것으로 볼 레이어. 플레이어 레이어는 뺀다")]
        LayerMask collisionMask = ~0;

        /// <summary>이 레이어의 충돌체는 카메라가 지나간다 (돛대, 망대 난간 등).</summary>
        public const string PassThroughLayer = "CameraPassThrough";

        [Header("1인칭")]
        [SerializeField, Tooltip("1인칭 눈높이 (m)")] float eyeHeight = 0.82f;

        [Header("회전")]
        [SerializeField, Tooltip("스틱을 끝까지 밀었을 때 초당 각도")] float stickSensitivity = 180f;
        [SerializeField] float minPitch = -40f;
        [SerializeField] float maxPitch = 75f;

        [Header("고정 시점")]
        [SerializeField, Tooltip("고정 시점으로 옮겨 가는 속도")] float overrideSharpness = 8f;
        [SerializeField, Tooltip("중심을 바꾼 동안(누웠을 때) 위아래로 볼 수 있는 각도 (도)")] float overridePitchLimit = 12f;

        // 커서를 풀어 두라고 요청한 메뉴들. 하나라도 있으면 커서를 잠그지 않는다.
        static readonly HashSet<object> cursorHolders = new HashSet<object>();
        static PlayerCamera active;

        readonly List<(Renderer renderer, ShadowCastingMode mode)> hiddenRenderers = new List<(Renderer, ShadowCastingMode)>();
        Transform overridePose;
        InputAction lookAction;
        float yaw;
        float pitch = 10f;
        float currentHeight;
        float currentDistance;

        public PlayerMotor Target
        {
            get => target;
            set
            {
                SetBodyHidden(false);
                target = value;
                if (target != null)
                {
                    yaw = target.transform.eulerAngles.y;
                    currentDistance = distance;
                }
                // 따라갈 캐릭터가 없으면(접속 전 등) 커서를 풀어 UI 를 누를 수 있게 한다.
                SetCursorLocked(target != null && isActiveAndEnabled);
                ApplyMode();
            }
        }

        public ViewMode Mode
        {
            get => mode;
            set
            {
                mode = value;
                ApplyMode();
            }
        }

        /// <summary>화면 중앙에서 앞으로 나가는 레이. 상호작용 대상을 고를 때 쓴다.</summary>
        public Ray AimRay => new Ray(transform.position, transform.forward);

        /// <summary>
        /// 카메라를 정해진 시점으로 옮겨 고정한다 (옷장, 컷신 등). null 이면 다시 캐릭터를 따라간다.
        /// 고정되어 있는 동안은 마우스로 돌릴 수 없고 커서가 풀린다.
        /// </summary>
        public void SetOverridePose(Transform pose)
        {
            overridePose = pose;
            SetBodyHidden(false);
            SetCursorLocked(pose == null && target != null);
        }

        public bool HasOverridePose => overridePose != null;

        /// <summary>
        /// 3인칭 카메라가 도는 중심을 바꾼다 (누웠을 때 몸 가운데 등). null 이면 캐릭터 머리.
        /// 고정 시점과 달리 마우스로 돌릴 수 있고, 벽에 막히면 앞으로 당겨진다.
        /// </summary>
        public Transform PivotOverride { get; set; }

        /// <summary>3인칭 카메라 거리를 바꾼다 (m). 0 이하면 기본 거리.</summary>
        public float DistanceOverride { get; set; }

        /// <summary>
        /// 메뉴가 열려 있는 동안 커서를 풀어 두게 한다 (owner 는 보통 메뉴 자신).
        /// 모든 메뉴가 놓으면 조작 중일 때 커서를 다시 잠근다.
        /// </summary>
        public static void HoldCursor(object owner, bool hold)
        {
            bool changed = hold ? cursorHolders.Add(owner) : cursorHolders.Remove(owner);
            if (!changed)
            {
                return;
            }
            if (cursorHolders.Count > 0)
            {
                SetCursorLocked(false);
            }
            else if (active != null)
            {
                active.RestoreCursor();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            cursorHolders.Clear();
            active = null;
        }

        void RestoreCursor()
        {
            SetCursorLocked(target != null && overridePose == null && isActiveAndEnabled);
        }

        void Awake()
        {
            lookAction = InputSystem.actions.FindAction("Player/Look", true);
            // 돛대처럼 가늘고 높은 기둥은 카메라가 지나가게 둔다 (막히면 카메라가 캐릭터에 붙어 시야가 이상해진다).
            int passThrough = LayerMask.NameToLayer(PassThroughLayer);
            if (passThrough >= 0)
            {
                collisionMask &= ~(1 << passThrough);
            }
            var cam = GetComponent<Camera>();
            if (cam != null)
            {
                cam.nearClipPlane = nearClip;
            }
        }

        void OnEnable()
        {
            active = this;
            SetCursorLocked(target != null);
            if (target != null)
            {
                yaw = target.transform.eulerAngles.y;
            }
            currentHeight = mode == ViewMode.FirstPerson ? eyeHeight : pivotHeight;
            currentDistance = distance;
            ApplyMode();
        }

        void OnDisable()
        {
            if (active == this)
            {
                active = null;
            }
            SetCursorLocked(false);
            SetBodyHidden(false);
        }

        void OnValidate()
        {
            if (Application.isPlaying && isActiveAndEnabled)
            {
                ApplyMode();
            }
        }

        void Update()
        {
            // 창 밖을 눌렀다 돌아왔을 때 등: 화면을 클릭하면 다시 잠근다.
            // 메뉴가 열려 있거나 UI 버튼을 누르는 클릭은 잠그지 않는다 (잠그면 버튼이 눌리지 않는다).
            if (target != null && overridePose == null && cursorHolders.Count == 0 && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame
                && Cursor.lockState != CursorLockMode.Locked && !IsPointerOverUI())
            {
                SetCursorLocked(true);
            }
        }

        void LateUpdate()
        {
            if (overridePose != null)
            {
                float t = 1f - Mathf.Exp(-overrideSharpness * Time.deltaTime);
                transform.SetPositionAndRotation(
                    Vector3.Lerp(transform.position, overridePose.position, t),
                    Quaternion.Slerp(transform.rotation, overridePose.rotation, t));
                // 고정 시점이 풀리면 지금 화면 방향에서 이어서 돌 수 있게 맞춰 둔다.
                yaw = transform.eulerAngles.y;
                return;
            }

            if (target == null)
            {
                return;
            }

            Vector2 look = ReadLook();
            float dt = Time.deltaTime;
            float wantedHeight = (mode == ViewMode.FirstPerson ? eyeHeight : pivotHeight) * target.HeightRatio;
            currentHeight = Mathf.Lerp(currentHeight, wantedHeight, 1f - Mathf.Exp(-heightSharpness * dt));
            Vector3 pivot = PivotOverride != null ? PivotOverride.position : target.transform.position + Vector3.up * currentHeight;

            pitch = Mathf.Clamp(pitch + (Letsail.Core.GameSettings.InvertY ? 1f : -1f) * look.y, minPitch, maxPitch);
            // 중심을 바꾼 동안(누웠을 때)은 좁은 칸에서 위아래로 막히지 않게 거의 수평으로만 본다.
            if (PivotOverride != null)
            {
                pitch = Mathf.Clamp(pitch, -overridePitchLimit, overridePitchLimit);
            }

            if (mode == ViewMode.FirstPerson)
            {
                target.AddYaw(look.x);
                yaw = target.transform.eulerAngles.y;
                transform.SetPositionAndRotation(pivot, Quaternion.Euler(pitch, yaw, 0f));
                return;
            }

            // 배가 돌면 카메라도 같이 돌아서, 배 위에서 보는 방향이 유지된다.
            yaw += look.x + target.PlatformYawDelta;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);

            // 어깨 쪽으로 먼저 비킨 뒤 뒤로 물러난다. 각 단계에서 벽에 막히면 그 앞에서 멈춘다.
            Vector3 right = rotation * Vector3.right;
            Vector3 shoulder = pivot + right * CastDistance(pivot, right * Mathf.Sign(shoulderOffset), Mathf.Abs(shoulderOffset)) * Mathf.Sign(shoulderOffset);
            Vector3 back = rotation * Vector3.back;
            float maxDistance = DistanceOverride > 0f ? DistanceOverride : distance;
            float wanted = Mathf.Max(minDistance, CastDistance(shoulder, back, maxDistance));
            currentDistance = wanted < currentDistance ? wanted : Mathf.MoveTowards(currentDistance, wanted, returnSpeed * dt);

            transform.SetPositionAndRotation(shoulder + back * currentDistance, rotation);
            SetBodyHidden(currentDistance < hideBodyDistance);
        }

        Vector2 ReadLook()
        {
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                return Vector2.zero;
            }
            Vector2 look = lookAction.ReadValue<Vector2>();
            bool fromPointer = lookAction.activeControl?.device is Pointer;
            return look * (fromPointer ? Letsail.Core.GameSettings.MouseSensitivity : stickSensitivity * Time.deltaTime);
        }

        float CastDistance(Vector3 origin, Vector3 direction, float maxDistance)
        {
            if (maxDistance <= 0f)
            {
                return 0f;
            }
            return Physics.SphereCast(origin, collisionRadius, direction, out RaycastHit hit, maxDistance, collisionMask, QueryTriggerInteraction.Ignore)
                ? hit.distance
                : maxDistance;
        }

        void ApplyMode()
        {
            if (target == null)
            {
                return;
            }
            // 3인칭은 이동 방향을 바라보고, 1인칭은 마우스로 몸을 돌린다.
            target.FaceMoveDirection = mode == ViewMode.ThirdPerson;
            SetBodyHidden(mode == ViewMode.FirstPerson);
        }

        void SetBodyHidden(bool hidden)
        {
            if (hidden == hiddenRenderers.Count > 0)
            {
                return;
            }
            if (hidden && target != null)
            {
                foreach (var r in target.GetComponentsInChildren<Renderer>(true))
                {
                    hiddenRenderers.Add((r, r.shadowCastingMode));
                    r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }
                return;
            }
            foreach (var (r, shadowMode) in hiddenRenderers)
            {
                if (r != null)
                {
                    r.shadowCastingMode = shadowMode;
                }
            }
            hiddenRenderers.Clear();
        }

        static bool IsPointerOverUI()
        {
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject();
        }

        static void SetCursorLocked(bool locked)
        {
            locked &= cursorHolders.Count == 0;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
