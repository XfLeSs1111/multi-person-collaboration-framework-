#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Socket.Multiplayer.Editor
{
    /// <summary>MultiplayerConfig 的 UI Toolkit 检查器：页签分区 + 实时摘要 + 配置检查。</summary>
    [CustomEditor(typeof(MultiplayerConfig))]
    internal sealed class MultiplayerConfigEditor : UnityEditor.Editor, IEmbeddedInspector
    {
        private static readonly string[] TabNames = { "房间", "网络", "场景", "控制", "交互", "界面" };

        private bool embedded;

        public void SetEmbeddedLayout() => embedded = true;

        private VisualElement[] panes;
        private VisualElement legacyGroup;
        private VisualElement validationHost;
        private Label capacityLabel;
        private Label protocolLabel;

        private MultiplayerConfig Config => (MultiplayerConfig)target;

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

            BuildRoomPane(panes[0]);
            BuildNetworkPane(panes[1]);
            BuildScenePane(panes[2]);
            BuildControlPane(panes[3]);
            BuildInteractionPane(panes[4]);
            BuildInterfacePane(panes[5]);

            validationHost = new VisualElement { style = { marginTop = 8 } };
            root.Add(validationHost);

            root.Bind(serializedObject);
            MultiplayerInspectorUtility.ApplyLabels(root);
            root.TrackPropertyValue(serializedObject.FindProperty("defaultRoomTemplate"), _ => UpdateLegacyVisibility());
            root.TrackSerializedObjectValue(serializedObject, _ =>
            {
                UpdateLegacyVisibility();
                RefreshSummaries();
                RefreshValidation();
            });

            SwitchTab(0);
            UpdateLegacyVisibility();
            RefreshSummaries();
            RefreshValidation();
            return root;
        }

        private void SwitchTab(int active)
        {
            for (var i = 0; i < panes.Length; i++)
                panes[i].style.display = embedded || i == active ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateLegacyVisibility()
        {
            if (legacyGroup == null) return;
            legacyGroup.style.display = Config.UsesLegacyRoomRules ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void BuildRoomPane(VisualElement pane)
        {
            var so = serializedObject;

            pane.Add(MultiplayerInspectorUtility.Field(so, "defaultRoomTemplate", "默认房间模板"));

            legacyGroup = new VisualElement();
            legacyGroup.Add(MultiplayerInspectorUtility.Hint("未绑定房间模板时，使用下面的兼容人数与自动排列的出生点。"));
            legacyGroup.Add(MultiplayerInspectorUtility.Row(
                MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "minPlayers", "兼容最少人数"), "人"),
                MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "maxPlayers", "兼容最多人数"), "人")));
            pane.Add(legacyGroup);

            pane.Add(MultiplayerInspectorUtility.Field(so, "roomName", "房间名称"));
            pane.Add(MultiplayerInspectorUtility.Row(
                MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "maxRooms", "最大房间数"), "个"),
                MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "maxServerPlayers", "服务器最大连接数"), "人")));
            pane.Add(MultiplayerInspectorUtility.Row(
                MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "maxSpectators", "最大观战人数"), "人"),
                MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "roomIdleTimeout", "闲置房回收时间"), "秒")));

            capacityLabel = MultiplayerInspectorUtility.Summary("容量摘要", string.Empty, out var capacityRow);
            pane.Add(capacityRow);

            pane.Add(MultiplayerInspectorUtility.Row(
                MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "reconnectWindow", "断线重连窗口"), "秒"),
                MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "turnTimeoutSeconds", "回合思考时限"), "秒")));
            pane.Add(MultiplayerInspectorUtility.Row(
                MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "matchRecordLimit", "保存战绩条数"), "条"),
                MultiplayerInspectorUtility.Field(so, "recordReplays", "保存回放数据")));
            pane.Add(MultiplayerInspectorUtility.Field(so, "autoStartWhenAllReady", "全员准备后自动开始"));
            pane.Add(MultiplayerInspectorUtility.Field(so, "allowLateJoiners", "允许中途加入"));
        }

        private void BuildNetworkPane(VisualElement pane)
        {
            var so = serializedObject;

            pane.Add(MultiplayerInspectorUtility.Row(
                MultiplayerInspectorUtility.Field(so, "defaultAddress", "默认地址"),
                MultiplayerInspectorUtility.Field(so, "port", "KCP 端口")));

            pane.Add(MultiplayerInspectorUtility.EnumDropdown<NetworkStartMode>(
                so, "defaultStartMode", "启动模式",
                new[] { "手动启动", "主机模式", "客户端模式", "专用服务器" }));

            pane.Add(MultiplayerInspectorUtility.Row(
                MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "sendRate", "发送频率"), "次/秒"),
                MultiplayerInspectorUtility.Field(so, "defaultPlayerName", "默认玩家名")));

            pane.Add(MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "serverTtl", "局域网服务器保留时间"), "秒"));

            protocolLabel = MultiplayerInspectorUtility.Summary("协议版本", string.Empty, out var protocolRow);
            pane.Add(protocolRow);

            pane.Add(MultiplayerInspectorUtility.Hint("服务端限频：各入口每连接的令牌桶速率，容量为速率的两倍以吸收点击突发。"));
            pane.Add(MultiplayerInspectorUtility.Row(
                MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "roomCommandPerSecond", "房间操作/秒"), "次/秒"),
                MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "chatPerSecond", "聊天/秒"), "次/秒")));
            pane.Add(MultiplayerInspectorUtility.Row(
                MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "interactPerSecond", "交互/秒"), "次/秒"),
                MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "gameCommandPerSecond", "对局命令/秒"), "次/秒")));
        }

        private void BuildScenePane(VisualElement pane)
        {
            var so = serializedObject;
            pane.Add(MultiplayerInspectorUtility.Hint("当前架构只加载一个物理在线场景，各房间通过 MatchInterestManagement 隔离可见性。"));
            pane.Add(MultiplayerInspectorUtility.Field(so, "offlineScene", "离线启动场景"));
            pane.Add(MultiplayerInspectorUtility.Field(so, "lobbyScene", "在线大厅场景"));
        }

        private void BuildControlPane(VisualElement pane)
        {
            var so = serializedObject;
            pane.Add(MultiplayerInspectorUtility.Hint("移动由服务器执行，客户端按输入频率提交方向。"));
            pane.Add(MultiplayerInspectorUtility.Row(
                MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "moveSpeed", "移动速度"), "米/秒"),
                MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "inputSendRate", "输入频率"), "次/秒")));
            pane.Add(MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "spawnSpacing", "出生点延伸间距"), "米"));
        }

        private void BuildInteractionPane(VisualElement pane)
        {
            var so = serializedObject;
            pane.Add(MultiplayerInspectorUtility.Hint("交互对象的具体预制体和位置在房间模板中配置。"));
            pane.Add(MultiplayerInspectorUtility.Row(
                MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "interactionRange", "交互距离"), "米"),
                MultiplayerInspectorUtility.WithUnit(MultiplayerInspectorUtility.Field(so, "interactionLeaseSeconds", "占用时长"), "秒")));
            pane.Add(MultiplayerInspectorUtility.Field(so, "interactablesToggleOffAfterUse", "使用后关闭"));
        }

        private void BuildInterfacePane(VisualElement pane)
        {
            var so = serializedObject;
            pane.Add(MultiplayerInspectorUtility.Hint("新版可视化界面（uGUI）由 MultiplayerUi 运行时构建；旧版 IMGUI 面板仅作调试后备。"));
            pane.Add(MultiplayerInspectorUtility.Field(so, "useLegacyImGuiHud", "保留旧版 IMGUI 面板"));
        }

        private void RefreshSummaries()
        {
            var config = Config;
            if (config == null) return;
            if (capacityLabel != null)
                capacityLabel.text = $"{config.maxRooms} 个房间 / 单房 {config.EffectiveMaxPlayers} 人 / 整服 {config.ServerConnectionLimit} 人";
            if (protocolLabel != null)
                protocolLabel.text = $"V{MultiplayerProtocol.Version} / 配置签名 {MultiplayerProtocol.GetConfigSignature(config)}";
        }

        private void RefreshValidation()
        {
            if (validationHost == null) return;
            validationHost.Clear();
            var issues = GetValidationIssues(Config);
            if (issues.Count == 0)
            {
                validationHost.Add(MultiplayerInspectorUtility.Info("配置完整，可以用于启动多人联机。"));
                return;
            }

            validationHost.Add(MultiplayerInspectorUtility.Title($"配置检查（{issues.Count} 项待处理）"));
            foreach (var issue in issues)
                validationHost.Add(MultiplayerInspectorUtility.Info(issue, HelpBoxMessageType.Warning));
        }

        internal static List<string> GetValidationIssues(MultiplayerConfig config)
        {
            var issues = new List<string>();
            if (config == null)
            {
                issues.Add("未选择多人联机配置资源。");
                return issues;
            }

            if (string.IsNullOrWhiteSpace(config.roomName)) issues.Add("房间名称不能为空。");
            if (string.IsNullOrWhiteSpace(config.lobbyScene)) issues.Add("尚未配置在线大厅场景。");
            if (string.IsNullOrWhiteSpace(config.offlineScene)) issues.Add("尚未配置离线启动场景。");
            if (config.defaultRoomTemplate == null)
            {
                issues.Add("尚未配置默认房间模板，运行时将使用兼容参数和默认交互物布局。");
                if (config.minPlayers > config.maxPlayers) issues.Add("兼容最少人数不能大于兼容最多人数。");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(config.defaultRoomTemplate.templateName)) issues.Add("默认房间模板名称不能为空。");
                if (config.defaultRoomTemplate.minPlayers > config.defaultRoomTemplate.maxPlayers) issues.Add("房间模板的最少人数不能大于最多人数。");
                if (config.defaultRoomTemplate.PlayerSpawnCount == 0) issues.Add("房间模板没有配置玩家出生点，将从世界原点自动排列。");
            }

            if (config.EffectiveMinPlayers > config.EffectiveMaxPlayers) issues.Add("最少人数不能大于最多人数。");
            if (config.maxServerPlayers < config.EffectiveMaxPlayers) issues.Add("服务器最大连接数不能小于单个房间最多人数。");
            if (config.maxServerPlayers < config.maxRooms) issues.Add("服务器最大连接数小于最大房间数，部分房间可能无法容纳玩家。");
            if (!IsBuildScene(config.lobbyScene)) issues.Add("在线大厅场景未加入 Build Settings 或未启用。");
            if (!IsBuildScene(config.offlineScene)) issues.Add("离线启动场景未加入 Build Settings 或未启用。");
            return issues;
        }

        private static bool IsBuildScene(string path)
        {
            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.enabled && scene.path == path)
                    return true;
            return false;
        }
    }
}
#endif
