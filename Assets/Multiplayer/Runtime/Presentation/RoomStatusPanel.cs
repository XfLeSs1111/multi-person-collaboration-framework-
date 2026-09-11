using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Socket.Multiplayer
{
    public sealed class RoomStatusPanel : MonoBehaviour
    {
        [SerializeField] private SocketRoomManager networkManager;
        [SerializeField] private Text roomText;
        [SerializeField] private Text playersText;
        [SerializeField] private Text phaseText;
        [SerializeField, Min(0.1f)] private float refreshInterval = 0.2f;
        private float _nextRefresh;

        private void Awake()
        {
            if (networkManager == null) networkManager = FindFirstObjectByType<SocketRoomManager>();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + refreshInterval;
            if (networkManager == null) return;
            if (roomText != null) roomText.text = networkManager.RoomName;
            if (phaseText != null) phaseText.text = networkManager.CurrentPhase.ToString();
            if (playersText == null) return;

            var builder = new StringBuilder();
            var count = networkManager.CurrentPlayers.Count;
            var max = networkManager.Config == null ? networkManager.maxConnections : networkManager.Config.EffectiveMaxPlayers;
            builder.Append(count).Append('/').Append(max);
            foreach (var player in networkManager.CurrentPlayers)
                builder.AppendLine().Append(player.isLeader ? "[Leader] " : string.Empty).Append(player.displayName).Append(player.ready ? "  Ready" : "  Not Ready");
            playersText.text = builder.ToString();
        }
    }
}
