using UnityEngine;

namespace Socket.Multiplayer
{
    public readonly struct PlayerIntent
    {
        public readonly Vector2 Move;

        public PlayerIntent(Vector2 move)
        {
            Move = move;
        }
    }

    public interface IPlayerIntentSource
    {
        PlayerIntent ReadIntent();
    }
}
