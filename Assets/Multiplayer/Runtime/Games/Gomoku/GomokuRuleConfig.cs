using UnityEngine;

namespace Socket.Multiplayer
{
    /// <summary>
    /// 五子棋规则资产：静态配置，运行时棋盘状态由服务器 MatchSession 持有。
    /// 可点击棋盘预览已移到 UI Toolkit 检查器（GomokuRuleConfigEditor）。
    /// </summary>
    [CreateAssetMenu(menuName = "Socket/对战/五子棋规则", fileName = "GomokuRuleConfig")]
    public sealed class GomokuRuleConfig : MatchRulesConfig
    {
        [Tooltip("棋盘边长；5~25。")]
        [Range(5, 25)] public int boardSize = 15;

        [Tooltip("连成多少子获胜；3~10。")]
        [Range(3, 10)] public int winLength = 5;

        [Tooltip("棋盘落满且无人获胜时是否判和棋。")]
        public bool allowDraw = true;

        [Tooltip("是否允许第三名及之后的玩家以观战身份留在对局中。")]
        public bool allowSpectators = true;

        [Tooltip("是否随战绩保留落子序列供客户端回放。")]
        public bool enableReplay = true;

        public override string RulesetName => "五子棋";

        public override string RulesSummary =>
            $"{boardSize}×{boardSize} / {winLength} 子胜利 / {(allowDraw ? "允许和棋" : "不和棋")}";

        public override bool AllowsSpectators => allowSpectators;

        public override string SignatureFingerprint() =>
            $"gomoku:{boardSize}:{winLength}:{allowSpectators}:{allowDraw}:{enableReplay}";

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
        }
    }
}
