#if UNITY_EDITOR
using System.Collections.Generic;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Socket.Multiplayer.Editor
{
    [CustomEditor(typeof(MultiplayerConfig))]
    internal sealed class MultiplayerConfigEditor : OdinEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            DrawValidation((MultiplayerConfig)target);
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

        private static void DrawValidation(MultiplayerConfig config)
        {
            var issues = GetValidationIssues(config);
            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("配置检查", EditorStyles.boldLabel);

            if (issues.Count == 0)
                EditorGUILayout.HelpBox("配置完整，可以用于启动多人联机。", MessageType.Info);
            else
                foreach (var issue in issues) EditorGUILayout.HelpBox(issue, MessageType.Warning);
        }

        private static bool IsBuildScene(string path)
        {
            foreach (var scene in UnityEditor.EditorBuildSettings.scenes)
                if (scene.enabled && scene.path == path)
                    return true;
            return false;
        }
    }
}
#endif
