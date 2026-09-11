using System.Collections.Generic;
using UnityEngine;

namespace Socket.Multiplayer
{
    // Client-side replay of a stored match record. The rules kernel is pure logic and ships
    // with the client, so stepping through the recorded move list needs no server round
    // trip: the board renders from a locally rebuilt state while the live match keeps
    // running untouched underneath.
    public sealed partial class MultiplayerUi : MonoBehaviour
    {
        private readonly List<int> replayMoves = new List<int>();
        private GomokuState replayState;
        private GomokuRules replayRules;
        private int replayStep;
        private bool replaying;

        private void StartReplay(MatchRecordInfo record)
        {
            if (!ParseReplayMoves(record.replayPayload)) return;

            var ruleset = ResolveReplayRuleset();
            if (builtBoardSize != ruleset.BoardSize) BuildBoardGrid(ruleset.BoardSize);
            replayRules = new GomokuRules(ruleset);
            replayState = new GomokuState(ruleset.BoardSize);
            replayStep = 0;
            replaying = true;
            replayRow.gameObject.SetActive(true);
            boardSignature = string.Empty;
        }

        private void StopReplay()
        {
            replaying = false;
            replayRow.gameObject.SetActive(false);
            // Force the live board (or the lobby footprint) to redraw from the match state.
            boardSignature = string.Empty;
        }

        private void StepReplay(int delta)
        {
            if (!replaying || replayState == null) return;
            var target = Mathf.Clamp(replayStep + delta, 0, replayMoves.Count);
            if (target == replayStep) return;
            // Rebuild from scratch: the kernel is cheap and this keeps the state honest even
            // when stepping backwards.
            replayState = new GomokuState(replayState.BoardSize);
            replayStep = 0;
            for (var i = 0; i < target; i++) ApplyReplayMove(i);
            replayStep = target;
        }

        private void ApplyReplayMove(int index)
        {
            var seat = index % 2 == 0 ? 0 : 1;
            var actor = new MatchParticipant("replay-" + seat, "replay", seat, MatchParticipantRole.Player);
            replayRules.TryApply(replayState, actor, new PlaceStoneCommand(replayMoves[index]));
        }

        private void RenderReplay()
        {
            if (replayState == null) return;
            for (var i = 0; i < boardCells.Count && i < replayState.CellCount; i++)
            {
                var cell = replayState.GetCell(i);
                var stone = boardCellStones[i];
                stone.enabled = cell != GomokuCell.Empty;
                if (stone.enabled)
                {
                    var black = cell == GomokuCell.Black;
                    stone.color = black ? StoneBlack : StoneWhite;
                    boardCellRims[i].effectColor = black ? StoneBlackRim : StoneWhiteRim;
                }
                boardCells[i].interactable = false;
            }
            SetLastMoveMarker(replayStep == 0 ? -1 : replayMoves[replayStep - 1]);
            replayStatusText.text = $"第 {replayStep}/{replayMoves.Count} 手";
            boardStatusText.text = "回放中：用 ◀ ▶ 逐步查看，「退出回放」返回对局";
        }

        // Payload format is the game's own ("index:colour,index:colour", see
        // NetworkGomokuMatch.BuildReplayPayload) — the framework never parses it.
        private bool ParseReplayMoves(string payload)
        {
            replayMoves.Clear();
            if (string.IsNullOrEmpty(payload)) return false;
            foreach (var token in payload.Split(','))
            {
                var parts = token.Split(':');
                if (parts.Length == 0) continue;
                if (!int.TryParse(parts[0], out var index)) continue;
                replayMoves.Add(index);
            }
            return replayMoves.Count > 0;
        }

        private GomokuRuleset ResolveReplayRuleset()
        {
            var template = manager.Config == null ? null : manager.Config.defaultRoomTemplate;
            var ruleConfig = template == null ? null : template.rules as GomokuRuleConfig;
            if (ruleConfig != null) return ruleConfig.CreateRuleset();
            return new GomokuRuleset(15, 5, true, true, false);
        }
    }
}
