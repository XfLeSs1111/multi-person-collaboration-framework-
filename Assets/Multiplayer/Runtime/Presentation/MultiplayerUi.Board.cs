using System;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

namespace Socket.Multiplayer
{
    // Board view, room button states and shared lookups for the runtime UI.
    public sealed partial class MultiplayerUi : MonoBehaviour
    {
        private RectTransform CreateGrid(Transform parent)
        {
            var go = new GameObject("Grid", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = 528f;
            var grid = go.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(32f, 32f);
            grid.spacing = new Vector2(2f, 2f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 15;
            grid.childAlignment = TextAnchor.UpperLeft;
            return (RectTransform)go.transform;
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
            phaseText.text = $"阶段：{PhaseText(manager.CurrentPhase)}    成员 {manager.CurrentPlayers.Count}";

            readyButton.gameObject.SetActive(!isSpectator);
            readyButtonText.text = isReady ? "取消准备" : "准备";
            startButton.gameObject.SetActive(!isSpectator && isLeader && manager.CurrentPhase == RoomPhase.Lobby);
            returnButton.gameObject.SetActive(isLeader && manager.CurrentPhase == RoomPhase.InGame);
            cancelLeaveButton.gameObject.SetActive(true);
            cancelLeaveText.text = isLeader ? "取消房间" : "离开房间";
        }

        private void RefreshBoard()
        {
            if (!boardPanel.gameObject.activeSelf) return;
            var match = FindMatch(manager.LocalRoomId);
            if (match == null)
            {
                EnsureEmptyBoard();
                boardStatusText.text = "棋局将在开始游戏后创建";
                return;
            }
            if (match.BoardSize != builtBoardSize || boardCells.Count != match.BoardSize * match.BoardSize)
                BuildBoardGrid(match.BoardSize);
            var signature = $"{match.CellsText}|{match.Turn}|{(int)match.Result}|{(int)match.Phase}|{match.CurrentSeat}";
            if (signature != boardSignature)
            {
                boardSignature = signature;
                UpdateBoardCells(match);
            }
            boardStatusText.text = match.Result != GomokuResult.None
                ? $"结果：{ResultText(match.Result)}（共 {match.Turn} 手）"
                : $"{match.Turn} 手 · {(match.CurrentSeat == 0 ? "黑棋 X 回合" : "白棋 O 回合")} · {MatchPhaseText(match.Phase)}";
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
                boardCellLabels[i].text = string.Empty;
                boardCellLabels[i].color = Color.clear;
                boardCells[i].interactable = false;
            }
        }

        // Host can hold objects from several rooms at once (interest management only
        // hides renderers locally); always pick the match whose id is the local room.
        private static NetworkGomokuMatch FindMatch(Guid roomId)
        {
            foreach (var match in FindObjectsByType<NetworkGomokuMatch>(FindObjectsSortMode.None))
                if (match != null && match.MatchId == roomId)
                    return match;
            return null;
        }

        private void BuildBoardGrid(int boardSize)
        {
            for (var i = 0; i < boardCells.Count; i++)
                if (boardCells[i] != null) Destroy(boardCells[i].gameObject);
            boardCells.Clear();
            boardCellLabels.Clear();
            boardGrid.GetComponent<GridLayoutGroup>().constraintCount = boardSize;
            for (var i = 0; i < boardSize * boardSize; i++)
            {
                var go = new GameObject("Cell", typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(boardGrid, false);
                var image = go.GetComponent<Image>();
                image.color = CellColor;
                var cell = go.GetComponent<Button>();
                cell.targetGraphic = image;
                cell.transition = Selectable.Transition.None;
                var index = i;
                cell.onClick.AddListener(() => OnBoardCellClicked(index));
                var label = CreateText(go.transform, "Stone", string.Empty, 19, TextAnchor.MiddleCenter, StoneBlack);
                Stretch((RectTransform)label.transform, 0f, 0f);
                boardCells.Add(cell);
                boardCellLabels.Add(label);
            }
            builtBoardSize = boardSize;
            boardSignature = string.Empty;
        }

        private void UpdateBoardCells(NetworkGomokuMatch match)
        {
            var local = LocalPlayer;
            var canPlay = local != null && !local.IsSpectator && match.Phase == MatchPhase.Active;
            for (var i = 0; i < boardCells.Count; i++)
            {
                var cell = match.GetCell(i);
                var label = boardCellLabels[i];
                label.text = cell == GomokuCell.Black ? "X" : cell == GomokuCell.White ? "O" : string.Empty;
                label.color = cell == GomokuCell.Black ? StoneBlack : cell == GomokuCell.White ? StoneWhite : Color.clear;
                boardCells[i].interactable = canPlay && cell == GomokuCell.Empty;
            }
        }

        private void OnBoardCellClicked(int index)
        {
            var match = FindMatch(manager.LocalRoomId);
            if (match != null) match.CmdPlaceStone(index);
        }
    }
}
