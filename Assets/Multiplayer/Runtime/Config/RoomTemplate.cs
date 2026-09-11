using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Socket.Multiplayer
{
    [Serializable]
    public sealed class RoomPlayerSpawn
    {
        [TableColumnWidth(110, Resizable = false)]
        [LabelText("名称")]
        public string name = "出生点";

        [LabelText("位置")]
        public Vector3 position;

        [LabelText("旋转")]
        public Vector3 eulerAngles;
    }

    [Serializable]
    public sealed class RoomInteractableSpawn
    {
        [LabelText("交互物预制体"), AssetsOnly]
        public NetworkInteractable prefab;

        [LabelText("位置")]
        public Vector3 position;

        [LabelText("旋转")]
        public Vector3 eulerAngles;
    }

    [CreateAssetMenu(menuName = "Socket/房间模板", fileName = "RoomTemplate")]
    public sealed class RoomTemplate : ScriptableObject
    {
        [TabGroup("模板分区", "基础")]
        [InfoBox("模板只保存静态策划数据；运行中的玩家、准备状态和房间阶段仍由服务器维护。")]
        [LabelText("模板名称"), Required, Tooltip("用于策划识别模板，不会替代玩家创建的房间名称。")]
        public string templateName = "默认房间";

        [TabGroup("模板分区", "基础")]
        [LabelText("模板说明"), TextArea(2, 4)]
        public string description = "标准多人房间";

        [TabGroup("模板分区", "基础")]
        [HorizontalGroup("模板分区/基础/人数", LabelWidth = 72)]
        [LabelText("最少人数"), MinValue(1), SuffixLabel("人")]
        public int minPlayers = 1;

        [TabGroup("模板分区", "基础")]
        [HorizontalGroup("模板分区/基础/人数", LabelWidth = 72)]
        [LabelText("最多人数"), MinValue(1), SuffixLabel("人")]
        public int maxPlayers = 8;

        [TabGroup("模板分区", "基础")]
        [LabelText("最大观战人数"), MinValue(0), SuffixLabel("人")]
        public int maxSpectators = 20;

        [TabGroup("模板分区", "基础")]
        [LabelText("五子棋规则"), AssetsOnly]
        [Tooltip("配置了该资源后，房间开始时会创建对应的五子棋对局。未配置时使用默认 15×15 五子棋规则。")]
        public GomokuRuleConfig gomokuRules;

        [TabGroup("模板分区", "基础")]
        [ShowInInspector, ReadOnly, LabelText("规则摘要")]
        private string RuleSummary => $"{EffectiveMinPlayers}-{EffectiveMaxPlayers} 人 / {PlayerSpawnCount} 个出生点 / {InteractableCount} 个交互物";

        [TabGroup("模板分区", "出生点")]
        [InfoBox("玩家按列表顺序出生；人数超过列表长度时，从第一个点按全局间距自动延伸。")]
        [TableList(AlwaysExpanded = true, DrawScrollView = false)]
        [LabelText("出生点列表")]
        public RoomPlayerSpawn[] playerSpawns =
        {
            new RoomPlayerSpawn { name = "玩家 1", position = new Vector3(-3f, 0f, 0f) },
            new RoomPlayerSpawn { name = "玩家 2", position = new Vector3(-1f, 0f, 0f) },
            new RoomPlayerSpawn { name = "玩家 3", position = new Vector3(1f, 0f, 0f) },
            new RoomPlayerSpawn { name = "玩家 4", position = new Vector3(3f, 0f, 0f) }
        };

        [TabGroup("模板分区", "交互物")]
        [InfoBox("房间开始时由服务器生成，并自动绑定该房间的 MatchInterestManagement 可见性。")]
        [TableList(AlwaysExpanded = true, DrawScrollView = false)]
        [LabelText("交互物列表")]
        public RoomInteractableSpawn[] interactables =
        {
            new RoomInteractableSpawn { position = new Vector3(-2f, 0.4f, 2f) },
            new RoomInteractableSpawn { position = new Vector3(0f, 0.4f, 2f) },
            new RoomInteractableSpawn { position = new Vector3(2f, 0.4f, 2f) }
        };

        public int EffectiveMinPlayers => Mathf.Clamp(minPlayers, 1, EffectiveMaxPlayers);
        public int EffectiveMaxPlayers => Mathf.Clamp(maxPlayers, 1, 128);
        public int PlayerSpawnCount => playerSpawns == null ? 0 : playerSpawns.Length;
        public int InteractableCount => interactables == null ? 0 : interactables.Length;

        public Pose GetPlayerSpawnPose(int playerIndex, float spacing)
        {
            playerIndex = Mathf.Max(0, playerIndex);
            spacing = Mathf.Max(0.5f, spacing);

            if (playerSpawns != null && playerIndex < playerSpawns.Length && playerSpawns[playerIndex] != null)
            {
                var spawn = playerSpawns[playerIndex];
                return new Pose(spawn.position, Quaternion.Euler(spawn.eulerAngles));
            }

            var origin = playerSpawns != null && playerSpawns.Length > 0 && playerSpawns[0] != null
                ? playerSpawns[0].position
                : Vector3.zero;
            var column = playerIndex % 4;
            var row = playerIndex / 4;
            var position = origin + new Vector3(column * spacing, 0f, -row * spacing);
            return new Pose(position, Quaternion.identity);
        }

        private void OnValidate()
        {
            maxPlayers = Mathf.Clamp(maxPlayers, 1, 128);
            minPlayers = Mathf.Clamp(minPlayers, 1, maxPlayers);
            maxSpectators = Mathf.Clamp(maxSpectators, 0, 256);
            if (playerSpawns == null) playerSpawns = Array.Empty<RoomPlayerSpawn>();
            if (interactables == null) interactables = Array.Empty<RoomInteractableSpawn>();
        }
    }
}
