#if UNITY_EDITOR
using System;
using Mirror;
using NUnit.Framework;
using Socket.Multiplayer.Tests.Support;
using UnityEngine;

namespace Socket.Multiplayer.Tests
{
    /// <summary>
    /// Integration tests for the interaction lease on a live MemoryTransport server.
    /// EditMode never runs Update(), so the lease clock is driven explicitly through
    /// the ServerAdvanceLease / ServerTryAcquire(now) overloads (P4.5).
    /// </summary>
    public sealed class NetworkInteractableTests : SocketTestBase
    {
        [SetUp]
        public void StartTestServer() => StartServer();

        [Test]
        public void AcquireInRange_SetsHolderAndToggles()
        {
            var interactable = CreateInteractable(LobbyRoom.Id);
            var player = CreatePlayer("Alice", new Vector3(1f, 0f, 0f));

            interactable.ServerTryAcquire(player, 100f);

            Assert.AreEqual(player.netId, interactable.HolderNetId);
            Assert.IsTrue(interactable.IsToggled);
        }

        [Test]
        public void AcquireOutOfRange_IsRejected()
        {
            var interactable = CreateInteractable(LobbyRoom.Id);
            var player = CreatePlayer("Alice", new Vector3(100f, 0f, 0f));

            interactable.ServerTryAcquire(player, 100f);

            Assert.AreEqual(0u, interactable.HolderNetId);
        }

        [Test]
        public void AcquireWhileHeldByOther_IsRejectedAndHolderSurvives()
        {
            var interactable = CreateInteractable(LobbyRoom.Id);
            var first = CreatePlayer("Alice", new Vector3(1f, 0f, 0f));
            var second = CreatePlayer("Bob", new Vector3(1f, 0f, 0f));

            interactable.ServerTryAcquire(first, 100f);
            interactable.ServerTryAcquire(second, 101f);

            Assert.AreEqual(first.netId, interactable.HolderNetId);
        }

        [Test]
        public void SpectatorAndForeignRoom_AreRejected()
        {
            var interactable = CreateInteractable(LobbyRoom.Id);
            var spectator = CreatePlayer("Viewer", new Vector3(1f, 0f, 0f));
            spectator.ServerAssignRoom(LobbyRoom.Id, false, false, true);

            interactable.ServerTryAcquire(spectator, 100f);
            Assert.AreEqual(0u, interactable.HolderNetId);

            var foreign = CreateInteractable(Guid.NewGuid());
            var player = CreatePlayer("Alice", new Vector3(1f, 0f, 0f));

            foreign.ServerTryAcquire(player, 100f);
            Assert.AreEqual(0u, foreign.HolderNetId);
        }

        [Test]
        public void LeaseExpiry_ReleasesHolderAndAllowsReacquire()
        {
            var interactable = CreateInteractable(LobbyRoom.Id);
            var alice = CreatePlayer("Alice", new Vector3(1f, 0f, 0f));
            var bob = CreatePlayer("Bob", new Vector3(1f, 0f, 0f));

            interactable.ServerTryAcquire(alice, 100f); // default lease 10s -> expires at 110
            interactable.ServerAdvanceLease(109.9f);
            Assert.AreEqual(alice.netId, interactable.HolderNetId);

            interactable.ServerAdvanceLease(110f);
            Assert.AreEqual(0u, interactable.HolderNetId);

            interactable.ServerTryAcquire(bob, 111f);
            Assert.AreEqual(bob.netId, interactable.HolderNetId);
        }

        [Test]
        public void ReleaseOnlyWorksForTheHolder()
        {
            var interactable = CreateInteractable(LobbyRoom.Id);
            var alice = CreatePlayer("Alice", new Vector3(1f, 0f, 0f));
            var bob = CreatePlayer("Bob", new Vector3(1f, 0f, 0f));

            interactable.ServerTryAcquire(alice, 100f);
            interactable.ServerRelease(bob);
            Assert.AreEqual(alice.netId, interactable.HolderNetId);

            interactable.ServerRelease(alice);
            Assert.AreEqual(0u, interactable.HolderNetId);
        }

        private NetworkInteractable CreateInteractable(Guid matchId)
        {
            try
            {
                var go = new GameObject("Interactable");
                go.transform.SetParent(holder.transform, false);
                go.AddComponent<NetworkIdentity>();
                var match = go.AddComponent<NetworkMatch>();
                var interactable = go.AddComponent<NetworkInteractable>();
                InitializeIdentity(go); // EditMode: wire netIdentity before touching matchId
                match.matchId = matchId;
                NetworkServer.Spawn(go);
                return interactable;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
        }

        private NetworkPlayer CreatePlayer(string name, Vector3 position)
        {
            try
            {
                var go = new GameObject(name);
                go.transform.SetParent(holder.transform, false);
                go.transform.position = position;
                go.AddComponent<NetworkIdentity>();
                var player = go.AddComponent<NetworkPlayer>();
                InitializeIdentity(go);
                NetworkServer.Spawn(go);
                return player;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
        }
    }
}
#endif
