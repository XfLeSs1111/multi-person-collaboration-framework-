using UnityEngine;
using Mirror.Authenticators;

namespace Socket.Multiplayer
{
    public sealed class NetworkStartup : MonoBehaviour
    {
        [SerializeField] private SocketNetworkManager networkManager;
        [SerializeField] private UniqueNameAuthenticator nameAuthenticator;

        private void Start()
        {
            if (networkManager == null) networkManager = FindFirstObjectByType<SocketNetworkManager>();
            if (networkManager == null) return;

            var args = System.Environment.GetCommandLineArgs();

            if (nameAuthenticator == null)
                nameAuthenticator = FindFirstObjectByType<UniqueNameAuthenticator>();
            if (nameAuthenticator != null)
                nameAuthenticator.playerName = GetValue(args, "-name", networkManager.Config == null ? "Player" : networkManager.Config.defaultPlayerName);
            ClientSessionOptions.PlayerName = GetValue(args, "-name", networkManager.Config == null ? "Player" : networkManager.Config.defaultPlayerName);
            if (ushort.TryParse(GetValue(args, "-port", ""), out var port))
                networkManager.ApplyPort(port);
            if (HasFlag(args, "-server")) networkManager.StartServer();
            else if (HasFlag(args, "-host")) networkManager.StartHost();
            else if (HasFlag(args, "-client")) networkManager.StartClient(GetValue(args, "-client", networkManager.networkAddress));
            else networkManager.StartConfigured();
        }

        private static bool HasFlag(string[] args, string flag)
        {
            foreach (var arg in args) if (string.Equals(arg, flag, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string GetValue(string[] args, string flag, string fallback)
        {
            for (var i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], flag, System.StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return fallback;
        }
    }
}
