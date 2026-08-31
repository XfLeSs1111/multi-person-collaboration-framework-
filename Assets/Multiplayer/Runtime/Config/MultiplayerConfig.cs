using UnityEngine;

namespace Socket.Multiplayer
{
    public enum NetworkStartMode
    {
        Manual,
        Host,
        Client,
        Server
    }

    [CreateAssetMenu(menuName = "Socket/Multiplayer Config", fileName = "MultiplayerConfig")]
    public sealed class MultiplayerConfig : ScriptableObject
    {
        [Header("Session")]
        [Min(1)] public int maxPlayers = 4;
        [Min(1)] public int minPlayers = 1;
        [Min(1)] public ushort port = 7777;
        [Min(1)] public int sendRate = 30;
        public NetworkStartMode defaultStartMode = NetworkStartMode.Manual;
        public string defaultAddress = "localhost";
        public string defaultPlayerName = "Player";
        public string roomName = "Socket Room";
        public string offlineScene = "Bootstrap";
        public string lobbyScene = "SampleScene";
        public string gameplayScene = "SampleScene";
        public bool autoStartWhenAllReady;
        public bool allowLateJoiners;

        [Header("Player")]
        [Min(0.1f)] public float moveSpeed = 4f;
        [Min(1f)] public float inputSendRate = 20f;
        public float spawnSpacing = 2f;

        [Header("Interaction")]
        [Min(0.1f)] public float interactionRange = 3f;
        [Min(0.1f)] public float interactionLeaseSeconds = 10f;
        [Tooltip("Disable an interactable after the first accepted use.")]
        public bool interactablesToggleOffAfterUse;

        private void OnValidate()
        {
            maxPlayers = Mathf.Clamp(maxPlayers, 1, 128);
            minPlayers = Mathf.Clamp(minPlayers, 1, maxPlayers);
            inputSendRate = Mathf.Clamp(inputSendRate, 1f, 60f);
            sendRate = Mathf.Clamp(sendRate, 1, 120);
            moveSpeed = Mathf.Max(0.1f, moveSpeed);
            interactionRange = Mathf.Max(0.1f, interactionRange);
            interactionLeaseSeconds = Mathf.Max(0.1f, interactionLeaseSeconds);
        }
    }
}
