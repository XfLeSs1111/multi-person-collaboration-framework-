using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Socket.Multiplayer
{
    [Serializable]
    public sealed class RoomPlayerSpawn
    {
        [Tooltip("仅用于策划识别。")]
        public string name = "出生点";

        public Vector3 position;

        public Vector3 eulerAngles;
    }

    [Serializable]
    public sealed class RoomInteractableSpawn
    {
        public NetworkInteractable prefab;

        public Vector3 position;

        public Vector3 eulerAngles;
    }

    /// <summary>房间静态内容模板：人数、出生点、交互物布局与玩法规则资产。</summary>
    [CreateAssetMenu(menuName = "Socket/房间模板", fileName = "RoomTemplate")]
    public sealed class RoomTemplate : ScriptableObject
    {
        // ───────────────── 基础 ─────────────────
        [Tooltip("用于策划识别模板，不会替代玩家创建的房间名称。")]
        public string templateName = "默认房间";

        [TextArea(2, 4)] public string description = "标准多人房间";

        [Min(1)] public int minPlayers = 1;

        [Min(1)] public int maxPlayers = 8;

        [Min(0)] public int maxSpectators = 20;

        [FormerlySerializedAs("gomokuRules")]
        [Tooltip("绑定玩法规则资产（如五子棋规则）；未绑定时由玩法适配器使用默认规则。")]
        public MatchRulesConfig rules;

        // ───────────────── 出生点 ─────────────────
        [Tooltip("玩家按列表顺序出生；人数超过列表长度时，从第一个点按全局间距自动延伸。")]
        public RoomPlayerSpawn[] playerSpawns =
        {
            new RoomPlayerSpawn { name = "玩家 1", position = new Vector3(-3f, 0f, 0f) },
            new RoomPlayerSpawn { name = "玩家 2", position = new Vector3(-1f, 0f, 0f) },
            new RoomPlayerSpawn { name = "玩家 3", position = new Vector3(1f, 0f, 0f) },
            new RoomPlayerSpawn { name = "玩家 4", position = new Vector3(3f, 0f, 0f) }
        };

        // ───────────────── 交互物 ─────────────────
        [Tooltip("房间开始时由服务器生成，并自动绑定该房间的 MatchInterestManagement 可见性。")]
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
