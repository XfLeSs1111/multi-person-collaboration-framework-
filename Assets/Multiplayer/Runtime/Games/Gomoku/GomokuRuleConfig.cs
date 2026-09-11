using Sirenix.OdinInspector;
using UnityEngine;

namespace Socket.Multiplayer
{
    [CreateAssetMenu(menuName = "Socket/对战/五子棋规则", fileName = "GomokuRuleConfig")]
    public sealed class GomokuRuleConfig : ScriptableObject
    {
        [System.NonSerialized] private GomokuState previewState;
        [System.NonSerialized] private MatchParticipant previewBlack;
        [System.NonSerialized] private MatchParticipant previewWhite;

        [TabGroup("规则分区", "棋盘")]
        [InfoBox("五子棋规则是静态配置，运行时棋盘状态由服务器 MatchSession 持有。")]
        [LabelText("棋盘尺寸"), MinValue(5), MaxValue(25), SuffixLabel("×"), PropertyOrder(0)]
        public int boardSize = 15;

        [TabGroup("规则分区", "棋盘")]
        [LabelText("胜利连子数"), MinValue(3), MaxValue(10), SuffixLabel("子")]
        public int winLength = 5;

        [TabGroup("规则分区", "对局")]
        [LabelText("允许和棋"), ToggleLeft]
        public bool allowDraw = true;

        [TabGroup("规则分区", "观战")]
        [LabelText("允许观战"), ToggleLeft]
        public bool allowSpectators = true;

        [TabGroup("规则分区", "回放")]
        [LabelText("启用回放"), ToggleLeft]
        public bool enableReplay = true;

        [TabGroup("规则分区", "规则")]
        [ShowInInspector, Sirenix.OdinInspector.ReadOnly, LabelText("规则摘要")]
        private string Summary => $"{boardSize}×{boardSize} / {winLength} 子胜利 / {(allowDraw ? "允许和棋" : "不和棋")}";

        [TabGroup("规则分区", "预览")]
        [InfoBox("点击棋盘进行规则预览。这里使用和服务器相同的 GomokuRules，不会写入运行时对局。")]
        [OnInspectorGUI]
        private void DrawBoardPreview()
        {
            EnsurePreview();
            var size = previewState.BoardSize;
            var cellSize = 24f;
            for (var row = 0; row < size; row++)
            {
                GUILayout.BeginHorizontal();
                for (var column = 0; column < size; column++)
                {
                    var index = row * size + column;
                    var label = previewState.GetCell(index) == GomokuCell.Black
                        ? "X"
                        : previewState.GetCell(index) == GomokuCell.White ? "O" : "+";
                    if (GUILayout.Button(label, GUILayout.Width(cellSize), GUILayout.Height(cellSize)))
                        TryPreviewMove(index);
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Label(previewState.Result == GomokuResult.None
                ? $"预览回合：{(previewState.CurrentSeat == 0 ? "黑棋 X" : "白棋 O")} / 已落子 {previewState.Turn}"
                : $"预览结果：{previewState.Result} / 已落子 {previewState.Turn}");
        }

        [TabGroup("规则分区", "预览")]
        [Button("重置棋盘预览")]
        private void ResetPreview()
        {
            previewState = null;
            EnsurePreview();
        }

        public GomokuRuleset CreateRuleset()
        {
            var size = Mathf.Clamp(boardSize, 5, 25);
            return new GomokuRuleset(
                size,
                Mathf.Clamp(winLength, 3, Mathf.Min(10, size)),
                allowDraw,
                allowSpectators,
                enableReplay);
        }

        private void OnValidate()
        {
            boardSize = Mathf.Clamp(boardSize, 5, 25);
            winLength = Mathf.Clamp(winLength, 3, Mathf.Min(10, boardSize));
            previewState = null;
        }

        private void EnsurePreview()
        {
            var ruleset = CreateRuleset();
            if (previewState != null && previewState.BoardSize == ruleset.BoardSize) return;
            previewState = new GomokuState(ruleset.BoardSize);
            previewBlack = new MatchParticipant("preview-black", "黑棋", 0, MatchParticipantRole.Player);
            previewWhite = new MatchParticipant("preview-white", "白棋", 1, MatchParticipantRole.Player);
        }

        private void TryPreviewMove(int index)
        {
            var rules = new GomokuRules(CreateRuleset());
            var actor = previewState.CurrentSeat == 0 ? previewBlack : previewWhite;
            rules.TryApply(previewState, actor, new PlaceStoneCommand(index));
        }
    }

    public readonly struct GomokuRuleset
    {
        public int BoardSize { get; }
        public int WinLength { get; }
        public bool AllowDraw { get; }
        public bool AllowSpectators { get; }
        public bool EnableReplay { get; }

        public GomokuRuleset(int boardSize, int winLength, bool allowDraw, bool allowSpectators, bool enableReplay)
        {
            BoardSize = boardSize;
            WinLength = winLength;
            AllowDraw = allowDraw;
            AllowSpectators = allowSpectators;
            EnableReplay = enableReplay;
        }
    }
}
