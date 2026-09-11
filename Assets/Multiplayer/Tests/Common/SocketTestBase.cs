using Mirror;
using NUnit.Framework;
using UnityEngine;

namespace Socket.Multiplayer.Tests.Support
{
    /// <summary>
    /// Minimal EditMode test harness on top of MemoryTransport (adapted from Mirror's
    /// MirrorEditModeTest). Owns the network objects a test needs and guarantees a full
    /// teardown so Mirror's global statics never leak between test cases.
    /// </summary>
    public abstract class SocketTestBase
    {
        protected GameObject holder;
        protected MemoryTransport transport;

        [SetUp]
        public virtual void SetUp()
        {
            try
            {
                holder = new GameObject("TestHolder");
                holder.SetActive(false);
                transport = holder.AddComponent<MemoryTransport>();
                holder.SetActive(true);
                Transport.active = transport;
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
        }

        [TearDown]
        public virtual void TearDown()
        {
            NetworkServer.Shutdown();
            NetworkClient.Shutdown();
            // NetworkManager.singleton is read-only in this Mirror revision; tests that
            // create a manager must clear it themselves (via reflection) if needed.
            Transport.active = null;
            Object.DestroyImmediate(holder);
        }

        /// <summary>Pumps both ends: EditMode has no frame loop, so tests drive traffic explicitly.</summary>
        protected static void ProcessMessages()
        {
            if (Transport.active == null) return;
            Transport.active.ServerEarlyUpdate();
            Transport.active.ClientEarlyUpdate();
        }

        /// <summary>Starts a server on the active MemoryTransport and waits for NetworkServer.active.</summary>
        protected void StartServer()
        {
            try
            {
                NetworkServer.Listen(1);
                Assert.IsTrue(NetworkServer.active, "Server failed to start on MemoryTransport.");
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
        }

        /// <summary>Creates a networked object under the holder and optionally spawns it server-side.</summary>
        protected T CreateNetworked<T>(bool spawn, string name = null) where T : NetworkBehaviour
        {
            var go = new GameObject(name ?? typeof(T).Name);
            go.transform.SetParent(holder.transform, false);
            go.AddComponent<NetworkIdentity>();
            var component = go.AddComponent<T>();
            InitializeIdentity(go);
            if (spawn) NetworkServer.Spawn(go);
            return component;
        }

        /// <summary>Creates a NetworkMatch object spawned with the given match id.</summary>
        protected NetworkMatch CreateNetworkMatch(System.Guid matchId, string name = "Match")
        {
            var go = new GameObject(name);
            go.transform.SetParent(holder.transform, false);
            go.AddComponent<NetworkIdentity>();
            var match = go.AddComponent<NetworkMatch>();
            InitializeIdentity(go);
            match.matchId = matchId;
            NetworkServer.Spawn(go);
            return match;
        }

        /// <summary>
        /// EditMode never runs Awake, and Mirror keeps InitializeNetworkBehaviours internal
        /// exactly for this purpose ("internal so tests can add them after creating the
        /// NetworkIdentity"). We reach it via reflection because the Mirror assembly's
        /// InternalsVisibleTo does not cover this test assembly. Must run after all
        /// NetworkBehaviour components exist and before touching state like
        /// NetworkMatch.matchId (its setter reads NetworkBehaviour.isServer -> netIdentity).
        /// </summary>
        protected static void InitializeIdentity(GameObject go)
        {
            var identity = go.GetComponent<NetworkIdentity>();
            if (identity == null) return;
            typeof(NetworkIdentity)
                .GetMethod("InitializeNetworkBehaviours", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(identity, null);
        }
    }
}
