using System.Collections.Generic;
using UnityEngine;

namespace Socket.Multiplayer
{
    // Row container plus text formatting / error localization for the runtime UI.
    public sealed partial class MultiplayerUi : MonoBehaviour
    {
        private static RectTransform AddRow(Transform content, float height, List<GameObject> rows)
        {
            var row = CreateHBox(content, height, 6f);
            row.name = "Row";
            rows.Add(row.gameObject);
            return row;
        }

        private static string FormatBytes(float bytes)
        {
            if (bytes < 1024f) return $"{bytes:F0} B";
            if (bytes < 1024f * 1024f) return $"{bytes / 1024f:F1} KB";
            return $"{bytes / (1024f * 1024f):F1} MB";
        }

        private static string PhaseText(RoomPhase phase) =>
            phase == RoomPhase.InGame ? "对局中" : "大厅";

        private static string MatchPhaseText(MatchPhase phase)
        {
            switch (phase)
            {
                case MatchPhase.Waiting: return "等待开始";
                case MatchPhase.Active: return "对局进行中";
                case MatchPhase.Completed: return "已结束";
                default: return "已取消";
            }
        }

        private static string ResultText(GomokuResult result)
        {
            switch (result)
            {
                case GomokuResult.BlackWin: return "黑棋 X 胜";
                case GomokuResult.WhiteWin: return "白棋 O 胜";
                case GomokuResult.Draw: return "和棋";
                default: return "无";
            }
        }

        // The server keeps sending English debug text (RoomRegistry); the structured
        // MultiplayerErrorCode from M3-S.6 is what the user-facing UI switches on.
        private static string LocalizeError(string fallback, MultiplayerErrorCode code)
        {
            switch (code)
            {
                case MultiplayerErrorCode.NotRegistered: return "玩家尚未注册";
                case MultiplayerErrorCode.ProtocolMismatch: return "协议版本不兼容";
                case MultiplayerErrorCode.ConfigMismatch: return "联机配置不一致";
                case MultiplayerErrorCode.NameTaken: return "名称已被使用";
                case MultiplayerErrorCode.AlreadyInRoom: return "已在房间中";
                case MultiplayerErrorCode.RoomNotFound: return "房间不存在";
                case MultiplayerErrorCode.RoomFull: return "房间已满";
                case MultiplayerErrorCode.RoomStarted: return "房间已开始";
                case MultiplayerErrorCode.RoomNotStarted: return "房间尚未开始";
                case MultiplayerErrorCode.RoomLimitReached: return "房间数已达上限";
                case MultiplayerErrorCode.NotLeader: return "仅房主可执行该操作";
                case MultiplayerErrorCode.NotEnoughPlayers: return "人数不足";
                case MultiplayerErrorCode.NotAllReady: return "有玩家未准备";
                case MultiplayerErrorCode.AlreadyInLobby: return "房间已在大厅";
                case MultiplayerErrorCode.SpectatorLimitReached: return "观战人数已满";
                case MultiplayerErrorCode.SpectatorNotAllowed: return "该房间不允许观战";
                case MultiplayerErrorCode.SpectatorCannotReady: return "观战者不能准备";
                case MultiplayerErrorCode.ReadyOnlyInLobby: return "仅大厅阶段可准备";
                case MultiplayerErrorCode.NotInRoom: return "当前不在房间中";
                case MultiplayerErrorCode.StartFailed: return "对局启动失败";
                case MultiplayerErrorCode.RateLimited: return "操作过于频繁（已限流）";
                case MultiplayerErrorCode.UnknownOperation: return "未知操作";
            }
            return string.IsNullOrEmpty(fallback) ? "未知错误" : fallback;
        }
    }
}
