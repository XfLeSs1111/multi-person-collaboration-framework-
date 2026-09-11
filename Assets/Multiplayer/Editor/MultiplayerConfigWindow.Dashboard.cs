#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Socket.Multiplayer.Editor
{
    /// <summary>配置中心：总览页（状态卡片 + 快捷操作）。</summary>
    internal sealed partial class MultiplayerConfigWindow
    {
        private VisualElement BuildDashboard()
        {
            var page = new ScrollView { style = { flexGrow = 1 } };
            page.Add(MultiplayerInspectorUtility.PageTitle("多人联机配置中心"));

            if (config == null)
            {
                page.Add(MultiplayerInspectorUtility.Info("未加载配置资源。", HelpBoxMessageType.Warning));
                return page;
            }

            var issues = MultiplayerConfigEditor.GetValidationIssues(config);
            var status = new VisualElement();
            status.style.flexDirection = FlexDirection.Row;
            status.style.alignItems = Align.Center;
            status.style.marginBottom = 8f;
            status.Add(MultiplayerInspectorUtility.Badge(
                issues.Count == 0 ? "配置完整，可以启动" : $"{issues.Count} 项需要处理",
                issues.Count > 0));
            page.Add(status);

            var cards = new VisualElement();
            cards.AddToClassList("mp-actions");
            cards.Add(Card("房间", $"{config.maxRooms} 个房间\n单房 {config.EffectiveMinPlayers}-{config.EffectiveMaxPlayers} 人"));
            cards.Add(Card("网络", $"{config.defaultAddress}:{config.port}\n{config.sendRate} 次/秒"));
            cards.Add(Card(
                "模板",
                config.defaultRoomTemplate == null
                    ? "尚未绑定房间模板"
                    : $"{config.defaultRoomTemplate.templateName}\n{config.defaultRoomTemplate.PlayerSpawnCount} 出生点 / {config.defaultRoomTemplate.InteractableCount} 交互物"));
            page.Add(cards);

            var actionSection = new VisualElement();
            actionSection.AddToClassList("mp-section");
            actionSection.Add(MultiplayerInspectorUtility.SectionHeader("快捷操作"));
            var actions = new VisualElement();
            actions.AddToClassList("mp-actions");
            actions.Add(ActionButton("保存全部", SaveAll, true));
            actions.Add(ActionButton("检查配置", ShowValidationDialog));
            actions.Add(ActionButton("房间模板", CreateOrSelectRoomTemplate));
            actions.Add(ActionButton("定位配置", () =>
            {
                Selection.activeObject = config;
                EditorGUIUtility.PingObject(config);
            }));
            actions.Add(ActionButton("打开启动场景", OpenBootstrapScene));
            actionSection.Add(actions);
            page.Add(actionSection);
            return page;
        }

        private static VisualElement Card(string title, string body)
        {
            var card = new VisualElement();
            card.AddToClassList("mp-card");
            var titleLabel = new Label(title);
            titleLabel.AddToClassList("mp-card__title");
            card.Add(titleLabel);
            var bodyLabel = new Label(body);
            bodyLabel.AddToClassList("mp-card__body");
            card.Add(bodyLabel);
            return card;
        }

        private static Button ActionButton(string text, Action callback, bool primary = false)
        {
            var button = new Button(callback) { text = text };
            button.AddToClassList("mp-action");
            if (primary) button.AddToClassList("mp-action--primary");
            return button;
        }
    }
}
#endif
