using Mirror;
using UnityEngine;

namespace Socket.Multiplayer
{
    public sealed class RoomOperations : MonoBehaviour
    {
        public void ToggleReady()
        {
            var player = GetLocalPlayer();
            if (player != null) player.SubmitReady(!player.IsReady);
        }

        public void SetReady(bool value)
        {
            var player = GetLocalPlayer();
            if (player != null) player.SubmitReady(value);
        }

        public void StartGame()
        {
            var player = GetLocalPlayer();
            if (player != null && player.isLocalPlayer) player.CmdRequestStartGame();
        }

        public void ReturnToLobby()
        {
            if (NetworkServer.active && NetworkManager.singleton is SocketNetworkManager manager)
                manager.ServerReturnToLobby();
            else if (NetworkClient.active && NetworkClient.localPlayer != null)
                NetworkClient.localPlayer.GetComponent<NetworkPlayer>()?.CmdRequestReturnToLobby();
        }

        private static NetworkPlayer GetLocalPlayer()
        {
            return NetworkClient.localPlayer == null ? null : NetworkClient.localPlayer.GetComponent<NetworkPlayer>();
        }
    }
}
