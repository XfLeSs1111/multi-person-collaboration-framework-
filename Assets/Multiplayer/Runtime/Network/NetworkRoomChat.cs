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
        public readonly SyncList<RoomChatMessage> messages = new SyncList<RoomChatMessage>();
        public Guid RoomId => GetComponent<NetworkMatch>().matchId;

        [Server]
        public void ServerAssignRoom(Guid roomId)
        {
            GetComponent<NetworkMatch>().matchId = roomId;
        }

        [Command(requiresAuthority = false)]
        public void CmdSend(string body, NetworkConnectionToClient sender = null)
        {
            if (sender == null || sender.identity == null) return;
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
