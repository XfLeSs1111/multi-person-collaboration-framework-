using System;

namespace Socket.Multiplayer
{
    public enum GomokuCell : byte
    {
        Empty,
        Black,
        White
    }

    public enum GomokuResult : byte
    {
        None,
        BlackWin,
        WhiteWin,
        Draw
    }

    public readonly struct PlaceStoneCommand
    {
        public int CellIndex { get; }
        public PlaceStoneCommand(int cellIndex) => CellIndex = cellIndex;
    }

    public enum GomokuEventKind : byte
    {
        StonePlaced,
        MatchEnded
    }

    public readonly struct GomokuEvent
    {
        public GomokuEventKind Kind { get; }
        public string PlayerId { get; }
        public GomokuCell Cell { get; }
        public int CellIndex { get; }
        public int Turn { get; }
        public GomokuResult Result { get; }

        public GomokuEvent(GomokuEventKind kind, string playerId, GomokuCell cell, int cellIndex, int turn, GomokuResult result)
        {
            Kind = kind;
            PlayerId = playerId;
            Cell = cell;
            CellIndex = cellIndex;
            Turn = turn;
            Result = result;
        }
    }

    public sealed class GomokuState : IMatchState<GomokuState>
    {
        private readonly GomokuCell[] cells;

        public int BoardSize { get; }
        public int Turn { get; private set; }
        public int CurrentSeat { get; private set; }
        public GomokuResult Result { get; private set; }
        public int CellCount => cells.Length;

        public GomokuState(int boardSize)
        {
            if (boardSize < 1) throw new ArgumentOutOfRangeException(nameof(boardSize));
            BoardSize = boardSize;
            cells = new GomokuCell[boardSize * boardSize];
        }

        public GomokuCell GetCell(int index) => index < 0 || index >= cells.Length ? GomokuCell.Empty : cells[index];

        internal void Place(int index, GomokuCell cell)
        {
            cells[index] = cell;
            Turn++;
            CurrentSeat = CurrentSeat == 0 ? 1 : 0;
        }

        internal void SetResult(GomokuResult result) => Result = result;

        public GomokuState Clone()
        {
            var clone = new GomokuState(BoardSize);
            Array.Copy(cells, clone.cells, cells.Length);
            clone.Turn = Turn;
            clone.CurrentSeat = CurrentSeat;
            clone.Result = Result;
            return clone;
        }
    }
}
