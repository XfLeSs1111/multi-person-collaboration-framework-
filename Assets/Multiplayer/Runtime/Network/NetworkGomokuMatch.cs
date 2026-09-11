using System;
using Mirror;
using UnityEngine;

namespace Socket.Multiplayer
{
    /// <summary>
    /// Mirror adapter for the network-free Gomoku match kernel.
    /// The server owns MatchSession; clients only receive snapshots and confirmed events.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(NetworkMatch))]
    public sealed class NetworkGomokuMatch : NetworkBehaviour
    {
        [SyncVar] private string matchIdText;
        [SyncVar] private MatchPhase phase;
        [SyncVar] private int boardSize;
        [SyncVar] private int turn;
        [SyncVar] private int currentSeat;
        [SyncVar] private GomokuResult result;
        [SyncVar] private string cellsText;

        private MatchSession<GomokuState, PlaceStoneCommand, GomokuEvent> session;
        private GomokuRuleset ruleset;

        public Guid MatchId => Guid.TryParse(matchIdText, out var value) ? value : Guid.Empty;
        public MatchPhase Phase => phase;
        public int BoardSize => boardSize;
        public int Turn => turn;
        public int CurrentSeat => currentSeat;
        public GomokuResult Result => result;
        public string CellsText => cellsText ?? string.Empty;

        public GomokuCell GetCell(int index)
        {
            var cells = CellsText;
            return index >= 0 && index < cells.Length
                ? cells[index] == 'B' ? GomokuCell.Black : cells[index] == 'W' ? GomokuCell.White : GomokuCell.Empty
                : GomokuCell.Empty;
        }

        public event Action StateChanged;
        public event Action<GomokuEvent> StoneConfirmed;

        [Server]
        public void ServerInitialize(Guid roomId, GomokuRuleset matchRuleset)
        {
            matchIdText = roomId.ToString();
            ruleset = matchRuleset;
            var networkMatch = GetComponent<NetworkMatch>();
            networkMatch.matchId = roomId;
            session = new MatchSession<GomokuState, PlaceStoneCommand, GomokuEvent>(
                roomId, new GomokuState(matchRuleset.BoardSize), new GomokuRules(matchRuleset));
            boardSize = matchRuleset.BoardSize;
            phase = MatchPhase.Waiting;
            result = GomokuResult.None;
            RefreshSnapshot();
        }

        [Server]
        public bool ServerAddPlayer(string stableId, string displayName, int seatIndex)
        {
            if (session == null) return false;
            return session.AddParticipant(new MatchParticipant(stableId, displayName, seatIndex, MatchParticipantRole.Player));
        }

        [Server]
        public bool ServerAddSpectator(string stableId, string displayName)
        {
            if (session == null) return false;
            return session.AddParticipant(new MatchParticipant(stableId, displayName, -1, MatchParticipantRole.Spectator));
        }

        [Server]
        public void ServerRemoveParticipant(string stableId)
        {
            session?.RemoveParticipant(stableId);
        }

        [Server]
        public void ServerResetMatch()
        {
            if (ruleset.BoardSize < 1) return;
            session = new MatchSession<GomokuState, PlaceStoneCommand, GomokuEvent>(
                MatchId, new GomokuState(ruleset.BoardSize), new GomokuRules(ruleset));
            phase = MatchPhase.Waiting;
            turn = 0;
            currentSeat = 0;
            result = GomokuResult.None;
            RefreshSnapshot();
        }

        [Server]
        public bool ServerStartMatch()
        {
            if (session == null || session.PlayerCount < 2 || !session.Start()) return false;
            RefreshSnapshot();
            return true;
        }

        [Command(requiresAuthority = false)]
        public void CmdPlaceStone(int cellIndex, NetworkConnectionToClient sender = null)
        {
            if (!isServer || sender == null || sender.identity == null) return;
            if (!sender.identity.TryGetComponent<NetworkPlayer>(out var player)) return;
            if (player.IsSpectator) return;

            var result = session == null
                ? MatchCommandResult<GomokuEvent>.Reject("Match is not initialized.")
                : session.Submit(player.StableId, new PlaceStoneCommand(cellIndex), NetworkTime.localTime);
            if (!result.Accepted) return;

            RefreshSnapshot();
            RpcStoneConfirmed(result.Event.PlayerId, result.Event.Cell, result.Event.CellIndex, result.Event.Turn, result.Event.Result);
        }

        [ClientRpc]
        private void RpcStoneConfirmed(string playerId, GomokuCell cell, int cellIndex, int confirmedTurn, GomokuResult confirmedResult)
        {
            var gameEvent = new GomokuEvent(GomokuEventKind.StonePlaced, playerId, cell, cellIndex, confirmedTurn, confirmedResult);
            StoneConfirmed?.Invoke(gameEvent);
            StateChanged?.Invoke();
        }

        [Server]
        private void RefreshSnapshot()
        {
            if (session == null) return;
            var state = session.State;
            phase = session.Phase;
            boardSize = state.BoardSize;
            turn = state.Turn;
            currentSeat = state.CurrentSeat;
            result = state.Result;
            cellsText = SerializeCells(state);
            StateChanged?.Invoke();
        }

        private static string SerializeCells(GomokuState state)
        {
            var chars = new char[state.CellCount];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = state.GetCell(i) == GomokuCell.Black ? 'B' : state.GetCell(i) == GomokuCell.White ? 'W' : '.';
            return new string(chars);
        }
    }
}
