using System.Text;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

namespace Socket.Multiplayer
{
    public sealed class RoomStatusPanel : MonoBehaviour
    {
        [SerializeField] private SocketNetworkManager networkManager;
        [SerializeField] private Text roomText;
        [SerializeField] private Text playersText;
        [SerializeField] private Text phaseText;
        [SerializeField] private NetworkRoomState roomState;
        [SerializeField, Min(0.1f)] private float refreshInterval = 0.2f;
        private float _nextRefresh;

        private void Awake()
        {
            if (networkManager == null) networkManager = FindFirstObjectByType<SocketNetworkManager>();
            if (roomState == null) roomState = FindFirstObjectByType<NetworkRoomState>();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + refreshInterval;
            if (networkManager == null) return;
            if (roomText != null) roomText.text = roomState == null ? networkManager.RoomName : roomState.RoomName;
            if (phaseText != null) phaseText.text = (roomState == null ? networkManager.CurrentPhase : roomState.Phase).ToString();
            if (playersText == null) return;

            var players = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
            var builder = new StringBuilder();
            var count = roomState == null ? networkManager.ConnectedPlayerCount : roomState.PlayerCount;
            var max = roomState == null ? (networkManager.Config == null ? networkManager.maxConnections : networkManager.Config.maxPlayers) : roomState.MaxPlayers;
            builder.Append(count).Append('/').Append(max);
            foreach (var player in players)
                builder.AppendLine().Append(player.IsLeader ? "[Leader] " : string.Empty).Append(player.displayName).Append(player.IsReady ? "  Ready" : "  Not Ready");
            playersText.text = builder.ToString();
        }
    }
}
