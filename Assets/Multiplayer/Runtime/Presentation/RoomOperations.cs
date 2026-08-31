using Mirror;
using UnityEngine;

namespace Socket.Multiplayer
{
    public sealed class RoomOperations : MonoBehaviour
    {
        public void ToggleReady()
        {
            var rp = GetLocalRoomPlayer();
            if (rp != null) rp.CmdChangeReadyState(!rp.readyToBegin);
        }

        public void SetReady(bool value)
        {
            var rp = GetLocalRoomPlayer();
            if (rp != null) rp.CmdChangeReadyState(value);
        }

        public void StartGame()
        {
            var local = NetworkClient.localPlayer;
            if (local != null && local.TryGetComponent<SocketRoomPlayer>(out var rp)) rp.CmdRequestStartGame();
            else if (local != null && local.TryGetComponent<NetworkPlayer>(out var gp)) gp.CmdRequestStartGame();
        }

        public void ReturnToLobby()
        {
            // Route through the client command even for host (host is also a client in Mirror),
            // so the server-side leader/phase validation in ServerReturnToLobby is always enforced.
            var local = NetworkClient.localPlayer;
            if (local != null && local.TryGetComponent<SocketRoomPlayer>(out var rp)) rp.CmdRequestReturnToLobby();
            else if (local != null && local.TryGetComponent<NetworkPlayer>(out var gp)) gp.CmdRequestReturnToLobby();
        }

        private static SocketRoomPlayer GetLocalRoomPlayer()
        {
            return NetworkClient.localPlayer != null &&
                   NetworkClient.localPlayer.TryGetComponent<SocketRoomPlayer>(out var rp)
                ? rp
                : null;
        }
    }
}
