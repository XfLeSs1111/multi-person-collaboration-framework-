using System;
using System.Collections.Generic;
using System.Net;
using Mirror;
using Mirror.Discovery;
using UnityEngine;
using UnityEngine.Events;

namespace Socket.Multiplayer
{
    [Serializable]
    public sealed class SocketRoomFoundEvent : UnityEvent<SocketRoomDiscovery.RoomInfo> { }

    public sealed class SocketRoomDiscovery : NetworkDiscoveryBase<SocketRoomDiscovery.Request, SocketRoomDiscovery.Response>
    {
        [Serializable]
        public struct Request : NetworkMessage
        {
            public string requestedRoom;
        }

        [Serializable]
        public struct Response : NetworkMessage
        {
            public long serverId;
            public string roomName;
            public int playerCount;
            public int maxPlayers;
            public RoomPhase phase;
            public Uri uri;
            public IPEndPoint EndPoint { get; set; }
        }

        [Serializable]
        public struct RoomInfo
        {
            public long serverId;
            public string roomName;
            public string address;
            public int playerCount;
            public int maxPlayers;
            public RoomPhase phase;
        }

        [SerializeField] private SocketNetworkManager networkManager;
        [SerializeField] private string requestedRoom;
        [SerializeField] private SocketRoomFoundEvent onRoomFound = new SocketRoomFoundEvent();
        private readonly Dictionary<long, string> _lastResponses = new Dictionary<long, string>();

        public SocketRoomFoundEvent OnRoomFound => onRoomFound;

        private void Awake()
        {
            if (networkManager == null) networkManager = FindFirstObjectByType<SocketNetworkManager>();
            if (transport == null) transport = Transport.active;
        }

        protected override Request GetRequest() => new Request { requestedRoom = requestedRoom };

        protected override Response ProcessRequest(Request request, IPEndPoint endpoint)
        {
            if (networkManager == null) networkManager = FindFirstObjectByType<SocketNetworkManager>();
            if (networkManager == null) return default;
            return new Response
            {
                serverId = ServerId,
                roomName = networkManager.RoomName,
                playerCount = networkManager.ConnectedPlayerCount,
                maxPlayers = networkManager.Config == null ? networkManager.maxConnections : networkManager.Config.maxPlayers,
                phase = networkManager.CurrentPhase,
                uri = transport.ServerUri()
            };
        }

        protected override void ProcessResponse(Response response, IPEndPoint endpoint)
        {
            response.EndPoint = endpoint;
            if (response.uri == null) return;
            if (!string.IsNullOrWhiteSpace(requestedRoom) && !string.Equals(requestedRoom, response.roomName, StringComparison.OrdinalIgnoreCase)) return;
            var fingerprint = $"{response.roomName}|{response.playerCount}|{response.maxPlayers}|{response.phase}|{endpoint.Address}";
            if (_lastResponses.TryGetValue(response.serverId, out var previous) && previous == fingerprint) return;
            _lastResponses[response.serverId] = fingerprint;
            var uri = new UriBuilder(response.uri) { Host = endpoint.Address.ToString() }.Uri;
            onRoomFound.Invoke(new RoomInfo
            {
                serverId = response.serverId,
                roomName = response.roomName,
                address = uri.Host,
                playerCount = response.playerCount,
                maxPlayers = response.maxPlayers,
                phase = response.phase
            });
        }

        public void SetRequestedRoom(string value) => requestedRoom = value;
    }
}
