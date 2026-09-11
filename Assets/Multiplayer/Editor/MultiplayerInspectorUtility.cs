#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Socket.Multiplayer.Editor
{
    /// <summary>检查器与配置中心共用小工具：统一皮肤、表单行、摘要条、页签栏。</summary>
    internal static class MultiplayerInspectorUtility
    {
        internal const string StyleSheetPath = "Assets/Multiplayer/Editor/MultiplayerUiTheme.uss";

        /// <summary>挂载/刷新主题（基础样式 + 当前强调色片段）；可重复调用实现实时换肤。</summary>
        internal static void ApplySkin(VisualElement root)
        {
            // 先摘掉本项目此前挂过的样式，避免换肤时重复叠加
            for (var i = root.styleSheets.count - 1; i >= 0; i--)
            {
                var existing = root.styleSheets[i];
                var sheetName = existing == null ? null : existing.name;
                if (sheetName != null && (sheetName.StartsWith("MultiplayerUi") || sheetName.StartsWith("Accent") || sheetName.StartsWith("Surface")))
                    root.styleSheets.Remove(existing);
            }

            var theme = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (theme != null) root.styleSheets.Add(theme);
            var surfacePath = MultiplayerThemeSettings.SurfaceStyleSheetPath;
            if (!string.IsNullOrEmpty(surfacePath))
            {
                var surface = AssetDatabase.LoadAssetAtPath<StyleSheet>(surfacePath);
                if (surface != null) root.styleSheets.Add(surface);
            }

            var accent = AssetDatabase.LoadAssetAtPath<StyleSheet>(MultiplayerThemeSettings.AccentStyleSheetPath);
            if (accent != null) root.styleSheets.Add(accent);
            root.AddToClassList("mp-root");
        }

        internal static Label PageTitle(string text)
        {
            var label = new Label(text);
            label.AddToClassList("mp-page-title");
            return label;
        }

        /// <summary>分区标题：品牌色标记条 + 标题（放在卡片内做分组）。</summary>
        internal static VisualElement SectionHeader(string title)
        {
            var head = new VisualElement();
            head.AddToClassList("mp-section__head");
            var bar = new VisualElement();
            bar.AddToClassList("mp-section__bar");
            var label = new Label(title);
            label.AddToClassList("mp-section__title");
            head.Add(bar);
            head.Add(label);
            return head;
        }

        /// <summary>状态行：圆点 + 标签 + 值（用于配置检查、连接状态等）。</summary>
        internal static VisualElement ItemRow(string label, string value, string dotClass = null)
        {
            var row = new VisualElement();
            row.AddToClassList("mp-item");

            var dot = new VisualElement();
            dot.AddToClassList("mp-dot");
            if (!string.IsNullOrEmpty(dotClass)) dot.AddToClassList(dotClass);
            row.Add(dot);

            var labelElement = new Label(label);
            labelElement.AddToClassList("mp-item__label");
            row.Add(labelElement);

            var valueElement = new Label(value);
            valueElement.AddToClassList("mp-item__value");
            row.Add(valueElement);
            return row;
        }

        /// <summary>状态徽标（如“配置完整”“3 项待处理”）。</summary>
        internal static Label Badge(string text, bool warning = false)
        {
            var label = new Label(text);
            label.AddToClassList("mp-badge");
            if (warning) label.AddToClassList("mp-badge--warn");
            return label;
        }

        internal static Label Title(string text)
        {
            var label = new Label(text);
            label.AddToClassList("mp-title");
            return label;
        }

        internal static HelpBox Info(string text, HelpBoxMessageType type = HelpBoxMessageType.Info)
        {
            var box = new HelpBox(text, type);
            box.AddToClassList("mp-info");
            return box;
        }

        /// <summary>轻量说明文字（比 HelpBox 更安静，用于卡片内注解）。</summary>
        internal static Label Hint(string text)
        {
            var label = new Label(text);
            label.AddToClassList("mp-hint");
            return label;
        }

        internal static PropertyField Field(SerializedObject so, string path, string label = null)
        {
            var prop = so.FindProperty(path);
            var field = label == null ? new PropertyField(prop) : new PropertyField(prop, label);
            // Bind() 会把 label 重置为属性名，这里把中文标签存起来，绑定后再套回去。
            if (!string.IsNullOrEmpty(label)) field.userData = label;
            return field;
        }

        /// <summary>必须在 root.Bind(...) 之后调用：把中文标签套回被绑定重置的字段。</summary>
        internal static void ApplyLabels(VisualElement root)
        {
            root.Query<PropertyField>().ForEach(field =>
            {
                if (field.userData is string label) field.label = label;
            });
        }

        internal static VisualElement Row(params VisualElement[] cells)
        {
            var row = new VisualElement();
            row.AddToClassList("mp-row");
            foreach (var cell in cells)
            {
                cell.style.flexGrow = 1f;
                cell.style.marginRight = 6f;
                row.Add(cell);
            }
            return row;
        }

        internal static Label Unit(string unit)
        {
            var label = new Label(unit);
            label.AddToClassList("mp-unit");
            return label;
        }

        /// <summary>字段 + 单位后缀：字段定宽、单位紧跟（避免单位飘到行尾造成参差）。</summary>
        internal static VisualElement WithUnit(VisualElement field, string unit, float fieldWidth = 110f)
        {
            var row = new VisualElement();
            row.AddToClassList("mp-row");
            field.style.width = fieldWidth;
            field.style.flexGrow = 0f;
            field.style.flexShrink = 0f;
            row.Add(field);
            row.Add(Unit(unit));
            return row;
        }

        /// <summary>只读摘要条；返回 value 标签，可配合 TrackPropertyValue 实时刷新。</summary>
        internal static Label Summary(string caption, string value, out VisualElement container)
        {
            var row = new VisualElement();
            row.AddToClassList("mp-summary");
            var captionLabel = new Label(caption);
            captionLabel.AddToClassList("mp-summary__caption");
            var valueLabel = new Label(value);
            valueLabel.AddToClassList("mp-summary__value");
            row.Add(captionLabel);
            row.Add(valueLabel);
            container = row;
            return valueLabel;
        }

        /// <summary>枚举下拉框；displayNames 顺序必须与枚举声明顺序一致，改动直接写回 SerializedObject。</summary>
        internal static DropdownField EnumDropdown<TEnum>(SerializedObject so, string path, string label, string[] displayNames)
            where TEnum : struct, Enum
        {
            var prop = so.FindProperty(path);
            var field = new DropdownField(label, new List<string>(displayNames), prop.enumValueIndex);
            field.RegisterValueChangedCallback(_ =>
            {
                prop.enumValueIndex = field.index;
                so.ApplyModifiedProperties();
            });
            return field;
        }

        /// <summary>顶部页签栏（检查器与配置中心共用）。</summary>
        internal sealed class TabBar
        {
            private readonly Button[] buttons;

            internal VisualElement Root { get; }

            internal TabBar(string[] tabs, Action<int> onSelect)
            {
                Root = new VisualElement();
                Root.AddToClassList("mp-tabbar");
                buttons = new Button[tabs.Length];
                for (var i = 0; i < tabs.Length; i++)
                {
                    var index = i;
                    var button = new Button(() => onSelect(index)) { text = tabs[i] };
                    button.AddToClassList("mp-tab");
                    buttons[i] = button;
                    Root.Add(button);
                }
            }

            internal void SetActive(int active)
            {
                for (var i = 0; i < buttons.Length; i++)
                    buttons[i].EnableInClassList("mp-tab--active", i == active);
            }
        }
    }

    /// <summary>支持嵌入配置中心显示的检查器：嵌入时隐藏自身页签，改为分区堆叠。</summary>
    internal interface IEmbeddedInspector
    {
        void SetEmbeddedLayout();
    }
}
#endif
