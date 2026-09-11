using System.Globalization;

namespace Socket.Multiplayer
{
    public static class MultiplayerProtocol
    {
        // Protocol version changelog (M3-S.6). Bump whenever NetworkMessage layouts change.
        //   5 — added MatchRecordInfo[] to ClientRoomMessage (战绩/回放数据)
        //   4 — added MultiplayerErrorCode to ClientRoomMessage and AuthResponseMessage
        //   earlier revisions predate this changelog (room protocol, multi-room routing,
        //   auth handshake with protocol version + config signature)
        public const ushort Version = 5;

        public static string GetConfigSignature(MultiplayerConfig config)
        {
            if (config == null) return "NONE";

            var hash = 2166136261u;
            Add(ref hash, config.roomName);
            Add(ref hash, config.maxRooms.ToString(CultureInfo.InvariantCulture));
            Add(ref hash, config.EffectiveMinPlayers.ToString(CultureInfo.InvariantCulture));
            Add(ref hash, config.EffectiveMaxPlayers.ToString(CultureInfo.InvariantCulture));
            Add(ref hash, config.maxServerPlayers.ToString(CultureInfo.InvariantCulture));
            Add(ref hash, config.maxSpectators.ToString(CultureInfo.InvariantCulture));
            Add(ref hash, config.port.ToString(CultureInfo.InvariantCulture));
            Add(ref hash, config.sendRate.ToString(CultureInfo.InvariantCulture));
            Add(ref hash, config.moveSpeed.ToString(CultureInfo.InvariantCulture));
            Add(ref hash, config.inputSendRate.ToString(CultureInfo.InvariantCulture));
            Add(ref hash, config.spawnSpacing.ToString(CultureInfo.InvariantCulture));
            Add(ref hash, config.interactionRange.ToString(CultureInfo.InvariantCulture));
            Add(ref hash, config.allowLateJoiners.ToString(CultureInfo.InvariantCulture));
            Add(ref hash, config.autoStartWhenAllReady.ToString(CultureInfo.InvariantCulture));
            Add(ref hash, config.turnTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
            Add(ref hash, config.authTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
            Add(ref hash, config.matchRecordLimit.ToString(CultureInfo.InvariantCulture));
            Add(ref hash, config.recordReplays.ToString(CultureInfo.InvariantCulture));

            var template = config.defaultRoomTemplate;
            if (template != null)
            {
                Add(ref hash, template.templateName);
                Add(ref hash, template.minPlayers.ToString(CultureInfo.InvariantCulture));
                Add(ref hash, template.maxPlayers.ToString(CultureInfo.InvariantCulture));
                Add(ref hash, template.maxSpectators.ToString(CultureInfo.InvariantCulture));
                if (template.rules != null)
                    Add(ref hash, template.rules.SignatureFingerprint());

                if (template.playerSpawns != null)
                    foreach (var spawn in template.playerSpawns)
                    {
                        if (spawn == null) continue;
                        Add(ref hash, spawn.position.x.ToString(CultureInfo.InvariantCulture));
                        Add(ref hash, spawn.position.y.ToString(CultureInfo.InvariantCulture));
                        Add(ref hash, spawn.position.z.ToString(CultureInfo.InvariantCulture));
                        Add(ref hash, spawn.eulerAngles.x.ToString(CultureInfo.InvariantCulture));
                        Add(ref hash, spawn.eulerAngles.y.ToString(CultureInfo.InvariantCulture));
                        Add(ref hash, spawn.eulerAngles.z.ToString(CultureInfo.InvariantCulture));
                    }

                if (template.interactables != null)
                    foreach (var spawn in template.interactables)
                    {
                        if (spawn == null) continue;
                        Add(ref hash, spawn.prefab == null ? string.Empty : spawn.prefab.name);
                        Add(ref hash, spawn.position.x.ToString(CultureInfo.InvariantCulture));
                        Add(ref hash, spawn.position.y.ToString(CultureInfo.InvariantCulture));
                        Add(ref hash, spawn.position.z.ToString(CultureInfo.InvariantCulture));
                    }
            }

            return hash.ToString("X8", CultureInfo.InvariantCulture);
        }

        private static void Add(ref uint hash, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                hash ^= 0xff;
                hash *= 16777619u;
                return;
            }

            foreach (var character in value)
            {
                hash ^= character;
                hash *= 16777619u;
            }

            hash ^= 0xff;
            hash *= 16777619u;
        }
    }
}
