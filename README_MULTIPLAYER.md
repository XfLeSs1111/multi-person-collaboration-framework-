# Socket Multiplayer Framework

PC-first Mirror skeleton for a configurable 2-8 player shared room.

## Framework Model

The implementation follows the practical PC room flow used by Mirror's official Room/Chat/Discovery examples and the `mirror-lobby` GitHub boilerplate:

1. The session menu starts a Host, Client, or dedicated Server and optionally supplies a player name.
2. The server owns capacity, room phase, leader election, readiness, scene changes, chat, and shared interactions.
3. The first joined player is Leader. When that player leaves, the server assigns the next connected player as Leader.
4. Only the Leader can start a room, and only after `minPlayers` have joined and all players are Ready.
5. `NetworkRoomState` and `NetworkRoomChat` are spawned by the server so late-joining clients receive current room metadata and recent chat history.
6. `SocketRoomDiscovery` broadcasts PC LAN room metadata; public matchmaking and NAT traversal are intentionally separate services.

## Setup

1. Open the project with Unity `2022.3.16f1`.
2. Import Mirror `v96.10.0` from the Mirror Asset Store package or GitHub release. Mirror is distributed as an Asset package rather than a root UPM package, so it is intentionally not declared in `manifest.json`.
3. Run `Socket > Multiplayer > Initialize PC Demo`. This creates the config asset, Player/Interactable/RoomState/RoomChat prefabs, Bootstrap/Lobby/Game scenes, and Build Settings entries.
4. Select `Assets/MultiplayerGenerated/MultiplayerConfig.asset` to tune room and network values with the custom Inspector. Set `autoStartWhenAllReady` and `allowLateJoiners` as needed.

The runtime import excludes Mirror's editor test fixtures. Those fixtures intentionally contain invalid child `NetworkIdentity` components to test Mirror validation and are kept in `Temp/MirrorTestsBackup-20260830` for reference.
4. Add a `SocketNetworkManager` and `kcp2k.KcpTransport` to the bootstrap scene. Assign the config. The config controls the Mirror send rate and KCP port.
5. Create a player prefab with `NetworkIdentity`, `NetworkPlayer`, `LocalPlayerInput`, `NetworkTransformUnreliable`, and a visual/collider. Register it in the manager. Keep the player transform server-authoritative for this first slice.
6. Add `NetworkStartPosition` objects and one or more `NetworkInteractable` objects to the gameplay scene.
7. Optional: add Mirror's `UniqueNameAuthenticator` to the bootstrap object. `NetworkStartup` fills its name from `-name <player>` or `defaultPlayerName`.
8. Add `NetworkManagerHUD` for a quick keyboard-driven Host/Client smoke test, or add `NetworkStartup` for command-line/config startup. Set `defaultStartMode` to `Manual` for UI-driven startup, or `Host` for a local smoke test.
9. Add `RoomOperations` to a lobby canvas. Wire Ready/Start/Return buttons to its public methods. `R` also toggles Ready on the local player for smoke tests.
10. `NetworkRoomState` is spawned automatically by the server and replicates room metadata to clients; you may also place a `NetworkIdentity` + `NetworkRoomState` scene object if you prefer explicit scene wiring.
11. Add `RoomStatusPanel` to the lobby canvas and assign UI Text fields for room name, phase and roster.
12. Add `SocketRoomDiscovery` beside the NetworkManager for LAN browsing. Wire its `OnRoomFound` event to your server-list UI. It returns room name, IP, player count and phase over UDP broadcast; it is intended for local networks, not Internet NAT traversal.
13. Open `Socket > Multiplayer Framework` for the editor-side scene contract and quick access to the config asset.

## Command line

```text
-host
-server
-client <address>
-port <port>
-name <player>
```

The server remains authoritative for player movement and shared interaction state. Clients submit intent only. The config asset contains authored defaults; runtime room state is held by network objects on the server.

## Smoke-test controls

- `WASD` / arrow keys: move the local player.
- `R`: toggle the local player's Ready state.
- Use Mirror's `NetworkManagerHUD` buttons to start Host or Client.

## Current scope

- Windows PC, keyboard movement, Host/Client/Server.
- Configurable room capacity, port, movement and interaction lease.
- Server-validated shared interaction with conflict and timeout handling.
- Optional unique-name authentication and per-player Ready state.
- Leader election and transfer when the leader leaves.
- Lobby/game scene flow with server-validated Start and Return operations.
- LAN discovery with room name, capacity and phase metadata.
- Editor configuration inspector and framework window; runtime room status panel.
- Replicated room state and server-validated room chat with a recent-message buffer.

Not included yet: public matchmaking, NAT traversal, voice, persistence, complex physics, VR input, or host migration.
