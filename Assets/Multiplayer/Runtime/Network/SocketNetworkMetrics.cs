using Mirror;
using UnityEngine;

namespace Socket.Multiplayer
{
    [AddComponentMenu("Socket/Network Metrics")]
    public sealed class SocketNetworkMetrics : MonoBehaviour
    {
        public long IncomingBytes { get; private set; }
        public long OutgoingBytes { get; private set; }
        public long IncomingMessages { get; private set; }
        public long OutgoingMessages { get; private set; }
        public float IncomingBytesPerSecond { get; private set; }
        public float OutgoingBytesPerSecond { get; private set; }
        public double RoundTripTimeMs => NetworkClient.isConnected ? NetworkTime.rtt * 1000d : 0d;

        private long recentIncomingBytes;
        private long recentOutgoingBytes;
        private float nextSampleTime;

        private void Start()
        {
            NetworkDiagnostics.InMessageEvent += OnMessageIn;
            NetworkDiagnostics.OutMessageEvent += OnMessageOut;
            nextSampleTime = Time.unscaledTime + 1f;
        }

        private void OnDestroy()
        {
            NetworkDiagnostics.InMessageEvent -= OnMessageIn;
            NetworkDiagnostics.OutMessageEvent -= OnMessageOut;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextSampleTime) return;
            var elapsed = Mathf.Max(0.01f, Time.unscaledTime - (nextSampleTime - 1f));
            IncomingBytesPerSecond = recentIncomingBytes / elapsed;
            OutgoingBytesPerSecond = recentOutgoingBytes / elapsed;
            recentIncomingBytes = 0;
            recentOutgoingBytes = 0;
            nextSampleTime = Time.unscaledTime + 1f;
        }

        public void ResetTotals()
        {
            IncomingBytes = 0;
            OutgoingBytes = 0;
            IncomingMessages = 0;
            OutgoingMessages = 0;
            recentIncomingBytes = 0;
            recentOutgoingBytes = 0;
        }

        private void OnMessageIn(NetworkDiagnostics.MessageInfo info)
        {
            IncomingBytes += info.bytes;
            IncomingMessages += info.count;
            recentIncomingBytes += info.bytes;
        }

        private void OnMessageOut(NetworkDiagnostics.MessageInfo info)
        {
            var bytes = (long)info.bytes * info.count;
            OutgoingBytes += bytes;
            OutgoingMessages += info.count;
            recentOutgoingBytes += bytes;
        }
    }
}
