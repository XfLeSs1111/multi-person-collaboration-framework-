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
        internal const string StyleSheetPath = "Assets/Multiplayer/Editor/MultiplayerUiToolkit.uss";

        /// <summary>给根元素挂上共用样式表与根类名（检查器与窗口通用）。</summary>
        internal static void ApplySkin(VisualElement root)
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            root.AddToClassList("mp-root");
        }

        internal static Label PageTitle(string text)
        {
            var label = new Label(text);
            label.AddToClassList("mp-page-title");
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

        internal static PropertyField Field(SerializedObject so, string path, string label = null)
        {
            var prop = so.FindProperty(path);
            return label == null ? new PropertyField(prop) : new PropertyField(prop, label);
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

        /// <summary>字段 + 单位后缀（如“秒”“人”）的横向组合。</summary>
        internal static VisualElement WithUnit(VisualElement field, string unit)
        {
            var row = new VisualElement();
            row.AddToClassList("mp-row");
            field.style.flexGrow = 1f;
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
}
#endif
