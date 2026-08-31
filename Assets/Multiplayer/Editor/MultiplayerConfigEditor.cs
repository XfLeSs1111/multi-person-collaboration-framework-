#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Socket.Multiplayer.Editor
{
    [CustomEditor(typeof(MultiplayerConfig))]
    internal sealed class MultiplayerConfigEditor : UnityEditor.Editor
    {
        private SerializedProperty _maxPlayers;
        private SerializedProperty _minPlayers;
        private SerializedProperty _port;
        private SerializedProperty _sendRate;
        private SerializedProperty _defaultStartMode;
        private SerializedProperty _defaultAddress;
        private SerializedProperty _defaultPlayerName;
        private SerializedProperty _roomName;
        private SerializedProperty _offlineScene;
        private SerializedProperty _lobbyScene;
        private SerializedProperty _gameplayScene;
        private SerializedProperty _autoStart;
        private SerializedProperty _lateJoiners;
        private SerializedProperty _moveSpeed;
        private SerializedProperty _inputSendRate;
        private SerializedProperty _spawnSpacing;
        private SerializedProperty _interactionRange;
        private SerializedProperty _leaseSeconds;
        private SerializedProperty _toggleOff;

        private void OnEnable()
        {
            _maxPlayers = serializedObject.FindProperty("maxPlayers");
            _minPlayers = serializedObject.FindProperty("minPlayers");
            _port = serializedObject.FindProperty("port");
            _sendRate = serializedObject.FindProperty("sendRate");
            _defaultStartMode = serializedObject.FindProperty("defaultStartMode");
            _defaultAddress = serializedObject.FindProperty("defaultAddress");
            _defaultPlayerName = serializedObject.FindProperty("defaultPlayerName");
            _roomName = serializedObject.FindProperty("roomName");
            _offlineScene = serializedObject.FindProperty("offlineScene");
            _lobbyScene = serializedObject.FindProperty("lobbyScene");
            _gameplayScene = serializedObject.FindProperty("gameplayScene");
            _autoStart = serializedObject.FindProperty("autoStartWhenAllReady");
            _lateJoiners = serializedObject.FindProperty("allowLateJoiners");
            _moveSpeed = serializedObject.FindProperty("moveSpeed");
            _inputSendRate = serializedObject.FindProperty("inputSendRate");
            _spawnSpacing = serializedObject.FindProperty("spawnSpacing");
            _interactionRange = serializedObject.FindProperty("interactionRange");
            _leaseSeconds = serializedObject.FindProperty("interactionLeaseSeconds");
            _toggleOff = serializedObject.FindProperty("interactablesToggleOffAfterUse");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawSection("Room", () =>
            {
                EditorGUILayout.PropertyField(_roomName, new GUIContent("Room Name"));
                EditorGUILayout.PropertyField(_minPlayers, new GUIContent("Minimum Players"));
                EditorGUILayout.PropertyField(_maxPlayers, new GUIContent("Maximum Players"));
                EditorGUILayout.PropertyField(_autoStart, new GUIContent("Auto Start When Ready"));
                EditorGUILayout.PropertyField(_lateJoiners, new GUIContent("Allow Late Joiners"));
            });
            DrawSection("Scenes", () =>
            {
                EditorGUILayout.PropertyField(_lobbyScene, new GUIContent("Lobby Scene"));
                EditorGUILayout.PropertyField(_gameplayScene, new GUIContent("Gameplay Scene"));
                EditorGUILayout.PropertyField(_offlineScene, new GUIContent("Offline Scene"));
            });
            DrawSection("Network", () =>
            {
                EditorGUILayout.PropertyField(_port, new GUIContent("KCP Port"));
                EditorGUILayout.PropertyField(_sendRate, new GUIContent("Mirror Send Rate"));
                EditorGUILayout.PropertyField(_defaultStartMode, new GUIContent("Default Start Mode"));
                EditorGUILayout.PropertyField(_defaultAddress, new GUIContent("Default Address"));
                EditorGUILayout.PropertyField(_defaultPlayerName, new GUIContent("Default Player Name"));
            });
            DrawSection("Player", () =>
            {
                EditorGUILayout.PropertyField(_moveSpeed, new GUIContent("Move Speed"));
                EditorGUILayout.PropertyField(_inputSendRate, new GUIContent("Input Send Rate"));
                EditorGUILayout.PropertyField(_spawnSpacing, new GUIContent("Spawn Spacing"));
            });
            DrawSection("Interaction", () =>
            {
                EditorGUILayout.PropertyField(_interactionRange, new GUIContent("Interaction Range"));
                EditorGUILayout.PropertyField(_leaseSeconds, new GUIContent("Lease Seconds"));
                EditorGUILayout.PropertyField(_toggleOff, new GUIContent("Disable After Use"));
            });
            DrawValidation((MultiplayerConfig)target);
            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawSection(string title, System.Action content)
        {
            EditorGUILayout.Space(5f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) content();
        }

        private static void DrawValidation(MultiplayerConfig config)
        {
            var issues = new List<string>();
            if (string.IsNullOrWhiteSpace(config.roomName)) issues.Add("Room name is empty.");
            if (string.IsNullOrWhiteSpace(config.lobbyScene)) issues.Add("Lobby scene is not set.");
            if (string.IsNullOrWhiteSpace(config.gameplayScene)) issues.Add("Gameplay scene is not set.");
            if (string.IsNullOrWhiteSpace(config.offlineScene)) issues.Add("Offline scene is not set.");
            if (config.minPlayers > config.maxPlayers) issues.Add("Minimum players exceeds maximum players.");
            if (config.lobbyScene == config.gameplayScene) issues.Add("Lobby and gameplay scenes are the same; scene transitions will not be visible.");

            if (issues.Count == 0)
                EditorGUILayout.HelpBox("Configuration is ready for scene wiring.", MessageType.Info);
            else
                foreach (var issue in issues) EditorGUILayout.HelpBox(issue, MessageType.Warning);
        }
    }
}
#endif
