using System;
using System.Collections.Generic;

namespace Socket.Multiplayer
{
    /// <summary>认证准入结论。</summary>
    public enum NameAdmission
    {
        /// <summary>放行：新玩家，或持有正确重连凭据的原玩家。</summary>
        Accepted,

        /// <summary>该名字当前已有连接。</summary>
        AlreadyConnected,

        /// <summary>该名字在重连保留期内，且未提供或提供了错误凭据（冒名者）。</summary>
        ReservedForOwner
    }

    /// <summary>
    /// 名字占用与重连保留的纯逻辑台账（不依赖 Unity / Mirror，可被 dotnet 直接测试）。
    ///
    /// 设计要点：保留期内**只看名字无法区分本人与冒名者**，因此放行必须凭一次性凭据：
    /// 认证成功时签发 token，断线进入保留期时保留 token，重连时凭 token 放行并轮换；
    /// 保留期到期后 token 与保留一并失效（名字可被他人使用，旧凭据不可重放）。
    /// </summary>
    public sealed class NameReservationBook
    {
        private readonly HashSet<string> connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> reservedUntil = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        public int ConnectedCount => connected.Count;
        public int ReservationCount => reservedUntil.Count;

        /// <summary>准入判定（不修改状态）。</summary>
        public NameAdmission Admit(string name, string resumeToken, double now)
        {
            if (string.IsNullOrWhiteSpace(name)) return NameAdmission.Accepted;
            if (connected.Contains(name)) return NameAdmission.AlreadyConnected;
            if (!reservedUntil.TryGetValue(name, out var until) || until <= now) return NameAdmission.Accepted;

            return tokens.TryGetValue(name, out var expected) &&
                   !string.IsNullOrEmpty(resumeToken) &&
                   string.Equals(resumeToken, expected, StringComparison.Ordinal)
                ? NameAdmission.Accepted
                : NameAdmission.ReservedForOwner;
        }

        /// <summary>放行后登记连接：清除保留、轮换凭据并返回新 token（发回给该客户端保存）。</summary>
        public string Accept(string name)
        {
            var token = Guid.NewGuid().ToString("N");
            tokens[name] = token;
            reservedUntil.Remove(name);
            connected.Add(name);
            return token;
        }

        /// <summary>连接断开：释放“已连接”占用，但保留凭据以便本人重连。</summary>
        public void Release(string name)
        {
            if (!string.IsNullOrWhiteSpace(name)) connected.Remove(name);
        }

        /// <summary>进入重连保留期：占用名字直到 <paramref name="until"/>（凭据沿用连接时签发的那一个）。</summary>
        public void Reserve(string name, double until)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            connected.Remove(name);
            // 极小概率下没有凭据（例如保留期内服务器升级）：补发一个，虽无法被本人使用，
            // 但至少阻止他人在窗口内抢名。
            if (!tokens.ContainsKey(name)) tokens[name] = Guid.NewGuid().ToString("N");
            reservedUntil[name] = until;
        }

        /// <summary>清理过期保留（连同凭据一起失效），返回清理条数。</summary>
        public int Cleanup(double now)
        {
            List<string> stale = null;
            foreach (var pair in reservedUntil)
            {
                if (pair.Value > now) continue;
                stale = stale ?? new List<string>();
                stale.Add(pair.Key);
            }

            if (stale == null) return 0;
            foreach (var name in stale)
            {
                reservedUntil.Remove(name);
                tokens.Remove(name);
            }
            return stale.Count;
        }

        /// <summary>当前有效凭据（仅供调试与测试观察）。</summary>
        public string TokenFor(string name) =>
            !string.IsNullOrWhiteSpace(name) && tokens.TryGetValue(name, out var token) ? token : null;
    }
}
