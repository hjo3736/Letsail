using System.Collections.Generic;
using Letsail.Core;
using Letsail.Items;
using Letsail.Networking;
using Letsail.Player;
using Letsail.Ocean;
using Letsail.World.Islands;
using Unity.Netcode;
using UnityEngine;

namespace Letsail.Wildlife
{
    /// <summary>
    /// 야생동물 한 마리. 서버가 움직이고(NetworkTransform 으로 위치를 보낸다), 지금 동작은 번호 하나로 보낸다.
    /// 서식지(WildInfo.habitat)에 따라 움직임이 다르다.
    /// - 땅: 집 근처를 돌아다니고 쉬다가, 선원이 가까이 오면 도망친다.
    /// - 땅+물(거북, 바다사자): 도망칠 때 물로 들어가 헤엄친다.
    /// - 횃대 새(앵무새, 큰부리새): 나무 위에 앉아 있다가 놀라면 다른 나무로 날아간다.
    /// - 하늘 새(갈매기, 펠리컨, 독수리): 섬 위를 선회하다 가끔 내려앉는다.
    /// - 동굴(박쥐): 바위에 매달려 있다가 다가가면 날아올라 주위를 돈다.
    /// - 바다: 깊이 범위 안에서 헤엄친다 (해파리는 떠다니고, 문어는 바닥에 있다).
    /// 체력이 다하면 쓰러지고, 잠시 뒤 고기를 떨어뜨리고 사라진다. 맞으면 놀라서 도망친다.
    /// 선원이 멀리 있으면 쉰다 (움직이지 않아 네트워크로 보낼 것도 없다).
    /// 공격(선원에게 피해)은 아직 없다.
    /// </summary>
    public sealed class Animal : NetworkBehaviour, IDamageable
    {
        enum Mover { Ground, Amphibian, Perch, Soar, Cave, Swim, Drift, Floor }
        enum State { Idle, Wander, Flee, Fly, Land, Perched, Hang, Swim, Jump }

        [Header("구성 (야생동물 만들기 메뉴가 채운다)")]
        [SerializeField] WildInfo info;
        [SerializeField] Animator animator;
        [SerializeField, Tooltip("쓰러질 때 기울이는 모델")] Transform model;
        [SerializeField, Tooltip("애니메이터 상태 이름 (동작) 목록")] string[] actions;
        [SerializeField, Tooltip("동작마다 길이 (초)")] float[] actionLengths;
        [SerializeField] float bodyHeight = 0.3f;
        [SerializeField] float bodyRadius = 0.2f;
        [SerializeField, Tooltip("물에서 헤엄칠 때 원점을 수면 아래 이만큼 (m). 0 이면 몸 높이로 정한다 (하마 1.2, 악어 0.3)")] float swimDepth;

        [Header("사냥")]
        [SerializeField] float maxHealth = 20f;
        [SerializeField] int meatCount = 1;
        [SerializeField, Tooltip("떨어뜨리는 고기 (아이템 에셋 이름)")] string meatItem = "Item_Meat";
        [SerializeField] ItemCatalog catalog;
        [SerializeField] WorldItem worldItemPrefab;

        [Header("행동")]
        [SerializeField, Tooltip("이 거리 안에 선원이 없으면 쉰다 (m)")] float activeRadius = 160f;
        [SerializeField, Tooltip("맞으면 도망치는 시간 (초, 친화적인 동물)")] float fleeAfterHit = 6f;
        [SerializeField, Tooltip("있으면 적대적: 세력권에 들어온 선원을 먼저 공격한다")] AnimalAggression aggression;

        // 서버: 공격
        PlayerHealth prey;
        Vector3 territoryAnchor;
        float alertTimer;
        float attackTimer;
        float attackCooldown;
        float pendingHit = -1f;
        float calmTimer;

        readonly NetworkVariable<byte> action = new NetworkVariable<byte>();
        readonly NetworkVariable<bool> dead = new NetworkVariable<bool>();

        // 서버 상태
        Mover mover;
        State state;
        float health;
        float stateTimer;
        float actionTimer;
        Vector3 target;
        Vector3 threat;
        float fleeTimer;
        float orbitAngle;
        float orbitRadius;
        float orbitHeight;
        float orbitDirection = 1f;
        int perchIndex = -1;
        float deathTimer;
        bool meatDropped;
        System.Random random;

        // 모든 PC
        float deathRoll;
        int shownAction = -1;

        /// <summary>서버: 이 동물이 사는 섬.</summary>
        public IslandGenerator Island { get; set; }

        /// <summary>서버: 집 (돌아다니는 중심).</summary>
        public Vector3 Home { get; set; }

        /// <summary>서버: 집에서 이만큼까지 돌아다닌다 (m).</summary>
        public float HomeRadius { get; set; } = 12f;

        /// <summary>서버: 앉을 수 있는 곳 (나무 꼭대기, 바위 등).</summary>
        public List<Vector3> Perches { get; set; }

        /// <summary>서버: 죽었을 때 (스포너가 다시 만들지 않게 기억한다).</summary>
        public event System.Action<Animal> ServerDied;

        public WildInfo Info => info;
        public bool IsDead => dead.Value;
        public float Health => health;
        public float MaxHealth => maxHealth;

        /// <summary>적대적(세력권에 들어온 선원을 먼저 공격) / 친화적(도망침).</summary>
        public Temperament Temperament => aggression != null && aggression.preemptive ? Temperament.Hostile : Temperament.Friendly;

        public AnimalAggression Aggression => aggression;

        /// <summary>서버: 지금 쫓거나 공격하는 선원. 없으면 null.</summary>
        public PlayerHealth Target => prey;

        static float SeaLevel => OceanSystem.Instance != null ? OceanSystem.Instance.SeaLevel : 0f;

        void Awake()
        {
            if (aggression == null) aggression = GetComponent<AnimalAggression>();
        }

        public override void OnNetworkSpawn()
        {
            action.OnValueChanged += (_, v) => ApplyAction(v);
            ApplyAction(action.Value);
            if (!IsServer) return;
            health = maxHealth;
            random = new System.Random(unchecked((int)NetworkObjectId * 7919 + 13));
            ChooseMover();
            EnterStartState();
        }

        void ChooseMover()
        {
            string habitat = info != null ? info.habitat : "ground";
            string move = info != null ? info.move : "walk";
            mover = habitat switch
            {
                "ground_water" => Mover.Amphibian,
                "air_perch" => Perches != null && Perches.Count > 0 ? Mover.Perch : Mover.Ground,
                "air" => Mover.Soar,
                "air_cave" => Mover.Cave,
                "water_floor" => Mover.Floor,
                _ when habitat.StartsWith("water") => move == "drift" ? Mover.Drift : Mover.Swim,
                _ => Mover.Ground,
            };
        }

        void EnterStartState()
        {
            switch (mover)
            {
                case Mover.Perch:
                    perchIndex = NearestPerch(transform.position);
                    SetState(State.Perched, Range(15f, 70f));
                    break;
                case Mover.Soar:
                    orbitRadius = Mathf.Max(12f, HomeRadius);
                    orbitHeight = transform.position.y;
                    orbitAngle = Range(0f, Mathf.PI * 2f);
                    orbitDirection = random.NextDouble() < 0.5 ? 1f : -1f;
                    SetState(State.Fly, Range(25f, 60f));
                    break;
                case Mover.Cave:
                    SetState(State.Hang, 0f);
                    break;
                case Mover.Swim:
                case Mover.Drift:
                    orbitRadius = Mathf.Max(8f, HomeRadius);
                    orbitAngle = Range(0f, Mathf.PI * 2f);
                    orbitDirection = random.NextDouble() < 0.5 ? 1f : -1f;
                    SetState(State.Swim, Range(6f, 14f));
                    break;
                default:
                    SetState(State.Idle, Range(1f, 5f));
                    break;
            }
        }

        // ───────────────────────── 모든 PC: 모습 ─────────────────────────

        void ApplyAction(byte index)
        {
            if (animator == null || actions == null || index >= actions.Length || shownAction == index) return;
            shownAction = index;
            animator.CrossFadeInFixedTime(actions[index], 0.2f);
        }

        void LateUpdate()
        {
            if (model == null) return;
            if (dead.Value)
            {
                if (animator != null) animator.speed = 0f;
                deathRoll = Mathf.MoveTowards(deathRoll, 90f, 240f * Time.deltaTime);
                model.localRotation = Quaternion.Euler(0f, 0f, deathRoll);
            }
        }

        // ───────────────────────── 서버: 행동 ─────────────────────────

        void Update()
        {
            if (!IsServer || !IsSpawned) return;
            float dt = Time.deltaTime;
            if (dead.Value)
            {
                UpdateDead(dt);
                return;
            }
            float nearest = NearestPlayer(out Vector3 playerPosition);
            if (nearest > activeRadius) return;

            stateTimer -= dt;
            actionTimer -= dt;
            fleeTimer -= dt;
            if (aggression != null && UpdateHostile(dt)) return;
            // 적대적인 동물은 다가와도 도망치지 않는다.
            float flee = info != null && (aggression == null || !aggression.preemptive) ? info.fleeRadius : 0f;
            if (flee > 0f && nearest < flee)
            {
                threat = playerPosition;
                fleeTimer = Mathf.Max(fleeTimer, 2.5f);
            }
            bool scared = fleeTimer > 0f;

            switch (mover)
            {
                case Mover.Ground: UpdateGround(dt, scared, false); break;
                case Mover.Amphibian: UpdateGround(dt, scared, true); break;
                case Mover.Perch: UpdatePerch(dt, scared); break;
                case Mover.Soar: UpdateSoar(dt, scared); break;
                case Mover.Cave: UpdateCave(dt, nearest, playerPosition); break;
                case Mover.Floor: UpdateFloor(dt, scared); break;
                default: UpdateSwim(dt, scared); break;
            }
        }

        // 땅: 쉬기 ↔ 돌아다니기, 놀라면 도망. 땅+물은 물에 들어가면 수면에서 헤엄친다.
        void UpdateGround(float dt, bool scared, bool amphibian)
        {
            bool inWater = amphibian && WaterDepthAt(transform.position) > 0.35f;
            if (scared)
            {
                if (state != State.Flee) SetState(State.Flee, 0f);
                Vector3 away = Flat(transform.position - threat);
                if (away.sqrMagnitude < 0.01f) away = transform.forward;
                // 땅+물은 물 쪽으로 도망친다.
                if (amphibian && Island != null) away = Vector3.Lerp(away.normalized, Flat(transform.position - Island.transform.position).normalized, 0.6f);
                float speed = inWater ? SwimSpeed : RunSpeed;
                Play(inWater ? "Swim" : (Has("Run") ? "Run" : MoveClip));
                MoveGround(away.normalized, speed, dt, true, amphibian);
                return;
            }
            if (state == State.Flee) SetState(State.Idle, Range(1f, 3f));

            if (state == State.Idle)
            {
                if (inWater)
                {
                    // 물에 있으면 집(땅) 쪽으로 헤엄쳐 돌아간다.
                    target = Home;
                    SetState(State.Wander, 20f);
                    return;
                }
                if (actionTimer <= 0f) PlayIdleVariety();
                if (stateTimer <= 0f)
                {
                    target = PickGroundTarget();
                    SetState(State.Wander, 12f);
                }
                return;
            }

            if (state == State.Wander)
            {
                Vector3 to = Flat(target - transform.position);
                if (to.magnitude < 0.4f || stateTimer <= 0f)
                {
                    SetState(State.Idle, Range(2f, 7f));
                    actionTimer = 0f;
                    return;
                }
                Play(inWater ? "Swim" : MoveClip);
                if (!MoveGround(to.normalized, inWater ? SwimSpeed * 0.6f : WalkSpeed, dt, false, amphibian))
                {
                    target = PickGroundTarget();
                }
            }
        }

        // 한 걸음. 땅이 아니거나, 가파르거나, 막히거나, 집에서 너무 멀면 움직이지 않는다.
        bool MoveGround(Vector3 direction, float speed, float dt, bool fleeing, bool amphibian)
        {
            if (Island == null) return false;
            Vector3 position = transform.position;
            Vector3 step = direction * speed * dt;
            Vector3 next = position + step;
            if (!Island.SampleGround(next, out Vector3 ground, out float height, out float slope, out IslandSurface surface))
            {
                return false;
            }
            float depth = SeaLevel - ground.y;
            bool water = amphibian && depth > 0.35f;
            if (!water)
            {
                if (height < MinHeight || height > MaxHeight || slope > 38f) return Turn(direction, fleeing);
            }
            if (!fleeing && Flat(next - Home).magnitude > HomeRadius * 1.4f && Flat(next - Home).magnitude > Flat(position - Home).magnitude)
            {
                return Turn(direction, false);
            }
            // 나무·바위에 막히는지 (몸 높이에서 앞으로)
            if (Physics.SphereCast(position + Vector3.up * Mathf.Max(0.15f, bodyHeight), Mathf.Max(0.08f, bodyRadius * 0.8f), direction, out var hit,
                    step.magnitude + bodyRadius, 1 << IslandGenerator.IslandLayer, QueryTriggerInteraction.Ignore) && hit.normal.y < 0.6f)
            {
                return Turn(direction, fleeing);
            }
            if (water)
            {
                next.y = SeaLevel - (swimDepth > 0f ? swimDepth : Mathf.Max(0.12f, bodyHeight * 0.4f));
            }
            else
            {
                next = ground;
            }
            transform.position = next;
            Face(direction, dt);
            return true;
        }

        bool Turn(Vector3 direction, bool fleeing)
        {
            if (fleeing)
            {
                // 막히면 옆으로 돌아 도망친다.
                float side = random.NextDouble() < 0.5 ? -70f : 70f;
                threat = transform.position - Quaternion.Euler(0f, side, 0f) * direction * 3f;
            }
            return false;
        }

        Vector3 PickGroundTarget()
        {
            for (int i = 0; i < 12; i++)
            {
                float angle = Range(0f, Mathf.PI * 2f);
                float distance = Range(HomeRadius * 0.2f, HomeRadius);
                Vector3 p = Home + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                if (Island != null && Island.SampleGround(p, out Vector3 ground, out float height, out float slope, out _)
                    && height >= MinHeight && height <= MaxHeight && slope < 32f)
                {
                    return ground;
                }
            }
            return Home;
        }

        void PlayIdleVariety()
        {
            // 쉬는 동안 가끔 다른 동작 (먹기, 짖기, 꼬리 등).
            string[] options = { "Eat", "Peck", "Bark", "Scratch", "Croak", "Roar", "Sit", "Lie", "Talk" };
            var list = new List<string>();
            foreach (var o in options) if (Has(o)) list.Add(o);
            if (list.Count > 0 && random.NextDouble() < 0.35)
            {
                string pick = list[random.Next(list.Count)];
                Play(pick);
                actionTimer = Mathf.Max(LengthOf(pick), 2f);
                if (pick == "Lie" || pick == "Sit") actionTimer = Range(5f, 12f);
            }
            else
            {
                Play("Idle");
                actionTimer = Range(2f, 6f);
            }
        }

        // 횃대 새: 앉아 있다가 놀라면 다른 횃대로 날아간다.
        void UpdatePerch(float dt, bool scared)
        {
            switch (state)
            {
                case State.Perched:
                    if (perchIndex >= 0) transform.position = Perches[perchIndex];
                    if (actionTimer <= 0f)
                    {
                        string pick = random.NextDouble() < 0.3 && Has("Talk") ? "Talk" : random.NextDouble() < 0.3 && Has("Peck") ? "Peck" : "Idle";
                        Play(pick);
                        actionTimer = Mathf.Max(2f, LengthOf(pick));
                    }
                    if (scared || stateTimer <= 0f)
                    {
                        int next = perchIndex;
                        for (int i = 0; i < 6 && next == perchIndex; i++) next = random.Next(Perches.Count);
                        perchIndex = next;
                        target = Perches[perchIndex];
                        Play("TakeOff");
                        SetState(State.Fly, 0f);
                        actionTimer = LengthOf("TakeOff");
                    }
                    break;
                case State.Fly:
                    if (FlyTo(target, FlySpeed, dt, 4f))
                    {
                        Play("Land");
                        SetState(State.Land, LengthOf("Land"));
                    }
                    break;
                case State.Land:
                    transform.position = Vector3.MoveTowards(transform.position, target, 1f * dt);
                    if (stateTimer <= 0f)
                    {
                        transform.position = target;
                        SetState(State.Perched, Range(25f, 70f));
                        actionTimer = 0f;
                    }
                    break;
                default:
                    SetState(State.Perched, 0f);
                    break;
            }
        }

        // 하늘 새: 선회하다가 가끔 땅(해변)에 내려앉고, 놀라면 다시 날아오른다.
        void UpdateSoar(float dt, bool scared)
        {
            switch (state)
            {
                case State.Fly:
                {
                    orbitAngle += orbitDirection * FlySpeed / Mathf.Max(5f, orbitRadius) * dt;
                    Vector3 center = Home;
                    Vector3 wanted = new Vector3(center.x + Mathf.Cos(orbitAngle) * orbitRadius, orbitHeight + Mathf.Sin(orbitAngle * 2.3f) * 1.5f, center.z + Mathf.Sin(orbitAngle) * orbitRadius);
                    Vector3 move = wanted - transform.position;
                    transform.position = Vector3.MoveTowards(transform.position, wanted, FlySpeed * 1.5f * dt);
                    Face(move, dt);
                    if (actionTimer <= 0f)
                    {
                        bool flap = random.NextDouble() < 0.35;
                        Play(flap ? "Fly" : "Glide");
                        actionTimer = flap ? Range(1.5f, 3f) : Range(4f, 9f);
                    }
                    // 갈매기·펠리컨은 가끔 해변에 내려앉는다 (독수리는 높이 난다).
                    if (stateTimer <= 0f && Has("Land") && info != null && info.biome != "volcanic")
                    {
                        target = PickBeachSpot();
                        if (target != Vector3.zero)
                        {
                            SetState(State.Land, 30f);
                            Play("Glide");
                        }
                        else
                        {
                            stateTimer = Range(20f, 40f);
                        }
                    }
                    break;
                }
                case State.Land:
                    if (FlyTo(target, FlySpeed, dt, 2f))
                    {
                        Play("Land");
                        transform.position = target;
                        SetState(State.Idle, Range(10f, 30f));
                        actionTimer = LengthOf("Land");
                    }
                    break;
                case State.Idle:
                case State.Wander:
                    if (scared || stateTimer <= 0f)
                    {
                        Play("TakeOff");
                        orbitHeight = Range(6f, 12f) + SeaLevel;
                        orbitAngle = Mathf.Atan2(transform.position.z - Home.z, transform.position.x - Home.x);
                        SetState(State.Fly, Range(30f, 70f));
                        actionTimer = LengthOf("TakeOff");
                        break;
                    }
                    if (actionTimer <= 0f)
                    {
                        string pick = random.NextDouble() < 0.4 ? "Peck" : random.NextDouble() < 0.4 ? "Walk" : "Idle";
                        if (!Has(pick)) pick = "Idle";
                        Play(pick);
                        actionTimer = Mathf.Max(1.5f, LengthOf(pick));
                    }
                    if (CurrentAction == "Walk")
                    {
                        Vector3 dir = transform.forward;
                        if (!MoveGround(dir, WalkSpeed * 0.6f, dt, false, false)) Face(Quaternion.Euler(0f, 90f, 0f) * dir, 1f);
                    }
                    break;
                default:
                    SetState(State.Fly, Range(20f, 40f));
                    break;
            }
        }

        Vector3 PickBeachSpot()
        {
            if (Island == null) return Vector3.zero;
            Vector3 center = Island.transform.position;
            for (int i = 0; i < 16; i++)
            {
                float angle = Range(0f, Mathf.PI * 2f);
                Vector3 p = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Island.Radius * Range(0.75f, 1.05f);
                if (Island.SampleGround(p, out Vector3 ground, out float height, out float slope, out IslandSurface surface)
                    && height > 0f && height < 2f && slope < 25f)
                {
                    return ground;
                }
            }
            return Vector3.zero;
        }

        // 박쥐: 매달려 있다가 다가가면 깨어 집 주위를 날고, 한참 뒤 돌아와 다시 매달린다.
        void UpdateCave(float dt, float nearest, Vector3 playerPosition)
        {
            float wake = info != null ? Mathf.Max(info.aggroRadius, info.fleeRadius, 5f) : 5f;
            switch (state)
            {
                case State.Hang:
                    transform.position = Home;
                    Play("Hang");
                    if (nearest < wake || fleeTimer > 0f)
                    {
                        Play("WakeUp");
                        orbitRadius = Range(3f, 7f);
                        orbitHeight = Home.y + Range(1f, 3f);
                        orbitAngle = Range(0f, Mathf.PI * 2f);
                        SetState(State.Fly, Range(15f, 30f));
                        actionTimer = LengthOf("WakeUp");
                    }
                    break;
                case State.Fly:
                {
                    if (actionTimer <= 0f) Play("Fly");
                    orbitAngle += orbitDirection * FlySpeed / orbitRadius * dt;
                    Vector3 wanted = new Vector3(Home.x + Mathf.Cos(orbitAngle) * orbitRadius, orbitHeight + Mathf.Sin(orbitAngle * 3f) * 0.6f, Home.z + Mathf.Sin(orbitAngle) * orbitRadius);
                    Vector3 move = wanted - transform.position;
                    transform.position = Vector3.MoveTowards(transform.position, wanted, FlySpeed * 1.5f * dt);
                    Face(move, dt);
                    if (stateTimer <= 0f && nearest > wake * 1.5f)
                    {
                        SetState(State.Land, 20f);
                    }
                    break;
                }
                case State.Land:
                    if (FlyTo(Home, FlySpeed, dt, 1f))
                    {
                        SetState(State.Hang, 0f);
                    }
                    break;
                default:
                    SetState(State.Hang, 0f);
                    break;
            }
        }

        // 바다: 집 주위를 돌며 헤엄친다. 놀라면 빠르게 멀어진다. 돌고래·가오리는 가끔 뛰어오른다.
        void UpdateSwim(float dt, bool scared)
        {
            float speed = mover == Mover.Drift ? Mathf.Max(0.15f, WalkSpeed) : SwimSpeed;
            if (state == State.Jump)
            {
                // 뛰어오르기 클립 안에 높이 변화가 있다. 수면에 원점을 두고 앞으로만 간다.
                Vector3 p = transform.position + transform.forward * (4f / Mathf.Max(0.5f, LengthOf("Jump"))) * dt;
                p.y = SeaLevel;
                transform.position = p;
                if (stateTimer <= 0f) SetState(State.Swim, Range(6f, 14f));
                return;
            }
            if (scared && mover != Mover.Drift)
            {
                Vector3 away = Flat(transform.position - threat);
                if (away.sqrMagnitude < 0.01f) away = transform.forward;
                Play(Has("SwimFast") ? "SwimFast" : "Swim");
                SwimStep(away.normalized, speed * 2f, dt, true);
                return;
            }
            if (actionTimer <= 0f)
            {
                Play(mover == Mover.Drift ? (Has("Pulse") ? "Pulse" : "Idle") : "Swim");
                actionTimer = Range(4f, 9f);
            }
            if (stateTimer <= 0f)
            {
                orbitRadius = Mathf.Clamp(orbitRadius + Range(-6f, 6f), 6f, Mathf.Max(8f, HomeRadius));
                orbitHeight = SeaLevel + Range(info != null ? info.depthMin : -3f, info != null ? info.depthMax : -1f);
                stateTimer = Range(6f, 14f);
                if (Has("Jump") && random.NextDouble() < 0.25 && WaterDepthAt(transform.position) > 2.5f)
                {
                    Play("Jump");
                    SetState(State.Jump, LengthOf("Jump"));
                    return;
                }
            }
            orbitAngle += orbitDirection * speed / Mathf.Max(4f, orbitRadius) * dt;
            Vector3 wanted = new Vector3(Home.x + Mathf.Cos(orbitAngle) * orbitRadius, orbitHeight, Home.z + Mathf.Sin(orbitAngle) * orbitRadius);
            Vector3 to = wanted - transform.position;
            if (to.sqrMagnitude > 0.01f) SwimStep(to.normalized, speed, dt, false);
        }

        void SwimStep(Vector3 direction, float speed, float dt, bool fleeing)
        {
            Vector3 next = transform.position + direction * speed * dt;
            float top = SeaLevel + (info != null ? info.depthMax : -0.5f);
            float bottom = SeaLevel + (info != null ? info.depthMin : -5f);
            // 바닥보다 위에, 수면보다 아래에
            float floor = FloorAt(next);
            if (floor > next.y - 0.4f)
            {
                if (floor > top - 0.3f)
                {
                    // 너무 얕다: 섬 반대쪽(깊은 곳)으로 빠져나간다.
                    Vector3 outward = Island != null ? Flat(transform.position - Island.transform.position).normalized : -direction;
                    if (fleeing) threat = transform.position - outward * 3f;
                    Home += outward * speed * dt;
                    next = transform.position + outward * speed * dt;
                    floor = FloorAt(next);
                    next.y = Mathf.Min(transform.position.y, top);
                    if (floor > next.y - 0.2f) next.y = floor + 0.2f;
                    Face(outward, dt);
                    transform.position = next;
                    return;
                }
                next.y = floor + 0.5f;
            }
            next.y = Mathf.Clamp(next.y, Mathf.Max(bottom, floor + 0.4f), top);
            if (!fleeing && Flat(next - Home).magnitude > HomeRadius * 2f) orbitRadius = HomeRadius * 0.8f;
            Face(next - transform.position, dt);
            transform.position = next;
        }

        // 문어: 바닥에 있다가 가끔 조금 옮겨 간다.
        void UpdateFloor(float dt, bool scared)
        {
            if (state != State.Wander)
            {
                if (actionTimer <= 0f)
                {
                    Play("Idle");
                    actionTimer = Range(4f, 8f);
                }
                if (stateTimer <= 0f)
                {
                    target = Home + new Vector3(Range(-1f, 1f), 0f, Range(-1f, 1f)) * HomeRadius * 0.5f;
                    target.y = FloorAt(target);
                    SetState(State.Wander, 10f);
                    Play("Swim");
                }
                return;
            }
            Vector3 to = target - transform.position;
            if (to.magnitude < 0.3f || stateTimer <= 0f)
            {
                SetState(State.Idle, Range(8f, 20f));
                return;
            }
            Vector3 next = Vector3.MoveTowards(transform.position, target, WalkSpeed * 0.5f * dt);
            next.y = FloorAt(next);
            Face(to, dt);
            transform.position = next;
        }

        // 날아서 목표로 간다 (높이 올라갔다가 내려온다). 도착하면 true.
        bool FlyTo(Vector3 goal, float speed, float dt, float cruise)
        {
            Vector3 position = transform.position;
            float flat = Flat(goal - position).magnitude;
            float wantedY = flat > 4f ? Mathf.Max(goal.y, position.y) + Mathf.Min(cruise, flat * 0.3f) : goal.y;
            Vector3 aim = new Vector3(goal.x, wantedY, goal.z);
            Vector3 next = Vector3.MoveTowards(position, flat > 4f ? Vector3.MoveTowards(new Vector3(position.x, wantedY, position.z), aim, flat) : goal, speed * dt);
            Face(next - position, dt);
            transform.position = next;
            if (actionTimer <= 0f) Play(flat > 6f && random.NextDouble() < 0.5 ? "Glide" : "Fly");
            if (actionTimer <= 0f) actionTimer = Range(1.5f, 3f);
            return (goal - next).magnitude < 0.25f;
        }

        // ───────────────────────── 서버: 공격 (적대적) ─────────────────────────

        // 세력권 가운데. 집 둘레를 도는 바다 동물(상어 등)은 지금 자리를 기준으로 한다.
        Vector3 TerritoryCenter => mover == Mover.Swim || mover == Mover.Drift || mover == Mover.Soar ? transform.position : Home;

        bool IsSeaCreature => mover == Mover.Swim || mover == Mover.Drift || mover == Mover.Floor;

        bool provoked;

        // 쫓거나 공격 중이면 true (평소 움직임을 하지 않는다).
        bool UpdateHostile(float dt)
        {
            attackCooldown -= dt;
            calmTimer -= dt;

            if (pendingHit > 0f)
            {
                pendingHit -= dt;
                if (pendingHit <= 0f) ResolveAttack();
            }

            if (prey != null && !IsValidTarget(prey, territoryAnchor, aggression.territoryRadius * (provoked ? Mathf.Max(aggression.leash, aggression.retaliateRange) : aggression.leash)))
            {
                Disengage();
                return attackTimer > 0f;
            }
            if (prey == null)
            {
                if (calmTimer > 0f || !aggression.preemptive) return false;
                prey = FindTarget();
                if (prey == null) return false;
                territoryAnchor = TerritoryCenter;
                provoked = false;
                if (!string.IsNullOrEmpty(aggression.alertAction) && Has(aggression.alertAction))
                {
                    Play(aggression.alertAction);
                    alertTimer = LengthOf(aggression.alertAction);
                }
            }

            Vector3 goal = prey.transform.position;
            if (alertTimer > 0f)
            {
                alertTimer -= dt;
                if (mover == Mover.Cave) transform.position = Vector3.MoveTowards(transform.position, transform.position + Vector3.up, dt);
                Face(goal - transform.position, dt);
                return true;
            }
            if (attackTimer > 0f)
            {
                attackTimer -= dt;
                Face(goal - transform.position, dt);
                return true;
            }

            if (InReach(goal, aggression.attackRange))
            {
                Face(goal - transform.position, dt);
                if (attackCooldown <= 0f)
                {
                    string action = Has(aggression.attackAction) ? aggression.attackAction : Has("Attack") ? "Attack" : Has("Strike") ? "Strike" : null;
                    if (!string.IsNullOrEmpty(aggression.altAttackAction) && Has(aggression.altAttackAction) && random.NextDouble() < 0.4) action = aggression.altAttackAction;
                    float length = action != null ? LengthOf(action) : 1f;
                    if (action != null) Play(action);
                    attackTimer = length;
                    pendingHit = length * aggression.hitMoment;
                    attackCooldown = Mathf.Max(aggression.cooldown, length);
                }
                else if (mover == Mover.Cave)
                {
                    Play("Fly");
                }
                else
                {
                    Play(IsSeaCreature ? "Swim" : "Idle");
                }
                return true;
            }

            Chase(goal, dt);
            return true;
        }

        void Chase(Vector3 goal, float dt)
        {
            Vector3 to = goal - transform.position;
            float boost = aggression.chaseSpeed;
            switch (mover)
            {
                case Mover.Cave:
                case Mover.Soar:
                case Mover.Perch:
                {
                    // 날아서 가슴 높이로 덤빈다.
                    Vector3 aim = goal + Vector3.up * 1f;
                    Vector3 next = Vector3.MoveTowards(transform.position, aim, FlySpeed * boost * dt);
                    Face(aim - transform.position, dt);
                    transform.position = next;
                    Play("Fly");
                    if (mover == Mover.Cave) SetState(State.Fly, 4f);
                    break;
                }
                case Mover.Swim:
                case Mover.Drift:
                    Play(Has("SwimFast") ? "SwimFast" : "Swim");
                    SwimStep(to.normalized, SwimSpeed * 1.5f * boost, dt, true);
                    break;
                case Mover.Floor:
                {
                    // 바닥을 기어 다가간다.
                    Play("Swim");
                    Vector3 next = Vector3.MoveTowards(transform.position, new Vector3(goal.x, transform.position.y, goal.z), WalkSpeed * boost * dt);
                    next.y = FloorAt(next);
                    Face(to, dt);
                    transform.position = next;
                    break;
                }
                default:
                    Play(Has("Run") ? "Run" : MoveClip);
                    MoveGround(Flat(to).normalized, RunSpeed * boost, dt, true, mover == Mover.Amphibian);
                    break;
            }
        }

        bool InReach(Vector3 goal, float range)
        {
            Vector3 delta = goal - transform.position;
            if (mover == Mover.Ground || mover == Mover.Amphibian)
            {
                return Flat(delta).magnitude <= range && Mathf.Abs(delta.y) <= 1.8f;
            }
            // 날거나 헤엄치는 동물은 선원 몸 가운데까지 거리
            return (goal + Vector3.up * 0.8f - transform.position).magnitude <= range + 0.6f;
        }

        void ResolveAttack()
        {
            if (prey == null || dead.Value) return;
            if (!IsValidTarget(prey, territoryAnchor, float.MaxValue)) return;
            if (!InReach(prey.transform.position, aggression.attackRange * 1.4f)) return;
            prey.ServerTakeDamage(aggression.damage, transform.position);
        }

        void Disengage()
        {
            prey = null;
            provoked = false;
            calmTimer = 3f;
            pendingHit = -1f;
            // 집으로 돌아간다.
            switch (mover)
            {
                case Mover.Cave:
                    SetState(State.Land, 20f);
                    break;
                case Mover.Ground:
                case Mover.Amphibian:
                    SetState(State.Wander, 15f);
                    target = PickGroundTarget();
                    break;
            }
        }

        PlayerHealth FindTarget()
        {
            PlayerHealth best = null;
            float bestDistance = float.MaxValue;
            Vector3 center = TerritoryCenter;
            foreach (var player in NetworkPlayer.All)
            {
                if (player == null) continue;
                var health = player.GetComponent<PlayerHealth>();
                if (!IsValidTarget(health, center, aggression.territoryRadius)) continue;
                float d = Vector3.Distance(player.transform.position, transform.position);
                if (d < bestDistance)
                {
                    best = health;
                    bestDistance = d;
                }
            }
            return best;
        }

        // 노릴 수 있는 선원인지: 살아 있고, 배 위가 아니고, 이 동물이 갈 수 있는 곳(땅 / 물)에 있고, 세력권 안.
        bool IsValidTarget(PlayerHealth health, Vector3 center, float radius)
        {
            if (health == null || !health.IsSpawned || health.IsDowned || health.IsProtected) return false;
            if (!CanReach(health)) return false;
            Vector3 p = health.transform.position;
            float distance = IsSeaCreature || mover == Mover.Cave ? Vector3.Distance(p, center) : Flat(p - center).magnitude;
            return distance <= radius;
        }

        bool CanReach(PlayerHealth health)
        {
            Vector3 p = health.transform.position;
            if (Letsail.Ship.ShipMotion.FindShipUnder(p) != null) return false;
            var motor = health.GetComponent<PlayerMotor>();
            bool inWater = motor != null && motor.IsSwimming;
            if (IsSeaCreature) return inWater;
            if (mover == Mover.Cave || mover == Mover.Soar || mover == Mover.Perch) return true;
            return !inWater;
        }

        static PlayerHealth NearestHealth(Vector3 position)
        {
            PlayerHealth best = null;
            float bestDistance = float.MaxValue;
            foreach (var player in NetworkPlayer.All)
            {
                if (player == null) continue;
                float d = Vector3.Distance(player.transform.position, position);
                if (d < bestDistance)
                {
                    best = player.GetComponent<PlayerHealth>();
                    bestDistance = d;
                }
            }
            return best;
        }

        // ───────────────────────── 서버: 피해·죽음 ─────────────────────────

        public void ServerTakeDamage(float amount, Vector3 from)
        {
            if (!IsServer || dead.Value) return;
            health -= amount;
            if (health <= 0f)
            {
                Die();
                return;
            }
            if (aggression != null)
            {
                // 적대적: 도망치지 않고 공격한 선원을 쫓는다 (세력권 밖이라도 조금 더 멀리까지).
                var attacker = NearestHealth(from);
                if (attacker != null && CanReach(attacker) && Vector3.Distance(attacker.transform.position, TerritoryCenter) <= aggression.territoryRadius * aggression.retaliateRange)
                {
                    if (prey == null) territoryAnchor = TerritoryCenter;
                    prey = attacker;
                    provoked = true;
                    alertTimer = 0f;
                    calmTimer = 0f;
                    return;
                }
                // 적대적이면 반격할 수 없어도 도망치지 않는다. 방어적이면 도망친다.
                if (aggression.preemptive) return;
            }
            threat = from;
            fleeTimer = fleeAfterHit;
            if (mover == Mover.Perch && state == State.Perched) stateTimer = -999f;
            if (mover == Mover.Cave && state == State.Hang) fleeTimer = fleeAfterHit;
            if (mover == Mover.Soar && state != State.Fly && state != State.Land) stateTimer = 0f;
        }

        void Die()
        {
            dead.Value = true;
            deathTimer = 0f;
            ServerDied?.Invoke(this);
        }

        void UpdateDead(float dt)
        {
            deathTimer += dt;
            // 날던 것은 떨어지고 (땅이나 수면까지), 물속 동물은 수면으로 떠오른다.
            Vector3 p = transform.position;
            float ground = GroundOrWater(p);
            if (p.y > ground + 0.02f)
            {
                p.y = Mathf.Max(ground, p.y - 8f * dt);
                transform.position = p;
                return;
            }
            if (p.y < ground - 0.02f)
            {
                p.y = Mathf.Min(ground, p.y + 2.5f * dt);
                transform.position = p;
                return;
            }
            if (!meatDropped && deathTimer > 1.2f)
            {
                meatDropped = true;
                DropMeat();
            }
            if (deathTimer > 2.5f)
            {
                NetworkObject.Despawn(true);
            }
        }

        void DropMeat()
        {
            if (catalog == null || worldItemPrefab == null) return;
            int id = catalog.IndexOf(meatItem);
            if (id < 0) return;
            for (int i = 0; i < meatCount; i++)
            {
                Vector3 offset = new Vector3(Range(-0.4f, 0.4f), 0f, Range(-0.4f, 0.4f));
                Vector3 p = transform.position + offset;
                p.y = GroundOrWater(p);
                WorldItem.ServerSpawn(worldItemPrefab, id, p, Range(0f, 360f));
            }
        }

        // ───────────────────────── 도움 ─────────────────────────

        float WalkSpeed => info != null ? Mathf.Max(0.1f, info.speed) : 1f;
        float RunSpeed => info != null && info.runSpeed > 0f ? info.runSpeed : WalkSpeed * 2.2f;
        float SwimSpeed => info != null && info.swimSpeed > 0f ? info.swimSpeed : WalkSpeed * 1.5f;
        float FlySpeed => info != null ? Mathf.Max(2f, info.speed) : 4f;
        string MoveClip => Has("Walk") ? "Walk" : Has("Hop") ? "Hop" : Has("Slither") ? "Slither" : Has("Run") ? "Run" : "Idle";

        float MinHeight => info == null ? 0.1f : info.biome == "beach" ? -0.3f : info.biome == "jungle" ? 0.6f : 0.2f;
        float MaxHeight => info == null ? 50f : info.biome == "beach" ? 2.2f : 60f;

        string CurrentAction => actions != null && action.Value < actions.Length ? actions[action.Value] : null;

        bool Has(string name) => actions != null && System.Array.IndexOf(actions, name) >= 0;

        float LengthOf(string name)
        {
            int i = actions != null ? System.Array.IndexOf(actions, name) : -1;
            return i >= 0 && actionLengths != null && i < actionLengths.Length ? actionLengths[i] : 1f;
        }

        void Play(string name)
        {
            int i = actions != null ? System.Array.IndexOf(actions, name) : -1;
            if (i < 0) i = actions != null ? System.Array.IndexOf(actions, "Idle") : -1;
            if (i < 0) i = 0;
            if (action.Value != i) action.Value = (byte)i;
        }

        void SetState(State next, float duration)
        {
            state = next;
            stateTimer = duration;
        }

        float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void Face(Vector3 direction, float dt)
        {
            Vector3 flat = Flat(direction);
            if (flat.sqrMagnitude < 1e-6f) return;
            Quaternion wanted = Quaternion.LookRotation(flat.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, 360f * dt);
        }

        float NearestPlayer(out Vector3 position)
        {
            float best = float.MaxValue;
            position = transform.position + Vector3.forward * 1000f;
            foreach (var player in NetworkPlayer.All)
            {
                if (player == null) continue;
                float d = Vector3.Distance(player.transform.position, transform.position);
                if (d < best)
                {
                    best = d;
                    position = player.transform.position;
                }
            }
            return best;
        }

        int NearestPerch(Vector3 position)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < Perches.Count; i++)
            {
                float d = (Perches[i] - position).sqrMagnitude;
                if (d < bestDistance)
                {
                    best = i;
                    bestDistance = d;
                }
            }
            return best;
        }

        float FloorAt(Vector3 p)
        {
            if (Island != null && Island.SampleGround(p, out Vector3 ground, out _, out _, out _)) return ground.y;
            return SeaLevel - 30f;
        }

        float WaterDepthAt(Vector3 p) => SeaLevel - FloorAt(p);

        float GroundOrWater(Vector3 p)
        {
            float floor = FloorAt(p);
            return floor < SeaLevel - 0.3f ? SeaLevel : floor;
        }
    }
}
