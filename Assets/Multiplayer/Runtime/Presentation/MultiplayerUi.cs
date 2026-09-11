using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

namespace Socket.Multiplayer
{
    /// <summary>
    /// Primary visual front-end for the room stack (M2-V), built entirely at runtime.
    /// The generator rebuilds Bootstrap on every Regenerate, so a hand-authored scene
    /// UI would be wiped; building the hierarchy in code keeps the UI on the
    /// DontDestroyOnLoad manager object and survives regeneration and scene switches.
    /// </summary>
    [AddComponentMenu("Socket/多人联机界面")]
    public sealed partial class MultiplayerUi : MonoBehaviour
    {
        private SocketRoomManager manager;
        private SessionOperations session;
        private RoomOperations room;
        private SocketRoomDiscovery discovery;
        private SocketAuthenticator authenticator;

        private RectTransform sessionPanel, roomListPanel, roomPanel, boardPanel;
        private Text statusErrorText, statusMetricsText, sessionStateText, roomTitleText, phaseText, boardStatusText;
        private InputField nameInput, addressInput, createRoomInput, chatInput;
        private Button hostButton, joinButton, stopButton, browseButton, readyButton, startButton, returnButton, cancelLeaveButton;
        private Text browseButtonText, readyButtonText, cancelLeaveText;
        private RectTransform lanListContent, roomListContent, playerListContent, chatContent, boardGrid;

        private readonly List<GameObject> lanRows = new List<GameObject>();
        private readonly List<GameObject> roomRows = new List<GameObject>();
        private readonly List<GameObject> playerRows = new List<GameObject>();
        private readonly List<GameObject> chatRows = new List<GameObject>();
        private readonly List<Button> boardCells = new List<Button>();
        private readonly List<Text> boardCellLabels = new List<Text>();

        private bool roomsDirty = true, playersDirty = true, lanDirty = true, browsingLan;
        private int builtBoardSize;
        private int chatRowCount = -1;
        private Guid boundChatRoom = Guid.Empty;
        private string boardSignature = string.Empty;

        private NetworkPlayer LocalPlayer => NetworkClient.localPlayer == null
            ? null
            : NetworkClient.localPlayer.GetComponent<NetworkPlayer>();

        private void Awake()
        {
            if (Utils.IsHeadless()) { enabled = false; return; }
            manager = GetComponent<SocketRoomManager>();
            session = GetComponent<SessionOperations>();
            room = GetComponent<RoomOperations>();
            discovery = GetComponent<SocketRoomDiscovery>();
            authenticator = GetComponent<SocketAuthenticator>();
            BuildUi();
            if (manager != null)
            {
                manager.RoomStateChanged += OnRoomStateChanged;
                if (discovery != null) discovery.ServersChanged += OnServersChanged;
            }
        }

        private void OnDestroy()
        {
            if (manager != null) manager.RoomStateChanged -= OnRoomStateChanged;
            if (discovery != null) discovery.ServersChanged -= OnServersChanged;
        }

        private void OnRoomStateChanged() { roomsDirty = true; playersDirty = true; }
        private void OnServersChanged() { lanDirty = true; }

        private void Update()
        {
            if (manager == null) return;
            RefreshPanels();
            RefreshSessionWidgets();
            RefreshStatus();
            RefreshLists();
            RefreshRoomButtons();
            RefreshBoard();
        }

        private void RefreshPanels()
        {
            var connected = NetworkClient.active || NetworkServer.active;
            var inRoom = connected && manager.LocalRoomId != LobbyRoom.Id;
            roomListPanel.gameObject.SetActive(connected && !inRoom);
            roomPanel.gameObject.SetActive(inRoom);
            boardPanel.gameObject.SetActive(inRoom && manager.CurrentPhase == RoomPhase.InGame);
        }

        private void RefreshSessionWidgets()
        {
            var connected = NetworkClient.active || NetworkServer.active;
            hostButton.interactable = !connected;
            joinButton.interactable = !connected;
            stopButton.interactable = connected;
            browseButton.interactable = !connected;
        }

        private void RefreshStatus()
        {
            string state;
            if (NetworkServer.active && NetworkClient.active) state = "主机运行中";
            else if (NetworkServer.active) state = "专用服务器运行中";
            else if (NetworkClient.isConnecting) state = "连接中…";
            else if (NetworkClient.active) state = "已连接";
            else state = "未连接";
            if (!NetworkClient.active && !NetworkServer.active &&
                authenticator != null && !string.IsNullOrEmpty(authenticator.LastError))
                state = $"认证失败：{authenticator.LastError} [{authenticator.LastErrorCode}]";
            sessionStateText.text = state;

            statusErrorText.text = string.IsNullOrEmpty(manager.LastError)
                ? string.Empty
                : $"操作被拒：{LocalizeError(manager.LastError, manager.LastErrorCode)}";

            var metrics = manager.Metrics;
            statusMetricsText.text = metrics != null && (NetworkClient.active || NetworkServer.active)
                ? $"RTT {metrics.RoundTripTimeMs:F0} ms   IN {FormatBytes(metrics.IncomingBytesPerSecond)}/s   OUT {FormatBytes(metrics.OutgoingBytesPerSecond)}/s"
                : string.Empty;
        }

        private void RefreshLists()
        {
            if (roomsDirty) { roomsDirty = false; RebuildRoomRows(); }
            if (lanDirty) { lanDirty = false; RebuildLanRows(); }
            if (playersDirty) { playersDirty = false; RebuildPlayerRows(); }
            RefreshChatRows();
        }
    }
}
