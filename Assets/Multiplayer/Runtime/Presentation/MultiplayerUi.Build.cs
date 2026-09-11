using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Socket.Multiplayer
{
    // Runtime-built uGUI helpers: panels, labels, buttons, inputs, scroll views
    // and the gomoku board grid. Split from MultiplayerUi.cs to keep files small.
    public sealed partial class MultiplayerUi : MonoBehaviour
    {
        private static readonly Color PanelColor = new Color(0.07f, 0.09f, 0.13f, 0.94f);
        private static readonly Color TextColor = new Color(0.92f, 0.93f, 0.96f);
        private static readonly Color MutedColor = new Color(0.62f, 0.66f, 0.74f);
        private static readonly Color ButtonColor = new Color(0.22f, 0.29f, 0.42f, 1f);
        private static readonly Color ButtonHighlight = new Color(0.32f, 0.41f, 0.58f, 1f);
        private static readonly Color ErrorColor = new Color(1f, 0.52f, 0.46f);
        private static readonly Color BoardSurfaceColor = new Color(0.88f, 0.76f, 0.55f);
        private static readonly Color BoardLineColor = new Color(0.30f, 0.20f, 0.10f, 0.85f);
        private static readonly Color BoardStarColor = new Color(0.30f, 0.20f, 0.10f, 0.95f);
        // Hover/press on an open intersection previews the stone you are about to drop.
        private static readonly Color GhostColor = new Color(1f, 0.72f, 0.20f, 0.30f);
        private static readonly Color GhostPressedColor = new Color(1f, 0.72f, 0.20f, 0.55f);
        private static readonly Color StoneBlack = new Color(0.11f, 0.11f, 0.13f);
        private static readonly Color StoneWhite = new Color(0.96f, 0.96f, 0.98f);
        private static readonly Color StoneBlackRim = new Color(0.36f, 0.37f, 0.42f);
        private static readonly Color StoneWhiteRim = new Color(0.33f, 0.33f, 0.37f);
        private static readonly Color LastMoveColor = new Color(1f, 0.72f, 0.20f);

        private Font font;

        private void BuildUi()
        {
            font = LoadFont();
            var canvasObject = new GameObject("Multiplayer UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            EnsureEventSystem();
            BuildSessionPanel(canvasObject.transform);
            BuildRoomListPanel(canvasObject.transform);
            BuildRoomPanel(canvasObject.transform);
            BuildBoardPanel(canvasObject.transform);
            BuildStatusBar(canvasObject.transform);
        }

        // EventSystem is parented to the DontDestroyOnLoad manager so UI clicks keep
        // working after Bootstrap unloads and the Lobby scene is loaded alone.
        private void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            go.transform.SetParent(transform, false);
        }

        private void BuildSessionPanel(Transform parent)
        {
            sessionPanel = CreatePanel("SessionPanel", parent, new Vector2(16f, -16f), new Vector2(380f, 404f));
            var box = CreateVBox(sessionPanel, 6f);
            CreateLabel(box, "多人联机 · 可视化测试", 17, TextAnchor.MiddleLeft, TextColor, 26f);
            nameInput = CreateInput(box, "玩家名称", 30f);
            addressInput = CreateInput(box, "服务器地址（默认本机）", 30f);
            var buttons = CreateHBox(box, 32f, 6f);
            hostButton = CreateButton(buttons, "主机", 0f, OnHostClicked);
            joinButton = CreateButton(buttons, "连接", 0f, OnJoinClicked);
            stopButton = CreateButton(buttons, "断开", 0f, OnStopClicked);
            var browseRow = CreateHBox(box, 30f, 0f);
            browseButton = CreateButton(browseRow, "浏览局域网", 0f, OnBrowseClicked);
            browseButtonText = browseButton.GetComponentInChildren<Text>();
            var lanHeader = CreateHBox(box, 22f, 6f);
            CreateLabel(lanHeader, "局域网房间", 14, TextAnchor.MiddleLeft, MutedColor, 0f);
            CreateButton(lanHeader, "刷新", 64f, OnRefreshLanClicked);
            lanListContent = CreateScrollView(box, "LanList", 120f);
            sessionStateText = CreateLabel(box, "未连接", 14, TextAnchor.MiddleLeft, MutedColor, 22f);
        }

        private void BuildRoomListPanel(Transform parent)
        {
            roomListPanel = CreatePanel("RoomListPanel", parent, new Vector2(16f, -428f), new Vector2(380f, 292f));
            var box = CreateVBox(roomListPanel, 6f);
            CreateLabel(box, "大厅 · 房间列表", 16, TextAnchor.MiddleLeft, TextColor, 24f);
            var createRow = CreateHBox(box, 30f, 6f);
            createRoomInput = CreateInput(createRow, "新房间名称", 30f);
            CreateButton(createRow, "创建", 72f, OnCreateRoomClicked);
            roomListContent = CreateScrollView(box, "RoomList", 210f);
        }

        private void BuildRoomPanel(Transform parent)
        {
            roomPanel = CreatePanel("RoomPanel", parent, new Vector2(412f, -16f), new Vector2(440f, 620f));
            var box = CreateVBox(roomPanel, 6f);
            roomTitleText = CreateLabel(box, "房间", 17, TextAnchor.MiddleLeft, TextColor, 26f);
            phaseText = CreateLabel(box, string.Empty, 14, TextAnchor.MiddleLeft, MutedColor, 22f);
            var buttons = CreateHBox(box, 32f, 6f);
            readyButton = CreateButton(buttons, "准备", 0f, OnReadyClicked);
            readyButtonText = readyButton.GetComponentInChildren<Text>();
            startButton = CreateButton(buttons, "开始游戏", 0f, () => room.StartGame());
            startButtonText = startButton.GetComponentInChildren<Text>();
            surrenderButton = CreateButton(buttons, "认输", 0f, OnSurrenderClicked);
            returnButton = CreateButton(buttons, "返回大厅", 0f, () => room.ReturnToLobby());
            cancelLeaveButton = CreateButton(buttons, "离开房间", 0f, OnCancelLeaveClicked);
            cancelLeaveText = cancelLeaveButton.GetComponentInChildren<Text>();
            CreateLabel(box, "成员", 14, TextAnchor.MiddleLeft, MutedColor, 20f);
            playerListContent = CreateScrollView(box, "PlayerList", 110f);
            CreateLabel(box, "战绩（结束后可回看/回放）", 14, TextAnchor.MiddleLeft, MutedColor, 20f);
            matchRecordContent = CreateScrollView(box, "MatchRecords", 84f);
            CreateLabel(box, "聊天", 14, TextAnchor.MiddleLeft, MutedColor, 20f);
            chatContent = CreateScrollView(box, "ChatList", 190f);
            var chatRow = CreateHBox(box, 30f, 6f);
            chatInput = CreateInput(chatRow, "输入消息…", 30f);
            chatInput.onEndEdit.AddListener(OnChatSubmitted);
            CreateButton(chatRow, "发送", 64f, OnChatSendClicked);
        }

        private void BuildBoardPanel(Transform parent)
        {
            boardPanel = CreatePanel("BoardPanel", parent, new Vector2(868f, -16f), new Vector2(540f, 580f));
            var box = CreateVBox(boardPanel, 6f);
            boardStatusText = CreateLabel(box, "等待棋局创建…", 15, TextAnchor.MiddleLeft, TextColor, 24f);
            // Replay controls: hidden until a stored record is opened for review.
            replayRow = CreateHBox(box, 28f, 6f);
            replayStepBackButton = CreateButton(replayRow, "◀ 上一手", 0f, () => StepReplay(-1));
            replayStatusText = CreateLabel(replayRow, "回放", 14, TextAnchor.MiddleCenter, MutedColor, 0f);
            replayStepForwardButton = CreateButton(replayRow, "下一手 ▶", 0f, () => StepReplay(1));
            replayExitButton = CreateButton(replayRow, "退出回放", 0f, StopReplay);
            replayRow.gameObject.SetActive(false);
            boardGrid = CreateBoardSurface(box);
        }

        private void BuildStatusBar(Transform parent)
        {
            var barObject = new GameObject("StatusBar", typeof(RectTransform), typeof(Image));
            barObject.transform.SetParent(parent, false);
            var bar = (RectTransform)barObject.transform;
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.anchoredPosition = new Vector2(0f, 10f);
            bar.sizeDelta = new Vector2(-32f, 42f);
            barObject.GetComponent<Image>().color = PanelColor;
            statusErrorText = CreateText(bar, "Error", string.Empty, 14, TextAnchor.MiddleLeft, ErrorColor);
            Stretch((RectTransform)statusErrorText.transform, 12f, 380f);
            statusMetricsText = CreateText(bar, "Metrics", string.Empty, 13, TextAnchor.MiddleRight, MutedColor);
            Stretch((RectTransform)statusMetricsText.transform, 1140f, 12f);
        }

        private static RectTransform CreatePanel(string name, Transform parent, Vector2 topLeft, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
            rect.sizeDelta = size;
            go.GetComponent<Image>().color = PanelColor;
            return rect;
        }

        private static RectTransform CreateVBox(RectTransform panel, float spacing)
        {
            var go = new GameObject("VBox", typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(panel, false);
            var rect = (RectTransform)go.transform;
            Stretch(rect, 10f, 10f);
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 4, 4);
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return rect;
        }

        private static RectTransform CreateHBox(Transform parent, float height, float spacing)
        {
            var go = new GameObject("HBox", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            if (height > 0f) go.GetComponent<LayoutElement>().preferredHeight = height;
            return (RectTransform)go.transform;
        }

        private Text CreateLabel(Transform parent, string text, int size, TextAnchor anchor, Color color, float height)
        {
            var label = CreateText(parent, "Label", text, size, anchor, color);
            var element = label.gameObject.AddComponent<LayoutElement>();
            if (height > 0f) element.preferredHeight = height;
            else element.flexibleWidth = 1f;
            return label;
        }

        private Text CreateText(Transform parent, string name, string text, int size, TextAnchor anchor, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.alignment = anchor;
            label.color = color;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        private static void Stretch(RectTransform rect, float left, float right)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, 0f);
            rect.offsetMax = new Vector2(-right, 0f);
        }

        private Button CreateButton(Transform parent, string label, float preferredWidth, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button-" + label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            var element = go.GetComponent<LayoutElement>();
            if (preferredWidth > 0f) element.preferredWidth = preferredWidth;
            else element.flexibleWidth = 1f;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = ButtonColor;
            colors.highlightedColor = ButtonHighlight;
            colors.pressedColor = new Color(0.17f, 0.23f, 0.34f, 1f);
            colors.selectedColor = ButtonColor;
            colors.disabledColor = new Color(0.16f, 0.18f, 0.22f, 0.6f);
            button.colors = colors;
            var text = CreateText(go.transform, "Label", label, 14, TextAnchor.MiddleCenter, TextColor);
            Stretch((RectTransform)text.transform, 4f, 4f);
            if (onClick != null) button.onClick.AddListener(onClick);
            return button;
        }

        private InputField CreateInput(Transform parent, string placeholder, float height)
        {
            var go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);
            go.GetComponent<LayoutElement>().preferredHeight = height;
            var text = CreateText(go.transform, "Text", string.Empty, 15, TextAnchor.MiddleLeft, TextColor);
            text.supportRichText = false;
            Stretch((RectTransform)text.transform, 8f, 8f);
            var ghost = CreateText(go.transform, "Placeholder", placeholder, 15, TextAnchor.MiddleLeft, MutedColor);
            ghost.fontStyle = FontStyle.Italic;
            Stretch((RectTransform)ghost.transform, 8f, 8f);
            var input = go.GetComponent<InputField>();
            input.targetGraphic = go.GetComponent<Image>();
            input.textComponent = text;
            input.placeholder = ghost;
            input.lineType = InputField.LineType.SingleLine;
            return input;
        }

        private RectTransform CreateScrollView(Transform parent, string name, float height)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
            root.GetComponent<LayoutElement>().preferredHeight = height;
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(2f, 2f);
            viewportRect.offsetMax = new Vector2(-2f, -2f);
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 3f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = root.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
            return contentRect;
        }

        private static void ClearRows(List<GameObject> rows)
        {
            for (var i = 0; i < rows.Count; i++)
                if (rows[i] != null) Destroy(rows[i]);
            rows.Clear();
        }

        private Text AddRowLabel(Transform row, string text)
        {
            var label = CreateLabel(row, text, 14, TextAnchor.MiddleLeft, TextColor, 0f);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            return label;
        }

        private Button AddRowButton(Transform row, string label, float width, UnityEngine.Events.UnityAction onClick)
        {
            return CreateButton(row, label, width, onClick);
        }

        private static Font LoadFont()
        {
            var loaded = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return loaded != null ? loaded : Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
    }
}
