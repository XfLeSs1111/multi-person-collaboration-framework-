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
            var banner = new Label(issues.Count == 0
                ? "配置完整，可以启动多人联机"
                : $"发现 {issues.Count} 项需要处理，切到「配置检查」页查看明细");
            banner.AddToClassList("mp-banner");
            if (issues.Count > 0) banner.AddToClassList("mp-banner--warn");
            page.Add(banner);

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
            actions.Add(ActionButton("保存全部", SaveAll, ButtonKind.Success, true));
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

        /// <summary>标题区：图标 + 主标题 + 副标题 + 协议胶囊（参考 MCP for Unity 窗口）。</summary>
        private VisualElement BuildHero()
        {
            var hero = new VisualElement();
            hero.AddToClassList("mp-hero");

            var icon = new VisualElement();
            icon.AddToClassList("mp-hero__icon");
            hero.Add(icon);

            var text = new VisualElement();
            text.AddToClassList("mp-hero__text");
            var title = new Label("Socket 多人联机");
            title.AddToClassList("mp-hero__title");
            var subtitle = new Label("房间 / 玩法 / 网络策略的统一配置入口");
            subtitle.AddToClassList("mp-hero__subtitle");
            text.Add(title);
            text.Add(subtitle);
            hero.Add(text);

            var pill = new Label($"协议 V{MultiplayerProtocol.Version}");
            pill.AddToClassList("mp-pill");
            hero.Add(pill);
            return hero;
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

        private enum ButtonKind
        {
            Default,
            Primary,
            Success,
            Danger
        }

        private static Button ActionButton(string text, Action callback, ButtonKind kind = ButtonKind.Default, bool wide = false)
        {
            var button = new Button(callback) { text = text };
            button.AddToClassList("mp-btn");
            if (kind == ButtonKind.Primary) button.AddToClassList("mp-btn--primary");
            else if (kind == ButtonKind.Success) button.AddToClassList("mp-btn--success");
            else if (kind == ButtonKind.Danger) button.AddToClassList("mp-btn--danger");
            if (wide) button.AddToClassList("mp-btn--wide");
            return button;
        }
    }
}
#endif
