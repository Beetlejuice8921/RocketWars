using RocketWars.Net;
using RocketWars.Player;
using RocketWars.Stats;
using RocketWars.UI;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace RocketWars.EditorTools
{
    /// <summary>Воспроизводимая настройка: префаб игрока, объекты сцены Game, интерфейс, достижения.</summary>
    public static class RocketWarsSetup
    {
        const string PrefabFolder = "Assets/_Project/Prefabs";
        const string PlayerPrefabPath = PrefabFolder + "/Player.prefab";
        const string ScenePath = "Assets/_Project/Scenes/Game.unity";
        const string UiFolder = "Assets/_Project/UI";
        const string DataFolder = "Assets/_Project/Data";

        [MenuItem("RocketWars/Setup Network (prefab + scene)")]
        public static void SetupAll()
        {
            var prefab = BuildPlayerPrefab();
            SetupScene(prefab);
            EnsureAlwaysIncludedShaders();
            Debug.Log("[RocketWars] Network setup done");
        }

        [MenuItem("RocketWars/Rebuild Player Prefab")]
        public static GameObject BuildPlayerPrefab()
        {
            if (!AssetDatabase.IsValidFolder(PrefabFolder))
                AssetDatabase.CreateFolder("Assets/_Project", "Prefabs");

            var go = new GameObject("Player");
            go.AddComponent<NetworkObject>();
            go.AddComponent<CharacterController>();
            go.AddComponent<PlayerHealth>();
            go.AddComponent<FpsController>();
            go.AddComponent<NetworkPlayer>();
            go.AddComponent<FallRespawn>();
            go.AddComponent<ReviveInteraction>();
            go.AddComponent<PlayerVisual>();

            // движение у владельца; синхронизируем позицию и поворот вокруг Y
            var nt = go.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            nt.SyncRotAngleX = false;
            nt.SyncRotAngleZ = false;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, PlayerPrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static void SetupScene(GameObject playerPrefab)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                scene = EditorSceneManager.OpenScene(ScenePath);

            var oldBootstrap = GameObject.Find("Bootstrap");
            if (oldBootstrap != null)
                Object.DestroyImmediate(oldBootstrap);

            var netGo = GameObject.Find("NetworkManager") ?? new GameObject("NetworkManager");
            var nm = GetOrAdd<NetworkManager>(netGo);
            var transport = GetOrAdd<UnityTransport>(netGo);
            // 16 игроков + рассылки взрывов: стандартной очереди в 128 пакетов мало
            transport.MaxPacketQueueSize = 512;
            EditorUtility.SetDirty(transport);
            nm.NetworkConfig ??= new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = transport;
            nm.NetworkConfig.ConnectionApproval = true;
            nm.NetworkConfig.PlayerPrefab = null;
            var bootstrap = GetOrAdd<NetworkBootstrap>(netGo);
            bootstrap.playerPrefab = playerPrefab;
            GetOrAdd<LanDiscovery>(netGo);
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(netGo); // старое IMGUI-меню
            EditorUtility.SetDirty(nm);
            EditorUtility.SetDirty(bootstrap);

            var matchGo = GameObject.Find("MatchNet") ?? new GameObject("MatchNet");
            GetOrAdd<NetworkObject>(matchGo);
            GetOrAdd<MatchNet>(matchGo);
            GetOrAdd<RocketWars.Core.MatchManager>(matchGo);
            var stats = GetOrAdd<StatsNet>(matchGo);
            stats.library = BuildAchievements();
            EditorUtility.SetDirty(stats);

            var uiGo = GameObject.Find("UI") ?? new GameObject("UI");
            var doc = GetOrAdd<UIDocument>(uiGo);
            doc.panelSettings = BuildPanelSettings();
            var uiRoot = GetOrAdd<UiRoot>(uiGo);
            uiRoot.styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UiFolder + "/RocketWars.uss");
            EditorUtility.SetDirty(doc);
            EditorUtility.SetDirty(uiRoot);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static PanelSettings BuildPanelSettings()
        {
            string path = UiFolder + "/PanelSettings.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(ps, path);
            }
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(UiFolder + "/RocketWarsTheme.tss");
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            EditorUtility.SetDirty(ps);
            AssetDatabase.SaveAssets();
            return ps;
        }

        /// <summary>Достижения матча (порядок = биты маски, менять только добавлением в конец).</summary>
        [MenuItem("RocketWars/Rebuild Achievements")]
        public static AchievementLibrary BuildAchievements()
        {
            if (!AssetDatabase.IsValidFolder(DataFolder))
                AssetDatabase.CreateFolder("Assets/_Project", "Data");
            string folder = DataFolder + "/Achievements";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(DataFolder, "Achievements");

            var defs = new (string file, string title, string desc, AchievementRule rule, Color color, string badge)[]
            {
                ("Sniper", "Снайпер", "Больше всех положил врагов за матч", AchievementRule.MostEnemiesDowned, new Color(1f, 0.45f, 0.3f), "СН"),
                ("Medic", "Медик", "Больше всех поднял союзников", AchievementRule.MostRevives, new Color(0.45f, 0.9f, 0.5f), "МД"),
                ("Sapper", "Сапёр", "Больше всех взорвал вражеских ракетных шахт", AchievementRule.MostSilosDestroyed, new Color(1f, 0.85f, 0.3f), "СП"),
                ("Lemming", "Лемминг", "Чаще всех падал с острова", AchievementRule.MostFalls, new Color(0.5f, 0.8f, 1f), "ЛМ"),
                ("FriendlyFire", "Дружеский огонь", "Положил хотя бы одного союзника", AchievementRule.AnyTeamkill, new Color(1f, 0.55f, 0.8f), "ДО"),
                ("Unbreakable", "Несгибаемый", "Ни разу не лёг за весь матч", AchievementRule.NeverDowned, new Color(0.9f, 0.9f, 0.95f), "НС"),
            };

            string libPath = DataFolder + "/AchievementLibrary.asset";
            var lib = AssetDatabase.LoadAssetAtPath<AchievementLibrary>(libPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<AchievementLibrary>();
                AssetDatabase.CreateAsset(lib, libPath);
            }
            lib.achievements.Clear();

            foreach (var d in defs)
            {
                string path = $"{folder}/{d.file}.asset";
                var a = AssetDatabase.LoadAssetAtPath<AchievementDefinition>(path);
                if (a == null)
                {
                    a = ScriptableObject.CreateInstance<AchievementDefinition>();
                    AssetDatabase.CreateAsset(a, path);
                }
                a.title = d.title;
                a.description = d.desc;
                a.rule = d.rule;
                a.color = d.color;
                a.badge = d.badge;
                EditorUtility.SetDirty(a);
                lib.achievements.Add(a);
            }

            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            return lib;
        }

        /// <summary>Шейдеры, которые код ищет через Shader.Find, — иначе их вырежет из сборки.</summary>
        [MenuItem("RocketWars/Include Runtime Shaders")]
        public static void EnsureAlwaysIncludedShaders()
        {
            string[] names = { "Universal Render Pipeline/Lit", "Sprites/Default" };
            var settings = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(settings);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            foreach (var name in names)
            {
                var shader = Shader.Find(name);
                if (shader == null)
                    continue;
                bool present = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                        present = true;
                if (present)
                    continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            }
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }
    }
}
