using Mirror;
using UnityEngine;

namespace Socket.Multiplayer
{
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        [SyncVar] public string displayName;
        [SyncVar] public Color32 displayColor = Color.white;
        [SyncVar] private bool ready;
        [SyncVar] private bool leader;
        [SyncVar] private RoomPhase phase = RoomPhase.Lobby;
        [SyncVar] private Vector2 serverInput;

        [SerializeField] private MultiplayerConfig config;
        [SerializeField] private CharacterController characterController;

        private float _nextInputTime;

        public bool IsReady => ready;
        public bool IsLeader => leader;
        public RoomPhase Phase => phase;

        public override void OnStartServer()
        {
            base.OnStartServer();
            var manager = NetworkManager.singleton as SocketNetworkManager;
            config = manager != null ? manager.Config : config;
            displayName = connectionToClient != null && connectionToClient.authenticationData is string authenticatedName
                ? authenticatedName
                : $"Player {netId}";
            displayColor = Color.HSVToRGB((netId * 0.17f) % 1f, 0.7f, 0.95f);
            if (manager != null)
            {
                phase = manager.CurrentPhase;
                manager.ServerEnsureLeader(this);
            }
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            CmdSetDisplayName(ClientSessionOptions.PlayerName);
        }

        public override void OnStopServer()
        {
            foreach (var interactable in FindObjectsByType<NetworkInteractable>(FindObjectsSortMode.None))
                interactable.ServerReleaseById(netId);
            base.OnStopServer();
        }

        private void FixedUpdate()
        {
            if (!isServer || config == null) return;

            var movement = new Vector3(serverInput.x, 0f, serverInput.y);
            if (movement.sqrMagnitude > 1f) movement.Normalize();
            var delta = movement * config.moveSpeed * Time.fixedDeltaTime;
            if (characterController != null && characterController.enabled)
                characterController.Move(delta);
            else
                transform.position += delta;
        }

        [Command]
        public void CmdSetInput(Vector2 input)
        {
            if (input.sqrMagnitude > 1f) input.Normalize();
            serverInput = input;
        }

        [Command]
        public void CmdSetReady(bool value)
        {
            if (phase != RoomPhase.Lobby) return;
            ready = value;
            var manager = NetworkManager.singleton as SocketNetworkManager;
            if (value && manager != null && manager.Config != null && manager.Config.autoStartWhenAllReady)
                manager.ServerStartGame(this);
        }

        [Command]
        public void CmdSetDisplayName(string value)
        {
            value = SanitizeName(value);
            if (string.IsNullOrEmpty(value)) return;
            displayName = value;
            if (connectionToClient != null) connectionToClient.authenticationData = value;
        }

        [Server]
        public void ServerSetReady(bool value)
        {
            ready = value;
        }

        [Server]
        public void ServerSetLeader(bool value) => leader = value;

        [Server]
        public void ServerSetPhase(RoomPhase value) => phase = value;

        [Command]
        public void CmdRequestStartGame()
        {
            if (NetworkManager.singleton is SocketNetworkManager manager)
                manager.ServerStartGame(this);
        }

        [Command]
        public void CmdRequestReturnToLobby()
        {
            if (NetworkManager.singleton is SocketNetworkManager manager)
                manager.ServerReturnToLobby();
        }

        [Command]
        public void CmdRequestInteract(NetworkInteractable target)
        {
            if (target != null) target.ServerTryAcquire(this);
        }

        [Command]
        public void CmdReleaseInteract(NetworkInteractable target)
        {
            if (target != null) target.ServerRelease(this);
        }

        public void SubmitInput(Vector2 input)
        {
            if (!isLocalPlayer || !isClient) return;
            if (Time.unscaledTime < _nextInputTime) return;
            var rate = config == null ? 20f : config.inputSendRate;
            _nextInputTime = Time.unscaledTime + 1f / Mathf.Max(1f, rate);
            CmdSetInput(input);
        }

        public void SubmitReady(bool value)
        {
            if (isLocalPlayer && isClient) CmdSetReady(value);
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            value = value.Trim();
            return value.Length <= 24 ? value : value.Substring(0, 24);
        }
    }
}
