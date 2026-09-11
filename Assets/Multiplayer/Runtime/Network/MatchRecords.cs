using System;
using System.Collections.Generic;

namespace Socket.Multiplayer
{
    /// <summary>
    /// Framework-level record of one finished match. Deliberately game-agnostic: the
    /// labels come from the game adapter (DescribeResult / EndDetail / DescribeSeat) and
    /// the replay blob is opaque here — only the game that wrote it reads it back.
    /// </summary>
    [Serializable]
    public struct MatchRecordInfo
    {
        public Guid matchId;
        public string roomName;
        public string finishedAtLabel;
        public string resultLabel;
        public string endLabel;
        public string seatsLabel;
        /// <summary>Game-defined move/event list; empty when the replay policy is off.</summary>
        public string replayPayload;

        public bool HasReplay => !string.IsNullOrEmpty(replayPayload);
    }

    /// <summary>
    /// Bounded per-room history, owned by the server. Retention is a configuration
    /// policy (MultiplayerConfig.matchRecordLimit, 0 = remember nothing) rather than a
    /// code constant, so "how much do we keep" stays a 配置中心 decision.
    /// </summary>
    public sealed class MatchRecordBook
    {
        private readonly Dictionary<Guid, List<MatchRecordInfo>> records = new Dictionary<Guid, List<MatchRecordInfo>>();
        private readonly int limit;

        public MatchRecordBook(int limit) => this.limit = Math.Max(0, limit);

        public bool Enabled => limit > 0;

        public int Limit => limit;

        public void Record(Guid roomId, MatchRecordInfo record)
        {
            if (!Enabled) return;
            if (!records.TryGetValue(roomId, out var list))
            {
                list = new List<MatchRecordInfo>();
                records.Add(roomId, list);
            }
            list.Add(record);
            while (list.Count > limit) list.RemoveAt(0);
        }

        public IReadOnlyList<MatchRecordInfo> Get(Guid roomId) =>
            records.TryGetValue(roomId, out var list)
                ? (IReadOnlyList<MatchRecordInfo>)list
                : Array.Empty<MatchRecordInfo>();

        public void Forget(Guid roomId) => records.Remove(roomId);

        public void Clear() => records.Clear();
    }

    /// <summary>
    /// One wording for "how did it end" — used by the live HUD and by stored records,
    /// so a result always reads the same way after the fact.
    /// </summary>
    public static class MatchRecordLabels
    {
        public static string DescribeEnd(MatchEndReason reason, MatchForfeitCause cause, string detail)
        {
            switch (reason)
            {
                case MatchEndReason.Forfeit:
                    switch (cause)
                    {
                        case MatchForfeitCause.Surrender: return "认输";
                        case MatchForfeitCause.Disconnect: return "对方掉线";
                        case MatchForfeitCause.Leave: return "对方离开房间";
                        case MatchForfeitCause.Timeout: return "超时判负";
                        default: return "判负";
                    }
                case MatchEndReason.Cancelled: return "已取消";
                default: return string.IsNullOrEmpty(detail) ? "已结束" : detail;
            }
        }
    }
}
