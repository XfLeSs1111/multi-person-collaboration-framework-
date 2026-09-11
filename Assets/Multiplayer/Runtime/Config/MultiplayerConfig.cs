using Mirror;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Socket.Multiplayer
{
    public enum NetworkStartMode
    {
        [LabelText("手动启动")]
        Manual,
        [LabelText("主机模式")]
        Host,
        [LabelText("客户端模式")]
        Client,
        [LabelText("专用服务器")]
        Server
    }

    [CreateAssetMenu(menuName = "Socket/多人联机配置", fileName = "MultiplayerConfig")]
    public sealed class MultiplayerConfig : ScriptableObject
    {
        [TabGroup("配置分区", "房间")]
        [InfoBox("房间模板负责人数、出生点和交互物布局；未绑定模板时才会使用兼容人数。")]
        [LabelText("默认房间模板"), AssetsOnly, Required]
        [Tooltip("新建房间使用的静态内容模板；未配置时继续使用兼容参数。")]
        public RoomTemplate defaultRoomTemplate;

        [TabGroup("配置分区", "房间")]
        [LabelText("房间名称"), Tooltip("创建房间时使用的默认显示名称。")]
        public string roomName = "Socket Room";

        [TabGroup("配置分区", "房间")]
        [HorizontalGroup("配置分区/房间/兼容人数", LabelWidth = 88)]
        [ShowIf(nameof(UsesLegacyRoomRules))]
        [LabelText("兼容最少人数"), MinValue(1), SuffixLabel("人")]
        [Tooltip("仅在未配置默认房间模板时使用。")]
        public int minPlayers = 1;

        [TabGroup("配置分区", "房间")]
        [HorizontalGroup("配置分区/房间/兼容人数", LabelWidth = 88)]
        [ShowIf(nameof(UsesLegacyRoomRules))]
        [LabelText("兼容最多人数"), MinValue(1), SuffixLabel("人")]
        [Tooltip("仅在未配置默认房间模板时使用。")]
        public int maxPlayers = 4;

        [TabGroup("配置分区", "房间")]
        [HorizontalGroup("配置分区/房间/服务容量", LabelWidth = 88)]
        [LabelText("最大房间数"), MinValue(1), SuffixLabel("个")]
        public int maxRooms = 16;

        [TabGroup("配置分区", "房间")]
        [HorizontalGroup("配置分区/房间/服务容量", LabelWidth = 88)]
        [LabelText("服务器最大连接数"), MinValue(1), SuffixLabel("人")]
        [Tooltip("整个服务器允许同时连接的玩家数量。它应该大于或等于单个房间的最多人数。")]
        public int maxServerPlayers = 64;

        [TabGroup("配置分区", "房间")]
        [HorizontalGroup("配置分区/房间/服务容量", LabelWidth = 88)]
        [LabelText("最大观战人数"), MinValue(0), SuffixLabel("人")]
        public int maxSpectators = 20;

        [TabGroup("配置分区", "房间")]
        [Sirenix.OdinInspector.ShowInInspector, Sirenix.OdinInspector.ReadOnly, LabelText("容量摘要")]
        private string CapacitySummary => $"{maxRooms} 个房间 / 单房 {EffectiveMaxPlayers} 人 / 整服 {ServerConnectionLimit} 人";

        public int EffectiveMinPlayers => defaultRoomTemplate == null
            ? Mathf.Clamp(minPlayers, 1, EffectiveMaxPlayers)
            : defaultRoomTemplate.EffectiveMinPlayers;

        public int EffectiveMaxPlayers => defaultRoomTemplate == null
            ? Mathf.Clamp(maxPlayers, 1, 128)
            : defaultRoomTemplate.EffectiveMaxPlayers;

        public int ServerConnectionLimit => Mathf.Max(1, Mathf.Max(EffectiveMaxPlayers, maxServerPlayers));

        private bool UsesLegacyRoomRules => defaultRoomTemplate == null;

        [TabGroup("配置分区", "房间")]
        [HorizontalGroup("配置分区/房间/房间规则")]
        [ToggleLeft]
        [LabelText("全员准备后自动开始"), Tooltip("房间内所有玩家准备完成后，由服务器自动开始游戏。")]
        public bool autoStartWhenAllReady;

        [TabGroup("配置分区", "房间")]
        [HorizontalGroup("配置分区/房间/房间规则")]
        [ToggleLeft]
        [LabelText("允许中途加入"), Tooltip("允许玩家在房间已经开始后加入；所有房间仍位于同一个物理在线场景中。")]
        public bool allowLateJoiners;

        [TabGroup("配置分区", "网络")]
        [InfoBox("这里控制 Mirror 连接入口、KCP 端口和网络发送频率。")]
        [HorizontalGroup("配置分区/网络/连接", LabelWidth = 72)]
        [LabelText("默认地址"), Tooltip("客户端未指定服务器时使用的连接地址。")]
        public string defaultAddress = "localhost";

        [TabGroup("配置分区", "网络")]
        [HorizontalGroup("配置分区/网络/连接", LabelWidth = 72)]
        [LabelText("KCP 端口"), MinValue(1)]
        public ushort port = 7777;

        [TabGroup("配置分区", "网络")]
        [HorizontalGroup("配置分区/网络/模式", LabelWidth = 72)]
        [LabelText("启动模式")]
        public NetworkStartMode defaultStartMode = NetworkStartMode.Manual;

        [TabGroup("配置分区", "网络")]
        [HorizontalGroup("配置分区/网络/模式", LabelWidth = 72)]
        [LabelText("发送频率"), MinValue(1), SuffixLabel("次/秒")]
        public int sendRate = 30;

        [TabGroup("配置分区", "网络")]
        [LabelText("默认玩家名"), Tooltip("命令行或连接界面未填写名称时使用。")]
        public string defaultPlayerName = "Player";

        [TabGroup("配置分区", "网络")]
        [Sirenix.OdinInspector.ShowInInspector, Sirenix.OdinInspector.ReadOnly, LabelText("协议版本")]
        private string ProtocolSummary => $"V{MultiplayerProtocol.Version} / 配置签名 {MultiplayerProtocol.GetConfigSignature(this)}";

        [TabGroup("配置分区", "网络")]
        [PropertySpace(SpaceBefore = 8)]
        [LabelText("局域网服务器保留时间"), MinValue(0.5f), SuffixLabel("秒")]
        [Tooltip("局域网服务器停止发送心跳后，在发现列表中继续保留的时间。")]
        public float serverTtl = 3f;

        [TabGroup("配置分区", "网络")]
        [PropertySpace(SpaceBefore = 8)]
        [InfoBox("服务端限频（M3-S.5）：各入口每连接的令牌桶速率，容量为速率的两倍以吸收点击突发。")]
        [HorizontalGroup("配置分区/网络/服务端限频", LabelWidth = 88)]
        [LabelText("房间操作/秒"), MinValue(0.5f), SuffixLabel("次/秒")]
        [Tooltip("创建/加入/准备/开始/返回等房间消息的单连接速率上限。")]
        public float roomCommandPerSecond = 2f;

        [TabGroup("配置分区", "网络")]
        [HorizontalGroup("配置分区/网络/服务端限频", LabelWidth = 88)]
        [LabelText("聊天/秒"), MinValue(0.5f), SuffixLabel("次/秒")]
        public float chatPerSecond = 2f;

        [TabGroup("配置分区", "网络")]
        [HorizontalGroup("配置分区/网络/服务端限频", LabelWidth = 88)]
        [LabelText("交互/秒"), MinValue(0.5f), SuffixLabel("次/秒")]
        public float interactPerSecond = 2f;

        [TabGroup("配置分区", "网络")]
        [HorizontalGroup("配置分区/网络/服务端限频", LabelWidth = 88)]
        [LabelText("对局命令/秒"), MinValue(0.5f), SuffixLabel("次/秒")]
        [Tooltip("落子等对局命令的令牌桶速率；规则拒绝依然生效，此处只防刷。")]
        public float gameCommandPerSecond = 4f;

        [TabGroup("配置分区", "场景")]
        [InfoBox("当前架构只加载一个物理在线场景，各房间通过 MatchInterestManagement 隔离可见性。")]
        [LabelText("离线启动场景"), Scene, Tooltip("停止主机、服务器或客户端后返回的 Bootstrap 场景。")]
        public string offlineScene = "Bootstrap";

        [TabGroup("配置分区", "场景")]
        [LabelText("在线大厅场景"), Scene, Tooltip("服务器启动后加载的唯一物理在线场景。")]
        public string lobbyScene = "SampleScene";

        [TabGroup("配置分区", "控制")]
        [InfoBox("移动由服务器执行，客户端按输入频率提交方向。")]
        [HorizontalGroup("配置分区/控制/移动", LabelWidth = 72)]
        [LabelText("移动速度"), MinValue(0.1f), SuffixLabel("米/秒")]
        public float moveSpeed = 4f;

        [TabGroup("配置分区", "控制")]
        [HorizontalGroup("配置分区/控制/移动", LabelWidth = 72)]
        [LabelText("输入频率"), PropertyRange(1f, 60f), SuffixLabel("次/秒")]
        public float inputSendRate = 20f;

        [TabGroup("配置分区", "控制")]
        [LabelText("出生点延伸间距"), MinValue(0.5f), SuffixLabel("米")]
        public float spawnSpacing = 2f;

        [TabGroup("配置分区", "交互")]
        [InfoBox("交互对象的具体预制体和位置在房间模板中配置。")]
        [HorizontalGroup("配置分区/交互/参数", LabelWidth = 72)]
        [LabelText("交互距离"), MinValue(0.1f), SuffixLabel("米")]
        public float interactionRange = 3f;

        [TabGroup("配置分区", "交互")]
        [HorizontalGroup("配置分区/交互/参数", LabelWidth = 72)]
        [LabelText("占用时长"), MinValue(0.1f), SuffixLabel("秒")]
        public float interactionLeaseSeconds = 10f;

        [TabGroup("配置分区", "交互")]
        [ToggleLeft]
        [LabelText("使用后关闭"), Tooltip("可交互对象首次成功使用后立即禁用。")]
        public bool interactablesToggleOffAfterUse;

        [TabGroup("配置分区", "界面")]
        [InfoBox("新版可视化界面（uGUI）由 MultiplayerUi 运行时构建；旧版 IMGUI 面板仅作调试后备。")]
        [ToggleLeft]
        [LabelText("保留旧版 IMGUI 面板"), Tooltip("勾选后与新界面并存，仅用于调试排障。")]
        public bool useLegacyImGuiHud;

        private void OnValidate()
        {
            maxPlayers = Mathf.Clamp(maxPlayers, 1, 128);
            minPlayers = Mathf.Clamp(minPlayers, 1, maxPlayers);
            maxRooms = Mathf.Clamp(maxRooms, 1, 256);
            maxServerPlayers = Mathf.Clamp(maxServerPlayers, EffectiveMaxPlayers, 1024);
            maxSpectators = Mathf.Clamp(maxSpectators, 0, 256);
            inputSendRate = Mathf.Clamp(inputSendRate, 1f, 60f);
            sendRate = Mathf.Clamp(sendRate, 1, 120);
            moveSpeed = Mathf.Max(0.1f, moveSpeed);
            interactionRange = Mathf.Max(0.1f, interactionRange);
            interactionLeaseSeconds = Mathf.Max(0.1f, interactionLeaseSeconds);
            serverTtl = Mathf.Max(0.5f, serverTtl);
            spawnSpacing = Mathf.Max(0.5f, spawnSpacing);
            roomCommandPerSecond = Mathf.Max(0.5f, roomCommandPerSecond);
            chatPerSecond = Mathf.Max(0.5f, chatPerSecond);
            interactPerSecond = Mathf.Max(0.5f, interactPerSecond);
            gameCommandPerSecond = Mathf.Max(0.5f, gameCommandPerSecond);
        }
    }
}
