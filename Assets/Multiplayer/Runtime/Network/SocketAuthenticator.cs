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
        /// <summary>名字占用与重连保留台账（保留期内凭一次性凭据放行原玩家）。</summary>
        private NameReservationBook book = new NameReservationBook();

        /// <summary>本客户端保存的重连凭据：断线重连时附在认证请求里（同进程内有效）。</summary>
        private string resumeToken = string.Empty;

        // 未完成握手的连接：connectionId → 连上来的时刻（P1-10 握手超时）。
        private readonly Dictionary<int, double> pendingAuthSince = new Dictionary<int, double>();
        private double nextAuthSweep;

        public string ResumeToken => resumeToken;

        public string playerName = "Player";
        public string LastError { get; private set; }
        public MultiplayerErrorCode LastErrorCode { get; private set; }

        public struct AuthRequestMessage : NetworkMessage
        {
            public ushort protocolVersion;
            public string configSignature;
            public string authUsername;
            /// <summary>断线重连凭据；新玩家为空。</summary>
            public string resumeToken;
        }

        public struct AuthResponseMessage : NetworkMessage
        {
            public bool success;
            public string message;
            // Structured counterpart of <see cref="message"/> (M3-S.6).
            public MultiplayerErrorCode errorCode;
            /// <summary>服务器签发的重连凭据（成功时才有）。</summary>
            public string resumeToken;
        }

        public override void OnStartServer()
        {
            book = new NameReservationBook();
            pendingAuthSince.Clear();
            NetworkServer.RegisterHandler<AuthRequestMessage>(OnAuthRequestMessage, false);
            NetworkServer.OnConnectedEvent += NotePendingConnection;
            NetworkServer.OnDisconnectedEvent += ForgetPendingConnection;
        }

        public override void OnStopServer()
        {
            NetworkServer.OnConnectedEvent -= NotePendingConnection;
            NetworkServer.OnDisconnectedEvent -= ForgetPendingConnection;
            NetworkServer.UnregisterHandler<AuthRequestMessage>();
            pendingAuthSince.Clear();
            book = new NameReservationBook();
        }

        private void NotePendingConnection(NetworkConnectionToClient conn)
        {
            if (conn == null) return;
            pendingAuthSince[conn.connectionId] = NetworkTime.localTime;
        }

        private void ForgetPendingConnection(NetworkConnectionToClient conn)
        {
            if (conn != null) pendingAuthSince.Remove(conn.connectionId);
        }

        /// <summary>握手超时秒数：超时未认证的连接直接断开（0 = 不限制）。</summary>
        private float AuthTimeoutSeconds
        {
            get
            {
                var manager = NetworkManager.singleton as SocketRoomManager;
                return manager != null && manager.Config != null ? manager.Config.authTimeoutSeconds : 0f;
            }
        }

        // 半开连接看门狗：只占着连接槽不发认证请求的客户端（或恶意探测）不会永久残留。
        public void Update()
        {
            if (!NetworkServer.active) return;
            var timeout = AuthTimeoutSeconds;
            if (timeout <= 0f)
            {
                if (pendingAuthSince.Count > 0) pendingAuthSince.Clear();
                return;
            }

            var now = NetworkTime.localTime;
            if (now < nextAuthSweep) return;
            nextAuthSweep = now + 1d;

            List<int> expired = null;
            foreach (var pair in pendingAuthSince)
            {
                if (now - pair.Value < timeout) continue;
                if (expired == null) expired = new List<int>();
                expired.Add(pair.Key);
            }
            if (expired == null) return;

            foreach (var id in expired)
            {
                pendingAuthSince.Remove(id);
                NetworkConnectionToClient conn;
                if (!NetworkServer.connections.TryGetValue(id, out conn) || conn == null) continue;
                conn.Send(new AuthResponseMessage
                {
                    success = false,
                    message = "认证超时：请检查网络后重试。",
                    errorCode = MultiplayerErrorCode.AuthTimeout
                });
                conn.Disconnect();
            }
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
            book.Cleanup(NetworkTime.localTime);
            var admission = book.Admit(name, message.resumeToken, NetworkTime.localTime);
            if (admission == NameAdmission.AlreadyConnected)
            {
                Reject(conn, "玩家名称已被使用，请更换名称。", MultiplayerErrorCode.NameTaken);
                return;
            }
            if (admission == NameAdmission.ReservedForOwner)
            {
                Reject(conn, "该名称正在等待原玩家重连；请使用原客户端重连，或更换名称。", MultiplayerErrorCode.NameTaken);
                return;
            }

            // 签发（并轮换）一次性重连凭据：原客户端会保存它，断线回来后凭它放行。
            var issuedToken = book.Accept(name);
            pendingAuthSince.Remove(conn.connectionId);
            conn.authenticationData = name;
            conn.Send(new AuthResponseMessage { success = true, message = "认证成功", resumeToken = issuedToken });
            ServerAccept(conn);
        }

        public void ReleaseName(string name)
        {
            if (!string.IsNullOrWhiteSpace(name)) book.Release(name.Trim());
        }

        /// <summary>
        /// Books a name while its owner is inside the reconnect window (M3 P2.19), so a
        /// new client cannot steal the seat by taking the same display name.
        /// </summary>
        public void ReserveName(string name, double until)
        {
            if (!string.IsNullOrWhiteSpace(name)) book.Reserve(name.Trim(), until);
        }

        /// <summary>Releases name reservations whose reconnect window has lapsed.</summary>
        public void CleanupNameReservations(double now) => book.Cleanup(now);

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
                authUsername = playerName,
                resumeToken = resumeToken
            });
        }

        public void OnAuthResponseMessage(AuthResponseMessage message)
        {
            if (message.success)
            {
                LastError = string.Empty;
                LastErrorCode = MultiplayerErrorCode.None;
                // 保存服务器签发的重连凭据：断线后凭它回到原座位（名字相同不足以证明身份）。
                if (!string.IsNullOrEmpty(message.resumeToken)) resumeToken = message.resumeToken;
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
