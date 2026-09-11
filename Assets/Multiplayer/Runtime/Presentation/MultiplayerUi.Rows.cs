using System;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

namespace Socket.Multiplayer
{
    // Interaction handlers and list rebuilding for the runtime UI.
    public sealed partial class MultiplayerUi : MonoBehaviour
    {
        private void OnHostClicked()
        {
            ApplySessionFields();
            StopBrowsingIfActive();
            session?.StartHost();
        }

        private void OnJoinClicked()
        {
            ApplySessionFields();
            StopBrowsingIfActive();
            session?.StartClient();
        }

        private void OnStopClicked() => session?.StopSession();

        private void OnBrowseClicked()
        {
            if (discovery == null) return;
            browsingLan = !browsingLan;
            if (browsingLan) discovery.StartBrowsing();
            else discovery.StopBrowsing();
            browseButtonText.text = browsingLan ? "停止浏览" : "浏览局域网";
        }

        private void OnRefreshLanClicked() => discovery?.BroadcastDiscoveryRequest();

        private void OnCreateRoomClicked()
        {
            if (room == null) return;
            room.CreateRoom(string.IsNullOrWhiteSpace(createRoomInput.text) ? "Socket Room" : createRoomInput.text);
            createRoomInput.text = string.Empty;
        }

        private void OnReadyClicked() => room?.ToggleReady();

        private void OnSurrenderClicked() => FindMatch(manager.LocalRoomId)?.CmdResign();

        private void OnCancelLeaveClicked()
        {
            var local = LocalPlayer;
            if (local != null && local.IsLeader) room?.CancelRoom();
            else room?.LeaveRoom();
        }

        private void OnChatSendClicked() => SendChat();

        private void OnChatSubmitted(string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) SendChat();
        }

        private void SendChat()
        {
            var chat = FindChat(manager.LocalRoomId);
            if (chat == null || string.IsNullOrWhiteSpace(chatInput.text)) return;
            chat.CmdSend(chatInput.text);
            chatInput.text = string.Empty;
        }

        private void ApplySessionFields()
        {
            if (session == null) return;
            session.SetPlayerName(string.IsNullOrWhiteSpace(nameInput.text) ? "Player" : nameInput.text);
            session.SetServerAddress(string.IsNullOrWhiteSpace(addressInput.text) ? "localhost" : addressInput.text);
        }

        private void StopBrowsingIfActive()
        {
            if (discovery == null || !discovery.IsBrowsing) return;
            browsingLan = false;
            browseButtonText.text = "浏览局域网";
            discovery.StopBrowsing();
        }

        private void RebuildRoomRows()
        {
            ClearRows(roomRows);
            if (!(NetworkClient.active || NetworkServer.active)) return;
            if (manager.LocalRoomId != LobbyRoom.Id) return;
            foreach (var info in manager.AvailableRooms)
            {
                var row = AddRow(roomListContent, 34f, roomRows);
                AddRowLabel(row, $"{info.roomName}  {info.playerCount}/{info.maxPlayers}  观战 {info.spectatorCount}/{info.maxSpectators}  {PhaseText(info.phase)}");
                var roomId = info.roomId;
                if (info.phase == RoomPhase.InGame)
                    AddRowButton(row, "观战", 56f, () => room.SpectateRoom(roomId));
                else if (!info.isFull)
                    AddRowButton(row, "加入", 56f, () => room.JoinRoom(roomId));
            }
        }

        private void RebuildLanRows()
        {
            ClearRows(lanRows);
            if (discovery == null || NetworkClient.active || NetworkServer.active) return;
            foreach (var entry in discovery.Servers)
            {
                var row = AddRow(lanListContent, 34f, lanRows);
                AddRowLabel(row, $"{entry.roomName}  {entry.playerCount}/{entry.maxPlayers}  {PhaseText(entry.phase)}  {entry.address}:{entry.port}");
                var target = entry;
                AddRowButton(row, "加入", 56f, () =>
                {
                    StopBrowsingIfActive();
                    ApplySessionFields();
                    session?.JoinRoom(target);
                });
            }
        }

        private void RebuildPlayerRows()
        {
            ClearRows(playerRows);
            if (!(NetworkClient.active || NetworkServer.active)) return;
            var localName = LocalPlayer == null ? string.Empty : LocalPlayer.displayName;
            var match = FindMatch(manager.LocalRoomId);
            foreach (var info in manager.CurrentPlayers)
            {
                var row = AddRow(playerListContent, 30f, playerRows);
                var seat = match == null ? -1 : match.SeatOf(info.displayName);
                // Tell 对战 (with the game's own seat label) apart from 观战, so a 3rd member
                // in a duel room is obviously watching instead of "playing without a turn".
                var suffix = info.isSpectator ? "观战席" : info.ready ? "已准备" : "未准备";
                var role = match == null || info.isSpectator
                    ? string.Empty
                    : $" · {(seat >= 0 ? match.DescribeSeat(seat) : "观战")}";
                var self = info.displayName == localName ? "（我）" : string.Empty;
                AddRowLabel(row, $"{(info.isLeader ? "[房主] " : string.Empty)}{info.displayName}{self}  {suffix}{role}");
            }
        }

        private void RefreshChatRows()
        {
            var chat = FindChat(manager.LocalRoomId);
            if (chat == null)
            {
                if (!string.IsNullOrEmpty(chatSignature)) { ClearRows(chatRows); chatSignature = string.Empty; boundChatRoom = Guid.Empty; }
                return;
            }
            if (chat.RoomId != boundChatRoom)
            {
                boundChatRoom = chat.RoomId;
                chatSignature = string.Empty;
                ClearRows(chatRows);
            }
            // Length alone is not a safe signature: once the server reaches
            // maxMessages it trims the oldest line, so a new message keeps the same
            // count and the panel would stop refreshing. Include the newest line.
            var last = chat.messages.Count == 0 ? default : chat.messages[chat.messages.Count - 1];
            var signature = $"{chat.messages.Count}|{last.sender}|{last.body}";
            if (signature == chatSignature) return;
            ClearRows(chatRows);
            foreach (var message in chat.messages)
            {
                var row = AddRow(chatContent, 24f, chatRows);
                var label = AddRowLabel(row, $"{message.sender}: {message.body}");
                label.fontSize = 13;
            }
            chatSignature = signature;
        }

        // Records only ever append or fall off the tail, so count + newest id is enough of
        // a signature to know when the list has to be rebuilt.
        private void RefreshMatchRecordRows()
        {
            var records = manager.MatchRecords;
            var lastId = records.Count == 0 ? Guid.Empty : records[records.Count - 1].matchId;
            var signature = $"{manager.LocalRoomId}|{records.Count}|{lastId}";
            if (signature == recordSignature) return;
            recordSignature = signature;
            RebuildMatchRecordRows(records);
        }

        private void RebuildMatchRecordRows(System.Collections.Generic.IReadOnlyList<MatchRecordInfo> records)
        {
            ClearRows(matchRecordRows);
            if (records.Count == 0)
            {
                var empty = AddRow(matchRecordContent, 24f, matchRecordRows);
                AddRowLabel(empty, "暂无战绩（每局结束后自动记录）");
                return;
            }

            // Newest first: that is what a player looks for right after a game.
            for (var i = records.Count - 1; i >= 0; i--)
            {
                var record = records[i];
                var row = AddRow(matchRecordContent, 30f, matchRecordRows);
                AddRowLabel(row, $"{record.resultLabel} · {record.endLabel} · {record.seatsLabel} · {record.finishedAtLabel}");
                if (!record.HasReplay) continue;
                var captured = record;
                AddRowButton(row, "回放", 52f, () => StartReplay(captured));
            }
        }

        // Host and client can both have several room chats alive; pick the one whose
        // NetworkMatch id equals the local room (a plain first-object lookup would
        // grab another room's chat on the host).
        private static NetworkRoomChat FindChat(Guid roomId)
        {
            foreach (var chat in FindObjectsByType<NetworkRoomChat>(FindObjectsSortMode.None))
                if (chat != null && chat.RoomId == roomId)
                    return chat;
            return null;
        }
    }
}
