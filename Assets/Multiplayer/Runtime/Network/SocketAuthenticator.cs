using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Socket.Multiplayer
{
    /// <summary>
    /// Lightweight compatibility handshake for the demo protocol.
    /// This is a version/config check, not a replacement for production account auth.
    /// </summary>
    public sealed class SocketAuthenticator : NetworkAuthenticator
    {
        private readonly HashSet<string> activeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public string playerName = "Player";
        public string LastError { get; private set; }
        public MultiplayerErrorCode LastErrorCode { get; private set; }

        public struct AuthRequestMessage : NetworkMessage
        {
            public ushort protocolVersion;
            public string configSignature;
            public string authUsername;
        }

        public struct AuthResponseMessage : NetworkMessage
        {
            public bool success;
            public string message;
            // Structured counterpart of <see cref="message"/> (M3-S.6).
            public MultiplayerErrorCode errorCode;
        }

        public override void OnStartServer()
        {
            activeNames.Clear();
            NetworkServer.RegisterHandler<AuthRequestMessage>(OnAuthRequestMessage, false);
        }

        public override void OnStopServer()
        {
            NetworkServer.UnregisterHandler<AuthRequestMessage>();
            activeNames.Clear();
        }

        public override void OnServerAuthenticate(NetworkConnectionToClient conn) { }

        public void OnAuthRequestMessage(NetworkConnectionToClient conn, AuthRequestMessage message)
        {
            if (conn == null || conn.isAuthenticated) return;

            var manager = NetworkManager.singleton as SocketRoomManager;
            var expectedSignature = MultiplayerProtocol.GetConfigSignature(manager == null ? null : manager.Config);
            if (message.protocolVersion != MultiplayerProtocol.Version)
            {
                Reject(conn, $"协议版本不兼容：客户端 {message.protocolVersion}，服务器 {MultiplayerProtocol.Version}。", MultiplayerErrorCode.ProtocolMismatch);
                return;
            }

            if (!string.Equals(message.configSignature, expectedSignature, StringComparison.Ordinal))
            {
                Reject(conn, "联机配置不一致，请使用同一份房间模板和配置资源。", MultiplayerErrorCode.ConfigMismatch);
                return;
            }

            var name = SanitizeName(message.authUsername);
            if (activeNames.Contains(name))
            {
                Reject(conn, "玩家名称已被使用，请更换名称。", MultiplayerErrorCode.NameTaken);
                return;
            }

            activeNames.Add(name);
            conn.authenticationData = name;
            conn.Send(new AuthResponseMessage { success = true, message = "认证成功" });
            ServerAccept(conn);
        }

        public void ReleaseName(string name)
        {
            if (!string.IsNullOrWhiteSpace(name)) activeNames.Remove(name.Trim());
        }

        public override void OnStartClient()
        {
            LastError = string.Empty;
            LastErrorCode = MultiplayerErrorCode.None;
            NetworkClient.RegisterHandler<AuthResponseMessage>(OnAuthResponseMessage, false);
        }

        public override void OnStopClient()
        {
            NetworkClient.UnregisterHandler<AuthResponseMessage>();
        }

        public override void OnClientAuthenticate()
        {
            var manager = NetworkManager.singleton as SocketRoomManager;
            NetworkClient.Send(new AuthRequestMessage
            {
                protocolVersion = MultiplayerProtocol.Version,
                configSignature = MultiplayerProtocol.GetConfigSignature(manager == null ? null : manager.Config),
                authUsername = playerName
            });
        }

        public void OnAuthResponseMessage(AuthResponseMessage message)
        {
            if (message.success)
            {
                LastError = string.Empty;
                LastErrorCode = MultiplayerErrorCode.None;
                ClientAccept();
            }
            else
            {
                LastError = message.message ?? "认证失败";
                LastErrorCode = message.errorCode;
                UnityEngine.Debug.LogError($"多人联机认证失败：{LastError} ({LastErrorCode})");
                ClientReject();
            }
        }

        private void Reject(NetworkConnectionToClient conn, string message, MultiplayerErrorCode errorCode)
        {
            conn.isAuthenticated = false;
            conn.Send(new AuthResponseMessage { success = false, message = message, errorCode = errorCode });
            StartCoroutine(RejectAfterDelay(conn));
        }

        private IEnumerator RejectAfterDelay(NetworkConnectionToClient conn)
        {
            yield return new WaitForSeconds(0.5f);
            if (conn != null) ServerReject(conn);
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Player";
            value = value.Trim();
            return value.Length <= 24 ? value : value.Substring(0, 24);
        }
    }
}
