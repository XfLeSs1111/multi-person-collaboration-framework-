using UnityEngine;

namespace Socket.Multiplayer
{
    public sealed class LocalPlayerInput : MonoBehaviour
    {
        [SerializeField] private NetworkPlayer player;
        private RoomOperations roomOperations;

        private void Awake()
        {
            if (player == null) player = GetComponent<NetworkPlayer>();
            roomOperations = FindFirstObjectByType<RoomOperations>();
        }

        private void Update()
        {
            if (player == null || !player.isLocalPlayer) return;
            var input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            player.SubmitInput(input);
            if (Input.GetKeyDown(KeyCode.R) && roomOperations != null)
                roomOperations.ToggleReady();
        }
    }
}
