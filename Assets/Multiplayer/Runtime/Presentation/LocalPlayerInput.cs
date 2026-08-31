using UnityEngine;

namespace Socket.Multiplayer
{
    public sealed class LocalPlayerInput : MonoBehaviour
    {
        [SerializeField] private NetworkPlayer player;

        private void Awake()
        {
            if (player == null) player = GetComponent<NetworkPlayer>();
        }

        private void Update()
        {
            if (player == null || !player.isLocalPlayer) return;
            var input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            player.SubmitInput(input);
            // R-key Ready lives on SocketRoomPlayer (lobby player); the game player has no Ready state.
        }
    }
}
