# Socket Multiplayer Framework

PC-first Mirror skeleton for one server hosting multiple isolated rooms.

## Architecture

The runtime now uses a single physical online scene:

1. `SocketRoomManager : NetworkManager` owns network lifecycle and routes room messages.
2. `RoomRegistry` owns room rules without depending on Unity scenes or network objects.
3. `NetworkMatch` plus `MatchInterestManagement` isolates players, chat, state and interactables by room `Guid`.
4. Every connected player starts in the non-empty `LobbyRoom.Id`; creating or joining a room changes only that player's match.
5. Starting a room changes its phase and spawns room interactables. It never calls global `ServerChangeScene`.
6. `SocketRoomDiscovery` advertises all rooms on a server and prunes stale LAN responses by TTL.

This removes the old `NetworkRoomManager` assumptions: one global `roomSlots`, one global phase, two player prefabs, and global Lobby/Game scene transitions.

## Setup

1. Open the project with Unity `2022.3.16f1`.
2. Run `Socket > Multiplayer > Initialize PC Demo`, or `Regenerate Demo` after generator changes.
3. The generated setup contains:
   - `Bootstrap.unity`: persistent `SocketRoomManager`, `MatchInterestManagement`, transport, discovery and HUD.
   - `Lobby.unity`: the single physical online scene.
   - `Player.prefab`: `NetworkIdentity`, `NetworkMatch`, `NetworkPlayer`, input and transform sync.
   - `NetworkRoomState`, `NetworkRoomChat` and `Interactable` prefabs with `NetworkMatch`.
4. Select `Assets/MultiplayerGenerated/MultiplayerConfig.asset` for the standard inspector, or open `Socket > Multiplayer > Open Odin Config Editor`.

The generated config exposes room capacity, maximum room count, port, movement, interaction leases, late joining, discovery TTL and the two scene paths. Both scenes are validated against Build Settings.

## Quick Start

1. Open `Assets/MultiplayerGenerated/Scenes/Bootstrap.unity`.
2. Press Play and click `Host`.
3. Click `Create Room` to move the local player out of the shared lobby.
4. A second client can connect, see the room list, and click `Join`.
5. Players can toggle `Ready`; the leader can click `Start`.
6. `Start` affects only that room. `Lobby` destroys that room's interactables and returns its phase to `Lobby`.

## Command Line

```text
-host
-server
-client <address>
-port <port>
-name <player>
```

## Scope

Implemented: authoritative movement, room create/join/leave/cancel, leader transfer, ready gating, per-room phase, per-room chat/state, match-isolated interactables, LAN discovery and Odin configuration entry point.

Not included: public matchmaking, NAT traversal, voice, persistence, host migration, and a room-specific visual environment prefab. The current single physical scene is the stable foundation for those later additions.
