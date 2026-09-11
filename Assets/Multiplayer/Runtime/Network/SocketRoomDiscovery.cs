using System;
using System.Collections.Generic;
using System.Linq;
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
            public RoomInfo[] rooms;
            // Legacy summary fields keep older discovery senders readable during rollout.
            public string roomName;
            public int playerCount;
            public int maxPlayers;
            public RoomPhase phase;
            public ushort port;
            public Uri uri;
            public IPEndPoint EndPoint { get; set; }
        }

        [Serializable]
        public struct RoomInfo
        {
            public long serverId;
            public Guid roomId;
            public string roomName;
            public string address;
            public ushort port;
            public int playerCount;
            public int maxPlayers;
            public RoomPhase phase;
        }

        [Serializable]
        public sealed class ServerEntry
        {
            public long serverId;
            public List<RoomInfo> rooms = new List<RoomInfo>();
            public float lastSeen;
        }

        [Header("Server Table")]
        [Min(0.5f)] [SerializeField] private float serverTtl = 3f;
        [Min(0.1f)] [SerializeField] private float pruneInterval = 0.5f;

        [SerializeField] private SocketRoomManager networkManager;
        [SerializeField] private string requestedRoom;
        [SerializeField] private SocketRoomFoundEvent onRoomFound = new SocketRoomFoundEvent();

        private readonly Dictionary<long, ServerEntry> _servers = new Dictionary<long, ServerEntry>();
        private readonly List<RoomInfo> _snapshot = new List<RoomInfo>();
        private float _nextPrune;
        private bool _wasServerActive;

        public event Action ServersChanged;
        public SocketRoomFoundEvent OnRoomFound => onRoomFound;
        public bool IsBrowsing => clientUdpClient != null;
        public float ServerTtl => serverTtl;

        public IReadOnlyList<RoomInfo> Servers
        {
            get
            {
                if (_snapshot.Count == 0 && _servers.Count > 0) RebuildSnapshot();
                return _snapshot;
            }
        }

        private void Awake()
        {
            if (networkManager == null) networkManager = FindFirstObjectByType<SocketRoomManager>();
            if (transport == null) transport = Transport.active;
            if (networkManager != null && networkManager.Config != null && networkManager.Config.serverTtl > 0f)
                serverTtl = networkManager.Config.serverTtl;
        }

        private void Update()
        {
            if (!Utils.IsHeadless())
            {
                var serverActive = NetworkServer.active;
                if (serverActive != _wasServerActive)
                {
                    _wasServerActive = serverActive;
                    if (serverActive)
                    {
                        try { AdvertiseServer(); }
                        catch (Exception e) { Debug.LogException(e, this); }
                    }
                    else
                    {
                        StopAdvertising();
                    }
                }
            }

            if (_servers.Count == 0 || Time.unscaledTime < _nextPrune) return;
            _nextPrune = Time.unscaledTime + pruneInterval;
            PruneExpiredServers();
        }

        protected override Request GetRequest() => new Request { requestedRoom = requestedRoom };

        protected override Response ProcessRequest(Request request, IPEndPoint endpoint)
        {
            if (networkManager == null) networkManager = FindFirstObjectByType<SocketRoomManager>();
            if (networkManager == null) return default;

            var uri = transport.ServerUri();
            var advertisedRooms = networkManager.GetDiscoveryRooms()
                .Select(room => new RoomInfo
                {
                    serverId = ServerId,
                    roomId = room.roomId,
                    roomName = room.roomName,
                    playerCount = room.playerCount,
                    maxPlayers = room.maxPlayers,
                    phase = room.phase,
                    port = uri == null || uri.Port <= 0 ? (ushort)0 : (ushort)uri.Port
                })
                .ToList();

            // A server with no created rooms is still discoverable so a client can
            // connect and create the first room.
            if (advertisedRooms.Count == 0)
            {
                advertisedRooms.Add(new RoomInfo
                {
                    serverId = ServerId,
                    roomId = Guid.Empty,
                    roomName = networkManager.Config == null ? "Socket Server" : networkManager.Config.roomName,
                    playerCount = 0,
                    maxPlayers = networkManager.Config == null ? networkManager.maxConnections : networkManager.Config.EffectiveMaxPlayers,
                    phase = RoomPhase.Lobby,
                    port = uri == null || uri.Port <= 0 ? (ushort)0 : (ushort)uri.Port
                });
            }

            return new Response
            {
                serverId = ServerId,
                rooms = advertisedRooms.ToArray(),
                roomName = advertisedRooms[0].roomName,
                playerCount = advertisedRooms.Sum(room => room.playerCount),
                maxPlayers = advertisedRooms.Sum(room => room.maxPlayers),
                phase = advertisedRooms[0].phase,
                port = advertisedRooms[0].port,
                uri = uri
            };
        }

        protected override void ProcessResponse(Response response, IPEndPoint endpoint)
        {
            response.EndPoint = endpoint;
            if (response.uri == null) return;

            var rooms = response.rooms == null || response.rooms.Length == 0
                ? new[] { new RoomInfo
                    {
                        serverId = response.serverId,
                        roomId = Guid.Empty,
                        roomName = response.roomName,
                        playerCount = response.playerCount,
                        maxPlayers = response.maxPlayers,
                        phase = response.phase
                    } }
                : response.rooms;

            if (!string.IsNullOrWhiteSpace(requestedRoom) &&
                !rooms.Any(room => string.Equals(requestedRoom, room.roomName, StringComparison.OrdinalIgnoreCase)))
                return;

            var address = endpoint.Address.ToString();
            var port = response.port > 0
                ? response.port
                : (response.uri.Port > 0 ? (ushort)response.uri.Port : (ushort)0);
            var normalizedRooms = rooms
                .Select(room =>
                {
                    room.serverId = response.serverId;
                    room.address = address;
                    room.port = port;
                    return room;
                })
                .ToList();

            var changed = false;
            if (_servers.TryGetValue(response.serverId, out var entry))
            {
                changed = !AreRoomsEqual(entry.rooms, normalizedRooms);
                entry.lastSeen = Time.unscaledTime;
                entry.rooms = normalizedRooms;
            }
            else
            {
                _servers[response.serverId] = new ServerEntry
                {
                    serverId = response.serverId,
                    rooms = normalizedRooms,
                    lastSeen = Time.unscaledTime
                };
                changed = true;
            }

            if (!changed) return;
            RebuildSnapshot();
            foreach (var room in normalizedRooms)
                onRoomFound.Invoke(room);
            ServersChanged?.Invoke();
        }

        public void StartBrowsing()
        {
            if (!SupportedOnThisPlatform) return;
            StartDiscovery();
        }

        public void StopBrowsing()
        {
            StopDiscovery();
            ClearServers();
        }

        public void ClearServers()
        {
            if (_servers.Count == 0) return;
            _servers.Clear();
            RebuildSnapshot();
            ServersChanged?.Invoke();
        }

        // NOTE (M3 P2.16): "SetRequestedRoom" was a dead API — the filter below stayed
        // server-level (a whole server is hidden when none of its rooms matches) and no
        // caller existed. Kept the field as a legacy wire slot; use it only if a room-name
        // gate gets a real UI entry point later.
        private void StopAdvertising()
        {
            if (serverUdpClient == null) return;
            try { serverUdpClient.Close(); }
            catch (Exception) { }
            serverUdpClient = null;
        }

        private void PruneExpiredServers()
        {
            var cutoff = Time.unscaledTime - serverTtl;
            var expired = _servers
                .Where(pair => pair.Value.lastSeen < cutoff)
                .Select(pair => pair.Key)
                .ToList();
            if (expired.Count == 0) return;
            foreach (var id in expired) _servers.Remove(id);
            RebuildSnapshot();
            ServersChanged?.Invoke();
        }

        private void RebuildSnapshot()
        {
            _snapshot.Clear();
            foreach (var entry in _servers.Values)
                _snapshot.AddRange(entry.rooms);
            _snapshot.Sort((left, right) => string.Compare(left.roomName, right.roomName, StringComparison.OrdinalIgnoreCase));
        }

        private static bool AreRoomsEqual(IReadOnlyList<RoomInfo> left, IReadOnlyList<RoomInfo> right)
        {
            if (left.Count != right.Count) return false;
            for (var i = 0; i < left.Count; i++)
            {
                var a = left[i];
                var b = right[i];
                if (a.roomId != b.roomId || a.roomName != b.roomName || a.address != b.address ||
                    a.port != b.port || a.playerCount != b.playerCount || a.maxPlayers != b.maxPlayers ||
                    a.phase != b.phase)
                    return false;
            }
            return true;
        }
    }
}
