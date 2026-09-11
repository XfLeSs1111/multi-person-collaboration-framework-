#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Socket.Multiplayer.Editor
{
    /// <summary>GomokuRuleConfig 的 UI Toolkit 检查器：规则字段 + 可点击棋盘预览（等价旧 Odin 预览）。</summary>
    [CustomEditor(typeof(GomokuRuleConfig))]
    internal sealed class GomokuRuleConfigEditor : UnityEditor.Editor
    {
        private static readonly string[] TabNames = { "棋盘", "对局", "观战", "回放", "预览" };

        private VisualElement[] panes;
        private VisualElement boardHost;
        private Label summaryLabel;
        private Label turnLabel;

        private GomokuState previewState;
        private MatchParticipant previewBlack;
        private MatchParticipant previewWhite;

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            var root = new VisualElement();
            MultiplayerInspectorUtility.ApplySkin(root);

            var tabs = new MultiplayerInspectorUtility.TabBar(TabNames, SwitchTab);
            root.Add(tabs.Root);

            var contentHost = new VisualElement { style = { marginTop = 4 } };
            panes = new VisualElement[TabNames.Length];
            for (var i = 0; i < panes.Length; i++)
            {
                panes[i] = new VisualElement();
                panes[i].AddToClassList("mp-pane");
                contentHost.Add(panes[i]);
            }
            root.Add(contentHost);

            var so = serializedObject;
            panes[0].Add(MultiplayerInspectorUtility.Info("五子棋规则是静态配置，运行时棋盘状态由服务器 MatchSession 持有。"));
            panes[0].Add(MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "boardSize", "棋盘尺寸"), "×"));
            panes[0].Add(MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "winLength", "胜利连子数"), "子"));
            summaryLabel = MultiplayerInspectorUtility.Summary("规则摘要", string.Empty, out var summaryRow);
            panes[0].Add(summaryRow);

            panes[1].Add(MultiplayerInspectorUtility.Field(so, "allowDraw", "允许和棋"));
            panes[2].Add(MultiplayerInspectorUtility.Field(so, "allowSpectators", "允许观战"));
            panes[3].Add(MultiplayerInspectorUtility.Field(so, "enableReplay", "启用回放"));

            panes[4].Add(MultiplayerInspectorUtility.Info("点击棋盘进行预览。这里使用和服务器相同的 GomokuRules，不会写入运行时对局。"));
            boardHost = new VisualElement();
            boardHost.AddToClassList("mp-board");
            panes[4].Add(boardHost);
            turnLabel = new Label();
            turnLabel.style.marginTop = 4;
            panes[4].Add(turnLabel);
            var reset = new Button(() => { previewState = null; RebuildPreview(); }) { text = "重置棋盘预览" };
            reset.style.marginTop = 4;
            panes[4].Add(reset);

            root.Bind(serializedObject);
            root.TrackSerializedObjectValue(serializedObject, _ =>
            {
                RefreshSummary();
                RebuildPreview();
            });

            SwitchTab(0);
            RefreshSummary();
            RebuildPreview();
            return root;
        }

        private void SwitchTab(int active)
        {
            for (var i = 0; i < panes.Length; i++)
                panes[i].style.display = i == active ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshSummary()
        {
            var config = (GomokuRuleConfig)target;
            if (summaryLabel == null || config == null) return;
            summaryLabel.text = $"{config.boardSize}×{config.boardSize} / {config.winLength} 子胜利 / {(config.allowDraw ? "允许和棋" : "不和棋")}";
        }

        private void RebuildPreview()
        {
            if (boardHost == null) return;
            var config = (GomokuRuleConfig)target;
            if (config == null) return;

            var ruleset = config.CreateRuleset();
            if (previewState == null || previewState.BoardSize != ruleset.BoardSize)
            {
                previewState = new GomokuState(ruleset.BoardSize);
                previewBlack = new MatchParticipant("preview-black", "黑棋", 0, MatchParticipantRole.Player);
                previewWhite = new MatchParticipant("preview-white", "白棋", 1, MatchParticipantRole.Player);
            }

            boardHost.Clear();
            var size = previewState.BoardSize;
            for (var row = 0; row < size; row++)
            {
                var rowElement = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                for (var column = 0; column < size; column++)
                {
                    var index = row * size + column;
                    var cellState = previewState.GetCell(index);
                    var button = new Button(() => TryPreviewMove(index));
                    button.AddToClassList("mp-cell");
                    button.EnableInClassList("mp-cell--black", cellState == GomokuCell.Black);
                    button.EnableInClassList("mp-cell--white", cellState == GomokuCell.White);
                    rowElement.Add(button);
                }
                boardHost.Add(rowElement);
            }

            turnLabel.text = previewState.Result == GomokuResult.None
                ? $"预览回合：{(previewState.CurrentSeat == 0 ? "黑棋" : "白棋")} / 已落子 {previewState.Turn}"
                : $"预览结果：{previewState.Result} / 已落子 {previewState.Turn}";
        }

        private void TryPreviewMove(int index)
        {
            var config = (GomokuRuleConfig)target;
            if (config == null || previewState == null) return;
            var rules = new GomokuRules(config.CreateRuleset());
            var actor = previewState.CurrentSeat == 0 ? previewBlack : previewWhite;
            rules.TryApply(previewState, actor, new PlaceStoneCommand(index));
            RebuildPreview();
        }

    }
}
#endif
