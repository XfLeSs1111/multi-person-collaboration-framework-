#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Socket.Multiplayer.Editor
{
    internal sealed class MultiplayerFrameworkWindow : EditorWindow
    {
        private MultiplayerConfig _config;

        [MenuItem("Socket/Multiplayer Framework")]
        private static void Open()
        {
            GetWindow<MultiplayerFrameworkWindow>("Multiplayer Framework");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("PC Multiplayer Framework", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Configure the authored room asset, then wire the network manager and lobby controls in the scene.", MessageType.Info);
            _config = (MultiplayerConfig)EditorGUILayout.ObjectField("Multiplayer Config", _config, typeof(MultiplayerConfig), false);

            using (new EditorGUI.DisabledScope(_config == null))
            {
                if (GUILayout.Button("Select Config")) Selection.activeObject = _config;
                if (GUILayout.Button("Open Setup Guide"))
                    Application.OpenURL("https://mirror-networking.gitbook.io/docs/manual/general/getting-started");
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Scene Contract", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("1. Bootstrap: SocketRoomManager + KcpTransport + NetworkStartup");
            EditorGUILayout.LabelField("2. Player Prefab: NetworkIdentity + NetworkPlayer + LocalPlayerInput");
            EditorGUILayout.LabelField("3. Lobby Canvas: SessionOperations + RoomOperations");
            EditorGUILayout.LabelField("4. Build Settings: lobby and gameplay scenes included");
        }
    }
}
#endif
