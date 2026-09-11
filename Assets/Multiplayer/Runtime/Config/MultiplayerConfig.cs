using UnityEngine;

namespace Socket.Multiplayer
{
    public enum NetworkStartMode
    {
        Manual,
        Host,
        Client,
        Server
    }

    /// <summary>
    /// 多人联机全局配置。策略性取值全部集中在这里，运行时由 SocketRoomManager 消费。
    /// 已从 Odin 迁移到 Unity 内置特性 + UI Toolkit 自定义检查器（见 <see cref="Socket.Multiplayer.Editor.MultiplayerConfigEditor"/>）。
    /// </summary>
    [CreateAssetMenu(menuName = "Socket/多人联机配置", fileName = "MultiplayerConfig")]
    public sealed class MultiplayerConfig : ScriptableObject
    {
        // ───────────────── 房间 ─────────────────
        [Tooltip("新建房间使用的静态内容模板；未配置时继续使用兼容参数。")]
        public RoomTemplate defaultRoomTemplate;

        [Tooltip("创建房间时使用的默认显示名称。")]
        public string roomName = "Socket Room";

        [Tooltip("仅在未配置默认房间模板时使用。")]
        [Min(1)] public int minPlayers = 1;

        [Tooltip("仅在未配置默认房间模板时使用。")]
        [Min(1)] public int maxPlayers = 4;

        [Min(1)] public int maxRooms = 16;

        [Tooltip("整个服务器允许同时连接的玩家数量。它应该大于或等于单个房间的最多人数。")]
        [Min(1)] public int maxServerPlayers = 64;

        [Min(0)] public int maxSpectators = 20;

        [Tooltip("房内长时间无操作自动回收该房间并让成员返回大厅（0 表示关闭）。空房（0 成员）仍会立即删除。")]
        [Min(0f)] public float roomIdleTimeout = 120f;

        [Tooltip("玩家掉线后保留其座位与显示名称的时长，期间用同名重连可回到原房间（0 表示关闭重连）。")]
        [Min(0f)] public float reconnectWindow = 60f;

        [Tooltip("单回合思考时间上限：超时由服务器判定当前行动方负（0 表示关闭计时）。")]
        [Min(0f)] public float turnTimeoutSeconds = 60f;

        [Tooltip("每个房间保留的历史对局记录条数，供结束后查看战绩（0 = 不记录）。")]
        [Min(0)] public int matchRecordLimit = 10;

        [Tooltip("随战绩保存该局的落子序列；客户端可在本地逐步重放（关闭则只留结果）。")]
        public bool recordReplays = true;

        [Tooltip("房间内所有玩家准备完成后，由服务器自动开始游戏。")]
        public bool autoStartWhenAllReady;

        [Tooltip("允许玩家在房间已经开始后加入；所有房间仍位于同一个物理在线场景中。")]
        public bool allowLateJoiners;

        // ───────────────── 网络 ─────────────────
        [Tooltip("客户端未指定服务器时使用的连接地址。")]
        public string defaultAddress = "localhost";

        public ushort port = 7777;

        public NetworkStartMode defaultStartMode = NetworkStartMode.Manual;

        [Min(1)] public int sendRate = 30;

        [Tooltip("命令行或连接界面未填写名称时使用。")]
        public string defaultPlayerName = "Player";

        [Tooltip("局域网服务器停止发送心跳后，在发现列表中继续保留的时间。")]
        [Min(0.5f)] public float serverTtl = 3f;

        [Tooltip("创建/加入/准备/开始/返回等房间消息的单连接速率上限。")]
        [Min(0.5f)] public float roomCommandPerSecond = 2f;

        [Min(0.5f)] public float chatPerSecond = 2f;

        [Min(0.5f)] public float interactPerSecond = 2f;

        [Tooltip("落子等对局命令的令牌桶速率；规则拒绝依然生效，此处只防刷。")]
        [Min(0.5f)] public float gameCommandPerSecond = 4f;

        // ───────────────── 场景 ─────────────────
        [Tooltip("停止主机、服务器或客户端后返回的 Bootstrap 场景。")]
        public string offlineScene = "Bootstrap";

        [Tooltip("服务器启动后加载的唯一物理在线场景。")]
        public string lobbyScene = "SampleScene";

        // ───────────────── 控制 ─────────────────
        [Min(0.1f)] public float moveSpeed = 4f;

        [Range(1f, 60f)] public float inputSendRate = 20f;

        [Min(0.5f)] public float spawnSpacing = 2f;

        // ───────────────── 交互 ─────────────────
        [Min(0.1f)] public float interactionRange = 3f;

        [Min(0.1f)] public float interactionLeaseSeconds = 10f;

        [Tooltip("可交互对象首次成功使用后立即禁用。")]
        public bool interactablesToggleOffAfterUse;

        // ───────────────── 界面 ─────────────────
        [Tooltip("勾选后与新界面并存，仅用于调试排障。")]
        public bool useLegacyImGuiHud;

        // ───────────────── 计算属性 ─────────────────
        public int EffectiveMinPlayers => defaultRoomTemplate == null
            ? Mathf.Clamp(minPlayers, 1, EffectiveMaxPlayers)
            : defaultRoomTemplate.EffectiveMinPlayers;

        public int EffectiveMaxPlayers => defaultRoomTemplate == null
            ? Mathf.Clamp(maxPlayers, 1, 128)
            : defaultRoomTemplate.EffectiveMaxPlayers;

        public int ServerConnectionLimit => Mathf.Max(1, Mathf.Max(EffectiveMaxPlayers, maxServerPlayers));

        /// <summary>未绑定房间模板时使用兼容人数参数（配置中心据此显示/隐藏相关字段）。</summary>
        public bool UsesLegacyRoomRules => defaultRoomTemplate == null;

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
            roomIdleTimeout = Mathf.Max(0f, roomIdleTimeout);
            reconnectWindow = Mathf.Max(0f, reconnectWindow);
            turnTimeoutSeconds = Mathf.Max(0f, turnTimeoutSeconds);
            matchRecordLimit = Mathf.Clamp(matchRecordLimit, 0, 100);
            roomCommandPerSecond = Mathf.Max(0.5f, roomCommandPerSecond);
            chatPerSecond = Mathf.Max(0.5f, chatPerSecond);
            interactPerSecond = Mathf.Max(0.5f, interactPerSecond);
            gameCommandPerSecond = Mathf.Max(0.5f, gameCommandPerSecond);
        }
    }
}
