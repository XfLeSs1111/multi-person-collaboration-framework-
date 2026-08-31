using Mirror;
using UnityEngine;

namespace Socket.Multiplayer
{
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        [SyncVar] public string displayName;
        [SyncVar] public Color32 displayColor = Color.white;
        [SyncVar] private bool leader;
        [SyncVar] private Vector2 serverInput;

        [SerializeField] private CharacterController characterController;

        private float _nextInputTime;

        public bool IsLeader => leader;

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (characterController == null) characterController = GetComponent<CharacterController>();
        }

        public override void OnStopServer()
        {
            foreach (var interactable in FindObjectsByType<NetworkInteractable>(FindObjectsSortMode.None))
                interactable.ServerReleaseById(netId);
            base.OnStopServer();
        }

        // Identity is copied server-side from the room player at game start
        // (see SocketRoomManager.OnRoomServerSceneLoadedForPlayer).
        [Server]
        public void SetIdentity(string name, Color32 color, bool isLeader)
        {
            displayName = name;
            displayColor = color;
            leader = isLeader;
        }

        private void FixedUpdate()
        {
            if (!isServer) return;
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
        public void CmdSetInput(Vector2 input)
        {
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
            var cfg = (NetworkManager.singleton as SocketRoomManager)?.Config;
            var rate = cfg == null ? 20f : cfg.inputSendRate;
            _nextInputTime = Time.unscaledTime + 1f / Mathf.Max(1f, rate);
            CmdSetInput(input);
        }
    }
}
