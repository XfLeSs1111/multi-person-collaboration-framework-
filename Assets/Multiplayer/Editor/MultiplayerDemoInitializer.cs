#if UNITY_EDITOR
using System.Collections.Generic;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Socket.Multiplayer.Editor
{
    public static class MultiplayerDemoInitializer
    {
        private const string Root = "Assets/MultiplayerGenerated";
        private const string PrefabRoot = Root + "/Prefabs";
        private const string SceneRoot = Root + "/Scenes";
        private const string ConfigPath = Root + "/MultiplayerConfig.asset";
        private const string RoomTemplatePath = Root + "/DefaultRoomTemplate.asset";
        private const string GomokuConfigPath = Root + "/GomokuRuleConfig.asset";

        [MenuItem("Socket/Multiplayer/Initialize PC Demo")]
        public static void Initialize()
        {
            EnsureFolder(Root);
            EnsureFolder(PrefabRoot);
            EnsureFolder(SceneRoot);

            var config = CreateConfig();
            var playerPrefab = CreatePlayerPrefab();
            var interactablePrefab = CreateInteractablePrefab(config);
            var gomokuRules = CreateGomokuRuleConfig();
            config.defaultRoomTemplate = CreateRoomTemplate(interactablePrefab.GetComponent<NetworkInteractable>(), gomokuRules);
            EditorUtility.SetDirty(config);
            var roomStatePrefab = CreateSimpleNetworkPrefab<NetworkRoomState>("NetworkRoomState");
            var roomChatPrefab = CreateSimpleNetworkPrefab<NetworkRoomChat>("NetworkRoomChat");
            var gomokuMatchPrefab = CreateSimpleNetworkPrefab<NetworkGomokuMatch>("NetworkGomokuMatch");

            var bootstrap = CreateBootstrapScene(config, playerPrefab, interactablePrefab, roomStatePrefab, roomChatPrefab, gomokuMatchPrefab);
            var lobby = CreateLobbyScene();
            ConfigureBuildSettings(bootstrap, lobby);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            // Bootstrap is the playable entry scene: it holds the DontDestroyOnLoad
            // SocketRoomManager + HUD, and Host switches into the Lobby (onlineScene).
            EditorSceneManager.OpenScene(bootstrap, OpenSceneMode.Single);
            Debug.Log("Socket PC multiplayer demo initialized. Open Assets/MultiplayerGenerated/Scenes/Bootstrap.unity and press Play, then click Host.");
        }

        // Regenerate deletes the generated root and rebuilds everything from current code.
        // Initialize() itself stays idempotent ("skip if exists") so manual edits to generated
        // assets are not clobbered by a plain re-run; use Regenerate only when the generator changed.
        [MenuItem("Socket/Multiplayer/Regenerate Demo")]
        public static void Regenerate()
        {
            if (!EditorUtility.DisplayDialog(
                    "Regenerate Demo",
                    $"This will delete {Root} and rebuild config, prefabs and scenes from current code.\n\nContinue?",
                    "Yes, Regenerate",
                    "Cancel"))
                return;

            AssetDatabase.DeleteAsset(Root);
            Initialize();
        }

        // Headless entry point for CI / batchmode rebuild of the generated demo tree.
        // (Regenerate() shows a confirmation dialog which auto-cancels under -batchmode.)
        public static void RunRegenerateBatch()
        {
            AssetDatabase.DeleteAsset(Root);
            Initialize();
            EditorApplication.Exit(0);
        }

        private static MultiplayerConfig CreateConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<MultiplayerConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<MultiplayerConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            config.minPlayers = 1;
            config.maxPlayers = 8;
            config.maxRooms = 16;
            config.maxServerPlayers = 64;
            config.maxSpectators = 20;
            config.port = 7777;
            config.sendRate = 30;
            config.defaultStartMode = NetworkStartMode.Manual;
            config.defaultAddress = "localhost";
            config.defaultPlayerName = "Player";
            config.roomName = "Socket PC Room";
            config.offlineScene = "Assets/MultiplayerGenerated/Scenes/Bootstrap.unity";
            config.lobbyScene = "Assets/MultiplayerGenerated/Scenes/Lobby.unity";
            config.autoStartWhenAllReady = false;
            config.allowLateJoiners = false;
            config.roomCommandPerSecond = 2f;
            config.chatPerSecond = 2f;
            config.interactPerSecond = 2f;
            config.gameCommandPerSecond = 4f;
            config.useLegacyImGuiHud = false;
            config.roomIdleTimeout = 120f;
            EditorUtility.SetDirty(config);
            return config;
        }

        private static GameObject CreatePlayerPrefab()
        {
            var path = PrefabRoot + "/Player.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var root = new GameObject("Player");
            root.AddComponent<NetworkIdentity>();
            root.AddComponent<NetworkMatch>();
            root.AddComponent<NetworkPlayer>();
            root.AddComponent<LocalPlayerInput>();
            var transformSync = root.AddComponent<NetworkTransformUnreliable>();
            var transformSerialized = new SerializedObject(transformSync);
            transformSerialized.FindProperty("syncDirection").enumValueIndex = 0;
            transformSerialized.ApplyModifiedPropertiesWithoutUndo();
            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;

            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = Vector3.up * 0.9f;
            visual.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());

            var player = root.GetComponent<NetworkPlayer>();
            var playerSerialized = new SerializedObject(player);
            playerSerialized.FindProperty("characterController").objectReferenceValue = controller;
            playerSerialized.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static GameObject CreateInteractablePrefab(MultiplayerConfig config)
        {
            var path = PrefabRoot + "/Interactable.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "Interactable";
            root.transform.localScale = Vector3.one * 0.8f;
            root.AddComponent<NetworkIdentity>();
            root.AddComponent<NetworkMatch>();
            var interactable = root.AddComponent<NetworkInteractable>();
            var serialized = new SerializedObject(interactable);
            serialized.FindProperty("config").objectReferenceValue = config;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static GomokuRuleConfig CreateGomokuRuleConfig()
        {
            var rules = AssetDatabase.LoadAssetAtPath<GomokuRuleConfig>(GomokuConfigPath);
            if (rules == null)
            {
                rules = ScriptableObject.CreateInstance<GomokuRuleConfig>();
                AssetDatabase.CreateAsset(rules, GomokuConfigPath);
            }

            rules.boardSize = 15;
            rules.winLength = 5;
            rules.allowDraw = true;
            rules.allowSpectators = true;
            rules.enableReplay = true;
            EditorUtility.SetDirty(rules);
            return rules;
        }

        private static RoomTemplate CreateRoomTemplate(NetworkInteractable interactablePrefab, GomokuRuleConfig gomokuRules)
        {
            var template = AssetDatabase.LoadAssetAtPath<RoomTemplate>(RoomTemplatePath);
            if (template == null)
            {
                template = ScriptableObject.CreateInstance<RoomTemplate>();
                AssetDatabase.CreateAsset(template, RoomTemplatePath);
            }

            template.templateName = "默认房间";
            template.description = "标准 8 人联机房间";
            template.minPlayers = 1;
            template.maxPlayers = 8;
            template.maxSpectators = 20;
            template.gomokuRules = gomokuRules;
            if (template.interactables != null)
                foreach (var spawn in template.interactables)
                    if (spawn != null && spawn.prefab == null)
                        spawn.prefab = interactablePrefab;
            EditorUtility.SetDirty(template);
            return template;
        }

        private static GameObject CreateSimpleNetworkPrefab<T>(string name) where T : Component
        {
            var path = PrefabRoot + "/" + name + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var root = new GameObject(name);
            root.AddComponent<NetworkIdentity>();
            root.AddComponent<NetworkMatch>();
            root.AddComponent<T>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static string CreateBootstrapScene(MultiplayerConfig config, GameObject player, GameObject interactable, GameObject state, GameObject chat, GameObject gomokuMatch)
        {
            var path = SceneRoot + "/Bootstrap.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // The offline scene keeps its own camera and light: the IMGUI HUD is the only UI
            // before a session starts (and after returning offline), so the game view must
            // not sit in "no cameras rendering". Lobby gets the same pair via its default setup.
            var bootstrapCamera = new GameObject("Main Camera");
            bootstrapCamera.tag = "MainCamera";
            bootstrapCamera.AddComponent<Camera>();
            bootstrapCamera.AddComponent<AudioListener>();
            var bootstrapLight = new GameObject("Directional Light");
            var lightComponent = bootstrapLight.AddComponent<Light>();
            lightComponent.type = LightType.Directional;
            bootstrapLight.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var managerObject = new GameObject("SocketRoomManager");
            var manager = managerObject.AddComponent<SocketRoomManager>();
            managerObject.AddComponent<kcp2k.KcpTransport>();
            managerObject.AddComponent<NetworkStartup>();
            managerObject.AddComponent<SocketAuthenticator>();
            managerObject.AddComponent<SessionOperations>();
            managerObject.AddComponent<PcRoomHud>();
            // The visual uGUI front-end (M2-V): built at runtime by the component so the
            // generator does not have to persist a widget hierarchy inside the scene.
            managerObject.AddComponent<MultiplayerUi>();
            // RoomOperations must live on the persistent manager object: the HUD wakes
            // before Lobby objects exist, and the single online scene keeps room
            // operations owned by SocketRoomManager.
            managerObject.AddComponent<RoomOperations>();
            // LAN discovery rides along on the DontDestroyOnLoad object so it survives
            // session lifetime; it advertises while a server runs and browses when the HUD
            // toggles Browse LAN.
            managerObject.AddComponent<SocketRoomDiscovery>();
            var managerSerialized = new SerializedObject(manager);
            managerSerialized.FindProperty("config").objectReferenceValue = config;
            managerSerialized.FindProperty("roomStatePrefab").objectReferenceValue = state.GetComponent<NetworkRoomState>();
            managerSerialized.FindProperty("roomChatPrefab").objectReferenceValue = chat.GetComponent<NetworkRoomChat>();
            managerSerialized.FindProperty("interactablePrefab").objectReferenceValue = interactable.GetComponent<NetworkInteractable>();
            managerSerialized.FindProperty("gomokuMatchPrefab").objectReferenceValue = gomokuMatch.GetComponent<NetworkGomokuMatch>();
            managerSerialized.ApplyModifiedPropertiesWithoutUndo();
            manager.playerPrefab = player;
            manager.spawnPrefabs = new List<GameObject> { state.gameObject, chat.gameObject, interactable, gomokuMatch };
            manager.authenticator = managerObject.GetComponent<SocketAuthenticator>();
            manager.offlineScene = config.offlineScene;
            manager.onlineScene = config.lobbyScene;
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }

        private static string CreateLobbyScene()
        {
            var path = SceneRoot + "/Lobby.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            CreateFloor();
            CreateStartPositions(4);
            // RoomOperations is NOT generated here: it lives on the DontDestroyOnLoad
            // manager object in Bootstrap so the HUD can reach it in every scene.
            // RoomStatusPanel/RoomChatPanel/SessionOperations/Canvas are also not generated:
            // the IMGUI PcRoomHud (carried by the DontDestroyOnLoad manager object) is the UI,
            // and adding empty uGUI shells produced only duplicated/stale panels in the past.
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }

        private static void CreateFloor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = Vector3.one * 5f;
        }

        private static void CreateStartPositions(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var start = new GameObject("StartPosition_" + i);
                start.AddComponent<NetworkStartPosition>();
                start.transform.position = new Vector3((i % 4) * 2f - 3f, 0f, (i / 4) * 2f);
            }
        }

        private static void ConfigureBuildSettings(params string[] scenes)
        {
            var buildScenes = new List<EditorBuildSettingsScene>();
            foreach (var scene in scenes) buildScenes.Add(new EditorBuildSettingsScene(scene, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();
        }

        private static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
