using System;
using Mirror;
using UnityEngine;

namespace Socket.Multiplayer
{
    [RequireComponent(typeof(NetworkMatch))]
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        [SyncVar] public string displayName;
        [SyncVar] public Color32 displayColor = Color.white;
        [SyncVar] private bool leader;
        [SyncVar] private bool ready;
        [SyncVar] private bool spectator;
        [SyncVar] private string roomIdText;
        private Vector2 serverInput;
        private InputAdmission inputAdmission;
        private uint inputSequence;

        [SerializeField] private CharacterController characterController;
        private NetworkMatch _networkMatch;

        private float _nextInputTime;

        public bool IsLeader => leader;
        public bool IsReady => ready;
        public bool IsSpectator => spectator;
        public string StableId => connectionToClient == null ? string.Empty : connectionToClient.authenticationData as string;
        public Guid RoomId => Guid.TryParse(roomIdText, out var value) ? value : LobbyRoom.Id;

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (characterController == null) characterController = GetComponent<CharacterController>();
            _networkMatch = GetComponent<NetworkMatch>();
            var config = (NetworkManager.singleton as SocketRoomManager)?.Config;
            inputAdmission = new InputAdmission(config == null ? 20f : config.inputSendRate);
            if (NetworkManager.singleton is SocketRoomManager manager)
                manager.RegisterPlayer(this);
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            if (NetworkManager.singleton is SocketRoomManager manager)
                manager.ClientJoinPendingRoom();
        }

        public override void OnStopServer()
        {
            foreach (var interactable in FindObjectsByType<NetworkInteractable>(FindObjectsSortMode.None))
                interactable.ServerReleaseById(netId);
            base.OnStopServer();
        }

        [Server]
        public void ServerSetIdentity(string name, Color32 color)
        {
            displayName = name;
            displayColor = color;
        }

        [Server]
        public void ServerAssignRoom(Guid roomId, bool isLeader, bool isReady, bool isSpectator = false)
        {
            roomIdText = roomId.ToString();
            leader = isLeader;
            ready = isReady;
            spectator = isSpectator;
            if (_networkMatch == null) _networkMatch = GetComponent<NetworkMatch>();
            if (_networkMatch != null) _networkMatch.matchId = roomId;
        }

        [Server]
        public void ServerTeleport(Pose pose)
        {
            serverInput = Vector2.zero;
            var controllerWasEnabled = characterController != null && characterController.enabled;
            if (controllerWasEnabled) characterController.enabled = false;
            transform.SetPositionAndRotation(pose.position, pose.rotation);
            if (controllerWasEnabled) characterController.enabled = true;
        }

        private void FixedUpdate()
        {
            if (!isServer || spectator) return;
            var cfg = (NetworkManager.singleton as SocketRoomManager)?.Config;
            if (cfg == null) return;

            var movement = new Vector3(serverInput.x, 0f, serverInput.y);
            if (movement.sqrMagnitude > 1f) movement.Normalize();
            var delta = movement * cfg.moveSpeed * Time.fixedDeltaTime;
            if (characterController != null && characterController.enabled)
                characterController.Move(delta);
            else
                transform.position += delta;
        }

        [Command]
        public void CmdSetInput(uint sequence, Vector2 input)
        {
            if (!isServer) return;
            if (inputAdmission == null)
            {
                var config = (NetworkManager.singleton as SocketRoomManager)?.Config;
                inputAdmission = new InputAdmission(config == null ? 20f : config.inputSendRate);
            }
            if (!inputAdmission.TryAccept(sequence, Time.unscaledTime)) return;
            if (input.sqrMagnitude > 1f) input.Normalize();
            serverInput = input;
        }

        [Command]
        public void CmdRequestStartGame()
        {
            if (NetworkManager.singleton is SocketRoomManager manager)
                manager.TryStartGame(connectionToClient);
        }

        [Command]
        public void CmdRequestReturnToLobby()
        {
            if (NetworkManager.singleton is SocketRoomManager manager)
                manager.ServerReturnToLobby(connectionToClient);
        }

        [Server]
        public void ServerSetReady(bool value) => ready = value;

        [Command]
        public void CmdRequestInteract(NetworkInteractable target)
        {
            if (spectator) return;
            if (target != null) target.ServerTryAcquire(this);
        }

        [Command]
        public void CmdReleaseInteract(NetworkInteractable target)
        {
            if (target != null) target.ServerRelease(this);
        }

        public void SubmitInput(Vector2 input)
        {
            if (!isLocalPlayer || !isClient || spectator) return;
            if (Time.unscaledTime < _nextInputTime) return;
            var cfg = (NetworkManager.singleton as SocketRoomManager)?.Config;
            var rate = cfg == null ? 20f : cfg.inputSendRate;
            _nextInputTime = Time.unscaledTime + 1f / Mathf.Max(1f, rate);
            inputSequence++;
            CmdSetInput(inputSequence, input);
        }
    }
}
