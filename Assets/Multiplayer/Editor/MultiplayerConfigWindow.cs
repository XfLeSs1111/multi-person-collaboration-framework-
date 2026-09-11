#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Socket.Multiplayer.Editor
{
    /// <summary>联机配置中心（UI Toolkit）：总览 / 全局配置 / 房间模板 / 五子棋规则 / 配置检查。</summary>
    internal sealed partial class MultiplayerConfigWindow : EditorWindow
    {
        private const string ConfigPath = "Assets/MultiplayerGenerated/MultiplayerConfig.asset";
        private const string TemplatePath = "Assets/MultiplayerGenerated/DefaultRoomTemplate.asset";

        private static readonly string[] TabNames = { "总览", "全局配置", "房间模板", "玩法规则", "配置检查", "外观" };

        [SerializeField] private MultiplayerConfig config;
        [SerializeField] private int activeTab;

        private VisualElement contentHost;
        private Button[] navButtons;
        private UnityEditor.Editor configEditor;
        private UnityEditor.Editor templateEditor;
        private UnityEditor.Editor ruleEditor;

        [MenuItem("Socket/多人联机/打开配置中心")]
        internal static void Open()
        {
            var selectedConfig = Selection.activeObject as MultiplayerConfig
                                 ?? AssetDatabase.LoadAssetAtPath<MultiplayerConfig>(ConfigPath);
            if (selectedConfig == null)
            {
                EditorUtility.DisplayDialog(
                    "多人联机配置",
                    "未找到配置资源。请先生成演示资源，或在 Project 窗口中选中一个 MultiplayerConfig。",
                    "确定");
                return;
            }

            var window = GetWindow<MultiplayerConfigWindow>();
            window.titleContent = new GUIContent("联机配置中心");
            window.minSize = new Vector2(760f, 560f);
            window.config = selectedConfig;
            window.Show();
            window.Focus();
        }

        private void CreateGUI()
        {
            if (config == null) config = AssetDatabase.LoadAssetAtPath<MultiplayerConfig>(ConfigPath);

            MultiplayerInspectorUtility.ApplySkin(rootVisualElement);
            rootVisualElement.Clear();

            rootVisualElement.Add(BuildHero());

            var bar = new VisualElement();
            bar.AddToClassList("mp-pillbar");
            navButtons = new Button[TabNames.Length];
            for (var i = 0; i < TabNames.Length; i++)
            {
                var index = i;
                var item = new Button(() => SelectTab(index)) { text = TabNames[i] };
                item.AddToClassList("mp-pill-tab");
                navButtons[i] = item;
                bar.Add(item);
            }
            rootVisualElement.Add(bar);

            contentHost = new VisualElement { style = { flexGrow = 1 } };
            rootVisualElement.Add(contentHost);

            SelectTab(activeTab);
        }

        private void OnEnable() => MultiplayerThemeSettings.Changed += OnThemeChanged;

        private void OnDisable() => MultiplayerThemeSettings.Changed -= OnThemeChanged;

        private void OnThemeChanged()
        {
            MultiplayerInspectorUtility.ApplySkin(rootVisualElement);
            SelectTab(activeTab);
        }

        private void OnFocus() => SelectTab(activeTab);

        private void SelectTab(int index)
        {
            activeTab = Mathf.Clamp(index, 0, TabNames.Length - 1);
            if (navButtons != null)
                for (var i = 0; i < navButtons.Length; i++)
                    navButtons[i].EnableInClassList("mp-pill-tab--active", i == activeTab);
            if (contentHost == null) return;

            contentHost.Clear();
            switch (activeTab)
            {
                case 0:
                    contentHost.Add(BuildDashboard());
                    break;
                case 1:
                    contentHost.Add(HostEditor(EditorFor(config, ref configEditor)));
                    break;
                case 2:
                    contentHost.Add(config != null && config.defaultRoomTemplate != null
                        ? HostEditor(EditorFor(config.defaultRoomTemplate, ref templateEditor))
                        : BuildMissingTemplate());
                    break;
                case 3:
                    contentHost.Add(BuildRulePage());
                    break;
                case 4:
                    contentHost.Add(BuildValidation());
                    break;
                default:
                    contentHost.Add(BuildAppearance());
                    break;
            }
        }

        private static UnityEditor.Editor EditorFor(UnityEngine.Object target, ref UnityEditor.Editor cache)
        {
            if (target == null) return null;
            if (cache != null && cache.target == target) return cache;
            if (cache != null) DestroyImmediate(cache);
            cache = UnityEditor.Editor.CreateEditor(target);
            return cache;
        }

        private static VisualElement HostEditor(UnityEditor.Editor editor)
        {
            var host = new ScrollView { style = { flexGrow = 1 } };
            if (editor == null)
            {
                host.Add(MultiplayerInspectorUtility.Info("未加载对象。", HelpBoxMessageType.Warning));
                return host;
            }

            if (editor is IEmbeddedInspector embedded) embedded.SetEmbeddedLayout();
            editor.serializedObject.Update();
            var ui = editor.CreateInspectorGUI();
            if (ui == null)
            {
                host.Add(MultiplayerInspectorUtility.Info("该对象未提供 UI Toolkit 检查器。", HelpBoxMessageType.Warning));
                return host;
            }

            ui.style.paddingLeft = 8;
            ui.style.paddingRight = 8;
            host.Add(ui);
            return host;
        }
    }
}
#endif
