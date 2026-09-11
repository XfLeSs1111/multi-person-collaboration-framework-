#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Socket.Multiplayer.Editor
{
    /// <summary>
    /// Menu-driven local session driver: automates host -> create room -> ready -> start
    /// so the visual UI can be inspected without manual clicking (agent/batch friendly).
    /// In a single-player gomoku room the start step is expected to be rejected
    /// ("not enough players") — that rejection banner is part of what this demonstrates.
    /// </summary>
    public static class MultiplayerDebugDriver
    {
        private static int step;
        private static double nextStepTime;

        [MenuItem("Socket/Multiplayer/Debug Drive Local Session")]
        public static void Drive()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Debug Driver] 先进入 Play 模式，再执行该菜单。");
                return;
            }
            step = 0;
            nextStepTime = EditorApplication.timeSinceStartup + 2d;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.update -= Tick;
                return;
            }
            if (EditorApplication.timeSinceStartup < nextStepTime) return;

            var manager = UnityEngine.Object.FindFirstObjectByType<SocketRoomManager>();
            var roomOps = UnityEngine.Object.FindFirstObjectByType<RoomOperations>();
            switch (step)
            {
                case 0:
                    if (manager != null) manager.StartHost();
                    break;
                case 1:
                    if (roomOps != null) roomOps.CreateRoom("调试房间");
                    break;
                case 2:
                    if (roomOps != null) roomOps.SetReady(true);
                    break;
                case 3:
                    if (roomOps != null) roomOps.StartGame();
                    break;
                default:
                    EditorApplication.update -= Tick;
                    Debug.Log("[Debug Driver] 会话驱动完成（单人房，Start 预期被拒）。");
                    return;
            }

            step++;
            nextStepTime = EditorApplication.timeSinceStartup + 2d;
        }
    }
}
#endif
