#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Socket.Multiplayer.Editor
{
    /// <summary>RoomTemplate 的 UI Toolkit 检查器：基础 / 出生点 / 交互物 三分区。</summary>
    [CustomEditor(typeof(RoomTemplate))]
    internal sealed class RoomTemplateEditor : UnityEditor.Editor, IEmbeddedInspector
    {
        private static readonly string[] TabNames = { "基础", "出生点", "交互物" };

        private bool embedded;

        public void SetEmbeddedLayout() => embedded = true;

        private VisualElement[] panes;
        private Label summaryLabel;

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            var root = new VisualElement();
            MultiplayerInspectorUtility.ApplySkin(root);

            if (!embedded)
            {
                var tabs = new MultiplayerInspectorUtility.TabBar(TabNames, SwitchTab);
                root.Add(tabs.Root);
            }

            var contentHost = new VisualElement { style = { marginTop = 4 } };
            panes = new VisualElement[TabNames.Length];
            for (var i = 0; i < panes.Length; i++)
            {
                panes[i] = new VisualElement();
                panes[i].AddToClassList("mp-pane");
                panes[i].AddToClassList("mp-section");
                if (embedded) panes[i].Add(MultiplayerInspectorUtility.SectionHeader(TabNames[i]));
                contentHost.Add(panes[i]);
            }
            root.Add(contentHost);

            var so = serializedObject;

            panes[0].Add(MultiplayerInspectorUtility.Field(so, "templateName", "模板名称"));
            panes[0].Add(MultiplayerInspectorUtility.Field(so, "description", "模板说明"));
            panes[0].Add(MultiplayerInspectorUtility.Row(
                MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "minPlayers", "最少人数"), "人"),
                MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "maxPlayers", "最多人数"), "人"),
                MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "maxSpectators", "最大观战人数"), "人")));
            panes[0].Add(MultiplayerInspectorUtility.Field(so, "rules", "玩法规则（可选）"));

            summaryLabel = MultiplayerInspectorUtility.Summary("规则摘要", string.Empty, out var summaryRow);
            panes[0].Add(summaryRow);

            panes[1].Add(MultiplayerInspectorUtility.Info("玩家按列表顺序出生；人数超过列表长度时，从第一个点按全局间距自动延伸。"));
            panes[1].Add(new PropertyField(so.FindProperty("playerSpawns"), "出生点列表"));

            panes[2].Add(MultiplayerInspectorUtility.Info("房间开始时由服务器生成，并自动绑定该房间的 MatchInterestManagement 可见性。"));
            panes[2].Add(new PropertyField(so.FindProperty("interactables"), "交互物列表"));

            root.Bind(serializedObject);
            MultiplayerInspectorUtility.ApplyLabels(root);
            root.TrackSerializedObjectValue(serializedObject, _ => RefreshSummary());

            SwitchTab(0);
            RefreshSummary();
            return root;
        }

        private void SwitchTab(int active)
        {
            for (var i = 0; i < panes.Length; i++)
                panes[i].style.display = embedded || i == active ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshSummary()
        {
            var template = (RoomTemplate)target;
            if (summaryLabel == null || template == null) return;
            summaryLabel.text =
                $"{template.EffectiveMinPlayers}-{template.EffectiveMaxPlayers} 人 / {template.PlayerSpawnCount} 个出生点 / {template.InteractableCount} 个交互物";
        }
    }
}
#endif
