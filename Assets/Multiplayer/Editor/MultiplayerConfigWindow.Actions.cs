#if UNITY_EDITOR
using System;
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
            var panel = new VisualElement();
            panel.AddToClassList("mp-section");
            panel.Add(MultiplayerInspectorUtility.SectionHeader("检查结果"));

            if (issues.Count == 0)
                panel.Add(MultiplayerInspectorUtility.ItemRow("通过", "所有关键配置均有效，可以启动多人联机。"));
            else
                foreach (var issue in issues)
                    panel.Add(MultiplayerInspectorUtility.ItemRow("待处理", issue, "mp-dot--warn"));

            page.Add(panel);
            page.Add(ActionButton("重新检查", () => SelectTab(activeTab), ButtonKind.Primary));
            return page;
        }

        /// <summary>色板选择行：色块 + 名称按钮，选中项用主色按钮突出。</summary>
        private static VisualElement SwatchChoices(
            int count,
            Func<int, string> nameAt,
            Func<int, Color> colorAt,
            Func<int, bool> selectedAt,
            Action<int> pick)
        {
            var row = new VisualElement();
            row.AddToClassList("mp-actions");
            for (var i = 0; i < count; i++)
            {
                var index = i;

                var cell = new VisualElement();
                cell.style.flexDirection = FlexDirection.Row;
                cell.style.alignItems = Align.Center;
                cell.style.marginRight = 10f;
                cell.style.marginBottom = 6f;

                var chip = new VisualElement();
                chip.style.width = 14f;
                chip.style.height = 14f;
                chip.style.borderTopLeftRadius = 4f;
                chip.style.borderTopRightRadius = 4f;
                chip.style.borderBottomLeftRadius = 4f;
                chip.style.borderBottomRightRadius = 4f;
                chip.style.backgroundColor = colorAt(index);
                chip.style.marginRight = 6f;

                var button = new Button(() => pick(index)) { text = nameAt(index) };
                button.AddToClassList("mp-btn");
                if (selectedAt(index)) button.AddToClassList("mp-btn--primary");

                cell.Add(chip);
                cell.Add(button);
                row.Add(cell);
            }
            return row;
        }

        private VisualElement BuildAppearance()
        {
            var page = new ScrollView { style = { flexGrow = 1 } };
            page.Add(MultiplayerInspectorUtility.PageTitle("外观"));

            var section = new VisualElement();
            section.AddToClassList("mp-section");
            section.Add(MultiplayerInspectorUtility.SectionHeader("主题色"));
            section.Add(MultiplayerInspectorUtility.Hint(
                "强调色用于侧边选中项、分区标记条、主按钮与摘要条；底色调与控件样式跟随 Unity 编辑器皮肤（明/暗自动适配）。"));

            section.Add(SwatchChoices(
                MultiplayerThemeSettings.Presets.Length,
                i => MultiplayerThemeSettings.Presets[i].Name,
                i => MultiplayerThemeSettings.Presets[i].Swatch,
                i => i == MultiplayerThemeSettings.AccentIndex,
                i => MultiplayerThemeSettings.AccentIndex = i));
            page.Add(section);

            var surfaceSection = new VisualElement();
            surfaceSection.AddToClassList("mp-section");
            surfaceSection.Add(MultiplayerInspectorUtility.SectionHeader("背景基调"));
            surfaceSection.Add(MultiplayerInspectorUtility.Hint(
                "默认跟随 Unity 编辑器皮肤（明/暗自动适配）；也可以固定成深色面板，卡片与棋盘会一起变。"));
            surfaceSection.Add(SwatchChoices(
                MultiplayerThemeSettings.Surfaces.Length,
                i => MultiplayerThemeSettings.Surfaces[i].Name,
                i => MultiplayerThemeSettings.Surfaces[i].Swatch,
                i => i == MultiplayerThemeSettings.SurfaceIndex,
                i => MultiplayerThemeSettings.SurfaceIndex = i));
            page.Add(surfaceSection);

            var status = new VisualElement();
            status.style.flexDirection = FlexDirection.Row;
            status.style.alignItems = Align.Center;
            status.Add(MultiplayerInspectorUtility.Badge(
                $"强调色：{MultiplayerThemeSettings.Presets[MultiplayerThemeSettings.AccentIndex].Name} / 背景：{MultiplayerThemeSettings.Surfaces[MultiplayerThemeSettings.SurfaceIndex].Name}"));
            surfaceSection.Add(status);
            surfaceSection.Add(MultiplayerInspectorUtility.Hint("设置保存在本机用户偏好（EditorPrefs），不随工程同步；点击即时生效。"));
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
