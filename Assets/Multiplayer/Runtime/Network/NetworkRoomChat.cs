using System;
using Mirror;
using UnityEngine;

namespace Socket.Multiplayer
{
    [Serializable]
    public struct RoomChatMessage
    {
        public string sender;
        public string body;
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkMatch))]
    public sealed class NetworkRoomChat : NetworkBehaviour
    {
        [SerializeField, Min(1)] private int maxMessages = 64;
        [SyncVar] private string roomIdText;
        public readonly SyncList<RoomChatMessage> messages = new SyncList<RoomChatMessage>();

        // NetworkMatch.matchId is a server-only field (its setter throws when the
        // server is not active), so a client can never match a chat object to its
        // room through it. Mirror the id in a SyncVar like NetworkRoomState and
        // NetworkGomokuMatch do; otherwise FindChat(localRoomId) returns null on
        // every client and room chat looks one-way (host only sees their own lines).
        public Guid RoomId => Guid.TryParse(roomIdText, out var value) ? value : Guid.Empty;

        [Server]
        public void ServerAssignRoom(Guid roomId)
        {
            roomIdText = roomId.ToString();
            GetComponent<NetworkMatch>().matchId = roomId;
        }

        [Command(requiresAuthority = false)]
        public void CmdSend(string body, NetworkConnectionToClient sender = null)
        {
            if (sender == null || sender.identity == null) return;
            if (NetworkManager.singleton is SocketRoomManager rateManager &&
                !rateManager.ServerTryConsumeRate(sender.connectionId, RateLimitKind.Chat)) return;
            body = Sanitize(body);
            if (string.IsNullOrEmpty(body)) return;

            var player = sender.identity.GetComponent<NetworkPlayer>();
            if (player == null || player.RoomId != RoomId) return;
            messages.Add(new RoomChatMessage
            {
                sender = player == null ? "Player" : player.displayName,
                body = body
            });

            while (messages.Count > maxMessages) messages.RemoveAt(0);
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            value = value.Trim();
            return value.Length <= 200 ? value : value.Substring(0, 200);
        }
    }
}
