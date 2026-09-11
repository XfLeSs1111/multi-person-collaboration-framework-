#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Socket.Multiplayer.Editor
{
    /// <summary>配置中心主题色设置（EditorPrefs 保存，按用户生效，不污染工程配置）。</summary>
    internal static class MultiplayerThemeSettings
    {
        private const string AccentKey = "Socket.Multiplayer.Theme.AccentIndex";

        internal const string ThemeFolder = "Assets/Multiplayer/Editor/Theme/";

        internal struct AccentPreset
        {
            internal readonly string Name;
            internal readonly string FileName;
            internal readonly Color Swatch;

            internal AccentPreset(string name, string fileName, Color swatch)
            {
                Name = name;
                FileName = fileName;
                Swatch = swatch;
            }
        }

        internal static readonly AccentPreset[] Presets =
        {
            new AccentPreset("钢蓝", "AccentSteelBlue", new Color(0.29f, 0.56f, 0.91f)),
            new AccentPreset("青绿", "AccentTeal", new Color(0.24f, 0.72f, 0.65f)),
            new AccentPreset("紫罗兰", "AccentViolet", new Color(0.60f, 0.47f, 0.93f)),
            new AccentPreset("琥珀", "AccentAmber", new Color(0.90f, 0.67f, 0.25f)),
            new AccentPreset("玫红", "AccentRose", new Color(0.90f, 0.42f, 0.58f))
        };

        /// <summary>主题色变化通知（配置中心据此实时换肤）。</summary>
        internal static event Action Changed;

        internal static int AccentIndex
        {
            get => Mathf.Clamp(EditorPrefs.GetInt(AccentKey, 0), 0, Presets.Length - 1);
            set
            {
                EditorPrefs.SetInt(AccentKey, Mathf.Clamp(value, 0, Presets.Length - 1));
                Changed?.Invoke();
            }
        }

        internal static string AccentStyleSheetPath => ThemeFolder + Presets[AccentIndex].FileName + ".uss";
    }
}
#endif
