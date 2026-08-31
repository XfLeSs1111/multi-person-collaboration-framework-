using System.Text;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

namespace Socket.Multiplayer
{
    public sealed class RoomChatPanel : MonoBehaviour
    {
        [SerializeField] private NetworkRoomChat roomChat;
        [SerializeField] private InputField inputField;
        [SerializeField] private Text messagesText;
        private bool _dirty = true;
        private NetworkRoomChat _subscribedChat;

        private void Awake()
        {
            if (roomChat == null) roomChat = FindFirstObjectByType<NetworkRoomChat>();
        }

        private void Update()
        {
            if (roomChat == null) roomChat = FindFirstObjectByType<NetworkRoomChat>();
            if (roomChat != _subscribedChat)
            {
                if (_subscribedChat != null) _subscribedChat.messages.OnChange -= OnMessagesChanged;
                _subscribedChat = roomChat;
                if (_subscribedChat != null) _subscribedChat.messages.OnChange += OnMessagesChanged;
                _dirty = true;
            }
            if (!_dirty || roomChat == null || messagesText == null) return;

            var builder = new StringBuilder();
            foreach (var message in roomChat.messages)
                builder.AppendLine($"{message.sender}: {message.body}");
            messagesText.text = builder.ToString();
            _dirty = false;
        }

        private void OnMessagesChanged(SyncList<RoomChatMessage>.Operation _, int __, RoomChatMessage ___) => _dirty = true;

        private void OnDestroy()
        {
            if (_subscribedChat != null) _subscribedChat.messages.OnChange -= OnMessagesChanged;
        }

        public void Send()
        {
            if (roomChat == null || inputField == null || !NetworkClient.active) return;
            roomChat.CmdSend(inputField.text);
            inputField.text = string.Empty;
            inputField.ActivateInputField();
        }
    }
}
