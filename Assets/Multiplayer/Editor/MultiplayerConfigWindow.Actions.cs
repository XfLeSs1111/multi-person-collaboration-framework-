#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Socket.Multiplayer.Editor
{
    /// <summary>配置中心：房间模板缺省页、规则页、配置检查页与操作实现。</summary>
    internal sealed partial class MultiplayerConfigWindow
    {
        private VisualElement BuildMissingTemplate()
        {
            var page = new ScrollView { style = { flexGrow = 1 } };
            page.Add(MultiplayerInspectorUtility.PageTitle("房间模板"));
            page.Add(MultiplayerInspectorUtility.Info("当前配置尚未绑定模板。创建模板后，可以可视化配置人数、玩家出生点以及房间交互物。"));
            page.Add(ActionButton("创建并绑定默认模板", CreateOrSelectRoomTemplate));
            return page;
        }

        private VisualElement BuildRulePage()
        {
            var rules = config == null || config.defaultRoomTemplate == null
                ? null
                : config.defaultRoomTemplate.rules;
            if (rules == null)
            {
                var page = new ScrollView { style = { flexGrow = 1 } };
                page.Add(MultiplayerInspectorUtility.PageTitle("玩法规则"));
                page.Add(MultiplayerInspectorUtility.Info("尚未绑定玩法规则资产。在“房间模板 → 基础”页绑定规则资产后，这里会显示该玩法的规则字段与可视化预览。"));
                return page;
            }

            return HostEditor(EditorFor(rules, ref ruleEditor));
        }

        private VisualElement BuildValidation()
        {
            var page = new ScrollView { style = { flexGrow = 1 } };
            page.Add(MultiplayerInspectorUtility.PageTitle("配置检查"));

            if (config == null)
            {
                page.Add(MultiplayerInspectorUtility.Info("未加载配置资源。", HelpBoxMessageType.Warning));
                return page;
            }

            var issues = MultiplayerConfigEditor.GetValidationIssues(config);
            if (issues.Count == 0)
            {
                page.Add(MultiplayerInspectorUtility.Info("所有关键配置均有效，可以启动多人联机。"));
                return page;
            }

            foreach (var issue in issues)
                page.Add(MultiplayerInspectorUtility.Info(issue, HelpBoxMessageType.Warning));
            page.Add(ActionButton("重新检查", () => SelectTab(activeTab)));
            return page;
        }

        private void SaveAll()
        {
            if (config == null) return;
            EditorUtility.SetDirty(config);
            if (config.defaultRoomTemplate != null) EditorUtility.SetDirty(config.defaultRoomTemplate);
            if (config.defaultRoomTemplate != null && config.defaultRoomTemplate.rules != null)
                EditorUtility.SetDirty(config.defaultRoomTemplate.rules);
            AssetDatabase.SaveAssets();
            ShowNotification(new GUIContent("配置已保存"));
        }

        private void ShowValidationDialog()
        {
            if (config == null) return;
            var issues = MultiplayerConfigEditor.GetValidationIssues(config);
            var message = issues.Count == 0
                ? "配置检查通过，可以启动多人联机。"
                : string.Join("\n", issues);
            EditorUtility.DisplayDialog("多人联机配置检查", message, "确定");
            SelectTab(activeTab);
        }

        private void OpenBootstrapScene()
        {
            if (config == null || string.IsNullOrWhiteSpace(config.offlineScene)) return;
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(config.offlineScene);
            if (scene == null)
            {
                EditorUtility.DisplayDialog("无法打开场景", "离线启动场景路径无效，请先修正配置。", "确定");
                return;
            }

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(config.offlineScene);
        }

        private void CreateOrSelectRoomTemplate()
        {
            if (config == null) return;
            if (config.defaultRoomTemplate == null)
            {
                var template = AssetDatabase.LoadAssetAtPath<RoomTemplate>(TemplatePath);
                if (template == null)
                {
                    template = CreateInstance<RoomTemplate>();
                    AssetDatabase.CreateAsset(template, TemplatePath);
                }

                config.defaultRoomTemplate = template;
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
            }

            Selection.activeObject = config.defaultRoomTemplate;
            EditorGUIUtility.PingObject(config.defaultRoomTemplate);
            SelectTab(activeTab);
        }
    }
}
#endif
