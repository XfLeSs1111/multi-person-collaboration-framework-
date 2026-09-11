using System;

namespace Socket.Multiplayer
{
    public sealed class GomokuRules : IMatchRules<GomokuState, PlaceStoneCommand, GomokuEvent>, IMatchEventApplier<GomokuState, GomokuEvent>, IMatchForfeitRules<GomokuState, GomokuEvent>
    {
        private readonly GomokuRuleset ruleset;

        public GomokuRules(GomokuRuleset ruleset) => this.ruleset = ruleset;

        public MatchCommandResult<GomokuEvent> TryApply(GomokuState state, MatchParticipant actor, PlaceStoneCommand command)
        {
            if (state == null || actor == null) return MatchCommandResult<GomokuEvent>.Reject("Match state is invalid.");
            if (state.Result != GomokuResult.None) return MatchCommandResult<GomokuEvent>.Reject("Match has already ended.");
            if (actor.SeatIndex != state.CurrentSeat) return MatchCommandResult<GomokuEvent>.Reject("It is not this player's turn.");
            if (actor.SeatIndex < 0 || actor.SeatIndex > 1) return MatchCommandResult<GomokuEvent>.Reject("Player seat is invalid.");
            if (command.CellIndex < 0 || command.CellIndex >= state.CellCount) return MatchCommandResult<GomokuEvent>.Reject("Cell is outside the board.");
            if (state.GetCell(command.CellIndex) != GomokuCell.Empty) return MatchCommandResult<GomokuEvent>.Reject("Cell is already occupied.");

            var cell = actor.SeatIndex == 0 ? GomokuCell.Black : GomokuCell.White;
            state.Place(command.CellIndex, cell);
            var turn = state.Turn;
            var result = GetResult(state, command.CellIndex, cell);
            if (result != GomokuResult.None) state.SetResult(result);

            return MatchCommandResult<GomokuEvent>.Accept(
                new GomokuEvent(GomokuEventKind.StonePlaced, actor.StableId, cell, command.CellIndex, turn, result));
        }

        public bool IsFinished(GomokuState state) => state != null && state.Result != GomokuResult.None;

        /// <summary>
        /// A seated player who leaves loses: the opponent takes the win and the match
        /// completes instead of deadlocking on a turn its owner will never take.
        /// Extra room members without a duel seat (index &gt; 1) cannot command anyway,
        /// so their departure does not decide the match.
        /// </summary>
        public bool TryForfeit(GomokuState state, MatchParticipant leaver, out GomokuEvent finalEvent)
        {
            finalEvent = default;
            if (state == null || leaver == null) return false;
            if (state.Result != GomokuResult.None) return false;
            if (leaver.SeatIndex != 0 && leaver.SeatIndex != 1) return false;

            var result = leaver.SeatIndex == 0 ? GomokuResult.WhiteWin : GomokuResult.BlackWin;
            state.SetResult(result);
            finalEvent = new GomokuEvent(GomokuEventKind.MatchEnded, leaver.StableId, GomokuCell.Empty, -1, state.Turn, result);
            return true;
        }

        public void Apply(GomokuState state, GomokuEvent gameEvent)
        {
            if (state == null) return;
            if (gameEvent.Kind == GomokuEventKind.MatchEnded)
            {
                // Forfeit end: replay restores the result without a placement.
                if (gameEvent.Result != GomokuResult.None) state.SetResult(gameEvent.Result);
                return;
            }
            if (gameEvent.Kind != GomokuEventKind.StonePlaced) return;
            if (state.GetCell(gameEvent.CellIndex) != GomokuCell.Empty) return;
            if (gameEvent.Cell == GomokuCell.Empty) return;
            state.Place(gameEvent.CellIndex, gameEvent.Cell);
            if (gameEvent.Result != GomokuResult.None) state.SetResult(gameEvent.Result);
        }

        private GomokuResult GetResult(GomokuState state, int index, GomokuCell cell)
        {
            var row = index / state.BoardSize;
            var column = index % state.BoardSize;
            if (CountDirection(state, row, column, 1, 0, cell) + CountDirection(state, row, column, -1, 0, cell) - 1 >= ruleset.WinLength ||
                CountDirection(state, row, column, 0, 1, cell) + CountDirection(state, row, column, 0, -1, cell) - 1 >= ruleset.WinLength ||
                CountDirection(state, row, column, 1, 1, cell) + CountDirection(state, row, column, -1, -1, cell) - 1 >= ruleset.WinLength ||
                CountDirection(state, row, column, 1, -1, cell) + CountDirection(state, row, column, -1, 1, cell) - 1 >= ruleset.WinLength)
                return cell == GomokuCell.Black ? GomokuResult.BlackWin : GomokuResult.WhiteWin;

            if (state.Turn >= state.CellCount && ruleset.AllowDraw) return GomokuResult.Draw;
            return GomokuResult.None;
        }

        private static int CountDirection(GomokuState state, int row, int column, int rowStep, int columnStep, GomokuCell cell)
        {
            var count = 0;
            while (row >= 0 && row < state.BoardSize && column >= 0 && column < state.BoardSize)
            {
                var index = row * state.BoardSize + column;
                if (state.GetCell(index) != cell) break;
                count++;
                row += rowStep;
                column += columnStep;
            }
            return count;
        }
    }
}
