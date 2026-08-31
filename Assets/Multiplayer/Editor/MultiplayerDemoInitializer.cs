#if UNITY_EDITOR
using System.Collections.Generic;
using Mirror;
using Mirror.Authenticators;
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

        [MenuItem("Socket/Multiplayer/Initialize PC Demo")]
        public static void Initialize()
        {
            EnsureFolder(Root);
            EnsureFolder(PrefabRoot);
            EnsureFolder(SceneRoot);

            var config = CreateConfig();
            var playerPrefab = CreatePlayerPrefab();
            var roomPlayerPrefab = CreateRoomPlayerPrefab();
            var interactablePrefab = CreateInteractablePrefab(config);
            var roomStatePrefab = CreateSimpleNetworkPrefab<NetworkRoomState>("NetworkRoomState");
            var roomChatPrefab = CreateSimpleNetworkPrefab<NetworkRoomChat>("NetworkRoomChat");

            var bootstrap = CreateBootstrapScene(config, roomPlayerPrefab, playerPrefab, roomStatePrefab, roomChatPrefab);
            var lobby = CreateLobbyScene(config, interactablePrefab);
            var game = CreateGameScene(config, playerPrefab, interactablePrefab);
            ConfigureBuildSettings(bootstrap, lobby, game);

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
            config.port = 7777;
            config.sendRate = 30;
            config.defaultStartMode = NetworkStartMode.Manual;
            config.defaultAddress = "localhost";
            config.defaultPlayerName = "Player";
            config.roomName = "Socket PC Room";
            config.offlineScene = "Assets/MultiplayerGenerated/Scenes/Bootstrap.unity";
            config.lobbyScene = "Assets/MultiplayerGenerated/Scenes/Lobby.unity";
            config.gameplayScene = "Assets/MultiplayerGenerated/Scenes/Game.unity";
            config.autoStartWhenAllReady = false;
            // NetworkRoomManager's OnServerConnect kicks any connection while the active
            // scene is not the RoomScene, so late joining mid-game is not supported yet (P2).
            config.allowLateJoiners = false;
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
            root.AddComponent<NetworkPlayer>();
            root.AddComponent<LocalPlayerInput>();
            root.AddComponent<NetworkTransformUnreliable>();
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

        private static GameObject CreateRoomPlayerPrefab()
        {
            var path = PrefabRoot + "/RoomPlayer.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var root = new GameObject("RoomPlayer");
            root.AddComponent<NetworkIdentity>();
            root.AddComponent<SocketRoomPlayer>();
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
            var interactable = root.AddComponent<NetworkInteractable>();
            var serialized = new SerializedObject(interactable);
            serialized.FindProperty("config").objectReferenceValue = config;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static GameObject CreateSimpleNetworkPrefab<T>(string name) where T : Component
        {
            var path = PrefabRoot + "/" + name + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var root = new GameObject(name);
            root.AddComponent<NetworkIdentity>();
            root.AddComponent<T>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static string CreateBootstrapScene(MultiplayerConfig config, GameObject roomPlayer, GameObject player, GameObject state, GameObject chat)
        {
            var path = SceneRoot + "/Bootstrap.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var managerObject = new GameObject("SocketRoomManager");
            var manager = managerObject.AddComponent<SocketRoomManager>();
            managerObject.AddComponent<kcp2k.KcpTransport>();
            managerObject.AddComponent<NetworkStartup>();
            managerObject.AddComponent<UniqueNameAuthenticator>();
            managerObject.AddComponent<SessionOperations>();
            managerObject.AddComponent<PcRoomHud>();
            // RoomOperations must live on the DontDestroyOnLoad manager object: the HUD
            // dispatches Ready/Start/Lobby through it, and it must be reachable in both
            // the Lobby scene AND the Game scene (Lobby-return). A Lobby-scene object is
            // destroyed when the scene changes, which would silently kill the buttons.
            managerObject.AddComponent<RoomOperations>();
            var managerSerialized = new SerializedObject(manager);
            managerSerialized.FindProperty("config").objectReferenceValue = config;
            managerSerialized.FindProperty("roomStatePrefab").objectReferenceValue = state.GetComponent<NetworkRoomState>();
            managerSerialized.FindProperty("roomChatPrefab").objectReferenceValue = chat.GetComponent<NetworkRoomChat>();
            managerSerialized.ApplyModifiedPropertiesWithoutUndo();
            manager.roomPlayerPrefab = roomPlayer.GetComponent<SocketRoomPlayer>();
            manager.playerPrefab = player;
            manager.spawnPrefabs = new List<GameObject> { roomPlayer, player, state.gameObject, chat.gameObject };
            manager.authenticator = managerObject.GetComponent<UniqueNameAuthenticator>();
            manager.offlineScene = config.offlineScene;
            manager.onlineScene = config.lobbyScene;
            manager.RoomScene = config.lobbyScene;
            manager.GameplayScene = config.gameplayScene;
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }

        private static string CreateLobbyScene(MultiplayerConfig config, GameObject interactable)
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

        private static string CreateGameScene(MultiplayerConfig config, GameObject player, GameObject interactable)
        {
            var path = SceneRoot + "/Game.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            CreateFloor();
            CreateStartPositions(8);
            for (var i = 0; i < 3; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(interactable);
                instance.transform.position = new Vector3(i * 2f - 2f, 0.4f, 2f);
            }
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
