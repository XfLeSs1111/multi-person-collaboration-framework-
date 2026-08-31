using Mirror;
using UnityEngine;

namespace Socket.Multiplayer
{
    public sealed class NetworkInteractable : NetworkBehaviour
    {
        [SyncVar] private uint holderNetId;
        [SyncVar] private bool activeState = true;
        [SyncVar] private bool toggled;

        [SerializeField] private MultiplayerConfig config;
        private float _leaseExpiry;

        public uint HolderNetId => holderNetId;
        public bool IsToggled => toggled;

        [Server]
        public void ServerTryAcquire(NetworkPlayer requester)
        {
            var manager = NetworkManager.singleton as SocketNetworkManager;
            var range = manager != null && manager.Config != null ? manager.Config.interactionRange : 3f;
            var lease = manager != null && manager.Config != null ? manager.Config.interactionLeaseSeconds : 10f;
            var distance = Vector3.Distance(requester.transform.position, transform.position);
            var result = InteractionLeaseRules.TryAcquire(activeState, holderNetId, requester.netId, distance, range);
            if (!result.Accepted)
            {
                Debug.Log($"Interaction rejected: {result.Reason}");
                return;
            }

            holderNetId = requester.netId;
            toggled = !toggled;
            _leaseExpiry = Time.time + lease;
            if (manager != null && manager.Config != null && manager.Config.interactablesToggleOffAfterUse)
                activeState = false;
        }

        [Server]
        public void ServerRelease(NetworkPlayer requester)
        {
            if (requester == null) return;
            ServerReleaseById(requester.netId);
        }

        [Server]
        public void ServerReleaseById(uint requesterId)
        {
            if (!InteractionLeaseRules.CanRelease(holderNetId, requesterId)) return;
            holderNetId = 0;
            _leaseExpiry = 0f;
        }

        [Server]
        public void ServerReset()
        {
            holderNetId = 0;
            activeState = true;
            toggled = false;
            _leaseExpiry = 0f;
        }

        [ServerCallback]
        private void Update()
        {
            if (holderNetId != 0 && _leaseExpiry > 0f && Time.time >= _leaseExpiry)
                holderNetId = 0;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (config == null && NetworkManager.singleton is SocketNetworkManager manager)
                config = manager.Config;
            activeState = true;
            toggled = false;
        }
    }
}
