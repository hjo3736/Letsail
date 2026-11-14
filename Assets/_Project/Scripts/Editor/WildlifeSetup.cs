using System.Collections.Generic;
using System.IO;
using Letsail.Core;
using Letsail.Items;
using Letsail.Player;
using Letsail.Wildlife;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Letsail.Editor
{
    /// <summary>
    /// 가져온 야생동물·아이템으로 사냥에 필요한 것을 만든다. 메뉴 Letsail > 야생동물·아이템 만들기.
    /// 1. "Animal" 레이어 (공격 판정용)
    /// 2. 아이템 정의 (무기, 고기)와 아이템 목록, 바닥에 놓는 아이템 프리팹
    /// 3. 동물마다 애니메이터(클립 이름 = 상태 이름)와 네트워크 프리팹 (물고기는 무리 프리팹), 동물 목록
    /// 4. 네트워크 프리팹 목록을 로비 씬 NetworkManager 에 등록
    /// 5. 플레이어: PlayerHands, 애니메이터에 공격·줍기·나르기 동작
    /// 6. 항해 씬: 동물 스포너, 배 무기 상자
    /// 모델이 바뀌면 다시 누르면 된다.
    /// </summary>
    public static class WildlifeSetup
    {
        const string WildlifeModels = "Assets/_Project/Art/Models/Wildlife";
        const string ItemModels = "Assets/_Project/Art/Models/Items";
        const string EffectModels = "Assets/_Project/Art/Models/Effects";
        const string EffectPrefabRoot = "Assets/_Project/Prefabs/Effects";
        const string AnimationRoot = "Assets/_Project/Art/Animations/Wildlife";
        const string PrefabRoot = "Assets/_Project/Prefabs/Wildlife";
        const string ItemPrefabRoot = "Assets/_Project/Prefabs/Items";
        const string DataRoot = "Assets/_Project/ScriptableObjects";
        const string NetworkListPath = "Assets/_Project/Settings/Network/NetworkPrefabs_World.asset";
        const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player/Player.prefab";
        const string PlayerControllerPath = "Assets/_Project/Art/Animations/Player/AC_Player.controller";
        const string PlayerModelPath = "Assets/_Project/Art/Models/Player/player.fbx";
        const string LobbyScene = "Assets/_Project/Scenes/Lobby.unity";
        const string VoyageScene = "Assets/_Project/Scenes/Voyage.unity";
        const int AnimalLayer = 9;

        struct ItemSpec
        {
            public string name, model, display, attack, follow;
            public ItemKind kind;
            public float damage, range, hitMoment, cooldown;
        }

        // 순서가 네트워크 번호다. 바꾸지 말고 뒤에 추가한다.
        static readonly ItemSpec[] Items =
        {
            new ItemSpec { name = "Item_Cutlass", model = "Tools/Tool_Cutlass.fbx", display = "커틀러스", kind = ItemKind.Melee, attack = "Sword_Slash", damage = 25f, range = 1.7f, hitMoment = 0.45f, cooldown = 0.7f },
            new ItemSpec { name = "Item_WoodenSword", model = "Tools/Tool_WoodenSword.fbx", display = "목검", kind = ItemKind.Melee, attack = "Sword_Slash", damage = 12f, range = 1.6f, hitMoment = 0.45f, cooldown = 0.7f },
            new ItemSpec { name = "Item_Dagger", model = "Tools/Tool_Dagger.fbx", display = "단검", kind = ItemKind.Melee, attack = "Dagger_Stab", damage = 15f, range = 1.3f, hitMoment = 0.4f, cooldown = 0.5f },
            new ItemSpec { name = "Item_Flintlock", model = "Tools/Tool_Flintlock.fbx", display = "부싯돌 권총", kind = ItemKind.Gun, attack = "Pistol_Shoot", follow = "Pistol_Reload", damage = 40f, range = 30f, hitMoment = 0.3f, cooldown = 1f },
            new ItemSpec { name = "Item_Musket", model = "Tools/Tool_Musket.fbx", display = "장총", kind = ItemKind.Gun, attack = "Musket_Shoot", follow = "Musket_Reload", damage = 70f, range = 60f, hitMoment = 0.3f, cooldown = 1.5f },
            new ItemSpec { name = "Item_Meat", model = "Food/Food_MeatLeg.fbx", display = "고기", kind = ItemKind.Carry },
            new ItemSpec { name = "Item_BigMeat", model = "Food/Food_Ham.fbx", display = "큰 고기", kind = ItemKind.Carry },
            new ItemSpec { name = "Item_Fish", model = "Food/Food_GrilledFish.fbx", display = "생선", kind = ItemKind.Carry },
            new ItemSpec { name = "Item_Rum", model = "Food/Food_Rum.fbx", display = "럼", kind = ItemKind.Carry },
            new ItemSpec { name = "Item_CannonBall", model = "Ship/Ammo_CannonBall.fbx", display = "포탄", kind = ItemKind.Carry },
            new ItemSpec { name = "Item_Apple", model = "Food/Food_Apple.fbx", display = "사과", kind = ItemKind.Carry },
            new ItemSpec { name = "Item_Banana", model = "Food/Food_Banana.fbx", display = "바나나", kind = ItemKind.Carry },
            new ItemSpec { name = "Item_Coconut", model = "Food/Food_Coconut.fbx", display = "코코넛", kind = ItemKind.Carry },
            new ItemSpec { name = "Item_Pineapple", model = "Food/Food_Pineapple.fbx", display = "파인애플", kind = ItemKind.Carry },
            // 섬 보스의 유물 (모델은 임시: 빛나는 구슬. Blender 유물 모델이 오면 바꾼다)
            new ItemSpec { name = "Item_Relic_Jungle", model = "Relic:Jungle", display = "정글의 심장", kind = ItemKind.Relic },
            new ItemSpec { name = "Item_Relic_Volcanic", model = "Relic:Volcanic", display = "용암 핵", kind = ItemKind.Relic },
        };

        // 종마다 체력·고기 (없으면 크기로 정한다)
        static readonly Dictionary<string, (float hp, string meat, int count)> AnimalOverrides = new Dictionary<string, (float, string, int)>
        {
            { "Jaguar", (90f, "Item_BigMeat", 2) },
            { "Boar", (60f, "Item_BigMeat", 2) },
            { "GiantTortoise", (120f, "Item_BigMeat", 3) },
            { "SeaLion", (50f, "Item_BigMeat", 2) },
            { "Shark", (100f, "Item_BigMeat", 2) },
            { "Octopus", (80f, "Item_BigMeat", 2) },
            { "MantaRay", (60f, "Item_BigMeat", 2) },
            { "Dolphin", (50f, "Item_BigMeat", 2) },
            { "Pelican", (25f, "Item_Meat", 2) },
            { "Vulture", (30f, "Item_Meat", 2) },
            { "Jellyfish", (8f, "Item_Meat", 0) },
            { "Elephant", (200f, "Item_BigMeat", 4) },
            { "Rhino", (150f, "Item_BigMeat", 3) },
            { "Hippo", (160f, "Item_BigMeat", 3) },
            { "Giraffe", (120f, "Item_BigMeat", 3) },
            { "Lion", (100f, "Item_BigMeat", 2) },
            { "Tiger", (110f, "Item_BigMeat", 2) },
            { "Bear", (120f, "Item_BigMeat", 3) },
            { "Gorilla", (90f, "Item_BigMeat", 2) },
            { "Crocodile", (100f, "Item_BigMeat", 2) },
            { "Komodo", (80f, "Item_BigMeat", 2) },
            { "BarrelCrab", (60f, "Item_Rum", 1) },
            { "TideSnail", (80f, "Item_BigMeat", 2) },
            { "BloomToad", (40f, "Item_Meat", 2) },
            { "Mossback", (160f, "Item_BigMeat", 3) },
            { "EmberGolem", (200f, "Item_Meat", 0) },
            { "MagmaSalamander", (90f, "Item_BigMeat", 2) },
            { "AshHound", (45f, "Item_Meat", 1) },
            { "CinderMoth", (25f, "Item_Meat", 0) },
            { "LanternAngler", (70f, "Item_Fish", 2) },
            { "CoralTurtle", (300f, "Item_BigMeat", 4) },
        };

        struct Combat
        {
            public bool preemptive;
            public float territory, damage, range, cooldown, hit;
            public string attack, alt, alert;

            public Combat(bool preemptive, float territory, float damage, float range, float cooldown, float hit, string attack = "Attack", string alert = null, string alt = null)
            {
                this.preemptive = preemptive; this.territory = territory; this.damage = damage; this.range = range;
                this.cooldown = cooldown; this.hit = hit; this.attack = attack; this.alert = alert; this.alt = alt;
            }
        }

        // 공격하는 동물. 적대적(true) = 세력권에 들어오면 먼저 공격, 방어적(false) = 맞았을 때만 반격.
        // 세력권(m), 피해, 사거리(m), 간격(초), 맞는 순간(README 맞는 프레임 ÷ 전체), 공격 동작, 알아챌 때 동작, 섞어 쓰는 공격
        static readonly Dictionary<string, Combat> Combats = new Dictionary<string, Combat>
        {
            // 적대적
            { "Snake", new Combat(true, 4f, 10f, 1.3f, 2f, 0.45f, "Strike") },
            { "LavaBeetle", new Combat(true, 3f, 5f, 0.7f, 1.5f, 0.5f) },
            { "Bat", new Combat(true, 7f, 4f, 0.9f, 1.6f, 0.5f, alert: "WakeUp") },
            { "Boar", new Combat(true, 10f, 15f, 1.4f, 2f, 0.45f) },
            { "Jaguar", new Combat(true, 16f, 25f, 2f, 2.5f, 0.45f, alert: "Roar") },
            { "Octopus", new Combat(true, 5f, 15f, 2.6f, 2.5f, 0.5f) },
            { "Shark", new Combat(true, 14f, 30f, 2.2f, 3f, 0.5f) },
            { "Lion", new Combat(true, 14f, 25f, 2.4f, 2.5f, 0.6f, alert: "Roar", alt: "Swipe") },
            { "Tiger", new Combat(true, 16f, 30f, 2.4f, 2.5f, 0.6f, alert: "Roar", alt: "Swipe") },
            { "Bear", new Combat(true, 10f, 30f, 1.8f, 2.5f, 0.57f, alert: "Roar") },
            { "Rhino", new Combat(true, 12f, 35f, 2.2f, 3f, 0.5f) },
            { "Hippo", new Combat(true, 8f, 40f, 2.4f, 3f, 0.55f, alert: "Yawn") },
            { "Crocodile", new Combat(true, 6f, 30f, 2f, 2.5f, 0.4f) },
            { "Gorilla", new Combat(true, 8f, 20f, 1.8f, 2.5f, 0.55f, alert: "ChestBeat") },
            { "Komodo", new Combat(true, 8f, 20f, 1.6f, 2.5f, 0.4f) },
            { "BloomToad", new Combat(true, 4f, 10f, 2.5f, 2f, 0.39f, alert: "Croak") },
            { "EmberGolem", new Combat(true, 6f, 35f, 2.4f, 3f, 0.56f) },
            { "MagmaSalamander", new Combat(true, 7f, 20f, 1.8f, 2.5f, 0.4f) },
            { "AshHound", new Combat(true, 12f, 12f, 1.5f, 1.8f, 0.47f, alert: "Howl") },
            { "CinderMoth", new Combat(true, 5f, 6f, 1.5f, 2.5f, 0.5f) },
            { "LanternAngler", new Combat(true, 8f, 25f, 2.2f, 3f, 0.44f) },
            // 방어적 (친화적이지만 맞으면 반격)
            { "Elephant", new Combat(false, 6f, 40f, 3f, 3f, 0.53f, alert: "Trumpet") },
            { "Giraffe", new Combat(false, 5f, 20f, 1.8f, 2.5f, 0.5f) },
            { "SeaLion", new Combat(false, 4f, 10f, 1.3f, 2f, 0.44f) },
            { "Monkey", new Combat(false, 4f, 5f, 1f, 1.5f, 0.53f) },
            { "Turtle", new Combat(false, 3f, 4f, 0.8f, 2f, 0.42f) },
            { "GiantTortoise", new Combat(false, 3f, 15f, 1.4f, 3f, 0.5f) },
            { "Dolphin", new Combat(false, 6f, 10f, 1.8f, 2.5f, 0.5f) },
            { "MantaRay", new Combat(false, 5f, 10f, 2.2f, 3f, 0.5f) },
            { "Jellyfish", new Combat(false, 3f, 5f, 1f, 2f, 0.37f) },
            { "BarrelCrab", new Combat(false, 3f, 12f, 1.4f, 2f, 0.43f) },
            { "TideSnail", new Combat(false, 3f, 8f, 2f, 3f, 0.55f) },
            { "Mossback", new Combat(false, 5f, 30f, 2.6f, 3f, 0.5f) },
            { "CoralTurtle", new Combat(false, 6f, 30f, 3f, 3f, 0.52f) },
        };

        // 뼈에 붙이는 불빛 (README 추천 색·범위)
        static readonly Dictionary<string, (string bone, Color color, float range)> GlowLights = new Dictionary<string, (string, Color, float)>
        {
            { "CinderMoth", ("Body", new Color(1f, 0.45f, 0.15f), 3f) },
            { "LanternAngler", ("Lure2", new Color(1f, 0.82f, 0.38f), 4f) },
        };

        // 물에서 헤엄칠 때 원점 깊이 (README: 하마는 물 바닥, 악어는 눈과 등만)
        static readonly Dictionary<string, float> SwimDepths = new Dictionary<string, float>
        {
            { "Hippo", 1.2f }, { "Crocodile", 0.3f }, { "SeaLion", 0.3f }, { "Turtle", 0.15f },
        };

        [MenuItem("Letsail/야생동물·아이템 만들기")]
        public static void Run() => Debug.Log(RunAll());

        public static string RunAll()
        {
            var log = new System.Text.StringBuilder();
            EnsureLayer("Animal", AnimalLayer);
            var gunFlash = BuildEffect("FX_MuzzleFlash_Gun", 3f, 1.4f, log);
            var cannonFlash = BuildEffect("FX_MuzzleFlash_Cannon", 8f, 2.1f, log);
            // 포탄: 꼬리(반복, 포탄에 붙음), 폭발(배·나무·동물, 불빛 9프레임), 물보라, 땅 충격
            var ballTrail = BuildEffect("FX_CannonBall_Trail", 0f, 0f, log);
            var ballExplosion = BuildEffect("FX_CannonBall_Explosion", 10f, 2.1f, log, new Color(1f, 0.45f, 0.15f), 0.3f);
            var ballSplash = BuildEffect("FX_CannonBall_Splash", 0f, 2.1f, log);
            var ballImpact = BuildEffect("FX_CannonBall_Impact", 5f, 1.6f, log, new Color(1f, 0.45f, 0.15f), 0.15f);
            var catalog = BuildItems(log, gunFlash);
            SetupShip(catalog, cannonFlash, ballTrail, ballExplosion, ballSplash, ballImpact, log);
            var worldItem = BuildWorldItemPrefab(catalog);
            var wildlife = BuildAnimals(catalog, worldItem, log);
            AssetDatabase.SaveAssets();
            var networkList = BuildNetworkList(worldItem, wildlife);
            FixPrefabHashes(worldItem, wildlife);
            SetupPlayer(catalog, worldItem, log);
            SetupPlayerAnimator(log);
            SetupScenes(networkList, wildlife, catalog, worldItem, log);
            AssetDatabase.SaveAssets();
            log.Insert(0, $"아이템 {catalog.Items.Count}개, 동물 프리팹 {wildlife.Prefabs.Count}개를 만들었어요.\n");
            return log.ToString();
        }

        // ───────────────────────── 공통 ─────────────────────────

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        static void EnsureLayer(string name, int index)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            var element = layers.GetArrayElementAtIndex(index);
            if (element.stringValue == name) return;
            if (!string.IsNullOrEmpty(element.stringValue)) Debug.LogWarning($"레이어 {index} 를 {element.stringValue} 에서 {name} 로 바꿔요");
            element.stringValue = name;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetLayer(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        static void SetField(Object target, string field, System.Action<SerializedProperty> set)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogWarning($"{target.GetType().Name}.{field} 이 없어요");
                return;
            }
            set(p);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ───────────────────────── 아이템 ─────────────────────────

        static ItemCatalog BuildItems(System.Text.StringBuilder log, GameObject gunFlash)
        {
            var list = new List<ItemDefinition>();
            foreach (var spec in Items)
            {
                var def = LoadOrCreate<ItemDefinition>($"{DataRoot}/Items/{spec.name}.asset");
                def.displayName = spec.display;
                def.kind = spec.kind;
                def.model = spec.model.StartsWith("Relic:")
                    ? BuildRelicModel(spec.model.Substring(6))
                    : AssetDatabase.LoadAssetAtPath<GameObject>($"{ItemModels}/{spec.model}");
                def.attackAction = spec.attack;
                def.followAction = spec.follow;
                def.damage = spec.damage;
                def.range = spec.range;
                def.hitMoment = spec.hitMoment;
                def.cooldown = spec.cooldown;
                def.restEuler = new Vector3(90f, 0f, 0f);
                def.restHeight = spec.kind == ItemKind.Carry ? 0.07f : 0.04f;
                if (spec.name == "Item_CannonBall")
                {
                    // 포탄은 원점이 공 가운데라 반지름만큼 띄운다.
                    def.restEuler = Vector3.zero;
                    def.restHeight = 0.18f;
                }
                // 총구 불꽃
                def.fireEffect = spec.kind == ItemKind.Gun ? gunFlash : null;
                def.muzzleName = spec.name == "Item_Flintlock" ? "Tool_Flintlock_Muzzle" : spec.name == "Item_Musket" ? "Tool_Musket_Muzzle" : null;
                def.fireEffectScale = spec.name == "Item_Musket" ? 1.3f : 1f;
                def.relic = Relics.TryGetValue(spec.name, out var relicBonus) ? relicBonus : default;
                if (spec.kind == ItemKind.Relic)
                {
                    def.restEuler = Vector3.zero;
                    def.restHeight = 0.2f;
                }
                if (def.model == null) log.Append($"아이템 모델이 없어요: {spec.model}\n");
                EditorUtility.SetDirty(def);
                list.Add(def);
            }
            var catalog = LoadOrCreate<ItemCatalog>($"{DataRoot}/Items/ItemCatalog.asset");
            catalog.SetItems(list);
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        static WorldItem BuildWorldItemPrefab(ItemCatalog catalog)
        {
            string path = $"{ItemPrefabRoot}/WorldItem.prefab";
            EnsureFolder(ItemPrefabRoot);
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var go = existing != null ? PrefabUtility.LoadPrefabContents(path) : new GameObject("WorldItem");
            try
            {
                if (go.GetComponent<NetworkObject>() == null) go.AddComponent<NetworkObject>();
                if (go.GetComponent<Interactable>() == null) go.AddComponent<Interactable>();
                var item = go.GetComponent<WorldItem>() ?? go.AddComponent<WorldItem>();
                SetField(item, "catalog", p => p.objectReferenceValue = catalog);
                SetField(go.GetComponent<NetworkObject>(), "SceneMigrationSynchronization", p => p.boolValue = false);
                PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                if (existing != null) PrefabUtility.UnloadPrefabContents(go);
                else Object.DestroyImmediate(go);
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<WorldItem>();
        }

        // ───────────────────────── 배 대포 ─────────────────────────

        const string ShipPrefabPath = "Assets/_Project/Prefabs/PlayerShip/playership_001.prefab";

        // ───────────────────────── 섬 보스·유물 ─────────────────────────

        // 유물 효과 (배 선장실 선반에 놓여 있는 동안 선원 전체)
        static readonly Dictionary<string, RelicBonus> Relics = new Dictionary<string, RelicBonus>
        {
            { "Item_Relic_Jungle", new RelicBonus { description = "체력 재생 3배 · 최대 체력 +25", healthRegen = 2f, maxHealth = 25f } },
            { "Item_Relic_Volcanic", new RelicBonus { description = "대포 피해 +50% · 무기 피해 +25%", cannonDamage = 0.5f, weaponDamage = 0.25f } },
        };

        struct BossSpec
        {
            public string name, baseSpecies, display, relic;
            public float scale, health;
            public Combat combat;
        }

        // 섬 보스 (임시: 있는 생물을 크게 키운다. Blender 보스 모델이 오면 baseSpecies 대신 그 모델로 바꾼다)
        static readonly BossSpec[] Bosses =
        {
            new BossSpec { name = "Tropical", baseSpecies = "Mossback", display = "숲의 거수", relic = "Item_Relic_Jungle", scale = 1.6f, health = 600f,
                combat = new Combat(true, 14f, 35f, 3.4f, 2.8f, 0.5f) },
            new BossSpec { name = "Volcanic", baseSpecies = "EmberGolem", display = "용암 거인", relic = "Item_Relic_Volcanic", scale = 1.6f, health = 800f,
                combat = new Combat(true, 14f, 45f, 3.6f, 3.2f, 0.56f) },
        };

        // 보스가 쓰러질 때 흩뿌리는 보상 (아이템 이름, 최소, 최대)
        static readonly (string item, int min, int max)[] BossLoot =
        {
            ("Item_CannonBall", 4, 6), ("Item_BigMeat", 2, 3), ("Item_Apple", 1, 2), ("Item_Banana", 1, 2),
            ("Item_Coconut", 0, 2), ("Item_Pineapple", 0, 1), ("Item_Rum", 1, 2),
        };

        // 임시 유물 모델: 빛나는 구슬 (원점 = 구슬 가운데)
        static GameObject BuildRelicModel(string kind)
        {
            string dir = $"{ItemPrefabRoot}/Relics";
            EnsureFolder(dir);
            string materialDir = "Assets/_Project/Art/Materials/Items";
            EnsureFolder(materialDir);
            Color color = kind == "Jungle" ? new Color(0.35f, 0.9f, 0.45f) : new Color(1f, 0.45f, 0.15f);
            string materialPath = $"{materialDir}/M_Relic_{kind}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.6f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 1.5f);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(material);

            var root = new GameObject($"Relic_{kind}");
            try
            {
                var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(orb.GetComponent<Collider>());
                orb.name = "Orb";
                orb.transform.SetParent(root.transform, false);
                orb.transform.localScale = Vector3.one * 0.32f;
                orb.GetComponent<MeshRenderer>().sharedMaterial = material;
                return PrefabUtility.SaveAsPrefabAsset(root, $"{dir}/Relic_{kind}.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // 있는 동물 프리팹을 바탕으로 보스 프리팹을 만든다 (크기·체력·공격을 키우고 BossAnimal 을 단다).
        static GameObject BuildBossPrefab(BossSpec spec, List<GameObject> animals, ItemCatalog catalog, WorldItem worldItem, System.Text.StringBuilder log)
        {
            var source = animals.Find(p => p != null && p.name == "Animal_" + spec.baseSpecies);
            if (source == null)
            {
                log.Append($"보스 바탕 동물이 없어요: {spec.baseSpecies}\n");
                return null;
            }
            string dir = $"{PrefabRoot}/Boss";
            EnsureFolder(dir);
            string path = $"{dir}/Boss_{spec.name}.prefab";
            var root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(source));
            try
            {
                root.name = "Boss_" + spec.name;
                root.transform.localScale = Vector3.one * spec.scale;
                var animal = root.GetComponent<Animal>();
                var so = new SerializedObject(animal);
                so.FindProperty("maxHealth").floatValue = spec.health;
                so.FindProperty("meatCount").intValue = 0;
                so.FindProperty("bodyHeight").floatValue *= spec.scale;
                so.FindProperty("bodyRadius").floatValue *= spec.scale;
                var aggression = root.GetComponent<AnimalAggression>() ?? root.AddComponent<AnimalAggression>();
                aggression.preemptive = spec.combat.preemptive;
                aggression.territoryRadius = spec.combat.territory;
                aggression.damage = spec.combat.damage;
                aggression.attackRange = spec.combat.range;
                aggression.cooldown = spec.combat.cooldown;
                aggression.hitMoment = spec.combat.hit;
                aggression.attackAction = spec.combat.attack;
                aggression.altAttackAction = spec.combat.alt;
                aggression.alertAction = spec.combat.alert;
                so.FindProperty("aggression").objectReferenceValue = aggression;
                so.ApplyModifiedPropertiesWithoutUndo();

                var boss = root.GetComponent<BossAnimal>() ?? root.AddComponent<BossAnimal>();
                var bso = new SerializedObject(boss);
                bso.FindProperty("displayName").stringValue = spec.display;
                bso.FindProperty("relicItem").stringValue = spec.relic;
                bso.FindProperty("catalog").objectReferenceValue = catalog;
                bso.FindProperty("worldItemPrefab").objectReferenceValue = worldItem;
                var loot = bso.FindProperty("loot");
                loot.arraySize = BossLoot.Length;
                for (int i = 0; i < BossLoot.Length; i++)
                {
                    var e = loot.GetArrayElementAtIndex(i);
                    e.FindPropertyRelative("item").stringValue = BossLoot[i].item;
                    e.FindPropertyRelative("min").intValue = BossLoot[i].min;
                    e.FindPropertyRelative("max").intValue = BossLoot[i].max;
                }
                bso.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // 항해 씬 HUD 에 보스 체력바 (체력 게이지와 같은 캔버스)
        static void EnsureBossHealthUI(System.Text.StringBuilder log)
        {
            if (Object.FindAnyObjectByType<Letsail.UI.BossHealthUI>(FindObjectsInactive.Include) != null) return;
            var health = Object.FindAnyObjectByType<Letsail.UI.HealthUI>(FindObjectsInactive.Include);
            var canvas = health != null ? health.GetComponentInParent<Canvas>(true) : Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                log.Append("HUD 캔버스가 없어 보스 체력바를 못 만들었어요\n");
                return;
            }
            var go = new GameObject("BossHealth", typeof(RectTransform));
            go.transform.SetParent(canvas.rootCanvas.transform, false);
            go.AddComponent<Letsail.UI.BossHealthUI>();
            log.Append($"{go.scene.name} 에 보스 체력바 추가\n");
        }

        // 배에 대포 발사·장전·포탄 바구니를 붙인다.
        static void SetupShip(ItemCatalog catalog, GameObject cannonFlash, GameObject trail, GameObject explosion, GameObject splash, GameObject impact, System.Text.StringBuilder log)
        {
            var root = PrefabUtility.LoadPrefabContents(ShipPrefabPath);
            try
            {
                var relics = root.GetComponent<Letsail.Ship.ShipRelics>() ?? root.AddComponent<Letsail.Ship.ShipRelics>();
                SetField(relics, "catalog", p => p.objectReferenceValue = catalog);
                var cannons = root.GetComponent<Letsail.Ship.ShipCannons>() ?? root.AddComponent<Letsail.Ship.ShipCannons>();
                var so = new SerializedObject(cannons);
                so.FindProperty("catalog").objectReferenceValue = catalog;
                so.FindProperty("muzzleFlash").objectReferenceValue = cannonFlash;
                so.FindProperty("trailEffect").objectReferenceValue = trail;
                so.FindProperty("explosionEffect").objectReferenceValue = explosion;
                so.FindProperty("splashEffect").objectReferenceValue = splash;
                so.FindProperty("impactEffect").objectReferenceValue = impact;
                so.FindProperty("ballModel").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>($"{ItemModels}/Ship/Ammo_CannonBall.fbx");
                so.FindProperty("basketFullModel").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>($"{ItemModels}/Ship/Basket_CannonBalls.fbx");
                so.FindProperty("basketEmptyModel").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>($"{ItemModels}/Ship/Basket_Empty.fbx");
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, ShipPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ───────────────────────── 이펙트 ─────────────────────────

        // 총구 불꽃: 클립 하나를 Fire 상태로 재생하고, 불꽃이 보이는 동안 불빛을 켠다. 그림자는 끈다.
        static GameObject BuildEffect(string name, float lightRange, float lifetime, System.Text.StringBuilder log, Color? lightColor = null, float flashSeconds = 0.17f)
        {
            string modelPath = $"{EffectModels}/{name}.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            AnimationClip clip = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            {
                if (o is AnimationClip c && !c.name.StartsWith("__preview")) clip = c;
            }
            if (model == null || clip == null)
            {
                log.Append($"이펙트가 없어요: {name}\n");
                return null;
            }
            string controllerDir = "Assets/_Project/Art/Animations/Effects";
            EnsureFolder(controllerDir);
            string controllerPath = $"{controllerDir}/AC_{name}.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine = controller.layers[0].stateMachine;
            foreach (var s in machine.states) machine.RemoveState(s.state);
            var fire = machine.AddState("Fire");
            fire.motion = clip;
            machine.defaultState = fire;
            EditorUtility.SetDirty(controller);

            EnsureFolder(EffectPrefabRoot);
            string path = $"{EffectPrefabRoot}/{name}.prefab";
            // 클립이 모델 루트의 위치·회전까지 움직이므로(0 으로), 빈 부모 아래에 넣어 부모 기준으로만 움직이게 한다.
            // (모델을 바로 루트로 쓰면 생기자마자 월드 원점으로 순간이동한다.)
            var root = new GameObject(name);
            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.transform.SetParent(root.transform, false);
            modelInstance.transform.localPosition = Vector3.zero;
            modelInstance.transform.localRotation = Quaternion.identity;
            try
            {
                var animator = modelInstance.GetComponent<Animator>() ?? modelInstance.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // 불꽃이 보이는 동안 켜는 불빛 (범위 0 이면 없음: 물보라·연기 꼬리)
                Light light = null;
                if (lightRange > 0f)
                {
                    var lightGo = new GameObject("FlashLight");
                    lightGo.transform.SetParent(root.transform, false);
                    lightGo.transform.localPosition = new Vector3(0f, 0f, 0.3f);
                    light = lightGo.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.color = lightColor ?? new Color(1f, 0.82f, 0.3f);
                    light.range = lightRange;
                    light.intensity = 3f;
                    light.shadows = LightShadows.None;
                }
                var flash = root.AddComponent<MeshEffect>();
                var so = new SerializedObject(flash);
                so.FindProperty("animator").objectReferenceValue = animator;
                so.FindProperty("flashLight").objectReferenceValue = light;
                so.FindProperty("lifetime").floatValue = lifetime;
                so.FindProperty("flashSeconds").floatValue = flashSeconds;
                so.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ───────────────────────── 동물 ─────────────────────────

        static WildlifeCatalog BuildAnimals(ItemCatalog catalog, WorldItem worldItem, System.Text.StringBuilder log)
        {
            var prefabs = new List<GameObject>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { WildlifeModels }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var info = model.GetComponentInChildren<WildInfo>(true);
                if (info == null)
                {
                    log.Append($"wild 속성이 없어 건너뜀: {path}\n");
                    continue;
                }
                string species = model.name;
                string biomeFolder = Path.GetFileName(Path.GetDirectoryName(path));
                var clips = LoadClips(path, species);
                var controller = BuildController(species, clips);
                var prefab = info.IsSchool
                    ? BuildSchoolPrefab(species, biomeFolder, model, info, controller, catalog, worldItem)
                    : BuildAnimalPrefab(species, biomeFolder, model, info, controller, clips, catalog, worldItem);
                if (prefab != null) prefabs.Add(prefab);
            }
            // 섬 보스 (임시: 있는 생물을 크게. Blender 보스 모델이 오면 바꾼다)
            foreach (var boss in Bosses)
            {
                var bossPrefab = BuildBossPrefab(boss, prefabs, catalog, worldItem, log);
                if (bossPrefab != null) prefabs.Add(bossPrefab);
            }
            prefabs.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            var wildlife = LoadOrCreate<WildlifeCatalog>($"{DataRoot}/Wildlife/WildlifeCatalog.asset");
            wildlife.SetPrefabs(prefabs);
            EditorUtility.SetDirty(wildlife);
            return wildlife;
        }

        // 클립 이름 "<종>_<동작>" → 동작 이름
        static List<(string action, AnimationClip clip)> LoadClips(string path, string species)
        {
            var list = new List<(string, AnimationClip)>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (o is not AnimationClip clip || clip.name.StartsWith("__preview")) continue;
                string action = clip.name.StartsWith(species + "_") ? clip.name.Substring(species.Length + 1) : clip.name.Substring(clip.name.LastIndexOf('_') + 1);
                list.Add((action, clip));
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
            return list;
        }

        static AnimatorController BuildController(string species, List<(string action, AnimationClip clip)> clips)
        {
            EnsureFolder(AnimationRoot);
            string path = $"{AnimationRoot}/AC_{species}.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var machine = controller.layers[0].stateMachine;
            foreach (var s in machine.states) machine.RemoveState(s.state);
            AnimatorState defaultState = null;
            foreach (var (action, clip) in clips)
            {
                var state = machine.AddState(action);
                state.motion = clip;
                state.writeDefaultValues = true;
            }
            foreach (var preferred in new[] { "Idle", "Swim", "Fly", "Hang", "Pulse" })
            {
                foreach (var s in machine.states)
                {
                    if (s.state.name == preferred)
                    {
                        defaultState = s.state;
                        break;
                    }
                }
                if (defaultState != null) break;
            }
            if (defaultState == null && machine.states.Length > 0) defaultState = machine.states[0].state;
            machine.defaultState = defaultState;
            EditorUtility.SetDirty(controller);
            return controller;
        }

        static Bounds LocalBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            var b = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (var r in renderers)
            {
                var rb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? rb.min.x : rb.max.x, (i & 2) == 0 ? rb.min.y : rb.max.y, (i & 4) == 0 ? rb.min.z : rb.max.z);
                    var local = root.transform.InverseTransformPoint(corner);
                    if (first)
                    {
                        b = new Bounds(local, Vector3.zero);
                        first = false;
                    }
                    else b.Encapsulate(local);
                }
            }
            return b;
        }

        static void ConfigureNetworkTransform(NetworkTransform nt)
        {
            var so = new SerializedObject(nt);
            void Set(string n, bool v) { var p = so.FindProperty(n); if (p != null) p.boolValue = v; }
            void SetF(string n, float v) { var p = so.FindProperty(n); if (p != null) p.floatValue = v; }
            var mode = so.FindProperty("AuthorityMode");
            if (mode != null) mode.enumValueIndex = 0; // 서버
            Set("SyncPositionX", true); Set("SyncPositionY", true); Set("SyncPositionZ", true);
            Set("SyncRotAngleX", false); Set("SyncRotAngleY", true); Set("SyncRotAngleZ", false);
            Set("SyncScaleX", false); Set("SyncScaleY", false); Set("SyncScaleZ", false);
            Set("Interpolate", true); Set("InLocalSpace", false);
            SetF("PositionThreshold", 0.01f); SetF("RotAngleThreshold", 1f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject BuildAnimalPrefab(string species, string folder, GameObject model, WildInfo source, AnimatorController controller,
            List<(string action, AnimationClip clip)> clips, ItemCatalog catalog, WorldItem worldItem)
        {
            string dir = $"{PrefabRoot}/{folder}";
            EnsureFolder(dir);
            string path = $"{dir}/Animal_{species}.prefab";
            var root = new GameObject("Animal_" + species);
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                instance.name = "Model";
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                foreach (var extra in instance.GetComponentsInChildren<WildInfo>(true)) Object.DestroyImmediate(extra);
                var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                SetLayer(root, AnimalLayer);

                var info = root.AddComponent<WildInfo>();
                info.CopyFrom(source);

                // 맞는 판정용 트리거 (몸 크기)
                var b = LocalBounds(root);
                float width = Mathf.Max(b.size.x, 0.12f);
                float length = Mathf.Max(b.size.z, 0.12f);
                float height = Mathf.Max(b.size.y, 0.08f);
                if (length > width * 1.4f && length > height * 1.2f)
                {
                    var capsule = root.AddComponent<CapsuleCollider>();
                    capsule.direction = 2;
                    capsule.center = b.center;
                    capsule.radius = Mathf.Max(0.12f, Mathf.Min(width, height) * 0.55f);
                    capsule.height = Mathf.Max(length, capsule.radius * 2f);
                    capsule.isTrigger = true;
                }
                else
                {
                    var sphere = root.AddComponent<SphereCollider>();
                    sphere.center = b.center;
                    sphere.radius = Mathf.Max(0.15f, Mathf.Max(width, length, height) * 0.5f);
                    sphere.isTrigger = true;
                }

                var networkObject = root.AddComponent<NetworkObject>();
                SetField(networkObject, "SceneMigrationSynchronization", p => p.boolValue = false);
                ConfigureNetworkTransform(root.AddComponent<NetworkTransform>());

                var animal = root.AddComponent<Animal>();
                // wild_size 가 빠진 FBX 는 몸 크기로 정한다.
                if (string.IsNullOrEmpty(info.size))
                {
                    float longest = Mathf.Max(width, length, height);
                    info.size = longest < 0.45f ? "small" : longest < 1.3f ? "medium" : longest < 3.2f ? "large" : "huge";
                }
                (float hp, string meat, int count) = AnimalOverrides.TryGetValue(species, out var o) ? o : info.SizeRank switch
                {
                    0 => (10f, "Item_Meat", 1),
                    1 => (20f, "Item_Meat", 1),
                    2 => (50f, "Item_BigMeat", 2),
                    _ => (120f, "Item_BigMeat", 3),
                };
                var so = new SerializedObject(animal);
                so.FindProperty("info").objectReferenceValue = info;
                so.FindProperty("animator").objectReferenceValue = animator;
                so.FindProperty("model").objectReferenceValue = instance.transform;
                var actions = so.FindProperty("actions");
                var lengths = so.FindProperty("actionLengths");
                actions.arraySize = clips.Count;
                lengths.arraySize = clips.Count;
                for (int i = 0; i < clips.Count; i++)
                {
                    actions.GetArrayElementAtIndex(i).stringValue = clips[i].action;
                    lengths.GetArrayElementAtIndex(i).floatValue = clips[i].clip.length;
                }
                so.FindProperty("bodyHeight").floatValue = height * 0.5f;
                so.FindProperty("bodyRadius").floatValue = Mathf.Min(width, length) * 0.5f;
                so.FindProperty("maxHealth").floatValue = hp;
                so.FindProperty("meatCount").intValue = count;
                so.FindProperty("meatItem").stringValue = meat;
                so.FindProperty("catalog").objectReferenceValue = catalog;
                so.FindProperty("worldItemPrefab").objectReferenceValue = worldItem;

                // 공격하는 동물(적대적·방어적)은 공격 설정을 단다.
                if (Combats.TryGetValue(species, out var combat))
                {
                    var aggression = root.AddComponent<AnimalAggression>();
                    aggression.preemptive = combat.preemptive;
                    aggression.territoryRadius = combat.territory;
                    aggression.damage = combat.damage;
                    aggression.attackRange = combat.range;
                    aggression.cooldown = combat.cooldown;
                    aggression.hitMoment = combat.hit;
                    aggression.attackAction = combat.attack;
                    aggression.altAttackAction = combat.alt;
                    aggression.alertAction = combat.alert;
                    so.FindProperty("aggression").objectReferenceValue = aggression;
                }
                so.FindProperty("swimDepth").floatValue = SwimDepths.TryGetValue(species, out var swimDepth) ? swimDepth : 0f;

                // 빛나는 생물: 불나방 몸, 랜턴 아귀 미끼
                if (GlowLights.TryGetValue(species, out var glow))
                {
                    Transform bone = null;
                    foreach (var t in instance.GetComponentsInChildren<Transform>(true)) if (t.name == glow.bone) bone = t;
                    if (bone != null)
                    {
                        var lightGo = new GameObject("Glow");
                        lightGo.transform.SetParent(bone, false);
                        var light = lightGo.AddComponent<Light>();
                        light.type = LightType.Point;
                        light.color = glow.color;
                        light.range = glow.range;
                        light.intensity = 2f;
                        light.shadows = LightShadows.None;
                    }
                }
                so.ApplyModifiedPropertiesWithoutUndo();

                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static GameObject BuildSchoolPrefab(string species, string folder, GameObject model, WildInfo source, AnimatorController controller, ItemCatalog catalog, WorldItem worldItem)
        {
            string dir = $"{PrefabRoot}/{folder}";
            EnsureFolder(dir);
            string path = $"{dir}/School_{species}.prefab";
            var root = new GameObject("School_" + species);
            try
            {
                root.layer = AnimalLayer;
                var info = root.AddComponent<WildInfo>();
                info.CopyFrom(source);
                var networkObject = root.AddComponent<NetworkObject>();
                SetField(networkObject, "SceneMigrationSynchronization", p => p.boolValue = false);
                ConfigureNetworkTransform(root.AddComponent<NetworkTransform>());
                var school = root.AddComponent<FishSchool>();
                var so = new SerializedObject(school);
                so.FindProperty("info").objectReferenceValue = info;
                so.FindProperty("fishModel").objectReferenceValue = model;
                so.FindProperty("controller").objectReferenceValue = controller;
                so.FindProperty("meatItem").stringValue = "Item_Fish";
                so.FindProperty("catalog").objectReferenceValue = catalog;
                so.FindProperty("worldItemPrefab").objectReferenceValue = worldItem;
                so.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ───────────────────────── 네트워크 ─────────────────────────

        static NetworkPrefabsList BuildNetworkList(WorldItem worldItem, WildlifeCatalog wildlife)
        {
            var list = LoadOrCreate<NetworkPrefabsList>(NetworkListPath);
            foreach (var existing in new List<NetworkPrefab>(list.PrefabList)) list.Remove(existing);
            list.Add(new NetworkPrefab { Prefab = worldItem.gameObject });
            foreach (var p in wildlife.Prefabs) list.Add(new NetworkPrefab { Prefab = p });
            EditorUtility.SetDirty(list);
            return list;
        }

        // 네트워크 프리팹 번호를 파일에 저장해 둔다 (복제 에디터·빌드가 같은 번호를 쓰게).
        static void FixPrefabHashes(WorldItem worldItem, WildlifeCatalog wildlife)
        {
            var all = new List<GameObject> { worldItem.gameObject };
            all.AddRange(wildlife.Prefabs);
            foreach (var prefab in all)
            {
                var no = prefab.GetComponent<NetworkObject>();
                var so = new SerializedObject(no);
                var p = so.FindProperty("GlobalObjectIdHash");
                if (p.longValue != no.PrefabIdHash)
                {
                    p.longValue = no.PrefabIdHash;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                EditorUtility.SetDirty(no);
                EditorUtility.SetDirty(prefab);
            }
            AssetDatabase.SaveAssets();
        }

        // ───────────────────────── 플레이어 ─────────────────────────

        static void SetupPlayer(ItemCatalog catalog, WorldItem worldItem, System.Text.StringBuilder log)
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var hands = root.GetComponent<PlayerHands>() ?? root.AddComponent<PlayerHands>();
                SetField(hands, "catalog", p => p.objectReferenceValue = catalog);
                SetField(hands, "worldItemPrefab", p => p.objectReferenceValue = worldItem);
                bool socket = false;
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) socket |= t.name == "Socket_Hand_R";
                if (!socket) log.Append("플레이어에 Socket_Hand_R 이 없어요\n");

                // 체력과 동료 일으키기 (쓰러진 선원 몸에 길게 누르는 상호작용)
                var health = root.GetComponent<PlayerHealth>() ?? root.AddComponent<PlayerHealth>();
                var revive = root.transform.Find("Revive");
                if (revive == null)
                {
                    revive = new GameObject("Revive").transform;
                    revive.SetParent(root.transform, false);
                    revive.localPosition = new Vector3(0f, 0.2f, 0f);
                }
                var interactable = revive.GetComponent<Interactable>() ?? revive.gameObject.AddComponent<Interactable>();
                if (revive.GetComponent<ReviveAnimation>() == null) revive.gameObject.AddComponent<ReviveAnimation>();
                var iso = new SerializedObject(interactable);
                iso.FindProperty("prompt").stringValue = "동료 일으키기";
                iso.FindProperty("radius").floatValue = 1.8f;
                iso.FindProperty("mode").enumValueIndex = (int)Interactable.InteractMode.Hold;
                iso.FindProperty("holdSeconds").floatValue = 2.5f;
                iso.FindProperty("aimHeight").floatValue = 0f;
                iso.ApplyModifiedPropertiesWithoutUndo();
                SetField(health, "reviveInteractable", p => p.objectReferenceValue = interactable);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void SetupPlayerAnimator(System.Text.StringBuilder log)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerControllerPath);
            if (controller == null)
            {
                log.Append("플레이어 애니메이터가 없어요\n");
                return;
            }
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(PlayerModelPath))
            {
                if (o is AnimationClip c && !c.name.StartsWith("__preview")) clips[c.name] = c;
            }
            var machine = controller.layers[0].stateMachine;
            AnimatorState locomotion = null;
            var existing = new HashSet<string>();
            foreach (var s in machine.states)
            {
                existing.Add(s.state.name);
                if (s.state.name == "Locomotion") locomotion = s.state;
            }
            int added = 0;
            for (int i = 0; i < PlayerAnimator.ActionStates.Length; i++)
            {
                string name = PlayerAnimator.ActionStates[i];
                if (existing.Contains(name)) continue;
                if (!clips.TryGetValue(name, out var clip))
                {
                    log.Append($"플레이어 클립이 없어요: {name}\n");
                    continue;
                }
                var state = machine.AddState(name, new Vector3(600f, 40f * (i % 30), 0f));
                state.motion = clip;
                var enter = machine.AddAnyStateTransition(state);
                enter.hasExitTime = false;
                enter.duration = 0.1f;
                enter.canTransitionToSelf = false;
                enter.AddCondition(AnimatorConditionMode.Equals, i + 1, "Action");
                if (locomotion != null)
                {
                    var exit = state.AddTransition(locomotion);
                    exit.hasExitTime = false;
                    exit.duration = 0.2f;
                    exit.AddCondition(AnimatorConditionMode.Equals, 0, "Action");
                }
                added++;
            }
            EditorUtility.SetDirty(controller);
            if (added > 0) log.Append($"플레이어 애니메이터에 동작 {added}개 추가\n");
            foreach (var loop in new[] { "Carry_Idle", "Carry_Walk" })
            {
                if (clips.TryGetValue(loop, out var c) && !c.isLooping) log.Append($"반복이 아닌 클립: {loop}\n");
            }
        }

        // ───────────────────────── 씬 ─────────────────────────

        // 숨 게이지를 복제해 그 위에 체력 게이지를 만든다 (쓰러졌을 때 안내 글자 포함).
        static void EnsureHealthUI(System.Text.StringBuilder log)
        {
            if (Object.FindAnyObjectByType<Letsail.UI.HealthUI>(FindObjectsInactive.Include) != null) return;
            var breath = Object.FindAnyObjectByType<Letsail.UI.BreathUI>(FindObjectsInactive.Include);
            if (breath == null)
            {
                log.Append("숨 게이지가 없어 체력 게이지를 못 만들었어요\n");
                return;
            }
            var bso = new SerializedObject(breath);
            var breathFill = bso.FindProperty("fill").objectReferenceValue as UnityEngine.UI.Image;
            string fillPath = breathFill != null ? AnimationUtility.CalculateTransformPath(breathFill.transform, breath.transform) : null;

            var copy = Object.Instantiate(breath.gameObject, breath.transform.parent);
            copy.name = "Health";
            Object.DestroyImmediate(copy.GetComponent<Letsail.UI.BreathUI>());
            var rect = (RectTransform)copy.transform;
            var source = (RectTransform)breath.transform;
            rect.anchoredPosition = source.anchoredPosition + new Vector2(0f, source.rect.height + 10f);

            // 쓰러졌을 때 안내
            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            labelGo.transform.SetParent(copy.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = new Vector2(0.5f, 1f);
            labelRect.anchorMax = new Vector2(0.5f, 1f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.anchoredPosition = new Vector2(0f, 6f);
            labelRect.sizeDelta = new Vector2(900f, 30f);
            var text = labelGo.GetComponent<UnityEngine.UI.Text>();
            var anyText = Object.FindAnyObjectByType<UnityEngine.UI.Text>(FindObjectsInactive.Include);
            if (anyText != null) text.font = anyText.font;
            text.fontSize = 20;
            text.alignment = TextAnchor.LowerCenter;
            text.color = new Color(1f, 0.85f, 0.8f);
            text.enabled = false;

            var ui = copy.AddComponent<Letsail.UI.HealthUI>();
            var hso = new SerializedObject(ui);
            hso.FindProperty("group").objectReferenceValue = copy.GetComponent<CanvasGroup>();
            if (fillPath != null)
            {
                var fillTransform = copy.transform.Find(fillPath);
                if (fillTransform != null) hso.FindProperty("fill").objectReferenceValue = fillTransform.GetComponent<UnityEngine.UI.Image>();
            }
            hso.FindProperty("label").objectReferenceValue = text;
            hso.ApplyModifiedPropertiesWithoutUndo();
            log.Append($"{copy.scene.name} 에 체력 게이지 추가\n");
        }

        static void SetupScenes(NetworkPrefabsList networkList, WildlifeCatalog wildlife, ItemCatalog catalog, WorldItem worldItem, System.Text.StringBuilder log)
        {
            // 로비: NetworkManager 에 프리팹 목록 등록
            var lobby = EditorSceneManager.OpenScene(LobbyScene);
            networkList = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkListPath);
            var manager = Object.FindAnyObjectByType<NetworkManager>();
            if (manager != null)
            {
                var lists = manager.NetworkConfig.Prefabs.NetworkPrefabsLists;
                if (!lists.Contains(networkList))
                {
                    lists.Add(networkList);
                    EditorUtility.SetDirty(manager);
                    EditorSceneManager.MarkSceneDirty(lobby);
                    log.Append("로비 NetworkManager 에 프리팹 목록 등록\n");
                }
                EnsureHealthUI(log);
                EditorSceneManager.MarkSceneDirty(lobby);
                EditorSceneManager.SaveScene(lobby);
            }
            else log.Append("로비에 NetworkManager 가 없어요\n");

            // 항해: 동물 스포너, 배 무기
            var voyage = EditorSceneManager.OpenScene(VoyageScene);
            // 씬을 열면 앞서 불러 둔 에셋 참조가 끊길 수 있어 다시 불러온다.
            wildlife = AssetDatabase.LoadAssetAtPath<WildlifeCatalog>($"{DataRoot}/Wildlife/WildlifeCatalog.asset");
            catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>($"{DataRoot}/Items/ItemCatalog.asset");
            worldItem = AssetDatabase.LoadAssetAtPath<GameObject>($"{ItemPrefabRoot}/WorldItem.prefab").GetComponent<WorldItem>();
            var spawner = Object.FindAnyObjectByType<WildlifeSpawner>();
            if (spawner == null)
            {
                var go = new GameObject("Wildlife");
                go.AddComponent<NetworkObject>();
                spawner = go.AddComponent<WildlifeSpawner>();
                log.Append("항해 씬에 Wildlife 추가\n");
            }
            SetField(spawner, "catalog", p => p.objectReferenceValue = wildlife);
            var armory = Object.FindAnyObjectByType<ShipArmory>();
            if (armory == null)
            {
                var go = new GameObject("ShipArmory");
                go.AddComponent<NetworkObject>();
                armory = go.AddComponent<ShipArmory>();
                log.Append("항해 씬에 ShipArmory 추가\n");
            }
            SetField(armory, "catalog", p => p.objectReferenceValue = catalog);
            SetField(armory, "worldItemPrefab", p => p.objectReferenceValue = worldItem);
            EnsureHealthUI(log);
            EnsureBossHealthUI(log);
            EditorSceneManager.MarkSceneDirty(voyage);
            EditorSceneManager.SaveScene(voyage);
            EditorSceneManager.OpenScene(LobbyScene);
        }
    }
}
