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
            foreach (var info in manager.CurrentPlayers)
            {
                var row = AddRow(playerListContent, 30f, playerRows);
                var suffix = info.isSpectator ? "观战" : info.ready ? "已准备" : "未准备";
                var self = info.displayName == localName ? "（我）" : string.Empty;
                AddRowLabel(row, $"{(info.isLeader ? "[房主] " : string.Empty)}{info.displayName}{self}  {suffix}");
            }
        }

        private void RefreshChatRows()
        {
            var chat = FindChat(manager.LocalRoomId);
            if (chat == null)
            {
                if (chatRowCount != -1) { ClearRows(chatRows); chatRowCount = -1; boundChatRoom = Guid.Empty; }
                return;
            }
            if (chat.RoomId != boundChatRoom)
            {
                boundChatRoom = chat.RoomId;
                chatRowCount = -1;
                ClearRows(chatRows);
            }
            if (chat.messages.Count == chatRowCount) return;
            ClearRows(chatRows);
            foreach (var message in chat.messages)
            {
                var row = AddRow(chatContent, 24f, chatRows);
                var label = AddRowLabel(row, $"{message.sender}: {message.body}");
                label.fontSize = 13;
            }
            chatRowCount = chat.messages.Count;
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
