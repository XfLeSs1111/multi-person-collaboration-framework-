using System;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

namespace Socket.Multiplayer
{
    // Board view, room button states and shared lookups for the runtime UI.
    public sealed partial class MultiplayerUi : MonoBehaviour
    {
        // Classic board geometry: intersections are BoardPitch apart, the outermost line
        // sits BoardEdge from the surface edge. On a 34px pitch stones are 28px wide.
        private const float BoardPitch = 34f;
        private const float BoardEdge = BoardPitch * 0.5f;
        private const float LineThickness = 1.5f;
        private const float StoneInset = 3f;
        private const float MarkerInset = 13f;

        // Wooden surface for the classic look: lines and click points are rebuilt as
        // children of it on every board-size change.
        private RectTransform CreateBoardSurface(Transform parent)
        {
            var go = new GameObject("BoardSurface", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = BoardSurfaceColor;
            go.GetComponent<LayoutElement>().preferredHeight = BoardPitch * 14f + BoardEdge * 2f;
            boardSurface = (RectTransform)go.transform;
            return boardSurface;
        }

        // Both layers anchor to the top-centre of the surface: a smaller board stays
        // centred and the line/point layers keep one shared coordinate system.
        private static void PlaceLayer(RectTransform rect, float side)
        {
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(side, side);
        }

        // Absolute child of a layer, positioned from that layer's top-left corner.
        private static RectTransform CreateMark(Transform parent, Vector2 topLeft, Vector2 size, Color color, Sprite sprite)
        {
            var go = new GameObject("Mark", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
            rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            if (sprite != null) image.sprite = sprite;
            return rect;
        }

        private void RefreshRoomButtons()
        {
            if (!roomPanel.gameObject.activeSelf)
            {
                roomTitleText.text = "房间";
                return;
            }
            var local = LocalPlayer;
            var isLeader = local != null && local.IsLeader;
            var isReady = local != null && local.IsReady;
            var isSpectator = local != null && local.IsSpectator;

            roomTitleText.text = $"房间：{manager.RoomName}";
            var match = FindMatch(manager.LocalRoomId);
            var seats = match == null
                ? string.Empty
                : $"    对战 {match.SeatedCount}/{match.MaxSeats} · 观战 {Mathf.Max(0, manager.CurrentPlayers.Count - match.SeatedCount)}";
            phaseText.text = $"阶段：{PhaseText(manager.CurrentPhase)}    成员 {manager.CurrentPlayers.Count}{seats}";

            readyButton.gameObject.SetActive(!isSpectator);
            readyButtonText.text = isReady ? "取消准备" : "准备";
            // A finished board keeps its result on screen; the room is already back in the
            // lobby, so the leader's button is simply the next game.
            var rematch = match != null && match.Phase == MatchPhase.Completed;
            startButtonText.text = rematch ? "再来一局" : "开始游戏";
            startButton.gameObject.SetActive(!isSpectator && isLeader && manager.CurrentPhase == RoomPhase.Lobby);
            returnButton.gameObject.SetActive(isLeader && manager.CurrentPhase == RoomPhase.InGame);
            cancelLeaveButton.gameObject.SetActive(true);
            cancelLeaveText.text = isLeader ? "取消房间" : "离开房间";
            // 认输 only makes sense mid-match for a seated player; the server ignores it
            // everywhere else (and reports 对局未在进行中).
            surrenderButton.gameObject.SetActive(!isSpectator && local != null && local.SeatIndex >= 0 &&
                                                 match != null && match.Phase == MatchPhase.Active);
        }

        private void RefreshBoard()
        {
            if (!boardPanel.gameObject.activeSelf) return;
            // A stored record being reviewed owns the board until the player exits replay.
            if (replaying)
            {
                RenderReplay();
                return;
            }
            var match = FindMatch(manager.LocalRoomId) as NetworkGomokuMatch;
            if (match == null)
            {
                EnsureEmptyBoard();
                boardStatusText.text = "棋局将在开始游戏后创建";
                return;
            }
            if (match.BoardSize != builtBoardSize || boardCells.Count != match.BoardSize * match.BoardSize)
                BuildBoardGrid(match.BoardSize);
            var seat = LocalPlayer == null ? -1 : LocalPlayer.SeatIndex;
            var signature = $"{match.CellsText}|{match.Turn}|{(int)match.Result}|{(int)match.Phase}|{match.CurrentSeat}|{match.LastCellIndex}|{seat}";
            if (signature != boardSignature)
            {
                boardSignature = signature;
                UpdateBoardCells(match);
            }
            boardStatusText.text = match.Phase == MatchPhase.Completed
                ? $"结果：{match.DescribeResult()}（{EndCauseText(match)}，共 {match.Progress} 手）· 准备后可再来一局"
                : TurnHint(match);
        }

        // Turn hint for the running match: whose turn it is, whether it is yours, and how
        // much thinking time the framework turn timer has left.
        private string TurnHint(NetworkMatchAdapter match)
        {
            var local = LocalPlayer;
            var seat = local == null ? -1 : local.SeatIndex;
            var role = seat >= 0 ? $"你是 {match.DescribeSeat(seat)}" : "你在观战";
            if (match.Phase != MatchPhase.Active)
                return $"{match.Progress} 手 · {MatchPhaseText(match.Phase)} · {role} · 双方准备后由房主开始";

            var left = match.TurnSecondsLeft;
            var timer = left > 0d ? $" · 剩 {Mathf.CeilToInt((float)left)}s" : string.Empty;
            if (seat < 0) return $"{match.Progress} 手 · {match.DescribeSeat(match.CurrentSeat)} 行棋{timer} · 你在观战";
            return match.IsLocalTurn(local)
                ? $"{match.Progress} 手 · 轮到你（{match.DescribeSeat(seat)}）{timer}"
                : $"{match.Progress} 手 · 等待对方（{match.DescribeSeat(match.CurrentSeat)}）{timer} · {role}";
        }

        // Before the match object exists the panel still shows the board footprint so
        // players can see the battlefield during the lobby phase (M2-V visual pass).
        private void EnsureEmptyBoard()
        {
            if (builtBoardSize == 0)
            {
                var size = 15;
                var template = manager.Config == null ? null : manager.Config.defaultRoomTemplate;
                if (template != null && template.gomokuRules != null) size = template.gomokuRules.boardSize;
                BuildBoardGrid(size);
            }
            if (boardSignature == "empty") return;
            boardSignature = "empty";
            for (var i = 0; i < boardCells.Count; i++)
            {
                boardCellStones[i].enabled = false;
                boardCells[i].interactable = false;
            }
            SetLastMoveMarker(-1);
        }

        // Host can hold objects from several rooms at once (interest management only
        // hides renderers locally); always pick the match whose id is the local room.
        // Framework-level lookup: only the board needs the game-specific type.
        private static NetworkMatchAdapter FindMatch(Guid roomId)
        {
            foreach (var match in FindObjectsByType<NetworkMatchAdapter>(FindObjectsSortMode.None))
                if (match != null && match.MatchId == roomId)
                    return match;
            return null;
        }

        private void BuildBoardGrid(int boardSize)
        {
            if (boardSurface == null) return;
            var side = BoardPitch * (boardSize - 1) + BoardEdge * 2f;
            boardSurface.GetComponent<LayoutElement>().preferredHeight = side;
            // Lines and points are rebuilt wholesale: simplest way to follow a board-size
            // change without tracking every generated mark.
            for (var i = boardSurface.childCount - 1; i >= 0; i--)
                Destroy(boardSurface.GetChild(i).gameObject);
            boardCells.Clear();
            boardCellStones.Clear();
            boardCellRims.Clear();
            // The marker is only a child of a point while it is showing; when idle it sits
            // under the manager object, so a rebuild has to destroy it explicitly.
            if (lastMoveMarker != null) Destroy(lastMoveMarker.gameObject);
            lastMoveMarker = null;
            lastMarkerCell = -1;

            BuildBoardLines(boardSize, side);

            var gridObject = new GameObject("Intersections", typeof(RectTransform), typeof(GridLayoutGroup));
            gridObject.transform.SetParent(boardSurface, false);
            var gridRect = (RectTransform)gridObject.transform;
            PlaceLayer(gridRect, side);
            var layout = gridObject.GetComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(BoardPitch, BoardPitch);
            layout.spacing = Vector2.zero;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = boardSize;
            layout.childAlignment = TextAnchor.UpperLeft;
            boardGrid = gridRect;

            for (var i = 0; i < boardSize * boardSize; i++)
            {
                var go = new GameObject("Point", typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(gridRect, false);
                var image = go.GetComponent<Image>();
                image.sprite = StoneSprite();
                image.color = Color.white;
                var point = go.GetComponent<Button>();
                point.targetGraphic = image;
                ApplyPointTint(point);
                var index = i;
                point.onClick.AddListener(() => OnBoardCellClicked(index));

                // A real stone (tinted circle) instead of an "X"/"O" glyph, plus an
                // Outline rim so white stones stay readable on the wooden board.
                var stoneObject = new GameObject("Stone", typeof(RectTransform), typeof(Image), typeof(Outline));
                stoneObject.transform.SetParent(go.transform, false);
                var stone = stoneObject.GetComponent<Image>();
                stone.sprite = StoneSprite();
                stone.raycastTarget = false;
                stone.enabled = false;
                var stoneRect = (RectTransform)stoneObject.transform;
                stoneRect.anchorMin = Vector2.zero;
                stoneRect.anchorMax = Vector2.one;
                stoneRect.offsetMin = new Vector2(StoneInset, StoneInset);
                stoneRect.offsetMax = new Vector2(-StoneInset, -StoneInset);
                var rim = stoneObject.GetComponent<Outline>();
                rim.effectDistance = new Vector2(1.2f, -1.2f);

                boardCells.Add(point);
                boardCellStones.Add(stone);
                boardCellRims.Add(rim);
            }
            BuildLastMoveMarker();
            builtBoardSize = boardSize;
            boardSignature = string.Empty;
        }

        // Grid lines plus the classic star points. Both are non-raycast marks, so they
        // never steal clicks from the intersection buttons layered above them.
        private void BuildBoardLines(int boardSize, float side)
        {
            var linesObject = new GameObject("Lines", typeof(RectTransform));
            linesObject.transform.SetParent(boardSurface, false);
            var lines = (RectTransform)linesObject.transform;
            PlaceLayer(lines, side);
            for (var i = 0; i < boardSize; i++)
            {
                var offset = BoardEdge + i * BoardPitch;
                CreateMark(lines, new Vector2(offset - LineThickness * 0.5f, 0f), new Vector2(LineThickness, side), BoardLineColor, null);
                CreateMark(lines, new Vector2(0f, -offset - LineThickness * 0.5f), new Vector2(side, LineThickness), BoardLineColor, null);
            }
            if (boardSize < 11) return;
            const float dot = 6f;
            var stars = new[] { 3, boardSize / 2, boardSize - 4 };
            foreach (var row in stars)
                foreach (var column in stars)
                    CreateMark(
                        lines,
                        new Vector2(BoardEdge + column * BoardPitch - dot * 0.5f, -(BoardEdge + row * BoardPitch) - dot * 0.5f),
                        new Vector2(dot, dot),
                        BoardStarColor,
                        StoneSprite());
        }

        // Kept out of boardGrid so the GridLayoutGroup never reserves a slot for it;
        // it is re-parented into the owning cell on demand.
        private void BuildLastMoveMarker()
        {
            var markerObject = new GameObject("LastMoveMarker", typeof(RectTransform), typeof(Image));
            markerObject.transform.SetParent(transform, false);
            lastMoveMarker = markerObject.GetComponent<Image>();
            lastMoveMarker.sprite = StoneSprite();
            lastMoveMarker.color = LastMoveColor;
            lastMoveMarker.raycastTarget = false;
            lastMoveMarker.enabled = false;
            lastMarkerCell = -1;
        }

        // Hover/press previews the stone you are about to drop on that intersection; a
        // blocked point must stay invisible instead of greying out.
        private static void ApplyPointTint(Button point)
        {
            var colors = point.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0f);
            colors.highlightedColor = GhostColor;
            colors.pressedColor = GhostPressedColor;
            colors.selectedColor = new Color(1f, 1f, 1f, 0f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0f);
            colors.fadeDuration = 0.05f;
            point.colors = colors;
            point.transition = Selectable.Transition.ColorTint;
        }

        // One shared procedural disc: the generator rebuilds this UI on every Regenerate,
        // so the board must not depend on authored sprite assets.
        private static Sprite stoneSprite;
        private static Sprite StoneSprite()
        {
            if (stoneSprite != null) return stoneSprite;
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            var pixels = new Color32[size * size];
            const float center = (size - 1) * 0.5f;
            const float radius = center - 0.5f;
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);
                    // 1px antialiased edge; 32px cells would look jagged otherwise.
                    var alpha = Mathf.Clamp01(radius - distance + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            stoneSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
            return stoneSprite;
        }

        private void UpdateBoardCells(NetworkGomokuMatch match)
        {
            var local = LocalPlayer;
            // Only the player holding the current seat may click; the server rejects the
            // rest anyway, but an inert board beats a rejected click.
            var canPlay = local != null && !local.IsSpectator &&
                          match.Phase == MatchPhase.Active && match.IsLocalTurn(local);
            for (var i = 0; i < boardCells.Count; i++)
            {
                var cell = match.GetCell(i);
                var stone = boardCellStones[i];
                stone.enabled = cell != GomokuCell.Empty;
                if (stone.enabled)
                {
                    var black = cell == GomokuCell.Black;
                    stone.color = black ? StoneBlack : StoneWhite;
                    boardCellRims[i].effectColor = black ? StoneBlackRim : StoneWhiteRim;
                }
                boardCells[i].interactable = canPlay && cell == GomokuCell.Empty;
            }
            SetLastMoveMarker(match.LastCellIndex);
        }

        // Single reuse of one marker object: re-parenting it into the owning cell is
        // cheaper than building (boardSize²) marker objects up front.
        private void SetLastMoveMarker(int index)
        {
            if (lastMoveMarker == null) return;
            if (index < 0 || index >= boardCells.Count)
            {
                lastMoveMarker.enabled = false;
                return;
            }
            if (lastMarkerCell != index)
            {
                lastMarkerCell = index;
                lastMoveMarker.transform.SetParent(boardCells[index].transform, false);
                var rect = (RectTransform)lastMoveMarker.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(MarkerInset, MarkerInset);
                rect.offsetMax = new Vector2(-MarkerInset, -MarkerInset);
            }
            lastMoveMarker.enabled = true;
        }

        private void OnBoardCellClicked(int index)
        {
            // Placement payloads are game-specific; the shared lookup stays framework-level.
            if (FindMatch(manager.LocalRoomId) is NetworkGomokuMatch match)
                match.CmdPlaceStone(index);
        }
    }
}
