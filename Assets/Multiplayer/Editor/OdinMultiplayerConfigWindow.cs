#if UNITY_EDITOR
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Socket.Multiplayer.Editor
{
    internal sealed class OdinMultiplayerConfigWindow : OdinMenuEditorWindow
    {
        private const string ConfigPath = "Assets/MultiplayerGenerated/MultiplayerConfig.asset";
        private const string TemplatePath = "Assets/MultiplayerGenerated/DefaultRoomTemplate.asset";

        [SerializeField, HideInInspector]
        private MultiplayerConfig config;

        protected override OdinMenuTree BuildMenuTree()
        {
            if (config == null) config = AssetDatabase.LoadAssetAtPath<MultiplayerConfig>(ConfigPath);

            var tree = new OdinMenuTree();
            tree.Selection.SupportsMultiSelect = false;
            tree.Add("总览", new DashboardPage(this), EditorIcons.House);

            if (config != null)
            {
                tree.Add("全局配置", config, EditorIcons.SettingsCog);
                tree.Add(
                    "房间模板",
                    config.defaultRoomTemplate == null
                        ? (object)new MissingTemplatePage(this)
                        : config.defaultRoomTemplate,
                    EditorIcons.GridBlocks);
                tree.Add("配置检查", new ValidationPage(this), HasIssues ? EditorIcons.AlertTriangle : EditorIcons.Checkmark);
            }

            return tree;
        }

        private bool HasIssues => MultiplayerConfigEditor.GetValidationIssues(config).Count > 0;

        private void SaveAll()
        {
            if (config == null) return;
            EditorUtility.SetDirty(config);
            if (config.defaultRoomTemplate != null) EditorUtility.SetDirty(config.defaultRoomTemplate);
            AssetDatabase.SaveAssets();
            ShowNotification(new GUIContent("配置已保存"));
        }

        private void ShowValidationDialog()
        {
            var issues = MultiplayerConfigEditor.GetValidationIssues(config);
            var message = issues.Count == 0
                ? "配置检查通过，可以启动多人联机。"
                : string.Join("\n", issues);
            EditorUtility.DisplayDialog("多人联机配置检查", message, "确定");
            ForceMenuTreeRebuild();
        }

        private void SelectConfig()
        {
            if (config == null) return;
            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
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
                ForceMenuTreeRebuild();
            }

            Selection.activeObject = config.defaultRoomTemplate;
            EditorGUIUtility.PingObject(config.defaultRoomTemplate);
        }

        [MenuItem("Socket/多人联机/打开配置中心")]
        internal static void Open()
        {
            var selectedConfig = Selection.activeObject as MultiplayerConfig;
            if (selectedConfig == null)
                selectedConfig = AssetDatabase.LoadAssetAtPath<MultiplayerConfig>(ConfigPath);

            if (selectedConfig == null)
            {
                EditorUtility.DisplayDialog(
                    "多人联机配置",
                    "未找到配置资源。请先生成演示资源，或在 Project 窗口中选中一个 MultiplayerConfig。",
                    "确定");
                return;
            }

            var window = GetWindow<OdinMultiplayerConfigWindow>();
            window.titleContent = new GUIContent("联机配置中心");
            window.minSize = new Vector2(980f, 680f);
            window.config = selectedConfig;
            window.ForceMenuTreeRebuild();
            window.Show();
            window.Focus();
        }

        private sealed class DashboardPage
        {
            private readonly OdinMultiplayerConfigWindow window;

            public DashboardPage(OdinMultiplayerConfigWindow window) => this.window = window;

            private MultiplayerConfig Config => window.config;
            private List<string> Issues => MultiplayerConfigEditor.GetValidationIssues(Config);

            [Title("多人联机配置中心", "服务器、房间模板与运行参数总览")]
            [ShowInInspector, ReadOnly, LabelText("当前状态")]
            [GUIColor(0.55f, 0.85f, 0.62f)]
            private string Status => Issues.Count == 0 ? "配置完整，可以启动" : $"发现 {Issues.Count} 项需要处理";

            [HorizontalGroup("系统摘要", Width = 0.333f)]
            [BoxGroup("系统摘要/房间")]
            [ShowInInspector, ReadOnly, HideLabel]
            private string RoomSummary => Config == null
                ? "未加载配置"
                : $"{Config.maxRooms} 个房间\n单房 {Config.EffectiveMinPlayers}-{Config.EffectiveMaxPlayers} 人";

            [HorizontalGroup("系统摘要", Width = 0.333f)]
            [BoxGroup("系统摘要/网络")]
            [ShowInInspector, ReadOnly, HideLabel]
            private string NetworkSummary => Config == null
                ? "未加载配置"
                : $"{Config.defaultAddress}:{Config.port}\n{Config.sendRate} 次/秒";

            [HorizontalGroup("系统摘要", Width = 0.333f)]
            [BoxGroup("系统摘要/模板")]
            [ShowInInspector, ReadOnly, HideLabel]
            private string TemplateSummary => Config == null || Config.defaultRoomTemplate == null
                ? "尚未绑定房间模板"
                : $"{Config.defaultRoomTemplate.templateName}\n{Config.defaultRoomTemplate.PlayerSpawnCount} 出生点 / {Config.defaultRoomTemplate.InteractableCount} 交互物";

            [ButtonGroup("主要操作")]
            [Button(SdfIconType.Save, "保存全部")]
            [GUIColor(0.45f, 0.78f, 0.55f)]
            private void SaveAll() => window.SaveAll();

            [ButtonGroup("主要操作")]
            [Button(SdfIconType.CheckCircle, "检查配置")]
            private void Validate() => window.ShowValidationDialog();

            [ButtonGroup("主要操作")]
            [Button(SdfIconType.Gear, "房间模板")]
            [GUIColor(0.45f, 0.68f, 0.85f)]
            private void OpenTemplate() => window.CreateOrSelectRoomTemplate();

            [ButtonGroup("辅助操作")]
            [Button(SdfIconType.Cursor, "定位配置")]
            private void LocateConfig() => window.SelectConfig();

            [ButtonGroup("辅助操作")]
            [Button(SdfIconType.Folder2Open, "打开启动场景")]
            private void OpenScene() => window.OpenBootstrapScene();
        }

        private sealed class MissingTemplatePage
        {
            private readonly OdinMultiplayerConfigWindow window;

            public MissingTemplatePage(OdinMultiplayerConfigWindow window) => this.window = window;

            [Title("房间模板", "当前配置尚未绑定模板")]
            [InfoBox("创建模板后，可以可视化配置人数、玩家出生点以及房间交互物。")]
            [Button(SdfIconType.Gear, "创建并绑定默认模板")]
            [GUIColor(0.45f, 0.68f, 0.85f)]
            private void CreateTemplate() => window.CreateOrSelectRoomTemplate();
        }

        private sealed class ValidationPage
        {
            private readonly OdinMultiplayerConfigWindow window;

            public ValidationPage(OdinMultiplayerConfigWindow window) => this.window = window;

            [Title("配置检查", "场景、容量和房间模板完整性")]
            [ShowInInspector, TableList(AlwaysExpanded = true, DrawScrollView = false)]
            [LabelText("检查结果")]
            private ValidationRow[] Results
            {
                get
                {
                    var issues = MultiplayerConfigEditor.GetValidationIssues(window.config);
                    if (issues.Count == 0)
                        return new[] { new ValidationRow("通过", "所有关键配置均有效，可以启动多人联机。") };

                    var rows = new ValidationRow[issues.Count];
                    for (var i = 0; i < issues.Count; i++)
                        rows[i] = new ValidationRow("需处理", issues[i]);
                    return rows;
                }
            }

            [Button(SdfIconType.CheckCircle, "重新检查")]
            private void Refresh() => window.ForceMenuTreeRebuild();
        }

        private sealed class ValidationRow
        {
            [TableColumnWidth(80, Resizable = false)]
            [DisplayAsString, LabelText("状态")]
            public string status;

            [DisplayAsString, LabelText("说明")]
            public string message;

            public ValidationRow(string status, string message)
            {
                this.status = status;
                this.message = message;
            }
        }
    }
}
#endif
