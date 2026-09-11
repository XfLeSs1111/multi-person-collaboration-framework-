#if UNITY_EDITOR
using UnityEditor;

namespace Socket.Multiplayer.Editor
{
    internal static class MultiplayerFrameworkWindow
    {
        [MenuItem("Socket/Multiplayer Framework")]
        private static void Open()
        {
            MultiplayerConfigWindow.Open();
        }
    }
}
#endif
